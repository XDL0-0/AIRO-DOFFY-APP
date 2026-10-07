using System.Collections;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace Doffy.UI
{
    /// <summary>
    /// Shared legacy-OVR-backed input and pose source for the wrist UI. The inspector references
    /// can be wired to the Teleoperation scene's Meta rig; a two-pass startup discovery is kept
    /// as a fallback for runtime-created workspace shells.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class WristInteractionSource : MonoBehaviour, IGameObjectFilter
    {
        private static WristInteractionSource instance;

        [Header("Meta rig")]
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftControllerAnchor;
        [SerializeField] private HandVisual controllerHandVisual;

        [Header("Right interaction input")]
        [SerializeField] private RayInteractor rightRay;
        [SerializeField] private ControllerRef rightController;
        [SerializeField] private PokeInteractor rightHandPoke;
        [SerializeField] private HandRef rightHand;

        [Header("Left wrist tracking input")]
        [SerializeField] private ControllerRef leftController;
        [SerializeField] private HandRef leftHand;

        private bool controllerModeActive;
        private bool handsActive;
        private bool leftTracked;
        private bool rightInputTracked;
        private Pose leftPose;

        public static WristInteractionSource Ensure()
        {
            if (instance != null)
                return instance;

            instance = Object.FindAnyObjectByType<WristInteractionSource>();
            if (instance == null)
                instance = new GameObject("Wrist Interaction Source").AddComponent<WristInteractionSource>();
            return instance;
        }

        /// <summary>True when hand input is the selected UI modality, even during brief occlusion.</summary>
        public bool HandsActive => handsActive;

        /// <summary>True when the current mode-appropriate left wrist tracking source is valid.</summary>
        public bool LeftTracked => leftTracked;

        /// <summary>True when the selected right input is tracked and its interactor is enabled.</summary>
        public bool RightInputTracked => rightInputTracked;

        /// <summary>The cached right-hand-only controller ray from the Meta Interaction rig.</summary>
        public RayInteractor RightRay => rightRay;

        /// <summary>The cached right index-finger poke interactor from the Meta Interaction rig.</summary>
        public PokeInteractor RightPoke => rightHandPoke;

        /// <summary>The tracked headset center-eye anchor, when the rig has initialized.</summary>
        public Transform Head => head;

        /// <summary>The left hand model, including Meta's controller-driven wrist skeleton.</summary>
        public HandVisual ControllerHandVisual => controllerHandVisual;

        /// <summary>True when the right controller pose is the selected UI input.</summary>
        public bool ControllerModeActive => controllerModeActive;

        /// <summary>
        /// True while the left wrist is presentable in an idle session. This does not require a
        /// usable right input, so brief right-hand occlusion can disable controls without hiding
        /// or resetting the mounted wrist UI.
        /// </summary>
        public bool CanPresentWrist
        {
            get
            {
                AppManager app = AppManager.Instance;
                return leftTracked && (controllerModeActive || handsActive) &&
                       app != null && !IsSessionOpen(app) && HasXrInputFocus();
            }
        }

        /// <summary>
        /// True while the teleoperation streaming lifecycle is open. Session presentation must
        /// follow this state instead of the squeeze-to-send gate so the wrist stays hidden while
        /// starting, streaming, or paused for tracking loss.
        /// </summary>
        public static bool IsSessionOpen(AppManager app)
        {
            return app != null && IsSessionOpen(app.SessionState, app.IsStreaming);
        }

        /// <summary>Pure session-presentation rule for one lifecycle state.</summary>
        public static bool IsSessionOpen(TeleopSessionState state, bool streaming)
        {
            if (!streaming)
                return false;
            switch (state)
            {
                case TeleopSessionState.Starting:
                case TeleopSessionState.VideoConnecting:
                case TeleopSessionState.Streaming:
                case TeleopSessionState.TrackingLost:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Requires a valid left wrist, the selected right input, an active app, and current HMD/input focus.
        /// The focus checks are live so a lost focus immediately rejects Meta interactor filters.
        /// </summary>
        public bool CanInteract
        {
            get
            {
                return CanPresentWrist && rightInputTracked;
            }
        }

        /// <summary>
        /// Scoped exception for the teleoperation recording control. It keeps the active-right-input
        /// and XR focus checks, while intentionally not requiring the left wrist or allowing normal
        /// workspace surfaces through during teleoperation.
        /// </summary>
        public bool CanInteractForRecording
        {
            get
            {
                AppManager app = AppManager.Instance;
                return IsSessionOpen(app) && HasXrInputFocus() && rightInputTracked;
            }
        }

        /// <summary>
        /// Floating camera panels use only the selected tracked right input and live XR
        /// focus. Their controls stay usable before and during teleoperation without
        /// requiring the left wrist to remain raised or tracked.
        /// </summary>
        public bool CanInteractForFloatingPanels => isActiveAndEnabled &&
            AppManager.Instance != null && head != null && head.gameObject.activeInHierarchy &&
            (handsActive || controllerModeActive) && rightInputTracked && HasXrInputFocus();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            ResolveReferences();
            RefreshTrackingState();
            StartCoroutine(ResolveReferencesOnceAfterStartup());
        }

        private IEnumerator ResolveReferencesOnceAfterStartup()
        {
            // OVR Interaction data-source providers finish their injection in their own Start calls.
            yield return null;
            ResolveReferences();
            RefreshTrackingState();
            if (!HasAnyUsableInputReferences())
                Debug.LogWarning("[WristInteractionSource] Meta rig/right ray/right poke or left pose source was not found; wrist UI input is unavailable.");
        }

        private void ResolveReferences()
        {
            if (cameraRig == null)
                cameraRig = Object.FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null)
            {
                if (head == null) head = cameraRig.centerEyeAnchor;
                if (leftControllerAnchor == null) leftControllerAnchor = cameraRig.leftControllerAnchor;
            }

            DiscoverMissingInteractionRefs();
            if (controllerHandVisual == null)
            {
                foreach (var visual in Object.FindObjectsByType<HandVisual>(FindObjectsInactive.Include))
                {
                    if (visual.Hand != null && !(visual.Hand is ShadowHand) &&
                        visual.Hand.Handedness == Handedness.Left)
                    {
                        controllerHandVisual = visual;
                        break;
                    }
                }
            }
        }

        private void DiscoverMissingInteractionRefs()
        {
            RayInteractor[] rays = Object.FindObjectsByType<RayInteractor>(
                FindObjectsInactive.Include);
            if (rightRay == null || rightController == null || leftController == null)
            {
                foreach (RayInteractor ray in rays)
                {
                    if (ray == null) continue;
                    ControllerRef controller = ray.GetComponent<ControllerRef>();
                    if (controller == null || !TryGetHandedness(controller, out Handedness side)) continue;

                    if (side == Handedness.Right)
                    {
                        if (rightController == null) rightController = controller;
                        if (rightRay == null && rightController == controller) rightRay = ray;
                    }
                    else if (side == Handedness.Left && leftController == null)
                    {
                        leftController = controller;
                    }
                }
            }

            PokeInteractor[] pokes = Object.FindObjectsByType<PokeInteractor>(
                FindObjectsInactive.Include);
            if (rightHandPoke == null || rightHand == null)
            {
                foreach (PokeInteractor poke in pokes)
                {
                    if (poke == null) continue;
                    HandRef hand = poke.GetComponent<HandRef>();
                    if (hand == null || !TryGetHandedness(hand, out Handedness side) || side != Handedness.Right)
                        continue;
                    if (rightHand == null) rightHand = hand;
                    if (rightHandPoke == null && rightHand == hand) rightHandPoke = poke;
                }
            }

            if (leftHand == null)
            {
                HandRef[] hands = Object.FindObjectsByType<HandRef>(
                    FindObjectsInactive.Include);
                foreach (HandRef hand in hands)
                {
                    if (hand != null && TryGetHandedness(hand, out Handedness side) && side == Handedness.Left)
                    {
                        leftHand = hand;
                        break;
                    }
                }
            }
        }

        private bool HasAnyUsableInputReferences() =>
            cameraRig != null && head != null && leftControllerAnchor != null &&
            ((rightController != null && rightRay != null && leftController != null) ||
             (rightHand != null && rightHandPoke != null && leftHand != null));

        private void LateUpdate() => RefreshTrackingState();

        private void RefreshTrackingState()
        {
            bool rightControllerTracked = IsControllerTracked(rightController, OVRInput.Controller.RTouch);
            bool rightHandTracked = IsHandTracked(rightHand);
            bool wasHandsActive = handsActive;
            bool wasControllerModeActive = controllerModeActive;
            bool hasActiveMode = TryGetActiveRightInput(out bool preferHands);

            bool selectHands = false;
            bool selectController = false;
            if (hasActiveMode)
            {
                if (preferHands)
                    selectHands = rightHandPoke != null;
                else
                    selectController = rightRay != null;
            }
            else if (rightHandTracked != rightControllerTracked)
            {
                // Older runtimes may not report an active-controller mask. Fall back only when
                // exactly one right-side modality is actually tracked, never merely connected.
                selectHands = rightHandTracked && rightHandPoke != null;
                selectController = rightControllerTracked && rightRay != null;
            }
            else if (wasHandsActive && TryBuildHandWristPose(leftHand, out _))
            {
                // With no reliable mode report, keep the previous modality only while its own
                // left wrist source remains live. Never substitute a different wrist pose.
                selectHands = true;
            }
            else if (wasControllerModeActive && TryGetLeftControllerPose(out _))
            {
                selectController = true;
            }

            // A live OVR mode report wins even when that right input is briefly occluded.
            // This keeps the selected left-wrist pose continuous while CanInteract closes.
            handsActive = selectHands;
            controllerModeActive = selectController;

            leftTracked = false;
            leftPose = Pose.identity;
            rightInputTracked = false;

            if (handsActive)
            {
                leftTracked = TryBuildHandWristPose(leftHand, out leftPose);
                rightInputTracked = rightHandPoke != null && rightHandPoke.isActiveAndEnabled &&
                                    rightHandPoke.gameObject.activeInHierarchy && rightHandTracked;
            }
            else if (controllerModeActive)
            {
                leftTracked = TryGetLeftControllerPose(out leftPose);
                rightInputTracked = rightRay != null && rightRay.isActiveAndEnabled &&
                                    rightRay.gameObject.activeInHierarchy && rightControllerTracked;
            }

            if (head == null && cameraRig != null)
                head = cameraRig.centerEyeAnchor;
        }

        private bool TryGetLeftControllerPose(out Pose pose)
        {
            pose = Pose.identity;
            if (!IsControllerTracked(leftController, OVRInput.Controller.LTouch) ||
                leftControllerAnchor == null || !leftControllerAnchor.gameObject.activeInHierarchy)
                return false;

            pose = new Pose(leftControllerAnchor.position, leftControllerAnchor.rotation);
            return true;
        }

        /// <summary>Returns the current mode-appropriate left wrist pose, failing closed on stale tracking.</summary>
        public bool TryGetLeftPose(out Pose pose)
        {
            pose = leftPose;
            return leftTracked;
        }

        /// <summary>Uses the same wrist and palm landmarks as the rendered controller-driven hand.</summary>
        public bool TryGetControllerWristPose(out Pose pose)
        {
            pose = Pose.identity;
            var visual = controllerHandVisual;
            if (!controllerModeActive || !leftTracked || visual == null || !visual.isActiveAndEnabled ||
                visual.Hand == null || !visual.Hand.IsConnected || !visual.Hand.IsTrackedDataValid)
                return false;
            var joints = visual.Joints;
            if (joints == null || joints.Count <= (int)HandJointId.HandPinky1) return false;
            var wrist = joints[(int)HandJointId.HandWristRoot];
            var middle = joints[(int)HandJointId.HandMiddle1];
            var index = joints[(int)HandJointId.HandIndex1];
            var pinky = joints[(int)HandJointId.HandPinky1];
            return wrist != null && middle != null && index != null && pinky != null &&
                TryBuildControllerWristFrame(wrist.position, middle.position, index.position, pinky.position, out pose);
        }

        private static bool TryBuildControllerWristFrame(Vector3 wrist, Vector3 middle,
            Vector3 index, Vector3 pinky, out Pose pose)
        {
            pose = Pose.identity;
            if (!IsFinite(wrist) || !IsFinite(middle) || !IsFinite(index) || !IsFinite(pinky)) return false;
            Vector3 forward = middle - wrist;
            Vector3 across = index - pinky;
            if (!IsFinite(forward) || !IsFinite(across) ||
                forward.sqrMagnitude < .000001f || across.sqrMagnitude < .000001f) return false;
            forward.Normalize();
            Vector3 dorsal = Vector3.Cross(forward, across);
            if (!IsFinite(dorsal) || dorsal.sqrMagnitude < .000001f) return false;
            pose = new Pose(wrist, Quaternion.LookRotation(forward, dorsal.normalized));
            return IsFinite(pose.rotation);
        }

        /// <summary>Meta Interaction SDK interactor filter used only by wrist surfaces.</summary>
        public bool Filter(GameObject gameObject)
        {
            if (!CanInteract || gameObject == null)
                return false;
            if (handsActive)
                return rightHandPoke != null && gameObject == rightHandPoke.gameObject;
            return controllerModeActive && rightRay != null && gameObject == rightRay.gameObject;
        }

        /// <summary>Accepts only the currently selected right controller ray or right hand poke for the recording panel.</summary>
        public bool FilterRecordingScope(GameObject gameObject)
        {
            if (!CanInteractForRecording || gameObject == null)
                return false;
            if (handsActive)
                return rightHandPoke != null && gameObject == rightHandPoke.gameObject;
            return controllerModeActive && rightRay != null && gameObject == rightRay.gameObject;
        }

        private static bool HasXrInputFocus()
        {
            // These public OVRManager status fields are used by TeleopTrackingGuard as well.
            try
            {
                return OVRManager.instance != null && OVRManager.isHmdPresent && OVRManager.instance.isUserPresent &&
                       OVRManager.hasVrFocus && OVRManager.hasInputFocus;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private static bool IsControllerTracked(ControllerRef controller, OVRInput.Controller ovrController)
        {
            if (controller == null) return false;
            try
            {
                return OVRInput.GetControllerPositionTracked(ovrController) &&
                       OVRInput.GetControllerOrientationTracked(ovrController) &&
                       controller.IsConnected && controller.IsPoseValid;
            }
            catch (System.Exception) { return false; }
        }

        private static bool IsHandTracked(HandRef hand)
        {
            if (hand == null || hand.Hand == null) return false;
            try { return hand.IsConnected && hand.IsTrackedDataValid; }
            catch (System.Exception) { return false; }
        }

        private static bool TryGetActiveRightInput(out bool preferHands)
        {
            preferHands = false;
            try
            {
                OVRInput.Controller active = OVRInput.GetActiveController();
                // OVRInput.Hands (and RHand) represent live hand input. Check hand bits before
                // Touch so a resting connected controller cannot steal a hand-mode wrist surface.
                if ((active & OVRInput.Controller.Hands) != 0)
                {
                    preferHands = true;
                    return true;
                }
                if ((active & OVRInput.Controller.Touch) != 0)
                    return true;

                OVRInput.Controller rightActive = OVRInput.GetActiveControllerForHand(
                    OVRInput.Handedness.RightHanded);
                if (rightActive == OVRInput.Controller.RHand)
                {
                    preferHands = true;
                    return true;
                }
                if (rightActive == OVRInput.Controller.RTouch)
                    return true;
            }
            catch (System.Exception)
            {
                // Physical tracking below may still provide an unambiguous fallback.
            }
            return false;
        }

        private static bool TryGetHandedness(ControllerRef controller, out Handedness handedness)
        {
            try { handedness = controller.Handedness; return true; }
            catch (MissingReferenceException) { handedness = Handedness.Left; return false; }
            catch (System.NullReferenceException) { handedness = Handedness.Left; return false; }
        }

        private static bool TryGetHandedness(HandRef hand, out Handedness handedness)
        {
            try { handedness = hand.Handedness; return true; }
            catch (MissingReferenceException) { handedness = Handedness.Left; return false; }
            catch (System.NullReferenceException) { handedness = Handedness.Left; return false; }
        }

        private static bool TryBuildHandWristPose(HandRef hand, out Pose pose)
        {
            pose = Pose.identity;
            try
            {
                if (!IsHandTracked(hand) || !hand.GetRootPose(out Pose wrist) ||
                    !hand.GetJointPose(HandJointId.HandIndex1, out Pose indexKnuckle) ||
                    !hand.GetJointPose(HandJointId.HandPinky0, out Pose pinkyKnuckle))
                    return false;

                Vector3 forward = indexKnuckle.position - wrist.position;
                Vector3 indexToPinky = indexKnuckle.position - pinkyKnuckle.position;
                if (!IsFinite(wrist.position) || !IsFinite(forward) || !IsFinite(indexToPinky) ||
                    forward.sqrMagnitude < 0.000001f || indexToPinky.sqrMagnitude < 0.000001f)
                    return false;

                forward.Normalize();
                // Left palm down: fingers +Z, index-minus-pinky +X; cross(+Z, +X) is dorsal +Y.
                Vector3 dorsal = Vector3.Cross(forward, indexToPinky);
                if (!IsFinite(dorsal) || dorsal.sqrMagnitude < 0.000001f)
                    return false;
                dorsal.Normalize();
                pose = new Pose(wrist.position, Quaternion.LookRotation(forward, dorsal));
                return IsFinite(pose.rotation);
            }
            catch (MissingReferenceException) { return false; }
            catch (System.NullReferenceException) { return false; }
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static bool IsFinite(Quaternion value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
            !float.IsNaN(value.w) && !float.IsInfinity(value.w);
    }
}
