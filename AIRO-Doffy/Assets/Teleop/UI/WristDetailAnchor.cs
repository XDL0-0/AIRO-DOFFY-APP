using UnityEngine;

namespace Doffy.UI
{
    /// <summary>
    /// Keeps a wrist detail canvas at a tracked hand/controller position while following its
    /// absolute horizontal yaw with a fixed book-like pitch.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class WristDetailAnchor : MonoBehaviour
    {
        private const float DirectionEpsilon = 0.000001f;
        private const float MinHandHeadingMagnitude = 0.1f;

        private WristInteractionSource source;
        private WristUISettings settings;
        private Quaternion readingYaw = Quaternion.identity;
        private Quaternion readingRotation = Quaternion.Euler(45f, 0f, 0f);
        private bool readingPoseValid;

        /// <summary>True after a reliable horizontal wrist heading has been sampled.</summary>
        public bool RotationLocked => readingPoseValid;

        /// <summary>The current world reading rotation, or the transform rotation before capture.</summary>
        public Quaternion ReadingRotation => readingPoseValid ? readingRotation : transform.rotation;

        public void Configure(WristInteractionSource interactionSource, WristUISettings wristSettings)
        {
            if (source != interactionSource || settings != wristSettings)
            {
                readingPoseValid = false;
                readingYaw = Quaternion.identity;
                readingRotation = BuildReadingRotation(Vector3.forward);
            }

            source = interactionSource;
            settings = wristSettings;

            if (isActiveAndEnabled)
                RefreshPose();
        }

        /// <summary>Aligns immediately to the current wrist heading, including when reopening.</summary>
        public void CaptureReadingPose() => RefreshPose();

        /// <summary>Follows the wrist's absolute horizontal yaw without a head-derived offset.</summary>
        public void RefreshPose()
        {
            if (source == null || settings == null || !source.TryGetLeftPose(out Pose wrist))
                return;

            if (TryGetHandHeading(wrist, out Vector3 heading))
            {
                readingYaw = Quaternion.LookRotation(heading, Vector3.up);
                readingRotation = BuildReadingRotation(heading);
                readingPoseValid = true;
            }
            // Near vertical, horizontal yaw is undefined: hold the last reliable yaw
            // (world-forward on the very first sample), while still following position.
            // The next reliable sample restores absolute yaw without creating an offset.
            ApplyPosition(wrist.position);
        }

        private void LateUpdate() => RefreshPose();

        private static bool TryGetHandHeading(Pose wrist, out Vector3 heading)
        {
            Vector3 horizontalForward = Vector3.ProjectOnPlane(wrist.rotation * Vector3.forward, Vector3.up);
            float horizontalMagnitude = horizontalForward.magnitude;
            if (horizontalMagnitude < MinHandHeadingMagnitude)
            {
                heading = Vector3.zero;
                return false;
            }

            heading = horizontalForward / horizontalMagnitude;
            return true;
        }

        private void ApplyPosition(Vector3 wristPosition)
        {
            float modeScale = source.HandsActive ? settings.handBraceletScale : 1f;
            Vector3 worldOffset =
                (readingYaw * Vector3.right) * (settings.pageOffset.x * modeScale) +
                (readingYaw * Vector3.forward) * (settings.pageOffset.y * modeScale) +
                Vector3.up * (settings.radius * modeScale + 0.10f);

            transform.SetPositionAndRotation(wristPosition + worldOffset, readingRotation);
        }

        private void OnEnable()
        {
            CaptureReadingPose();
        }

        /// <summary>Builds the fixed 45-degree reading orientation from a world-space direction.</summary>
        public static Quaternion BuildReadingRotation(Vector3 direction)
        {
            Vector3 horizontalForward = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontalForward.sqrMagnitude < DirectionEpsilon)
                horizontalForward = Vector3.forward;

            horizontalForward.Normalize();
            return Quaternion.LookRotation(horizontalForward, Vector3.up) * Quaternion.Euler(45f, 0f, 0f);
        }
    }
}
