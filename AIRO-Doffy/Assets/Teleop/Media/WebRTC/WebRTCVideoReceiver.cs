using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

/// <summary>
/// WebRTC PeerConnection 封装 —— 单连接接收多条远端视频轨道 + DataChannel。
///
/// 方案一架构: 一个 PeerConnection 承载多个 VideoTrack，
/// 每条 Track 对应一个摄像头（正面/侧面/手部特写等），
/// 通过 trackIndex 区分。同时内建一条 DataChannel 用于
/// 替代原有 8005 端口的分辨率/控制指令双向通信。
///
/// PeerConnection、轨道和异步操作都带有会话代际。重启或销毁时先让旧代失效，
/// 再释放 native 对象；旧协程和旧 WebRTC callback 因而不能触碰新 peer。
/// </summary>
public class WebRTCVideoReceiver : MonoBehaviour
{
    // ================================================================
    // 事件
    // ================================================================
    /// <summary>本地 SDP offer 生成完毕。</summary>
    public event Action<string> OnLocalOfferReady;

    /// <summary>本地 ICE candidate 产生。</summary>
    public event Action<string, string, int?> OnLocalIceCandidate;

    /// <summary>收到远端视频纹理 (trackIndex, texture)。</summary>
    public event Action<int, Texture> OnRemoteTexture;

    /// <summary>PeerConnection 状态变化。</summary>
    public event Action<string> OnPeerStateChanged;

    /// <summary>DataChannel 收到控制消息。</summary>
    public event Action<string> OnDataChannelMessage;

    /// <summary>DataChannel 进入 Open 状态，可用于刷新待发送控制消息。</summary>
    public event Action OnDataChannelOpened;

    /// <summary>出错。</summary>
    public event Action<string> OnError;

    // ================================================================
    // Inspector
    // ================================================================
    [Header("期望接收的视频轨道数量")]
    [Tooltip("对应 PC 端 addTrack() 的次数，每条轨道映射到一个摄像头")]
    private int expectedTrackCount = 1;

    public int ExpectedTrackCount
    {
        get => expectedTrackCount;
        set => expectedTrackCount = Mathf.Max(1, value);
    }

    // ================================================================
    // 内部状态
    // ================================================================
    private readonly object _stateLock = new object();
    private readonly HashSet<TaskCompletionSource<bool>> _pendingOperations =
        new HashSet<TaskCompletionSource<bool>>();
    private readonly WebRTCReceiverMediaState _media = new WebRTCReceiverMediaState();

    private RTCPeerConnection _peer;
    private Coroutine _updateCo;
    private bool _updateRunning;
    private int _generation;
    private bool _destroyed;

    public int ReceivedTrackCount => _media.TrackCount;

    // ================================================================
    // 公开 API
    // ================================================================
    public void InitializePeer()
    {
        lock (_stateLock)
        {
            if (_destroyed)
                return;
        }

        EnsureWebRtcUpdate();
        ClosePeer();

        RTCPeerConnection peer;
        int generation;
        try
        {
            peer = new RTCPeerConnection();
        }
        catch (Exception ex)
        {
            RaiseError($"PeerConnection creation failed: {ex.Message}");
            return;
        }

        lock (_stateLock)
        {
            if (_destroyed)
            {
                try { peer.Dispose(); } catch { }
                return;
            }

            _peer = peer;
            generation = _generation;
        }

        try
        {
            // 先挂 callback，再添加 transceiver，保证 native 状态变化不会丢给当前代。
            lock (_stateLock)
            {
                if (!IsCurrentLocked(peer, generation)) return;
                WebRTCPeerFactory.Configure(
                    peer,
                    ExpectedTrackCount,
                    state =>
                    {
                        if (IsCurrent(peer, generation)) RaisePeerStateChanged(state.ToString());
                    },
                    state =>
                    {
                        if (IsCurrent(peer, generation)) RaisePeerStateChanged($"ICE {state}");
                    },
                    candidate =>
                    {
                        if (!IsCurrent(peer, generation) || candidate == null) return;
                        try { RaiseLocalIceCandidate(candidate.Candidate, candidate.SdpMid, candidate.SdpMLineIndex); }
                        catch (Exception ex) { RaiseErrorIfCurrent(peer, generation, $"ICE callback failed: {ex.Message}"); }
                    },
                    trackEvent => _media.HandleTrack(
                        trackEvent,
                        () => IsCurrent(peer, generation),
                        RaiseRemoteTexture,
                        state => RaisePeerStateIfCurrent(peer, generation, state)),
                    channel => _media.HandleDataChannel(
                        channel,
                        () => IsCurrent(peer, generation),
                        RaiseDataChannelMessage,
                        RaiseDataChannelOpened,
                        () => { },
                        error => RaiseErrorIfCurrent(peer, generation, error),
                        state => RaisePeerStateIfCurrent(peer, generation, state)));
            }
        }
        catch (Exception ex)
        {
            RaiseErrorIfCurrent(peer, generation, $"AddTransceiver failed: {ex.Message}");
            ClosePeer();
        }
    }

    /// <summary>通过 DataChannel 发送控制指令（替代原 8005 UDP）。</summary>
    public void SendControlMessage(string message)
    {
        TrySendControlMessage(message);
    }

    /// <summary>
    /// 尝试发送控制指令。只有 native channel 确认处于 Open 状态后才返回 true，
    /// 调用方可据此保留待发送的初始/重连控制消息。
    /// </summary>
    public bool TrySendControlMessage(string message)
    {
        if (message == null)
            return false;

        lock (_stateLock)
        {
            if (_destroyed || _peer == null)
                return false;
        }
        return _media.TrySend(message, RaiseError);
    }

    public Task<bool> CreateAndSendOfferAsync()
    {
        TaskCompletionSource<bool> tcs = NewOperation();
        if (tcs == null)
            return Task.FromResult(false);

        RTCPeerConnection peer;
        int generation;
        lock (_stateLock)
        {
            peer = _peer;
            generation = _generation;
            if (_destroyed || peer == null)
            {
                RemoveOperation(tcs);
                tcs.TrySetResult(false);
                return tcs.Task;
            }
            _pendingOperations.Add(tcs);
        }

        try
        {
            StartCoroutine(WebRTCSessionOperations.CreateOffer(
                peer,
                () => IsCurrent(peer, generation),
                RaiseLocalOfferReady,
                error => RaiseErrorIfCurrent(peer, generation, error),
                result => CompleteOperation(tcs, result)));
        }
        catch (Exception ex)
        {
            RaiseErrorIfCurrent(peer, generation, $"CreateOffer scheduling failed: {ex.Message}");
            CompleteOperation(tcs, false);
        }
        return tcs.Task;
    }

    public Task<bool> SetRemoteAnswerAsync(string sdp)
    {
        TaskCompletionSource<bool> tcs = NewOperation();
        if (tcs == null)
            return Task.FromResult(false);

        RTCPeerConnection peer;
        int generation;
        lock (_stateLock)
        {
            peer = _peer;
            generation = _generation;
            if (_destroyed || peer == null || string.IsNullOrWhiteSpace(sdp))
            {
                RemoveOperation(tcs);
                tcs.TrySetResult(false);
                return tcs.Task;
            }
            _pendingOperations.Add(tcs);
        }

        try
        {
            StartCoroutine(WebRTCSessionOperations.SetAnswer(
                peer,
                sdp,
                () => IsCurrent(peer, generation),
                error => RaiseErrorIfCurrent(peer, generation, error),
                result => CompleteOperation(tcs, result)));
        }
        catch (Exception ex)
        {
            RaiseErrorIfCurrent(peer, generation, $"SetRemote scheduling failed: {ex.Message}");
            CompleteOperation(tcs, false);
        }
        return tcs.Task;
    }

    public void AddRemoteIceCandidate(string candidate, string sdpMid, int? sdpMLineIndex)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return;

        RTCPeerConnection peer;
        int generation;
        lock (_stateLock)
        {
            peer = _peer;
            generation = _generation;
            if (_destroyed || peer == null)
                return;
        }

        RTCIceCandidate iceCandidate = null;
        try
        {
            if (!IsCurrent(peer, generation))
                return;

            iceCandidate = new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = candidate,
                sdpMid = sdpMid,
                sdpMLineIndex = sdpMLineIndex ?? 0,
            });
            if (IsCurrent(peer, generation))
                peer.AddIceCandidate(iceCandidate);
        }
        catch (Exception ex)
        {
            RaiseErrorIfCurrent(peer, generation, $"AddIceCandidate failed: {ex.Message}");
        }
        finally
        {
            try { iceCandidate?.Dispose(); } catch { }
        }
    }

    public void ClosePeer()
    {
        RTCPeerConnection peer;
        List<TaskCompletionSource<bool>> pending;

        lock (_stateLock)
        {
            ++_generation;
            peer = _peer;
            pending = new List<TaskCompletionSource<bool>>(_pendingOperations);

            _peer = null;
            _pendingOperations.Clear();
        }

        foreach (TaskCompletionSource<bool> operation in pending)
            operation.TrySetResult(false);

        // 先断开 peer callback，旧 native 事件即使排队也会通过代际检查丢弃。
        if (peer != null)
        {
            try { peer.OnConnectionStateChange = null; } catch { }
            try { peer.OnIceConnectionChange = null; } catch { }
            try { peer.OnIceCandidate = null; } catch { }
            try { peer.OnTrack = null; } catch { }
            try { peer.OnDataChannel = null; } catch { }
        }

        _media.Dispose();

        if (peer != null)
        {
            try { peer.Close(); } catch { }
            try { peer.Dispose(); } catch { }
        }
    }

    // ================================================================
    // 协程在 WebRTCSessionOperations 中运行；receiver 只负责代际判断和完成 TCS。
    // ================================================================

    // ================================================================
    // 代际、操作和事件安全边界
    // ================================================================
    private void EnsureWebRtcUpdate()
    {
        lock (_stateLock)
        {
            if (_destroyed || _updateRunning)
                return;
        }

        try
        {
            Coroutine update = StartCoroutine(WebRTC.Update());
            lock (_stateLock)
            {
                if (_destroyed)
                {
                    StopCoroutine(update);
                    return;
                }
                _updateCo = update;
                _updateRunning = true;
            }
        }
        catch (Exception ex)
        {
            RaiseError($"WebRTC.Update scheduling failed: {ex.Message}");
        }
    }

    private TaskCompletionSource<bool> NewOperation()
    {
        lock (_stateLock)
        {
            if (_destroyed)
                return null;
            return new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void RemoveOperation(TaskCompletionSource<bool> operation)
    {
        lock (_stateLock)
            _pendingOperations.Remove(operation);
    }

    private void CompleteOperation(TaskCompletionSource<bool> operation, bool result)
    {
        if (operation == null)
            return;
        RemoveOperation(operation);
        operation.TrySetResult(result);
    }

    private bool IsCurrent(RTCPeerConnection peer, int generation)
    {
        lock (_stateLock)
            return IsCurrentLocked(peer, generation);
    }

    private bool IsCurrentLocked(RTCPeerConnection peer, int generation)
    {
        return !_destroyed && generation == _generation && ReferenceEquals(_peer, peer);
    }

    private void RaiseErrorIfCurrent(
        RTCPeerConnection peer, int generation, string message)
    {
        if (IsCurrent(peer, generation))
            RaiseError(message);
    }

    private void RaisePeerStateIfCurrent(
        RTCPeerConnection peer, int generation, string state)
    {
        if (IsCurrent(peer, generation))
            RaisePeerStateChanged(state);
    }

    private void RaiseLocalOfferReady(string sdp) => Raise(OnLocalOfferReady, sdp);
    private void RaiseLocalIceCandidate(string candidate, string sdpMid, int? index) =>
        Raise(OnLocalIceCandidate, candidate, sdpMid, index);
    private void RaiseRemoteTexture(int index, Texture texture) => Raise(OnRemoteTexture, index, texture);
    private void RaisePeerStateChanged(string state) => Raise(OnPeerStateChanged, state);
    private void RaiseDataChannelMessage(string message) => Raise(OnDataChannelMessage, message);
    private void RaiseDataChannelOpened() => Raise(OnDataChannelOpened);
    private void RaiseError(string message) => Raise(OnError, message);

    private static void Raise(Action callback)
    {
        try { callback?.Invoke(); } catch (Exception ex) { Debug.LogException(ex); }
    }

    private static void Raise<T>(Action<T> callback, T value)
    {
        try { callback?.Invoke(value); } catch (Exception ex) { Debug.LogException(ex); }
    }

    private static void Raise<T1, T2>(Action<T1, T2> callback, T1 a, T2 b)
    {
        try { callback?.Invoke(a, b); } catch (Exception ex) { Debug.LogException(ex); }
    }

    private static void Raise<T1, T2, T3>(Action<T1, T2, T3> callback, T1 a, T2 b, T3 c)
    {
        try { callback?.Invoke(a, b, c); } catch (Exception ex) { Debug.LogException(ex); }
    }

    private void OnDestroy()
    {
        lock (_stateLock)
            _destroyed = true;

        ClosePeer();
        StopAllCoroutines();
        lock (_stateLock)
        {
            _updateRunning = false;
            _updateCo = null;
        }
    }
}
