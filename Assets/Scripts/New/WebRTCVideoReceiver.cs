using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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
/// 依赖: com.unity.webrtc (Unity Package Manager)
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
    private RTCPeerConnection _peer;
    private readonly List<VideoStreamTrack> _remoteTracks = new();
    private RTCDataChannel _dataChannel;
    private Coroutine _updateCo;
    private bool _updateRunning;

    public int ReceivedTrackCount => _remoteTracks.Count;

    // ================================================================
    // 公开 API
    // ================================================================
    public void InitializePeer()
    {
        if (!_updateRunning)
        {
            _updateCo = StartCoroutine(WebRTC.Update());
            _updateRunning = true;
        }

        ClosePeer();
        _remoteTracks.Clear();

        _peer = new RTCPeerConnection();

        for (int i = 0; i < expectedTrackCount; i++)
            TryAddVideoRecvTransceiver();

        _peer.OnConnectionStateChange = s => OnPeerStateChanged?.Invoke(s.ToString());
        _peer.OnIceConnectionChange   = s => OnPeerStateChanged?.Invoke($"ICE {s}");

        _peer.OnIceCandidate = c =>
        {
            if (c != null)
                OnLocalIceCandidate?.Invoke(c.Candidate, c.SdpMid, c.SdpMLineIndex);
        };

        _peer.OnTrack = e =>
        {
            if (e.Track is VideoStreamTrack vt)
            {
                int idx = _remoteTracks.Count;
                _remoteTracks.Add(vt);
                vt.OnVideoReceived += tex => OnRemoteTexture?.Invoke(idx, tex);
                OnPeerStateChanged?.Invoke($"Track #{idx} added");
            }
        };

        _peer.OnDataChannel = channel =>
        {
            _dataChannel = channel;
            _dataChannel.OnMessage = bytes =>
            {
                string msg = Encoding.UTF8.GetString(bytes);
                OnDataChannelMessage?.Invoke(msg);
            };
            OnPeerStateChanged?.Invoke("DataChannel opened (remote)");
        };
    }

    /// <summary>通过 DataChannel 发送控制指令（替代原 8005 UDP）。</summary>
    public void SendControlMessage(string message)
    {
        if (_dataChannel == null || _dataChannel.ReadyState != RTCDataChannelState.Open) return;
        _dataChannel.Send(Encoding.UTF8.GetBytes(message));
    }

    public Task<bool> CreateAndSendOfferAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        StartCoroutine(CreateOfferCo(tcs));
        return tcs.Task;
    }

    public Task<bool> SetRemoteAnswerAsync(string sdp)
    {
        var tcs = new TaskCompletionSource<bool>();
        StartCoroutine(SetAnswerCo(sdp, tcs));
        return tcs.Task;
    }

    public void AddRemoteIceCandidate(string candidate, string sdpMid, int? sdpMLineIndex)
    {
        if (_peer == null || string.IsNullOrWhiteSpace(candidate)) return;
        _peer.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
        {
            candidate     = candidate,
            sdpMid        = sdpMid,
            sdpMLineIndex = sdpMLineIndex ?? 0,
        }));
    }

    public void ClosePeer()
    {
        foreach (var t in _remoteTracks)
            try { t.Dispose(); } catch { }
        _remoteTracks.Clear();

        _dataChannel = null;

        if (_peer != null) { _peer.Close(); _peer.Dispose(); _peer = null; }
    }

    // ================================================================
    // Coroutines
    // ================================================================
    private IEnumerator CreateOfferCo(TaskCompletionSource<bool> tcs)
    {
        if (_peer == null) { tcs.SetResult(false); yield break; }

        var op = _peer.CreateOffer();
        yield return op;
        if (op.IsError) { OnError?.Invoke($"CreateOffer failed: {op.Error.message}"); tcs.SetResult(false); yield break; }

        var desc = op.Desc;
        var setLocal = _peer.SetLocalDescription(ref desc);
        yield return setLocal;
        if (setLocal.IsError) { OnError?.Invoke($"SetLocal failed: {setLocal.Error.message}"); tcs.SetResult(false); yield break; }

        OnLocalOfferReady?.Invoke(desc.sdp);
        tcs.SetResult(true);
    }

    private IEnumerator SetAnswerCo(string sdp, TaskCompletionSource<bool> tcs)
    {
        if (_peer == null) { tcs.SetResult(false); yield break; }

        var desc = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
        var op = _peer.SetRemoteDescription(ref desc);
        yield return op;
        if (op.IsError) { OnError?.Invoke($"SetRemote failed: {op.Error.message}"); tcs.SetResult(false); yield break; }

        tcs.SetResult(true);
    }

    // ================================================================
    // 反射方式添加 video recv transceiver（兼容不同 Unity.WebRTC 版本）
    // ================================================================
    private void TryAddVideoRecvTransceiver()
    {
        if (_peer == null) return;
        try
        {
            foreach (var method in typeof(RTCPeerConnection).GetMethods())
            {
                if (method.Name != "AddTransceiver") continue;
                var ps = method.GetParameters();
                if (ps.Length < 1 || ps[0].ParameterType != typeof(TrackKind)) continue;

                object[] args = new object[ps.Length];
                args[0] = TrackKind.Video;

                if (ps.Length >= 2)
                {
                    var initType = ps[1].ParameterType;
                    var initVal  = Activator.CreateInstance(initType);
                    var dirProp  = initType.GetProperty("direction");
                    if (dirProp != null)
                        dirProp.SetValue(initVal, Enum.Parse(dirProp.PropertyType, "RecvOnly"));
                    args[1] = initVal;
                }

                method.Invoke(_peer, args);
                return;
            }
            OnError?.Invoke("No compatible AddTransceiver overload found");
        }
        catch (Exception ex) { OnError?.Invoke($"AddTransceiver failed: {ex.Message}"); }
    }

    private void OnDestroy()
    {
        ClosePeer();
        if (_updateRunning && _updateCo != null)
        {
            StopCoroutine(_updateCo);
            _updateRunning = false;
        }
    }
}
