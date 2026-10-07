using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
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
///
/// 该类只编排 Unity 主线程上的会话状态和 UI。信令客户端的网络 callback 全部先入
/// _mainQueue；每次启动都有独立 generation，旧连接或旧 peer 的迟到事件会被丢弃。
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
    private readonly ConcurrentQueue<Action> _mainQueue = new ConcurrentQueue<Action>();
    private RawImage[] _activePanels;   // 本次会话实际使用的面板组

    private int _sessionGeneration;
    private CancellationTokenSource _sessionCts;
    private bool _destroyed;
    private bool _videoFailureReported;
    private VideoStreamSessionBindings _bindings;
    private VideoStreamSessionProtocol _protocol;

    public SessionState CurrentState => _state;
    public int ActiveTrackCount => videoReceiver != null ? videoReceiver.ReceivedTrackCount : 0;

    private void Awake() => InitializeCameraPanels();

    /// <summary>Wires all presentation groups without starting signaling, peers, or cameras.</summary>
    public void InitializeCameraPanels()
    {
        InitializeCameraGroup(singlePanels);
        InitializeCameraGroup(dualPanels);
        InitializeCameraGroup(triPanels);
    }

    private static void InitializeCameraGroup(RawImage[] panels)
    {
        if (panels == null) return;
        foreach (RawImage panel in panels)
            if (panel != null) Doffy.UI.CameraPanelInteraction.Ensure(panel.rectTransform);
    }

    // ================================================================
    // 分辨率/控制指令 — 通过 DataChannel 替代 8005
    // ================================================================
    private readonly StringBuilder _ctrlSb = new StringBuilder(256);
    private string _lastCtrlMsg;
    private bool _resolutionDirty = true;

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
        if (!_resolutionDirty && msg == _lastCtrlMsg) return;

        // TrySendControlMessage 只有在 channel 真正 Open 并成功调用 Send 后才返回 true。
        // channel 尚未打开时保留 dirty，OnDataChannelOpened 会再次 flush。
        if (videoReceiver.TrySendControlMessage(msg))
        {
            _lastCtrlMsg = msg;
            _resolutionDirty = false;
        }
        else
        {
            _resolutionDirty = true;
        }
    }

    /// <summary>设置某个面板的分辨率标签。</summary>
    public void SetResolution(int panelIndex, string label)
    {
        if (resolutionLabels == null || panelIndex < 0 || panelIndex >= resolutionLabels.Length) return;
        resolutionLabels[panelIndex] = label;
        _resolutionDirty = true;
    }

    // ================================================================
    // 启动 / 停止
    // ================================================================
    public async Task<bool> StartVideoSession(string host, int signalingPort, string preset = "720p30")
    {
        if (_destroyed || _isStopping || _state != SessionState.Idle) return false;
        if (videoReceiver == null)
        {
            ReportVideoFailure("VideoReceiver missing");
            return false;
        }

        InitializeCameraPanels();

        int generation = ++_sessionGeneration;
        _sessionCts = new CancellationTokenSource();
        CancellationToken sessionToken = _sessionCts.Token;
        _videoFailureReported = false;
        _lastCtrlMsg = null;
        _resolutionDirty = true;

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

        int trackCount = Mathf.Max(1, _activePanels?.Length ?? numWebRTC);
        if (resolutionLabels == null || resolutionLabels.Length != trackCount)
        {
            resolutionLabels = new string[trackCount];
            for (int i = 0; i < resolutionLabels.Length; i++)
                resolutionLabels[i] = "x1.0";
        }

        _sessionId = Guid.NewGuid().ToString("N");
        VideoSignalingClient signaling = new VideoSignalingClient();
        _signaling = signaling;
        _state = SessionState.Connecting;
        _protocol = new VideoStreamSessionProtocol(
            signaling,
            videoReceiver,
            () => IsCurrent(generation, signaling),
            () => _sessionId,
            reason => Enqueue(() => FailForSession(reason, generation, signaling)),
            message => LogManager.Log("Video", message));
        _bindings = new VideoStreamSessionBindings(
            signaling,
            videoReceiver,
            Enqueue,
            env => HandleEnvelopeOnMain(env, generation, signaling),
            error => FailForSession(error, generation, signaling),
            () =>
            {
                if (IsCurrent(generation, signaling) && signaling.IsConnected &&
                    _state == SessionState.Connecting)
                    _state = SessionState.Connected;
            },
            () => FailForSession("Signaling disconnected", generation, signaling),
            sdp => BeginOfferSend(sdp, generation, signaling),
            (candidate, mid, index) => BeginIceSend(candidate, mid, index, generation, signaling),
            (index, texture) => OnTextureOnMain(index, texture, generation, signaling),
            message => OnControlMessageOnMain(message, generation, signaling),
            () =>
            {
                if (IsCurrent(generation, signaling)) SendResolutionControl();
            },
            error => FailForSession(error, generation, signaling),
            state => HandlePeerState(state, generation, signaling));

        // 必须在 InitializePeer 之前设置，否则 transceiver 数量仍按默认 1 创建。
        videoReceiver.ExpectedTrackCount = trackCount;
        videoReceiver.InitializePeer();

        LogManager.Log("Video", $"Connecting signaling ws://{host}:{signalingPort}");

        try
        {
            bool connected = await signaling.ConnectAsync(host, signalingPort, 4000);
            if (!connected || sessionToken.IsCancellationRequested ||
                !IsCurrent(generation, signaling))
            {
                await FailAndStopAsync("Signaling connect failed", generation, signaling);
                return false;
            }

            // OnConnected 是网络线程入队事件；若此时队列尚未处理，主线程仍可安全地
            // 从 Connecting 进入 Connected。旧代 callback 会因 IsCurrent 及状态检查被丢弃。
            if (_state == SessionState.Connecting)
                _state = SessionState.Connected;

            if (sessionToken.IsCancellationRequested || !IsCurrent(generation, signaling))
                return false;
            await signaling.SendAsync("hello", _sessionId,
                $"{{\"app_version\":\"{Application.version}\"," +
                $"\"video_preset\":\"{preset}\"," +
                $"\"track_count\":{trackCount}}}");
            if (sessionToken.IsCancellationRequested || !IsCurrent(generation, signaling))
                return false;

            await signaling.SendAsync("start_video", _sessionId, "{}");
            if (sessionToken.IsCancellationRequested || !IsCurrent(generation, signaling))
                return false;

            _state = SessionState.OfferSent;
            bool offerOk = await videoReceiver.CreateAndSendOfferAsync();
            if (sessionToken.IsCancellationRequested || !IsCurrent(generation, signaling))
                return false;
            if (!offerOk)
            {
                await FailAndStopAsync("Offer creation failed", generation, signaling);
                return false;
            }

            LogManager.Log("Video", $"Offer created, expecting {trackCount} tracks (mode={numWebRTC})...");
            return true;
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(generation, signaling))
                await FailAndStopAsync("Video session cancelled", generation, signaling);
            return false;
        }
        catch (Exception ex)
        {
            if (IsCurrent(generation, signaling))
                await FailAndStopAsync($"Video session failed: {ex.Message}", generation, signaling);
            return false;
        }
    }

    public async Task StopVideoSession(string reason = "user_stop")
    {
        if (_isStopping) return;

        _isStopping = true;
        _state = SessionState.Stopping;

        // 使旧 callback/协程立即失效，再执行可等待的 network/native 清理。
        ++_sessionGeneration;
        CancellationTokenSource sessionCts = _sessionCts;
        _sessionCts = null;
        try { sessionCts?.Cancel(); } catch { }

        VideoSignalingClient signaling = _signaling;
        _signaling = null;
        string sessionId = _sessionId;

        try
        {
            if (signaling != null && signaling.IsConnected)
            {
                await signaling.SendAsync("stop_video", sessionId,
                    new JObject { ["reason"] = reason ?? "user_stop" }
                        .ToString(Newtonsoft.Json.Formatting.None));
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        try
        {
            _bindings?.Dispose();
            _bindings = null;
            _protocol = null;
            if (videoReceiver != null)
                videoReceiver.ClosePeer();
            ClearPanels();

            if (signaling != null)
            {
                await signaling.DisconnectAsync();
                signaling.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            try { sessionCts?.Dispose(); } catch { }
            _sessionId = null;
            _lastCtrlMsg = null;
            _resolutionDirty = true;
            _state = SessionState.Idle;
            _isStopping = false;
            LogManager.Log("Video", $"Session stopped: {reason}");
        }
    }

    // Session callbacks are owned by VideoStreamSessionBindings.

    private void BeginOfferSend(string sdp, int generation, VideoSignalingClient signaling)
    {
        if (!IsCurrent(generation, signaling)) return;
        _ = _protocol?.SendOfferAsync(sdp);
    }

    private void BeginIceSend(
        string candidate, string sdpMid, int? index,
        int generation, VideoSignalingClient signaling)
    {
        if (!IsCurrent(generation, signaling)) return;
        _ = _protocol?.SendIceAsync(candidate, sdpMid, index);
    }

    private void OnTextureOnMain(
        int trackIndex, Texture texture, int generation, VideoSignalingClient signaling)
    {
        if (!IsCurrent(generation, signaling)) return;
        if (_activePanels != null && trackIndex >= 0 && trackIndex < _activePanels.Length &&
            _activePanels[trackIndex] != null)
            _activePanels[trackIndex].texture = texture;

        if (_state != SessionState.Playing)
        {
            _state = SessionState.Playing;
            LogManager.Log("Video", "First track playing");
        }
    }

    private void OnControlMessageOnMain(
        string message, int generation, VideoSignalingClient signaling)
    {
        if (IsCurrent(generation, signaling))
            LogManager.Log("Video", $"DataChannel control msg: {message}");
    }

    private void HandlePeerState(
        string state, int generation, VideoSignalingClient signaling)
    {
        if (!IsCurrent(generation, signaling) || string.IsNullOrEmpty(state)) return;
        if (state.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
            state.IndexOf("Disconnected", StringComparison.OrdinalIgnoreCase) >= 0 ||
            state.EndsWith("Closed", StringComparison.OrdinalIgnoreCase))
        {
            FailForSession($"Peer state: {state}", generation, signaling);
        }
    }

    // ================================================================
    // 信令信封分发
    // ================================================================
    private void HandleEnvelopeOnMain(
        VideoSignalingClient.Envelope env, int generation, VideoSignalingClient signaling)
    {
        if (!IsCurrent(generation, signaling) || env == null || env.SessionId != _sessionId)
            return;
        _ = _protocol?.HandleEnvelopeAsync(env);
    }

    // ================================================================
    // 主线程调度 & 辅助
    // ================================================================
    private void Update()
    {
        while (_mainQueue.TryDequeue(out Action action))
        {
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    private void Enqueue(Action action)
    {
        if (action != null && !_destroyed)
            _mainQueue.Enqueue(action);
    }

    private bool IsCurrent(int generation, VideoSignalingClient signaling)
    {
        return !_destroyed && !_isStopping && generation == _sessionGeneration &&
               ReferenceEquals(_signaling, signaling) && _state != SessionState.Idle;
    }

    private void ReportVideoFailure(string reason)
    {
        if (_videoFailureReported) return;
        _videoFailureReported = true;
        LogManager.Log("Video", $"FAIL: {reason}");
        // AppManager 隔离视频故障，遥操作状态由其自身生命周期继续管理。
        AppManager.Instance?.HandleVideoDisconnection(reason);
    }

    private async Task FailAndStopAsync(
        string reason, int generation, VideoSignalingClient signaling)
    {
        if (!IsCurrent(generation, signaling)) return;
        ReportVideoFailure(reason);
        try
        {
            await StopVideoSession("error");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void FailForSession(
        string reason, int generation, VideoSignalingClient signaling)
    {
        if (!IsCurrent(generation, signaling)) return;
        _ = FailAndStopAsync(reason ?? "Video signaling error", generation, signaling);
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

        foreach (RawImage panel in panels)
        {
            if (panel != null)
                panel.texture = null;
        }
    }

    private void OnDestroy()
    {
        _destroyed = true;
        ++_sessionGeneration;
        try { _sessionCts?.Cancel(); } catch { }
        try { _sessionCts?.Dispose(); } catch { }
        _sessionCts = null;

        VideoSignalingClient signaling = _signaling;
        _signaling = null;
        _bindings?.Dispose();
        _bindings = null;
        _protocol = null;
        if (videoReceiver != null)
            videoReceiver.ClosePeer();
        ClearPanels();
        try { signaling?.Dispose(); } catch (Exception ex) { Debug.LogException(ex); }
        _state = SessionState.Idle;
        _isStopping = false;
    }
}
