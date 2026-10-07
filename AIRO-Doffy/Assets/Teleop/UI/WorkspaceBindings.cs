using UnityEngine;

namespace Doffy.UI
{
    /// <summary>Resolve scene services once; pages never search the hierarchy.</summary>
    public sealed class WorkspaceBindings
    {
        public readonly AppManager App = Object.FindAnyObjectByType<AppManager>();
        public readonly RecordingController Recorder = Object.FindAnyObjectByType<RecordingController>();
        public readonly TrackingModeManager Tracking = Object.FindAnyObjectByType<TrackingModeManager>();
        public readonly UdpWindowManager Windows = Object.FindAnyObjectByType<UdpWindowManager>();
        public readonly TCPPoseReceiver Feedback = Object.FindAnyObjectByType<TCPPoseReceiver>();
        public readonly VideoStreamManager Video = Object.FindAnyObjectByType<VideoStreamManager>();
        public readonly CalibrationTool Calibration = Object.FindAnyObjectByType<CalibrationTool>();
        public readonly RobotBasePlacementTool Placement = Object.FindAnyObjectByType<RobotBasePlacementTool>();
        public readonly UpperLimbAkmManager UpperLimb = Object.FindAnyObjectByType<UpperLimbAkmManager>();
        public BodyPoseTelemetrySender BodyTelemetry => BodyPoseTelemetrySender.Instance;
        public readonly TactileSensorGenerator Tactile = Object.FindAnyObjectByType<TactileSensorGenerator>();
        public readonly ForceArrow[] Forces = Object.FindObjectsByType<ForceArrow>(FindObjectsInactive.Include);
        public readonly OVRPassthroughLayer[] Passthrough = Object.FindObjectsByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
        public readonly TeleopWorld World = Object.FindAnyObjectByType<TeleopWorld>();

        public void SetForceVisible(bool visible)
        {
            foreach (var force in Forces) if (force != null) force.gameObject.SetActive(visible);
        }

        public void SetTactileVisible(bool visible)
        {
            if (Tactile != null && Tactile.container != null) Tactile.container.gameObject.SetActive(visible);
        }

        public void SetPassthrough(bool visible)
        {
            foreach (var layer in Passthrough) if (layer != null) layer.enabled = visible;
        }
    }
}
