using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// WebSocket 信令客户端 —— 用于 WebRTC 视频流的 SDP/ICE 交换。
///
/// 协议: JSON 信封
/// {
///   "type":       "offer" | "answer" | "ice_candidate" | "hello" | ...,
///   "session_id": "...",
///   "payload":    { ... }
/// }
///
/// 依赖: Newtonsoft.Json（通过 Unity Package Manager 安装
/// com.unity.nuget.newtonsoft-json）。
///
/// 生命周期和 WebSocket I/O 集中在这里，调用方只需要订阅公开事件。发送通过
/// 单一 gate 串行化；接收按完整 WebSocket message 解码，避免 fragment 边界破坏
/// UTF-8 字符或让异常大的服务端消息占满内存。
/// </summary>
public sealed class VideoSignalingClient : IDisposable
{
    public sealed class Envelope
    {
        public string Type;
        public string SessionId;
        public string PayloadJson;
    }

    public event Action<Envelope> OnEnvelope;
    public event Action<string>   OnError;
    public event Action           OnConnected;
    public event Action           OnDisconnected;

    private readonly object _stateLock = new object();
    private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
    private readonly WebSocketMessageReader _messageReader = new WebSocketMessageReader();

    private ClientWebSocket _socket;
    private CancellationTokenSource _cts;
    private Task _receiveTask;
    private int _generation;
    private bool _connected;
    private bool _disposed;

    public bool IsConnected
    {
        get
        {
            lock (_stateLock)
                return _connected && _socket != null && _socket.State == WebSocketState.Open;
        }
    }

    // ================================================================
    // 连接 / 断开
    // ================================================================
    public async Task<bool> ConnectAsync(string host, int port, int timeoutMs = 4000)
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    RaiseError("Signaling client is disposed");
                    return false;
                }
            }

            // 同一个实例可以安全地重连。该操作和外部 DisconnectAsync 共用 gate，
            // 因此不会出现新连接被旧连接清理掉的情况。
            await DisconnectCoreAsync().ConfigureAwait(false);

            ClientWebSocket socket = new ClientWebSocket();
            CancellationTokenSource cts = new CancellationTokenSource();
            int generation;
            lock (_stateLock)
            {
                if (_disposed)
                {
                    cts.Dispose();
                    socket.Dispose();
                    RaiseError("Signaling client is disposed");
                    return false;
                }

                generation = ++_generation;
                _socket = socket;
                _cts = cts;
                _receiveTask = null;
                _connected = false;
            }

            try
            {
                using (CancellationTokenSource timeoutCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                {
                    timeoutCts.CancelAfter(Math.Max(1, timeoutMs));
                    await socket.ConnectAsync(
                        new Uri($"ws://{host}:{port}"), timeoutCts.Token).ConfigureAwait(false);
                }

                Task receiveTask = ReceiveLoopAsync(socket, cts, generation);
                lock (_stateLock)
                {
                    if (!IsCurrentLocked(socket, cts, generation) || _disposed)
                    {
                        // A receive failure or Dispose may have invalidated this attempt while
                        // ConnectAsync was completing.
                        cts.Cancel();
                    }
                    else
                    {
                        _receiveTask = receiveTask;
                        _connected = true;
                    }
                }

                if (!IsCurrent(socket, cts, generation) || !IsConnected)
                {
                    await DisconnectCoreAsync().ConfigureAwait(false);
                    return false;
                }

                RaiseConnected();
                return true;
            }
            catch (OperationCanceledException)
            {
                if (IsCurrent(socket, cts, generation))
                    await DisconnectCoreAsync().ConfigureAwait(false);
                RaiseError($"Signaling connect timed out: ws://{host}:{port}");
                return false;
            }
            catch (Exception ex)
            {
                if (IsCurrent(socket, cts, generation))
                    await DisconnectCoreAsync().ConfigureAwait(false);
                RaiseError($"Signaling connect failed: {ex.Message}");
                return false;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Dispose 同步等待清理完成，避免旧实现的 fire-and-forget DisconnectAsync 产生
    /// 未观察异常或在新会话中继续使用已经替换的 socket。
    /// </summary>
    public void Dispose()
    {
        lock (_stateLock)
            _disposed = true;

        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Dispose 不应把网络清理异常抛到 Unity 生命周期回调；记录后保证资源已
            // 从客户端状态中摘除。
            Debug.LogException(ex);
        }
    }

    private async Task DisconnectCoreAsync()
    {
        ClientWebSocket socket;
        CancellationTokenSource cts;
        Task receiveTask;
        bool wasConnected;

        lock (_stateLock)
        {
            socket = _socket;
            cts = _cts;
            receiveTask = _receiveTask;
            wasConnected = _connected;

            _socket = null;
            _cts = null;
            _receiveTask = null;
            _connected = false;
            ++_generation;
        }

        if (socket == null && cts == null && receiveTask == null)
            return;

        try { cts?.Cancel(); } catch { }

        // 先等待当前发送者退出，再关闭 socket。这样不会并发调用 ClientWebSocket 的
        // SendAsync/CloseAsync，也不会让旧发送任务拿到新连接的 token/socket。
        try
        {
            await _sendGate.WaitAsync().ConfigureAwait(false);
            _sendGate.Release();
        }
        catch (Exception ex)
        {
            RaiseError($"Signaling send cleanup failed: {ex.Message}");
        }

        try
        {
            if (socket != null &&
                (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived))
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "close", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch { }

        // ReceiveLoop 的 finally 会自己摘除匹配代际。若清理由 receive loop 内部触发，
        // 不能等待自身，否则会死锁。
        if (receiveTask != null &&
            (!Task.CurrentId.HasValue || Task.CurrentId.Value != receiveTask.Id))
        {
            try { await receiveTask.ConfigureAwait(false); } catch { }
        }

        try { socket?.Dispose(); } catch { }
        try { cts?.Dispose(); } catch { }

        if (wasConnected)
            RaiseDisconnected();
    }

    private bool IsCurrent(ClientWebSocket socket, CancellationTokenSource cts, int generation)
    {
        lock (_stateLock)
            return IsCurrentLocked(socket, cts, generation);
    }

    private bool IsCurrentLocked(
        ClientWebSocket socket, CancellationTokenSource cts, int generation)
    {
        return !_disposed &&
               generation == _generation &&
               ReferenceEquals(_socket, socket) &&
               ReferenceEquals(_cts, cts);
    }

    // ================================================================
    // 发送
    // ================================================================
    public async Task SendAsync(string type, string sessionId, string payloadJson)
    {
        byte[] bytes = SignalingEnvelopeCodec.Encode(type, sessionId, payloadJson);

        ClientWebSocket socket;
        CancellationTokenSource cts;
        int generation;
        lock (_stateLock)
        {
            socket = _socket;
            cts = _cts;
            generation = _generation;
            if (_disposed || !_connected || socket == null || cts == null ||
                socket.State != WebSocketState.Open)
                return;
        }

        await _sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (!IsCurrentLocked(socket, cts, generation) ||
                    !_connected || socket.State != WebSocketState.Open)
                    return;
            }

            await socket.SendAsync(
                new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Disconnect/close is an expected cancellation path.
        }
        catch (Exception ex)
        {
            if (IsCurrent(socket, cts, generation))
                RaiseError($"Signaling send failed: {ex.Message}");
        }
        finally
        {
            _sendGate.Release();
        }
    }

    // ================================================================
    // 接收循环
    // ================================================================
    private async Task ReceiveLoopAsync(
        ClientWebSocket socket, CancellationTokenSource cts, int generation)
    {
        CancellationToken token = cts.Token;
        try
        {
            while (!token.IsCancellationRequested && IsCurrent(socket, cts, generation))
            {
                string raw = await _messageReader.ReadTextAsync(socket, token)
                    .ConfigureAwait(false);
                if (raw == null) return;
                TryDispatch(raw);
            }
        }
        catch (OperationCanceledException)
        {
            // Disconnect and replacement connections cancel the old receive loop.
        }
        catch (Exception ex)
        {
            if (IsCurrent(socket, cts, generation))
                RaiseError($"Receive failed: {ex.Message}");
        }
        finally
        {
            FinishReceive(socket, generation);
        }
    }

    private void FinishReceive(ClientWebSocket socket, int generation)
    {
        bool wasConnected = false;
        CancellationTokenSource cts = null;
        lock (_stateLock)
        {
            if (ReferenceEquals(_socket, socket) && generation == _generation)
            {
                wasConnected = _connected;
                cts = _cts;
                _socket = null;
                _cts = null;
                _receiveTask = null;
                _connected = false;
                ++_generation;
            }
        }

        try { socket.Dispose(); } catch { }
        try { cts?.Dispose(); } catch { }
        if (wasConnected)
            RaiseDisconnected();
    }

    private void TryDispatch(string raw)
    {
        try
        {
            RaiseEnvelope(SignalingEnvelopeCodec.Decode(raw));
        }
        catch (Exception ex)
        {
            RaiseError($"Invalid signaling JSON: {ex.Message}");
        }
    }

    // ================================================================
    // 事件安全边界
    // ================================================================
    private void RaiseEnvelope(Envelope envelope)
    {
        try { OnEnvelope?.Invoke(envelope); }
        catch (Exception ex) { Debug.LogException(ex); }
    }

    private void RaiseError(string message)
    {
        try { OnError?.Invoke(message); }
        catch (Exception ex) { Debug.LogException(ex); }
    }

    private void RaiseConnected()
    {
        try { OnConnected?.Invoke(); }
        catch (Exception ex) { Debug.LogException(ex); }
    }

    private void RaiseDisconnected()
    {
        try { OnDisconnected?.Invoke(); }
        catch (Exception ex) { Debug.LogException(ex); }
    }
}
