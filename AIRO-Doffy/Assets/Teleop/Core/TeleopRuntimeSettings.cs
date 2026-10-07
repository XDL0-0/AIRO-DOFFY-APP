using System;

/// <summary>
/// Runtime settings shared by the teleoperation session and every transport.
/// This class deliberately has no Unity dependency so validation and persistence
/// can be tested without loading a scene.
/// </summary>
public sealed class TeleopRuntimeSettings
{
    public const int MinWebRtcTracks = 1;
    public const int MaxWebRtcTracks = 3;
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    public string ServerHost { get; set; } = "10.10.131.72";
    public int PosePort { get; set; } = 8001;
    public int ControlPort { get; set; } = 8005;
    public int VirtualRobotStatePort { get; set; } = 8011;
    public int TactilePort { get; set; } = 8012;
    public int SignalingPort { get; set; } = 8765;
    public int UdpVideoBasePort { get; set; } = 8000;

    public int TrackingMode { get; set; } = 0;
    public float SendRateHz { get; set; } = 30f;
    public bool UseBinaryProtocol { get; set; } = true;
    public int WebRtcTrackCount { get; set; } = MinWebRtcTracks;

    public bool PreventSleep { get; set; } = true;
    public bool PauseControlOnTrackingLoss { get; set; } = true;
    public bool RequireRecalibrateAfterTrackingLoss { get; set; } = true;

    public static TeleopRuntimeSettings Defaults()
    {
        return new TeleopRuntimeSettings();
    }

    public TeleopRuntimeSettings Clone()
    {
        return new TeleopRuntimeSettings
        {
            ServerHost = ServerHost,
            PosePort = PosePort,
            ControlPort = ControlPort,
            VirtualRobotStatePort = VirtualRobotStatePort,
            TactilePort = TactilePort,
            SignalingPort = SignalingPort,
            UdpVideoBasePort = UdpVideoBasePort,
            TrackingMode = TrackingMode,
            SendRateHz = SendRateHz,
            UseBinaryProtocol = UseBinaryProtocol,
            WebRtcTrackCount = WebRtcTrackCount,
            PreventSleep = PreventSleep,
            PauseControlOnTrackingLoss = PauseControlOnTrackingLoss,
            RequireRecalibrateAfterTrackingLoss = RequireRecalibrateAfterTrackingLoss
        };
    }

    /// <summary>Clamps values loaded from an old PlayerPrefs store or asset.</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(ServerHost))
            ServerHost = Defaults().ServerHost;

        PosePort = ClampPort(PosePort, 8001);
        ControlPort = ClampPort(ControlPort, 8005);
        VirtualRobotStatePort = ClampPort(VirtualRobotStatePort, 8011);
        TactilePort = ClampPort(TactilePort, 8012);
        SignalingPort = ClampPort(SignalingPort, 8765);
        UdpVideoBasePort = ClampPort(UdpVideoBasePort, 8000);
        TrackingMode = ClampTrackingMode(TrackingMode);
        WebRtcTrackCount = ClampTrackCount(WebRtcTrackCount);

        if (float.IsNaN(SendRateHz) || float.IsInfinity(SendRateHz) || SendRateHz <= 0f)
            SendRateHz = 30f;
    }

    public static int ClampTrackCount(int count)
    {
        return Math.Max(MinWebRtcTracks, Math.Min(MaxWebRtcTracks, count));
    }

    public static int ClampTrackingMode(int mode)
    {
        return mode <= 0 ? 0 : 1;
    }

    public static int ClampPort(int port, int fallback)
    {
        return port >= MinPort && port <= MaxPort ? port : fallback;
    }
}
