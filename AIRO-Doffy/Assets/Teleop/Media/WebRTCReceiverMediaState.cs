using System;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

/// <summary>
/// Owns remote tracks and the current DataChannel for one receiver. Peer/session validity is
/// supplied by the owner, so this object can release media without knowing MonoBehaviour life.
/// </summary>
internal sealed class WebRTCReceiverMediaState
{
    private readonly object _lock = new object();
    private readonly List<VideoStreamTrack> _tracks = new List<VideoStreamTrack>();
    private RTCDataChannel _dataChannel;

    public int TrackCount
    {
        get { lock (_lock) return _tracks.Count; }
    }

    public bool TrySend(string message, Action<string> onError)
    {
        lock (_lock)
            return WebRTCDataChannelBridge.TrySend(_dataChannel, message, onError);
    }

    public void HandleTrack(
        RTCTrackEvent trackEvent,
        Func<bool> isCurrent,
        Action<int, Texture> onTexture,
        Action<string> onState)
    {
        if (trackEvent == null || !Current(isCurrent)) return;
        VideoStreamTrack videoTrack = trackEvent.Track as VideoStreamTrack;
        if (videoTrack == null) return;

        int trackIndex;
        lock (_lock)
        {
            if (!Current(isCurrent)) return;
            trackIndex = _tracks.Count;
            int capturedIndex = trackIndex;
            videoTrack.OnVideoReceived += texture =>
            {
                if (Current(isCurrent)) onTexture?.Invoke(capturedIndex, texture);
            };
            _tracks.Add(videoTrack);
        }
        if (Current(isCurrent)) onState?.Invoke($"Track #{trackIndex} added");
    }

    public void HandleDataChannel(
        RTCDataChannel channel,
        Func<bool> isCurrent,
        Action<string> onMessage,
        Action onOpen,
        Action onClose,
        Action<string> onError,
        Action<string> onState)
    {
        if (channel == null || !Current(isCurrent)) return;
        RTCDataChannel previous;
        bool alreadyOpen;
        lock (_lock)
        {
            if (!Current(isCurrent)) return;
            previous = _dataChannel;
            _dataChannel = channel;
            alreadyOpen = WebRTCDataChannelBridge.Configure(
                channel,
                isCurrent,
                () => IsCurrentChannel(channel),
                onMessage,
                () =>
                {
                    onState?.Invoke("DataChannel opened");
                    onOpen?.Invoke();
                },
                () =>
                {
                    onState?.Invoke("DataChannel closed");
                    onClose?.Invoke();
                },
                onError);
        }

        if (previous != null && !ReferenceEquals(previous, channel))
            WebRTCDataChannelBridge.Dispose(previous);
        if (!Current(isCurrent)) return;
        onState?.Invoke("DataChannel received (remote)");
        if (alreadyOpen)
        {
            onState?.Invoke("DataChannel opened");
            onOpen?.Invoke();
        }
    }

    public void Dispose()
    {
        RTCDataChannel channel;
        List<VideoStreamTrack> tracks;
        lock (_lock)
        {
            channel = _dataChannel;
            tracks = new List<VideoStreamTrack>(_tracks);
            _dataChannel = null;
            _tracks.Clear();
        }
        WebRTCDataChannelBridge.Dispose(channel);
        foreach (VideoStreamTrack track in tracks)
        {
            try { track.Dispose(); } catch { }
        }
    }

    private bool IsCurrentChannel(RTCDataChannel channel)
    {
        lock (_lock) return ReferenceEquals(_dataChannel, channel);
    }

    private static bool Current(Func<bool> isCurrent)
    {
        try { return isCurrent != null && isCurrent(); }
        catch { return false; }
    }
}
