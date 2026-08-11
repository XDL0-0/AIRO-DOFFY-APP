using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// WebRTC 多路视频会话管理器。
///
/// 单 PeerConnection + 多 VideoTrack 方案:
///   PC 端每 addTrack(cam) 一次，这里按 trackIndex 分配到对应 Renderer。
///   同时内建 DataChannel 替代原有 8005 端口的分辨率/控制指令通信。
///
/// Inspector 设置:
///   videoPanels[]  — 拖入多个 RawImage UI 组件，
///                    顺序对应 PC 端 addTrack 的顺序。
///
/// 端口映射对照:
///   旧 8000/8002/8004 (UDP JPEG)  →  videoPanels[0] / [1] / [2] (WebRTC Track)
///   旧 8005 (UDP 控制指令)          →  DataChannel "control"
/// </summary>
public class VideoStreamManager : MonoBehaviour
{
    public enum SessionState { Idle, Connecting, OfferSent, Connected, Playing, Stopping }

    [Header("引用")]
    [SerializeField] private WebRTCVideoReceiver videoReceiver;

    [Header("视频面板 — Single (1路)")]
    [SerializeField] private RawImage[] singlePanels;   // 1 个 RawImage

    [Header("视频面板 — Dual (2路)")]
    [SerializeField] private RawImage[] dualPanels;     // 2 个 RawImage

    [Header("视频面板 — Tri (3路)")]
    [SerializeField] private RawImage[] triPanels;      // 3 个 RawImage

    [Header("分辨率控制")]
    [Tooltip("每个面板对应的分辨率倍数标签")]
    [SerializeField] private string[] resolutionLabels;

    private VideoSignalingClient _signaling;
    private SessionState _state = SessionState.Idle;
    private string _sessionId;
    private bool _isStopping;
    private readonly ConcurrentQueue<Action> _mainQueue = new();
    private RawImage[] _activePanels;   // 本次会话实际使用的面板组

    public SessionState CurrentState => _state;
    public int ActiveTrackCount => videoReceiver != null ? videoReceiver.ReceivedTrackCount : 0;

    // ================================================================
    // 分辨率/控制指令 — 通过 DataChannel 替代 8005
    // ================================================================
    private StringBuilder _ctrlSb = new StringBuilder(256);
    private string _lastCtrlMsg;

    /// <summary>
    /// 发送分辨率控制指令（格式兼容原版 UdpWindowManager.Resolution_loop）。
    /// 示例: "0,x1.0;1,x1.5;2,x2.0;Fine Control Mode,OFF;"
    /// </summary>
    public void SendResolutionControl(string focusModeLabel = "Fine Control Mode,OFF")
    {
        if (videoReceiver == null) return;

        _ctrlSb.Clear();
        int panelCount = _activePanels?.Length ?? 0;
        for (int i = 0; i < panelCount; i++)
        {
            string label = (resolutionLabels != null && i < resolutionLabels.Length)
                ? resolutionLabels[i] : "x1.0";
            _ctrlSb.Append(i).Append(',').Append(label).Append(';');
        }
        _ctrlSb.Append(focusModeLabel).Append(';');

        string msg = _ctrlSb.ToString();
        if (msg == _lastCtrlMsg) return;
        _lastCtrlMsg = msg;

        videoReceiver.SendControlMessage(msg);
    }

    /// <summary>设置某个面板的分辨率标签。</summary>
    public void SetResolution(int panelIndex, string label)
    {
        if (resolutionLabels == null || panelIndex < 0 || panelIndex >= resolutionLabels.Length) return;
        resolutionLabels[panelIndex] = label;
    }

    // ================================================================
    // 启动 / 停止
    // ================================================================
    public async Task<bool> StartVideoSession(string host, int signalingPort, string preset = "720p30")
    {
        if (_state != SessionState.Idle) return false;
        if (videoReceiver == null) { Fail("VideoReceiver missing"); return false; }

        // 根据 num_WebRTC 选择对应面板组
        int numWebRTC = AppManager.Instance != null
            ? Mathf.Clamp(AppManager.Instance.NumWebRTC, 1, 3)
            : 1;

        _activePanels = numWebRTC switch
        {
            1 => singlePanels,
            2 => dualPanels,
            3 => triPanels,
            _ => singlePanels
        };

        int trackCount = _activePanels?.Length ?? numWebRTC;

        if (resolutionLabels == null || resolutionLabels.Length != trackCount)
        {
            resolutionLabels = new string[trackCount];
            for (int i = 0; i < resolutionLabels.Length; i++)
                resolutionLabels[i] = "x1.0";
        }

        _sessionId = Guid.NewGuid().ToString("N");
        _signaling = new VideoSignalingClient();
        _signaling.OnEnvelope  += env => Enqueue(() => HandleEnvelope(env));
        _signaling.OnError     += err => Enqueue(() => Fail(err));
        _signaling.OnConnected += ()  => Enqueue(() => _state = SessionState.Connected);

        _state = SessionState.Connecting;
        LogManager.Log("Video", $"Connecting signaling ws://{host}:{signalingPort}");

        bool ok = await _signaling.ConnectAsync(host, signalingPort, 4000);
        if (!ok) { Fail("Signaling connect failed"); return false; }

        videoReceiver.InitializePeer();
        videoReceiver.OnLocalOfferReady   += OnOfferReady;
        videoReceiver.OnLocalIceCandidate += OnIceCandidate;
        videoReceiver.OnRemoteTexture     += OnTexture;
        videoReceiver.OnDataChannelMessage += OnControlMessage;
        videoReceiver.OnError             += Fail;

        await _signaling.SendAsync("hello", _sessionId,
            $"{{\"app_version\":\"{Application.version}\"," +
            $"\"video_preset\":\"{preset}\"," +
            $"\"track_count\":{trackCount}}}");
        await _signaling.SendAsync("start_video", _sessionId, "{}");

        _state = SessionState.OfferSent;
        bool offerOk = await videoReceiver.CreateAndSendOfferAsync();
        if (!offerOk) { Fail("Offer creation failed"); return false; }

        LogManager.Log("Video", $"Offer created, expecting {trackCount} tracks (mode={numWebRTC})...");
        return true;
    }

    public async Task StopVideoSession(string reason = "user_stop")
    {
        if (_isStopping) return;
        _isStopping = true;
        _state = SessionState.Stopping;

        try
        {
            if (_signaling != null && _signaling.IsConnected)
                await _signaling.SendAsync("stop_video", _sessionId,
                    new JObject { ["reason"] = reason }.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch { }

        if (videoReceiver != null)
        {
            videoReceiver.OnLocalOfferReady   -= OnOfferReady;
            videoReceiver.OnLocalIceCandidate -= OnIceCandidate;
            videoReceiver.OnRemoteTexture     -= OnTexture;
            videoReceiver.OnDataChannelMessage -= OnControlMessage;
            videoReceiver.OnError             -= Fail;
            videoReceiver.ClosePeer();
        }

        ClearPanels();
        _signaling?.Dispose();
        _signaling = null;
        _state = SessionState.Idle;
        _isStopping = false;
        LogManager.Log("Video", $"Session stopped: {reason}");
    }

    // ================================================================
    // 回调
    // ================================================================
    private async void OnOfferReady(string sdp)
    {
        if (_signaling == null) return;
        await _signaling.SendAsync("offer", _sessionId,
            new JObject { ["sdp"] = sdp }.ToString(Newtonsoft.Json.Formatting.None));
        LogManager.Log("Video", "Offer sent to host");
    }

    private async void OnIceCandidate(string candidate, string sdpMid, int? idx)
    {
        if (_signaling == null) return;
        var payload = new JObject
        {
            ["candidate"]     = candidate ?? "",
            ["sdpMid"]        = sdpMid,
            ["sdpMLineIndex"] = idx.HasValue ? idx.Value : JValue.CreateNull(),
        };
        await _signaling.SendAsync("ice_candidate", _sessionId,
            payload.ToString(Newtonsoft.Json.Formatting.None));
    }

    private void OnTexture(int trackIndex, Texture tex)
    {
        // OnVideoReceived 由 Unity.WebRTC 内部线程触发，必须派发到主线程操作 UI
        Enqueue(() =>
        {
            if (_activePanels != null && trackIndex < _activePanels.Length && _activePanels[trackIndex] != null)
                _activePanels[trackIndex].texture = tex;

            if (_state != SessionState.Playing)
            {
                _state = SessionState.Playing;
                LogManager.Log("Video", "First track playing");
            }
        });
    }

    private void OnControlMessage(string msg)
    {
        LogManager.Log("Video", $"DataChannel control msg: {msg}");
    }

    // ================================================================
    // 信令信封分发
    // ================================================================
    private async void HandleEnvelope(VideoSignalingClient.Envelope env)
    {
        if (env.SessionId != _sessionId) return;

        switch (env.Type)
        {
            case "answer":
                var root = JObject.Parse(env.PayloadJson);
                string sdp = root.Value<string>("sdp");
                if (!string.IsNullOrWhiteSpace(sdp))
                {
                    bool ok = await videoReceiver.SetRemoteAnswerAsync(sdp);
                    if (!ok) Fail("SetRemoteAnswer failed");
                    else LogManager.Log("Video", "Answer applied");
                }
                break;

            case "ice_candidate":
                var ice = JObject.Parse(env.PayloadJson);
                videoReceiver.AddRemoteIceCandidate(
                    ice.Value<string>("candidate"),
                    ice.Value<string>("sdpMid"),
                    ice["sdpMLineIndex"]?.Type == JTokenType.Integer ? ice.Value<int>("sdpMLineIndex") : null
                );
                break;

            case "hello_ack":
                LogManager.Log("Video", "hello_ack received");
                break;

            case "error":
                Fail(JObject.Parse(env.PayloadJson).Value<string>("message") ?? "Server error");
                break;
        }
    }

    // ================================================================
    // 主线程调度 & 辅助
    // ================================================================
    private void Update()
    {
        while (_mainQueue.TryDequeue(out Action action))
            try { action?.Invoke(); } catch { }
    }

    private void Enqueue(Action action) { if (action != null) _mainQueue.Enqueue(action); }

    private async void Fail(string reason)
    {
        LogManager.Log("Video", $"FAIL: {reason}");
        if (_state != SessionState.Idle)
            await StopVideoSession("error");
        AppManager.Instance?.HandleVideoDisconnection(reason);
    }

    private void ClearPanels()
    {
        ClearPanelGroup(singlePanels);
        ClearPanelGroup(dualPanels);
        ClearPanelGroup(triPanels);
        _activePanels = null;
    }

    private void ClearPanelGroup(RawImage[] panels)
    {
        if (panels == null) return;

        foreach (var panel in panels)
        {
            if (panel != null)
                panel.texture = null;
        }
    }
}
