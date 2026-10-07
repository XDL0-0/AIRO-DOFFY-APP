using UnityEngine;

[CreateAssetMenu(fileName = "TeleopConfig", menuName = "VR Teleop/Teleop Config")]
public class TeleopConfig : ScriptableObject
{
    [Header("Network")]
    public string defaultServerIP = "10.10.131.72";
    public int posePort = 8001;
    public int controlPort = 8005;
    public int virtualRobotStatePort = 8011;
    public int tactilePort = 8012;
    public int signalingPort = 8765;
    public int udpVideoBasePort = 8000;

    [Header("Streaming")]
    [Range(1, 3)] public int defaultWebRTCTrackCount = 1;
    public float sendRateHz = 30f;
    public bool useBinaryProtocol = true;

    [Header("Safety")]
    public bool preventSleep = true;
    public bool pauseControlOnTrackingLoss = true;
    public bool requireRecalibrateAfterTrackingLoss = true;

    /// <summary>
    /// Converts the serialized asset into the transport/session snapshot. The
    /// fields above remain public and unchanged so existing scene assets keep
    /// their serialized values and GUIDs.
    /// </summary>
    public TeleopRuntimeSettings CreateRuntimeSettings()
    {
        var settings = new TeleopRuntimeSettings
        {
            ServerHost = defaultServerIP,
            PosePort = posePort,
            ControlPort = controlPort,
            VirtualRobotStatePort = virtualRobotStatePort,
            TactilePort = tactilePort,
            SignalingPort = signalingPort,
            UdpVideoBasePort = udpVideoBasePort,
            WebRtcTrackCount = defaultWebRTCTrackCount,
            SendRateHz = sendRateHz,
            UseBinaryProtocol = useBinaryProtocol,
            PreventSleep = preventSleep,
            PauseControlOnTrackingLoss = pauseControlOnTrackingLoss,
            RequireRecalibrateAfterTrackingLoss = requireRecalibrateAfterTrackingLoss
        };
        settings.Normalize();
        return settings;
    }
}
