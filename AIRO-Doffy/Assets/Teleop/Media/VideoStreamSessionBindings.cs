using System;

/// <summary>
/// Owns the event delegate instances for one signaling/receiver session. Every callback is
/// queued before reaching the manager, and Dispose removes all delegates from that session.
/// </summary>
internal sealed class VideoStreamSessionBindings : IDisposable
{
    private readonly VideoSignalingClient _signaling;
    private readonly WebRTCVideoReceiver _receiver;
    private readonly Action<Action> _enqueue;
    private readonly Action<VideoSignalingClient.Envelope> _onEnvelope;
    private readonly Action<string> _onSignalingError;
    private readonly Action _onConnected;
    private readonly Action _onDisconnected;
    private readonly Action<string> _onOffer;
    private readonly Action<string, string, int?> _onIce;
    private readonly Action<int, UnityEngine.Texture> _onTexture;
    private readonly Action<string> _onData;
    private readonly Action _onDataOpen;
    private readonly Action<string> _onReceiverError;
    private readonly Action<string> _onPeerState;
    private bool _disposed;

    public VideoStreamSessionBindings(
        VideoSignalingClient signaling,
        WebRTCVideoReceiver receiver,
        Action<Action> enqueue,
        Action<VideoSignalingClient.Envelope> onEnvelope,
        Action<string> onSignalingError,
        Action onConnected,
        Action onDisconnected,
        Action<string> onOffer,
        Action<string, string, int?> onIce,
        Action<int, UnityEngine.Texture> onTexture,
        Action<string> onData,
        Action onDataOpen,
        Action<string> onReceiverError,
        Action<string> onPeerState)
    {
        _signaling = signaling;
        _receiver = receiver;
        _enqueue = enqueue;
        _onEnvelope = env => Queue(() => onEnvelope?.Invoke(env));
        _onSignalingError = error => Queue(() => onSignalingError?.Invoke(error));
        _onConnected = () => Queue(() => onConnected?.Invoke());
        _onDisconnected = () => Queue(() => onDisconnected?.Invoke());
        _onOffer = sdp => Queue(() => onOffer?.Invoke(sdp));
        _onIce = (candidate, mid, index) => Queue(() => onIce?.Invoke(candidate, mid, index));
        _onTexture = (index, texture) => Queue(() => onTexture?.Invoke(index, texture));
        _onData = message => Queue(() => onData?.Invoke(message));
        _onDataOpen = () => Queue(() => onDataOpen?.Invoke());
        _onReceiverError = error => Queue(() => onReceiverError?.Invoke(error));
        _onPeerState = state => Queue(() => onPeerState?.Invoke(state));

        _signaling.OnEnvelope += _onEnvelope;
        _signaling.OnError += _onSignalingError;
        _signaling.OnConnected += _onConnected;
        _signaling.OnDisconnected += _onDisconnected;
        _receiver.OnLocalOfferReady += _onOffer;
        _receiver.OnLocalIceCandidate += _onIce;
        _receiver.OnRemoteTexture += _onTexture;
        _receiver.OnDataChannelMessage += _onData;
        _receiver.OnDataChannelOpened += _onDataOpen;
        _receiver.OnError += _onReceiverError;
        _receiver.OnPeerStateChanged += _onPeerState;
    }

    private void Queue(Action action)
    {
        if (!_disposed)
            _enqueue?.Invoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _signaling.OnEnvelope -= _onEnvelope;
        _signaling.OnError -= _onSignalingError;
        _signaling.OnConnected -= _onConnected;
        _signaling.OnDisconnected -= _onDisconnected;
        _receiver.OnLocalOfferReady -= _onOffer;
        _receiver.OnLocalIceCandidate -= _onIce;
        _receiver.OnRemoteTexture -= _onTexture;
        _receiver.OnDataChannelMessage -= _onData;
        _receiver.OnDataChannelOpened -= _onDataOpen;
        _receiver.OnError -= _onReceiverError;
        _receiver.OnPeerStateChanged -= _onPeerState;
    }
}
