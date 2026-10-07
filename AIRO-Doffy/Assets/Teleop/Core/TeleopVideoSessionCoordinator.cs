using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Owns the async WebRTC lifetime independently of AppManager's UI facade.
/// VideoStreamManager predates cancellation, so this coordinator uses a
/// generation token plus serialized manager stops to discard stale starts
/// safely.
/// </summary>
public sealed class TeleopVideoSessionCoordinator
{
    private readonly object gate = new object();
    private readonly Func<VideoStreamManager> resolveManager;
    private readonly Func<string> host;
    private readonly Func<int> signalingPort;
    private readonly Func<int, bool> canStart;
    private readonly Action<string> log;
    private Task activeTask = Task.CompletedTask;
    private Task managerOperation = Task.CompletedTask;
    private CancellationTokenSource activeCancellation;
    private int generation;

    public TeleopVideoSessionCoordinator(Func<VideoStreamManager> resolveManager,
        Func<string> host, Func<int> signalingPort, Func<int, bool> canStart,
        Action<string> log = null)
    {
        this.resolveManager = resolveManager ?? throw new ArgumentNullException(nameof(resolveManager));
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.signalingPort = signalingPort ?? throw new ArgumentNullException(nameof(signalingPort));
        this.canStart = canStart ?? throw new ArgumentNullException(nameof(canStart));
        this.log = log ?? (_ => { });
    }

    public Task<bool> StartAsync(int streamGeneration, string reason)
    {
        var source = new CancellationTokenSource();
        Task previous;
        int currentGeneration;
        lock (gate)
        {
            previous = activeTask ?? Task.CompletedTask;
            activeCancellation?.Cancel();
            currentGeneration = ++generation;
            activeCancellation = source;
            Task<bool> task = RunStartAsync(previous, source, currentGeneration, streamGeneration, reason);
            activeTask = task;
            return task;
        }
    }

    public async Task StopAsync(string reason)
    {
        Task previous;
        int stopGeneration;
        lock (gate)
        {
            stopGeneration = ++generation;
            CancellationTokenSource source = activeCancellation;
            previous = activeTask ?? Task.CompletedTask;
            source?.Cancel();
            activeCancellation = null;
            activeTask = Task.CompletedTask;
        }

        await Ignore(previous);
        // A new StartAsync may have been requested while the cancelled start
        // unwound. In that case this stop is stale and must not tear down the
        // replacement session. The current-generation operation, if any, owns
        // the manager cleanup.
        await QueueManagerStop(reason, stopGeneration);
    }

    public void Cancel()
    {
        lock (gate)
        {
            ++generation;
            activeCancellation?.Cancel();
            activeCancellation = null;
            activeTask = Task.CompletedTask;
        }
    }

    private async Task<bool> RunStartAsync(Task previous, CancellationTokenSource source,
        int currentGeneration, int streamGeneration, string reason)
    {
        CancellationToken token = source.Token;
        try
        {
            Task stopOld = QueueManagerStop("replace_" + reason, currentGeneration);
            await Ignore(previous);
            await Ignore(stopOld);
            token.ThrowIfCancellationRequested();

            if (!CanStart(currentGeneration, streamGeneration)) return false;
            VideoStreamManager manager = resolveManager();
            if (manager == null)
            {
                log("VideoStreamManager missing");
                return false;
            }

            bool ok = await manager.StartVideoSession(host(), signalingPort(), "720p30");
            token.ThrowIfCancellationRequested();
            if (!ok) log("Video session failed to start");

            if (!CanStart(currentGeneration, streamGeneration))
            {
                await QueueManagerStop("stale_start", currentGeneration);
                return false;
            }
            return ok;
        }
        catch (OperationCanceledException)
        {
            await QueueManagerStop("cancelled_start", currentGeneration);
            return false;
        }
        catch (Exception error)
        {
            log("Video session exception: " + error.Message);
            await QueueManagerStop("exception", currentGeneration);
            return false;
        }
        finally
        {
            lock (gate)
            {
                if (currentGeneration == generation && ReferenceEquals(activeCancellation, source))
                {
                    activeCancellation = null;
                    activeTask = Task.CompletedTask;
                }
            }
            source.Dispose();
        }
    }

    private bool CanStart(int currentGeneration, int streamGeneration)
    {
        lock (gate)
        {
            return currentGeneration == generation && canStart(streamGeneration);
        }
    }

    private async Task StopManagerAsync(string reason)
    {
        VideoStreamManager manager = resolveManager();
        if (manager == null) return;
        try { await manager.StopVideoSession(reason); }
        catch (Exception error) { log("Video stop exception: " + error.Message); }
    }

    private Task QueueManagerStop(string reason, int expectedGeneration)
    {
        lock (gate)
        {
            Task previous = managerOperation ?? Task.CompletedTask;
            Task next = StopAfterAsync(previous, reason, expectedGeneration);
            managerOperation = next;
            return next;
        }
    }

    private async Task StopAfterAsync(Task previous, string reason, int expectedGeneration)
    {
        await Ignore(previous);
        if (!IsGenerationCurrent(expectedGeneration)) return;
        await StopManagerAsync(reason);
    }

    private bool IsGenerationCurrent(int expectedGeneration)
    {
        lock (gate) return expectedGeneration == generation;
    }

    private static async Task Ignore(Task task)
    {
        if (task == null) return;
        try { await task; } catch { }
    }
}
