using System;
using System.Threading.Tasks;

/// <summary>
/// Coordinates the teleop state machine, reference frame and video lifetime.
/// AppManager remains a scene-facing facade while this class owns session
/// generation/cancellation decisions.
/// </summary>
public sealed class TeleopSessionCoordinator
{
    private readonly TeleopSessionStateMachine state = new TeleopSessionStateMachine();
    private readonly TeleopVideoSessionCoordinator video;
    private readonly Func<bool> webRtcEnabled;
    private readonly Func<bool> destroyed;
    private readonly Func<RecordingController> recorder;
    private readonly Action<string> log;
    private bool starting;
    private int streamGeneration;

    public TeleopSessionCoordinator(Func<VideoStreamManager> resolveVideo,
        Func<string> host, Func<int> signalingPort, Func<bool> webRtcEnabled,
        Func<bool> destroyed, Func<RecordingController> recorder,
        Action<string> log = null)
    {
        this.webRtcEnabled = webRtcEnabled ?? throw new ArgumentNullException(nameof(webRtcEnabled));
        this.destroyed = destroyed ?? throw new ArgumentNullException(nameof(destroyed));
        this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        this.log = log ?? (_ => { });
        video = new TeleopVideoSessionCoordinator(
            resolveVideo, host, signalingPort,
            generation => !this.destroyed() && generation == streamGeneration &&
                state.IsStreaming && this.webRtcEnabled(),
            message => this.log(message));
    }

    public TeleopSessionState State => state.State;
    public bool IsStreaming => state.IsStreaming;
    public bool NeedsRecalibration => state.NeedsRecalibration;
    public bool CanSend => state.CanSend;
    public string StatusText => state.StatusText;
    public string LastError => state.LastError;
    public TeleopSessionStateMachine.StatusKind Status => state.Status;
    public bool IsStarting => starting;
    public event Action Changed
    {
        add { state.Changed += value; }
        remove { state.Changed -= value; }
    }

    public void CompleteInitialStop()
    {
        state.CompleteStop();
    }

    public void ClearError() => state.ClearError();
    public void SetError(string message) => state.SetError(message);
    public void MarkConnectionReady() => state.MarkConnectionReady();
    public void MarkConnectionChanged() => state.MarkConnectionChanged();
    public void BeginVideoConnecting() => state.BeginVideoConnecting();
    public void MarkStreaming() => state.MarkStreaming();

    public void CompleteVideoRestart(bool success, string reason)
    {
        if (!IsStreaming || !webRtcEnabled() || state.NeedsRecalibration) return;
        if (success) state.MarkStreaming();
        else state.MarkVideoUnavailable(reason);
    }

    public async Task StartAsync(Func<bool> calibrate, Action onStarted = null)
    {
        if (starting || IsStreaming) return;
        starting = true;
        int generation = ++streamGeneration;
        try
        {
            state.BeginStart();
            onStarted?.Invoke();
            if (calibrate == null || !calibrate())
            {
                Stop("calibration_failed");
                state.SetError("Error: Reference calibration failed");
                return;
            }

            log("Stream started");
            if (webRtcEnabled())
            {
                state.BeginVideoConnecting();
                bool ok = await video.StartAsync(generation, "start");
                if (generation != streamGeneration || !IsStreaming || !webRtcEnabled()) return;
                if (ok) state.MarkStreaming();
                else state.MarkVideoUnavailable("signaling unavailable");
            }
            else
            {
                state.MarkStreaming();
            }
        }
        catch (Exception error)
        {
            if (generation == streamGeneration && !destroyed())
            {
                Stop();
                state.SetError("Error: " + error.Message);
            }
        }
        finally
        {
            if (generation == streamGeneration) starting = false;
        }
    }

    public void Stop(string reason = "user_stop")
    {
        if (!IsStreaming && State == TeleopSessionState.Idle && !starting) return;
        ++streamGeneration;
        starting = false;
        state.BeginStopping();
        _ = video.StopAsync(reason);
        recorder()?.StopRecording();
        TeleopReferenceFrame.Clear();
        state.CompleteStop();
        log("Streaming stopped: " + reason);
    }

    public void HandleDisconnected(string reason)
    {
        if (!IsStreaming) return;
        ++streamGeneration;
        starting = false;
        _ = video.StopAsync("disconnect");
        recorder()?.StopRecording();
        TeleopReferenceFrame.Clear();
        state.MarkDisconnected(reason);
        log("Disconnected: " + reason);
    }

    public void HandleVideoDisconnection(string reason)
    {
        if (!IsStreaming) return;
        state.MarkVideoIssue(reason);
        log("Video issue while teleop remains active: " + reason);
    }

    public void HandleTrackingLost(string reason, bool pauseControl)
    {
        state.MarkTrackingLost(reason, pauseControl);
        if (IsStreaming && pauseControl)
            log("Control paused: " + reason);
    }

    public void HandleTrackingAvailable(string reason)
    {
        state.MarkTrackingAvailable(reason);
    }

    public void Recalibrate(Func<bool> calibrate)
    {
        if (calibrate == null || !calibrate())
        {
            state.MarkRecalibrationFailed();
            return;
        }
        state.MarkRecalibrated(IsStreaming);
    }

    public Task<bool> RestartVideoAsync(string reason)
    {
        return video.StartAsync(streamGeneration, reason);
    }

    public Task StopVideoAsync(string reason)
    {
        return video.StopAsync(reason);
    }

    public void CancelVideo()
    {
        video.Cancel();
    }
}
