using System;
using Unity.WebRTC;

/// <summary>Configures the typed Unity.WebRTC receive-only transceiver set for one peer.</summary>
internal static class WebRTCPeerFactory
{
    public static void Configure(
        RTCPeerConnection peer,
        int trackCount,
        DelegateOnConnectionStateChange onConnectionState,
        DelegateOnIceConnectionChange onIceConnection,
        DelegateOnIceCandidate onIceCandidate,
        DelegateOnTrack onTrack,
        DelegateOnDataChannel onDataChannel)
    {
        peer.OnConnectionStateChange = onConnectionState;
        peer.OnIceConnectionChange = onIceConnection;
        peer.OnIceCandidate = onIceCandidate;
        peer.OnTrack = onTrack;
        peer.OnDataChannel = onDataChannel;

        RTCRtpTransceiverInit init = new RTCRtpTransceiverInit
        {
            direction = RTCRtpTransceiverDirection.RecvOnly,
        };
        for (int i = 0; i < Math.Max(1, trackCount); i++)
            peer.AddTransceiver(TrackKind.Video, init);
    }
}
