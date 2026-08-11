using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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
/// 依赖: Newtonsoft.Json（通过 Unity Package Manager 安装 com.unity.nuget.newtonsoft-json）
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

    private ClientWebSocket       _socket;
    private CancellationTokenSource _cts;
    private Task                  _receiveTask;

    public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;

    // ================================================================
    // 连接 / 断开
    // ================================================================
    public async Task<bool> ConnectAsync(string host, int port, int timeoutMs = 4000)
    {
        try
        {
            await DisconnectAsync();
            _cts    = new CancellationTokenSource();
            _socket = new ClientWebSocket();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            linkedCts.CancelAfter(timeoutMs);

            await _socket.ConnectAsync(new Uri($"ws://{host}:{port}"), linkedCts.Token);
            _receiveTask = ReceiveLoopAsync(_cts.Token);
            OnConnected?.Invoke();
            return true;
        }
        catch (OperationCanceledException)
        {
            OnError?.Invoke($"Signaling connect timed out: ws://{host}:{port}");
            return false;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Signaling connect failed: {ex.Message}");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_cts != null) { try { _cts.Cancel(); } catch { } _cts.Dispose(); _cts = null; }
        if (_socket != null)
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "close", CancellationToken.None);
            }
            catch { }
            _socket.Dispose();
            _socket = null;
        }
        if (_receiveTask != null) { try { await _receiveTask; } catch { } _receiveTask = null; }
        OnDisconnected?.Invoke();
    }

    public void Dispose() => _ = DisconnectAsync();

    // ================================================================
    // 发送
    // ================================================================
    public async Task SendAsync(string type, string sessionId, string payloadJson)
    {
        if (!IsConnected) return;
        var envelope = new JObject
        {
            ["type"]       = type ?? "",
            ["session_id"] = sessionId ?? "",
            ["payload"]    = SafeParse(payloadJson),
        };
        byte[] bytes = Encoding.UTF8.GetBytes(envelope.ToString(Newtonsoft.Json.Formatting.None));
        await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
    }

    // ================================================================
    // 接收循环
    // ================================================================
    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        byte[] buf = new byte[16 * 1024];
        var sb = new StringBuilder();

        while (!token.IsCancellationRequested && _socket != null)
        {
            WebSocketReceiveResult result;
            try { result = await _socket.ReceiveAsync(new ArraySegment<byte>(buf), token); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { OnError?.Invoke($"Receive failed: {ex.Message}"); break; }

            if (result.MessageType == WebSocketMessageType.Close) break;

            sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
            if (!result.EndOfMessage) continue;

            TryDispatch(sb.ToString());
            sb.Clear();
        }
    }

    private void TryDispatch(string raw)
    {
        try
        {
            var root = JObject.Parse(raw);
            OnEnvelope?.Invoke(new Envelope
            {
                Type        = root.Value<string>("type") ?? "",
                SessionId   = root.Value<string>("session_id") ?? "",
                PayloadJson = root["payload"]?.ToString() ?? "{}",
            });
        }
        catch (Exception ex) { OnError?.Invoke($"Invalid signaling JSON: {ex.Message}"); }
    }

    private static JToken SafeParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JObject();
        try { return JToken.Parse(json); } catch { return new JObject(); }
    }
}
