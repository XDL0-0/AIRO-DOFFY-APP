using System;
using System.Reflection;
using Doffy.UI;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace Doffy.Editor
{
    /// <summary>Native Editor layout checks and idle Play smoke; never starts teleoperation or recording.</summary>
    [InitializeOnLoad]
    public static class WristUIValidation
    {
        private const string SmokeKey = "DOFFY.BraceletSmoke";
        private const int DragSegmentCount = 24;
        private const int NavigationControlCount = 6;
        private static readonly string[] NavigationLabels =
            { "Start Teleop", "Teleop Config", "Camera", "WRM Setting", "System Setting", "Exit APP" };
        private const float PositionTolerance = 0.0005f;
        private static double started;

        static WristUIValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(SmokeKey, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    started = EditorApplication.timeSinceStartup;
                    EditorApplication.update += FinishSmoke;
                }
            };
        }

        [MenuItem("Tools/DOFFY/Validate wrist bracelet")]
        public static void Validate()
        {
            TeleopBuild.ValidateScene();
            Require(PlayerSettings.bundleVersion == "0.9.7" && PlayerSettings.Android.bundleVersionCode == 16,
                "Bracelet update preserves version 0.9.7 / Android code 16");
            WorkspaceShell shell = UnityEngine.Object.FindAnyObjectByType<WorkspaceShell>();
            Require(shell != null, "Serialized wrist workspace exists");

            var serialized = new SerializedObject(shell);
            Require(serialized.FindProperty("legacyCanvas").objectReferenceValue != null, "Legacy canvas reference");
            Require(serialized.FindProperty("legacyVideoPanels").objectReferenceValue != null, "Legacy video reference");
            WristUISettings settings = serialized.FindProperty("wristSettings").objectReferenceValue as WristUISettings;
            Require(settings != null, "Wrist settings asset reference");
            Require(Near(settings.radius, .055f) && Near(settings.buttonWidth, .036f) &&
                    Near(settings.buttonHeight, .038f) && Near(settings.dialWidth, .020f),
                "Bracelet metre-scale radius, control size, and drag-band width");
            Require(Near(settings.controllerOffset, new Vector3(0f, 0f, -.105f)) &&
                    Near(settings.handOffset, new Vector3(0f, 0f, -.035f)) &&
                    Near(settings.controllerEuler, Vector3.zero) && Near(settings.handEuler, Vector3.zero),
                "Mode-specific wrist attachment offsets and neutral wrist frame");
            Require(settings.hideDownAngle > settings.showDownAngle &&
                    settings.hideFacingAngle > settings.showFacingAngle,
                "Visibility hysteresis ordering");
            Require(settings.hapticAmplitude > 0f && settings.hapticDuration > 0f,
                "Detent haptics are configured");

            WristInteractionSource source = UnityEngine.Object.FindAnyObjectByType<WristInteractionSource>();
            Require(source != null, "Serialized Meta input source exists");
            var sourceFields = new SerializedObject(source);
            foreach (string field in new[]
                     { "cameraRig", "rightRay", "rightController", "rightHandPoke", "rightHand", "leftController", "leftHand", "controllerHandVisual" })
            {
                SerializedProperty property = sourceFields.FindProperty(field);
                Require(property != null && property.objectReferenceValue != null, "Meta rig reference: " + field);
            }
            HandVisual controllerVisual = sourceFields.FindProperty("controllerHandVisual").objectReferenceValue as HandVisual;
            Require(controllerVisual != null && controllerVisual.name == "OVRLeftHandVisual",
                "Controller cuff explicitly binds the rendered left hand rather than a reticle preview");
            var visualFields = new SerializedObject(controllerVisual);
            Require(visualFields.FindProperty("_hand").objectReferenceValue != null &&
                    !(visualFields.FindProperty("_hand").objectReferenceValue is ShadowHand),
                "Controller wrist visual uses the tracked hand source rather than ShadowHand preview data");

            ValidateControllerWristFrame();
            ValidateHollowBodyMesh(settings);
            WristWorkspaceRegressionSmoke.ValidateAxes();
            Debug.Log("DOFFY bracelet scene/configuration and hollow-body geometry validation passed.");
        }

        /// <summary>Runs a small generated-mesh fixture without altering the authored scene.</summary>
        private static void ValidateHollowBodyMesh(WristUISettings settings)
        {
            Require(Resources.Load<Shader>("BraceletBody") != null, "Hollow bracelet shader is available in Resources");
            GameObject fixture = new GameObject("Bracelet body geometry validation");
            try
            {
                float outerRadius = settings.radius - .0015f;
                float innerRadius = outerRadius - .003f;
                float distalZ = settings.buttonHeight * .5f + .003f;
                float proximalZ = -settings.buttonHeight * .5f - settings.dialWidth - .005f;
                BraceletBody.Create(fixture.transform, outerRadius, distalZ, proximalZ);
                Transform body = fixture.transform.Find("Hollow bracelet body");
                Require(body != null, "Generated bracelet body object");
                MeshFilter filter = body.GetComponent<MeshFilter>();
                Require(filter != null && filter.sharedMesh != null, "Generated bracelet mesh");
                Vector3[] vertices = filter.sharedMesh.vertices;
                Require(vertices.Length == 64 * 4 * 4 && filter.sharedMesh.triangles.Length == 64 * 4 * 6,
                    "Closed 64-segment hollow cuff mesh topology");

                float minRadius = float.PositiveInfinity;
                float maxRadius = 0f;
                float minZ = float.PositiveInfinity;
                float maxZ = float.NegativeInfinity;
                foreach (Vector3 vertex in vertices)
                {
                    float radius = new Vector2(vertex.x, vertex.y).magnitude;
                    minRadius = Mathf.Min(minRadius, radius);
                    maxRadius = Mathf.Max(maxRadius, radius);
                    minZ = Mathf.Min(minZ, vertex.z);
                    maxZ = Mathf.Max(maxZ, vertex.z);
                }

                Require(minRadius >= innerRadius - 0.0001f && maxRadius <= outerRadius + 0.0001f,
                    "Cuff mesh has an open wrist bore and the configured outer radius");
                Require(Near(minZ, proximalZ) && Near(maxZ, distalZ), "Cuff mesh spans the configured wrist depth");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        public static void RunPlaySmoke()
        {
            Validate();
            SessionState.SetBool(SmokeKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void FinishSmoke()
        {
            if (EditorApplication.timeSinceStartup - started < 5) return;
            EditorApplication.update -= FinishSmoke;
            SessionState.SetBool(SmokeKey, false);
            try
            {
                WorkspaceShell shell = UnityEngine.Object.FindAnyObjectByType<WorkspaceShell>();
                Require(shell != null, "Runtime workspace exists");
                WristMount mount = shell.GetComponent<WristMount>();
                Require(mount != null, "Runtime wrist attachment built");
                Require(!mount.Visible, "Absent XR tracking fails closed");
                Require(!AppManager.Instance.IsStreaming, "Smoke never starts robot control");

                WristInteractionSource source = WristInteractionSource.Ensure();
                BraceletRotator rotator = shell.GetComponentInChildren<BraceletRotator>(true);
                Require(rotator != null && rotator.IsConfigured, "Runtime bracelet rotator is configured");
                Require(rotator.Frame == shell.transform, "Wrist frame remains separate and stable from rotating cuff visuals");
                Require(rotator.transform.name == "Wrist presentation", "Rotator runs on the wrist presentation, not the wrist frame");
                Require(Near(rotator.Radius, .055f), "Controller layout uses a 55 mm wrist-frame radius");
                WristUISettings settings = Resources.Load<WristUISettings>("WristUISettings");
                Require(settings != null, "Runtime wrist settings asset");
                float expectedBandZ = -settings.buttonHeight * .5f - settings.dialWidth * .5f - .003f;
                Require(Near(rotator.BandCenterZ, expectedBandZ) && Near(rotator.BandWidth, settings.dialWidth),
                    "Controller/finger drag band matches the cuff geometry");

                Transform rotor = rotator.transform.Find("Bracelet rotor");
                Require(rotor != null, "Rotating cuff visual frame exists");
                BraceletBody body = rotor.GetComponentInChildren<BraceletBody>(true);
                Require(body != null, "Hollow bracelet body is attached to the rotating cuff");
                ValidateRuntimeBody(body, settings);
                ValidateDragSegments(shell, rotor, rotator, settings);
                ValidateNavigationControls(shell, rotor, settings);
                ValidateRecordingPanel(shell, rotator);
                ValidateRecordingPanelPoseAndCapture(source);
                ValidateControllerWristMount(source, settings);
                ValidateModeLayout(shell, source, rotor, rotator, settings);
                ValidateSessionPresentationStates();
                ValidateReadingAnchor(shell, source);

                Require(source.RightRay != null && source.RightPoke != null, "Both Meta interaction modes remain available");
                Require(shell.GetComponentsInChildren<WristCanvasSurface>(true).Length >= DragSegmentCount + NavigationControlCount + 1,
                    "All 24 drag strips, six controls, and details canvas have surfaces");

                // Exercise actual active UI lifetimes without enabling any tracked XR input.
                rotator.gameObject.SetActive(true);
                ExerciseDetailPagesAndKeypad(shell);
                rotator.gameObject.SetActive(false);
                shell.StartCoroutine(WristInputRoutingSmoke.Run(shell, source));
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void ValidateControllerWristFrame()
        {
            MethodInfo build = typeof(WristInteractionSource).GetMethod("TryBuildControllerWristFrame",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(build != null, "Production controller wrist-frame geometry is inspectable");
            Vector3 wrist = new Vector3(.012f, -.007f, .021f);
            Vector3 middle = wrist + new Vector3(0f, 0f, .095f);
            Vector3 index = wrist + new Vector3(.038f, .004f, .084f);
            Vector3 pinky = wrist + new Vector3(-.029f, -.001f, .075f);
            Vector3 forward = Vector3.forward;
            Vector3 dorsal = new Vector3(-.005f, .067f, 0f).normalized;
            foreach (Quaternion rotation in new[] { Quaternion.identity, Quaternion.Euler(17f, 63f, -48f),
                         Quaternion.Euler(-73f, 142f, 119f), Quaternion.Euler(0f, -179f, 180f) })
            {
                Vector3 translation = new Vector3(-.37f, 1.16f, .42f);
                Require(BuildControllerFrame(build, translation + rotation * wrist, translation + rotation * middle,
                    translation + rotation * index, translation + rotation * pinky, out Pose pose),
                    "Nondegenerate controller wrist landmarks produce a frame under arbitrary rigid rotation");
                Require(Near(pose.position, translation + rotation * wrist, .00001f),
                    "Controller wrist frame is centered on the rendered wrist landmark");
                Require(Vector3.Dot(pose.rotation * Vector3.forward, rotation * forward) > .99999f &&
                        Vector3.Dot(pose.rotation * Vector3.up, rotation * dorsal) > .99999f,
                    "Controller frame follows the middle-finger centerline and left dorsal direction");
            }

            Vector3[][] degenerate = {
                new[] { wrist, wrist, index, pinky },
                new[] { wrist, middle, index, index },
                new[] { wrist, middle, wrist + Vector3.forward * .08f, wrist + Vector3.forward * .04f },
                new[] { wrist, wrist + Vector3.forward * .0001f, index, pinky },
                new[] { wrist, middle, pinky + Vector3.right * .0001f, pinky }
            };
            foreach (Vector3[] landmarks in degenerate)
                Require(!BuildControllerFrame(build, landmarks[0], landmarks[1], landmarks[2], landmarks[3], out _),
                    "Degenerate controller wrist landmarks fail closed");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                for (int landmark = 0; landmark < 4; landmark++)
                    for (int axis = 0; axis < 3; axis++)
                    {
                        Vector3[] landmarks = { wrist, middle, index, pinky };
                        landmarks[landmark][axis] = invalid;
                        Require(!BuildControllerFrame(build, landmarks[0], landmarks[1], landmarks[2], landmarks[3], out _),
                            "Non-finite controller wrist landmark components fail closed");
                    }
            Require(!BuildControllerFrame(build, Vector3.one * float.MaxValue, -Vector3.one * float.MaxValue,
                    index, pinky, out _), "Overflowing landmark differences fail closed");
            Debug.Log("DOFFY controller wrist geometry passed: wrist center, middle-finger axis, dorsal direction, rigid rotations and invalid landmarks.");
        }

        private static bool BuildControllerFrame(MethodInfo build, Vector3 wrist, Vector3 middle,
            Vector3 index, Vector3 pinky, out Pose pose)
        {
            object[] arguments = { wrist, middle, index, pinky, Pose.identity };
            bool valid = (bool)build.Invoke(null, arguments);
            pose = (Pose)arguments[4];
            return valid;
        }

        /// <summary>
        /// Uses production HandVisual skeleton updates and WristMount lifetimes synchronously.
        /// Only cached presentation input is seeded; no XR focus, app session or network callbacks are enabled.
        /// </summary>
        private static void ValidateControllerWristMount(WristInteractionSource source, WristUISettings settings)
        {
            Require(!AppManager.Instance.IsStreaming, "Controller wrist fixture starts with an idle robot session");
            Transform originalHead = source.Head;
            HandVisual originalVisual = source.ControllerHandVisual;
            bool originalHands = source.HandsActive;
            bool originalController = source.ControllerModeActive;
            bool originalTracked = source.LeftTracked;
            Pose originalPose = (Pose)ReadField(source, "leftPose");
            GameObject fixture = new GameObject("Controller wrist mount validation");
            GameObject visualObject = new GameObject("Controller hand visual validation");
            GameObject head = new GameObject("Controller wrist validation head");
            WristUISettings config = UnityEngine.Object.Instantiate(settings);
            WristMount mount = null;
            try
            {
                HandVisual visual = visualObject.AddComponent<HandVisual>();
                ControllerHandFixture hand = new ControllerHandFixture();
                visual.InjectAllHandSkeletonVisual(hand, visualObject.AddComponent<SkinnedMeshRenderer>());
                visual.InjectOptionalRoot(visualObject.transform);
                WriteField(visual, "_updateVisibility", false);
                // Joints are populated through the public list selected by this SDK's active skeleton mode.
                for (int i = 0; i < (int)HandJointId.HandEnd; i++)
                {
                    GameObject joint = new GameObject("Controller validation joint " + i);
                    joint.transform.SetParent(visualObject.transform, false);
                    visual.Joints.Add(joint.transform);
                }
                hand.SetPose(new Pose(new Vector3(-.31f, 1.03f, .68f), Quaternion.Euler(21f, 67f, -42f)));
                visual.UpdateSkeleton();
                WriteField(source, "head", head.transform);
                WriteField(source, "controllerHandVisual", visual);
                WriteField(source, "handsActive", false);
                WriteField(source, "controllerModeActive", true);
                WriteField(source, "leftTracked", true);
                Pose controller = new Pose(new Vector3(.63f, .72f, -.25f), Quaternion.Euler(-17f, -53f, 29f));
                WriteField(source, "leftPose", controller);
                Require(source.TryGetControllerWristPose(out Pose rendered), "Controller mode accepts a tracked rendered hand skeleton");
                AssertRenderedWrist(visual, rendered);
                Require(!Near(rendered.position, hand.RootPose.position, .005f) &&
                        !Near(rendered.position, controller.position, .01f),
                    "Rendered wrist landmark differs from both the hand root and controller grip anchors");

                mount = fixture.AddComponent<WristMount>();
                GameObject presentation = new GameObject("Controller wrist validation presentation");
                presentation.transform.SetParent(fixture.transform, false);
                int hides = 0;
                config.followSharpness = 2f;
                mount.Initialize(AppManager.Instance, source, config, presentation, () => hides++);
                TickMount(mount);
                AssertMountPose(mount, rendered, config.controllerEuler, Vector3.zero,
                    "Rendered wrist takes priority without applying the controller fallback offset twice");

                // The actual SDK method emits WhenHandVisualUpdated after writing rendered joints.
                for (int i = 0; i < 32; i++)
                {
                    hand.SetPose(new Pose(new Vector3(-.29f + i * .008f, .94f + i * .003f, .66f - i * .004f),
                        Quaternion.Euler(13f, -120f + i * 7f, -80f + i * 9f)));
                    visual.UpdateSkeleton();
                    Require(source.TryGetControllerWristPose(out rendered), "Rendered wrist remains valid during controller motion");
                    AssertRenderedWrist(visual, rendered);
                    AssertMountPose(mount, rendered, config.controllerEuler, Vector3.zero,
                        "Render-update event synchronizes translation, yaw and roll before another mount tick");
                    TickMount(mount);
                    AssertMountPose(mount, rendered, config.controllerEuler, Vector3.zero,
                        "Controller late update remains centered on the same rendered wrist");
                }

                // Guard every public API dependency without introducing XR data or invoking AppManager events.
                WriteField(source, "controllerModeActive", false);
                Require(!source.TryGetControllerWristPose(out _), "Inactive controller mode cannot use the rendered wrist");
                WriteField(source, "controllerModeActive", true);
                WriteField(source, "leftTracked", false);
                Require(!source.TryGetControllerWristPose(out _), "Lost controller tracking rejects stale rendered landmarks");
                WriteField(source, "leftTracked", true);
                hand.Connected = false;
                Require(!source.TryGetControllerWristPose(out _), "Disconnected hand skeleton rejects its rendered wrist");
                hand.Connected = true;
                hand.Tracked = false;
                Require(!source.TryGetControllerWristPose(out _), "Invalid hand data rejects its rendered wrist");
                hand.Tracked = true;
                Transform index = visual.Joints[(int)HandJointId.HandIndex1];
                visual.Joints[(int)HandJointId.HandIndex1] = null;
                Require(!source.TryGetControllerWristPose(out _), "A missing rendered landmark rejects the controller wrist");
                visual.Joints[(int)HandJointId.HandIndex1] = index;
                visual.enabled = false;
                Require(!source.TryGetControllerWristPose(out _), "Disabled hand visual rejects stale rendered wrist data");

                for (int i = 0; i < 32; i++)
                {
                    controller = new Pose(new Vector3(.13f + i * .007f, 1.12f - i * .004f, -.16f + i * .005f),
                        Quaternion.Euler(-23f, -110f + i * 8f, -90f + i * 11f));
                    WriteField(source, "leftPose", controller);
                    TickMount(mount);
                    AssertMountPose(mount, controller, config.controllerEuler, config.controllerOffset,
                        "Controller fallback follows translation, yaw and roll immediately with its configured offset");
                }

                // Switching to hands retains the authored hand offset and its existing follow blend.
                WriteField(source, "handsActive", true);
                WriteField(source, "controllerModeActive", false);
                Pose handPose = new Pose(new Vector3(-.2f, .91f, .72f), Quaternion.Euler(31f, 42f, 23f));
                WriteField(source, "leftPose", handPose);
                TickMount(mount);
                AssertMountPose(mount, handPose, config.handEuler, config.handOffset,
                    "Switching to hand tracking preserves the authored hand offset");
                Vector3 previousPosition = mount.transform.position;
                Quaternion previousRotation = mount.transform.rotation;
                handPose = new Pose(handPose.position + new Vector3(.21f, -.09f, .13f), Quaternion.Euler(-8f, 98f, -41f));
                WriteField(source, "leftPose", handPose);
                TickMount(mount);
                float blend = 1f - Mathf.Exp(-config.followSharpness * Time.unscaledDeltaTime);
                Require(Near(mount.transform.position, Vector3.Lerp(previousPosition,
                            handPose.position + handPose.rotation * config.handOffset, blend), .00001f) &&
                        Quaternion.Angle(mount.transform.rotation, Quaternion.Slerp(previousRotation,
                            handPose.rotation * Quaternion.Euler(config.handEuler), blend)) < .05f,
                    "Hand tracking retains its existing follow smoothing");
                WriteField(source, "handsActive", false);
                WriteField(source, "controllerModeActive", true);
                WriteField(source, "leftPose", controller);
                TickMount(mount);
                AssertMountPose(mount, controller, config.controllerEuler, config.controllerOffset,
                    "Switching back to controllers restores the correct fallback offset immediately");

                visual.enabled = true;
                visual.UpdateSkeleton();
                Require(source.TryGetControllerWristPose(out rendered), "Rendered tracking recovery produces a current wrist");
                AssertMountPose(mount, rendered, config.controllerEuler, Vector3.zero,
                    "Render tracking recovery resynchronizes the cuff without interpolation");
                presentation.SetActive(true); // Seed visibility only to verify the production loss path.
                int beforeLoss = hides;
                WriteField(source, "leftTracked", false);
                TickMount(mount);
                Require(!mount.Visible && !(bool)ReadField(mount, "poseInitialized") && hides == beforeLoss + 1,
                    "Tracking loss hides the cuff, cancels its presentation and resets pose initialization");
                Vector3 lostPosition = mount.transform.position;
                hand.SetPose(new Pose(new Vector3(.47f, .89f, -.32f), Quaternion.Euler(-19f, 143f, 71f)));
                visual.UpdateSkeleton();
                Require(Near(mount.transform.position, lostPosition, .00001f),
                    "Render updates during controller tracking loss cannot move the cuff from stale data");
                WriteField(source, "leftTracked", true);
                TickMount(mount);
                Require(source.TryGetControllerWristPose(out rendered), "Controller tracking recovery accepts the latest rendered wrist");
                AssertMountPose(mount, rendered, config.controllerEuler, Vector3.zero,
                    "Tracking recovery snaps to the latest rendered wrist before presentation resumes");
                Require((bool)ReadField(mount, "poseInitialized") && (source.CanPresentWrist || !mount.Visible),
                    "Recovered pose still respects the live XR focus presentation gate");

                mount.enabled = false;
                Vector3 disabledPosition = mount.transform.position;
                Require(ReadField(mount, "controllerHandVisual") == null, "Disabling the mount clears the render-update subscription");
                hand.SetPose(new Pose(new Vector3(-.43f, 1.19f, .59f), Quaternion.Euler(14f, -132f, -64f)));
                visual.UpdateSkeleton();
                Require(Near(mount.transform.position, disabledPosition, .00001f),
                    "Disabled mount cannot move on rendered skeleton callbacks");
                mount.enabled = true;
                TickMount(mount);
                hand.SetPose(new Pose(new Vector3(-.37f, 1.08f, .63f), Quaternion.Euler(19f, -92f, 52f)));
                visual.UpdateSkeleton();
                Require(source.TryGetControllerWristPose(out rendered), "Reenabled mount receives valid rendered wrist data");
                AssertMountPose(mount, rendered, config.controllerEuler, Vector3.zero,
                    "Reenabling binds the render callback and synchronizes the latest wrist");
                Debug.Log("DOFFY controller wrist mount smoke passed: rendered wrist priority, no duplicate offset, 32 render updates, 32 fallback motion steps, hand-mode preservation, loss/recovery and disable/rebind; device fit remains unverified.");
            }
            finally
            {
                if (mount != null) mount.enabled = false;
                WriteField(source, "head", originalHead);
                WriteField(source, "controllerHandVisual", originalVisual);
                WriteField(source, "handsActive", originalHands);
                WriteField(source, "controllerModeActive", originalController);
                WriteField(source, "leftTracked", originalTracked);
                WriteField(source, "leftPose", originalPose);
                UnityEngine.Object.DestroyImmediate(fixture);
                UnityEngine.Object.DestroyImmediate(visualObject);
                UnityEngine.Object.DestroyImmediate(head);
                UnityEngine.Object.DestroyImmediate(config);
            }
            Require(!AppManager.Instance.IsStreaming, "Controller wrist fixture restores idle presentation without robot commands");
        }

        private static void TickMount(WristMount mount) { Invoke(mount, "Update"); Invoke(mount, "LateUpdate"); }

        private static void AssertRenderedWrist(HandVisual visual, Pose pose)
        {
            Vector3 wrist = visual.Joints[(int)HandJointId.HandWristRoot].position;
            Vector3 forward = (visual.Joints[(int)HandJointId.HandMiddle1].position - wrist).normalized;
            Vector3 across = visual.Joints[(int)HandJointId.HandIndex1].position -
                             visual.Joints[(int)HandJointId.HandPinky1].position;
            Require(Near(pose.position, wrist, .00001f) &&
                    Vector3.Dot(pose.rotation * Vector3.forward, forward) > .99999f &&
                    Vector3.Dot(pose.rotation * Vector3.up, Vector3.Cross(forward, across).normalized) > .99999f,
                "Controller wrist API uses the current rendered wrist/middle/index/pinky world landmarks");
        }

        private static void AssertMountPose(WristMount mount, Pose wrist, Vector3 euler, Vector3 offset, string description)
        {
            Require(Near(mount.transform.position, wrist.position + wrist.rotation * offset, .00001f) &&
                    Quaternion.Angle(mount.transform.rotation, wrist.rotation * Quaternion.Euler(euler)) < .05f,
                description);
        }

        private sealed class ControllerHandFixture : IHand
        {
            public bool Connected = true, Tracked = true;
            public Pose RootPose;
            private readonly Pose[] joints = new Pose[(int)HandJointId.HandEnd];
            public Handedness Handedness => Handedness.Left;
            public bool IsConnected => Connected;
            public bool IsTrackedDataValid => Tracked;
            public bool IsHighConfidence => Connected && Tracked;
            public bool IsDominantHand => false;
            public float Scale => 1.13f;
            public bool IsPointerPoseValid => false;
            public int CurrentDataVersion => 1;
            public event Action WhenHandUpdated { add { } remove { } }
            public void SetPose(Pose root)
            {
                RootPose = root;
                for (int i = 0; i < joints.Length; i++) joints[i] = Pose.identity;
                Vector3 wrist = new Vector3(.012f, -.007f, .021f);
                joints[(int)HandJointId.HandWristRoot] = new Pose(wrist, Quaternion.identity);
                joints[(int)HandJointId.HandMiddle1] = new Pose(wrist + Vector3.forward * .095f, Quaternion.identity);
                joints[(int)HandJointId.HandIndex1] = new Pose(wrist + new Vector3(.038f, .004f, .084f), Quaternion.identity);
                joints[(int)HandJointId.HandPinky1] = new Pose(wrist + new Vector3(-.029f, -.001f, .075f), Quaternion.identity);
            }
            public bool GetRootPose(out Pose pose) { pose = RootPose; return Tracked; }
            public bool GetJointPosesLocal(out ReadOnlyHandJointPoses poses) { poses = new ReadOnlyHandJointPoses(joints); return Tracked; }
            public bool GetJointPoseLocal(HandJointId id, out Pose pose) { pose = joints[(int)id]; return Tracked; }
            public bool GetJointPose(HandJointId id, out Pose pose) { pose = Pose.identity; return false; }
            public bool GetJointPoseFromWrist(HandJointId id, out Pose pose) { pose = Pose.identity; return false; }
            public bool GetJointPosesFromWrist(out ReadOnlyHandJointPoses poses) { poses = ReadOnlyHandJointPoses.Empty; return false; }
            public bool GetPointerPose(out Pose pose) { pose = Pose.identity; return false; }
            public bool GetPalmPoseLocal(out Pose pose) { pose = Pose.identity; return false; }
            public bool GetFingerIsPinching(HandFinger finger) => false;
            public bool GetIndexFingerIsPinching() => false;
            public bool GetFingerIsHighConfidence(HandFinger finger) => false;
            public float GetFingerPinchStrength(HandFinger finger) => 0f;
        }

        private static void ValidateRuntimeBody(BraceletBody body, WristUISettings settings)
        {
            MeshFilter filter = body.GetComponent<MeshFilter>();
            Require(filter != null && filter.sharedMesh != null, "Runtime cuff mesh exists");
            float expectedOuter = settings.radius - .0015f;
            float expectedInner = expectedOuter - .003f;
            float minRadius = float.PositiveInfinity;
            float maxRadius = 0f;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                float radius = new Vector2(vertex.x, vertex.y).magnitude;
                minRadius = Mathf.Min(minRadius, radius);
                maxRadius = Mathf.Max(maxRadius, radius);
            }
            Require(minRadius >= expectedInner - .0001f && maxRadius <= expectedOuter + .0001f,
                "Runtime cuff mesh remains hollow and dimensioned around the 55 mm wrist frame");
        }

        private static void ValidateDragSegments(WorkspaceShell shell, Transform rotor,
            BraceletRotator rotator, WristUISettings settings)
        {
            BraceletDragHandle[] handles = shell.GetComponentsInChildren<BraceletDragHandle>(true);
            Require(handles.Length == DragSegmentCount, "Exactly 24 controller/finger drag handles were built");

            for (int i = 0; i < DragSegmentCount; i++)
            {
                Transform canvasTransform = rotor.Find("Bracelet drag segment " + i);
                Require(canvasTransform != null, "Drag canvas " + i + " exists under the rotating cuff");
                Require(canvasTransform.GetComponent<Canvas>() != null &&
                        canvasTransform.GetComponent<PointableCanvas>() != null,
                    "Drag canvas " + i + " owns a world-space PointableCanvas");
                BraceletDragHandle[] onCanvas = canvasTransform.GetComponentsInChildren<BraceletDragHandle>(true);
                Require(onCanvas.Length == 1 && onCanvas[0].transform.IsChildOf(canvasTransform),
                    "Drag canvas " + i + " has exactly one initialized drag handle");
                WristCanvasSurface surface = canvasTransform.GetComponentInChildren<WristCanvasSurface>(true);
                Require(surface != null && surface.Ray != null, "Drag canvas " + i + " has its own Meta surface");

                Vector3 position = canvasTransform.localPosition;
                Require(Near(new Vector2(position.x, position.y).magnitude, settings.radius) &&
                        Near(position.z, rotator.BandCenterZ), "Drag canvas " + i + " lies on the configured cylindrical band");

                float angle = i * 360f / DragSegmentCount * Mathf.Deg2Rad;
                Vector3 tangent = new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0f);
                Vector3 localRight = canvasTransform.localRotation * Vector3.right;
                Require(Mathf.Abs(Vector3.Dot(localRight, tangent)) > .99f,
                    "Drag canvas " + i + " is tangent to the cuff at its segment");
            }
        }

        private static void ValidateNavigationControls(WorkspaceShell shell, Transform rotor,
            WristUISettings settings)
        {
            const int count = NavigationControlCount;
            int actualControls = 0;
            foreach (Transform child in rotor)
                if (child.name.StartsWith("Bracelet control ", StringComparison.Ordinal)) actualControls++;
            Require(actualControls == count, "Exactly six menu controls surround the bracelet");
            for (int i = 0; i < count; i++)
            {
                Transform canvasTransform = rotor.Find("Bracelet control " + i);
                Require(canvasTransform != null, "Navigation canvas " + i + " exists under the rotating cuff");
                Require(canvasTransform.GetComponent<Canvas>() != null &&
                        canvasTransform.GetComponent<PointableCanvas>() != null,
                    "Navigation canvas " + i + " owns its PointableCanvas");
                Button button = canvasTransform.GetComponentInChildren<Button>(true);
                Require(button != null,
                    "Navigation canvas " + i + " owns its button");
                TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                Require(label != null && label.text.Replace('\n', ' ').Trim() == NavigationLabels[i],
                    "Menu order at ring position " + i + ": " + NavigationLabels[i]);
                WristCanvasSurface surface = canvasTransform.GetComponentInChildren<WristCanvasSurface>(true);
                Require(surface != null && surface.Ray != null,
                    "Navigation canvas " + i + " owns its Meta wrist surface");
                Require(Near(new Vector2(canvasTransform.localPosition.x, canvasTransform.localPosition.y).magnitude,
                    settings.radius), "Navigation canvas " + i + " sits at the bracelet radius");
                float radians = (90f - i * 360f / count) * Mathf.Deg2Rad;
                Require(Near(canvasTransform.localPosition,
                    new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * settings.radius),
                    "Menu controls follow the stated order around the ring");
            }
        }

        private static void ValidateRecordingPanel(WorkspaceShell shell, BraceletRotator rotator)
        {
            TeleopRecordPanel[] panels = UnityEngine.Object.FindObjectsByType<TeleopRecordPanel>(
                FindObjectsInactive.Include);
            Require(panels.Length == 1, "Exactly one independent teleoperation recording panel exists");
            TeleopRecordPanel panel = panels[0];
            Require(panel.transform.parent == null && !panel.transform.IsChildOf(rotator.transform),
                "Recording panel root is outside the wrist presentation lifetime");

            Canvas[] canvases = panel.GetComponentsInChildren<Canvas>(true);
            Require(canvases.Length == 1 && canvases[0].name == "Teleop record panel canvas" &&
                    canvases[0].renderMode == RenderMode.WorldSpace,
                "Recording panel has its own world-space canvas");
            Require(canvases[0].GetComponent<PointableCanvas>() != null,
                "Recording canvas has its own PointableCanvas");
            WristCanvasSurface surface = canvases[0].GetComponent<WristCanvasSurface>();
            Require(surface != null && surface.Ray != null, "Recording canvas has one separate Meta wrist surface");
            FieldInfo recordingOnlyField = typeof(WristCanvasSurface).GetField("recordingOnly",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(recordingOnlyField != null && (bool)recordingOnlyField.GetValue(surface),
                "Recording surface uses the recording-only interaction filter");

            Button[] buttons = canvases[0].GetComponentsInChildren<Button>(true);
            Require(buttons.Length == 3, "Session canvas exposes recording, undo and quit actions");
            Require(FindButton(panel, "Start recording") != null && FindButton(panel, "Quit teleop") != null && FindButton(panel, "Undo episode") != null,
                "Session panel offers Start recording, Undo episode and Quit teleop");
            TeleopPanelDragHandle[] handles = panel.GetComponentsInChildren<TeleopPanelDragHandle>(true);
            Require(handles.Length == 1 && handles[0].name == "Session panel drag bar",
                "Exactly one separate bottom drag bar owns panel movement");
            TeleopPanelDragHandle handle = handles[0];
            Require(handle.GetComponentsInChildren<TMP_Text>(true).Length == 0,
                "The session bottom drag bar is text-free");
            Require(handle.GetComponent<Graphic>() != null && handle.GetComponent<Graphic>().raycastTarget &&
                    handle.GetComponentInParent<Button>() == null && handle.GetComponentInChildren<Button>(true) == null,
                "Drag bar is a raycastable Graphic outside all three button event hierarchies");
            Require(ReferenceEquals(ReadField(handle, "target"), panel.transform) &&
                    ReferenceEquals(ReadField(handle, "source"), WristInteractionSource.Ensure()),
                "Drag bar binds the standalone panel and current Meta input source");
            RectTransform canvasRect = (RectTransform)canvases[0].transform;
            Rect handleBounds = BoundsInCanvas((RectTransform)handle.transform, canvasRect);
            Require(canvasRect.rect.Contains(handleBounds.min) && canvasRect.rect.Contains(handleBounds.max),
                "Drag bar remains inside the expanded session canvas");
            foreach (Button button in buttons)
            {
                Require(button.GetComponentInParent<TeleopPanelDragHandle>() == null &&
                        button.GetComponentInChildren<TeleopPanelDragHandle>(true) == null,
                    "Button presses cannot capture panel movement: " + button.name);
                Require(handleBounds.yMax < BoundsInCanvas((RectTransform)button.transform, canvasRect).yMin,
                    "Bottom drag bar has a physical gap below the action tile: " + button.name);
            }
            Require(panel.GetComponent<IDragHandler>() == null && canvases[0].GetComponent<IDragHandler>() == null,
                "Panel background and canvas cannot become movement targets");
            foreach (Button tile in shell.GetComponentsInChildren<Button>(true))
                Require(tile.name != "Record" && tile.name != "Start recording", "No recording tile remains on the bracelet");
            Require(!AppManager.Instance.IsStreaming, "Independent recorder smoke remains outside teleoperation");
            Require(!canvases[0].gameObject.activeSelf,
                "Recording canvas stays hidden when teleoperation is inactive");
        }

        /// <summary>
        /// Exercises production pose/lifetime methods synchronously, with only presentation-state
        /// backing fields changed. It never invokes the coordinator, recording, or robot callbacks.
        /// Capture seeds below validate release/cancellation; acquiring physical XR capture is separate.
        /// </summary>
        private static void ValidateRecordingPanelPoseAndCapture(WristInteractionSource source)
        {
            TeleopRecordPanel panel = UnityEngine.Object.FindAnyObjectByType<TeleopRecordPanel>();
            TeleopPanelDragHandle handle = panel.GetComponentInChildren<TeleopPanelDragHandle>(true);
            Canvas canvas = panel.GetComponentInChildren<Canvas>(true);
            object coordinator = ReadField(AppManager.Instance, "_session");
            object state = ReadField(coordinator, "state");
            object originalState = ReadField(state, "<State>k__BackingField");
            object originalStreaming = ReadField(state, "<IsStreaming>k__BackingField");
            Transform originalHead = source.Head;
            bool originalHands = source.HandsActive;
            bool originalController = source.ControllerModeActive;
            bool originalTracked = source.RightInputTracked;
            bool originalPoseInitialized = (bool)ReadField(panel, "poseInitialized");
            Vector3 originalForward = (Vector3)ReadField(panel, "lastHorizontalForward");
            Vector3 originalPosition = panel.transform.position;
            Quaternion originalRotation = panel.transform.rotation;
            Vector3 rootScale = panel.transform.localScale;
            Vector3 canvasScale = canvas.transform.localScale;
            bool originalVisible = canvas.gameObject.activeSelf;
            GameObject head = new GameObject("Session panel validation head");
            try
            {
                WriteField(source, "head", head.transform);
                WriteField(source, "rightInputTracked", false);
                SetPanelSession(state, false);
                TickPanel(panel);
                head.transform.SetPositionAndRotation(new Vector3(-.21f, 1.67f, .34f), Quaternion.Euler(27f, 65f, 18f));
                SetPanelSession(state, true);
                TickPanel(panel);
                Vector3 opening = ExpectedSessionPosition(head.transform);
                Require(canvas.gameObject.activeSelf && Near(panel.transform.position, opening, .0001f),
                    "Session opening places the panel 0.43 m horizontally forward and 0.22 m below the head");
                Require(Vector3.Dot(panel.transform.up, Vector3.up) > .9999f,
                    "Session opening ignores head pitch and roll");

                head.transform.SetPositionAndRotation(head.transform.position + new Vector3(.7f, .45f, -.25f),
                    Quaternion.Euler(-41f, -85f, 73f));
                FacePanel(panel);
                Require(Near(panel.transform.position, opening, .00001f),
                    "Head translation and rotation cannot move the session panel");
                Require(FacesHeadHorizontally(panel.transform, head.transform),
                    "Stationary panel smoothly converges to a horizontal user-facing rotation");
                Require(Near(panel.transform.localScale, rootScale, .000001f) &&
                        Near(canvas.transform.localScale, canvasScale, .000001f),
                    "Session facing never scales the panel or its canvas");

                Vector3 draggedPosition = opening + new Vector3(-.26f, .13f, .38f);
                panel.transform.SetPositionAndRotation(draggedPosition, Quaternion.Euler(0f, -127f, 0f));
                Quaternion capturedRotation = panel.transform.rotation;
                SeedCapture(handle, false, source.RightRay.Identifier);
                FacePanel(panel);
                Require(Near(panel.transform.position, draggedPosition, .00001f) &&
                        Quaternion.Angle(panel.transform.rotation, capturedRotation) < .001f,
                    "Held drag owns the pose and suspends automatic facing");
                handle.CancelDrag();
                FacePanel(panel);
                Require(Near(panel.transform.position, draggedPosition, .00001f) &&
                        FacesHeadHorizontally(panel.transform, head.transform),
                    "Releasing resumes horizontal facing at the user-chosen world position");
                Quaternion priorFacing = panel.transform.rotation;
                head.transform.position = panel.transform.position + Vector3.up * .8f;
                FacePanel(panel);
                Require(Quaternion.Angle(panel.transform.rotation, priorFacing) < .001f,
                    "A vertically aligned head preserves the last valid yaw");

                WriteField(source, "head", null);
                SeedCapture(handle, true, source.RightPoke.Identifier);
                TickPanel(panel);
                Require(!canvas.gameObject.activeSelf && !handle.IsDragging,
                    "Head-pose loss hides the panel and cancels capture");
                WriteField(source, "head", head.transform);
                TickPanel(panel);
                Require(Near(panel.transform.position, draggedPosition, .00001f),
                    "Head-pose recovery in the same session keeps the chosen panel position");

                SetPanelSession(state, false);
                SeedCapture(handle, false, source.RightRay.Identifier);
                TickPanel(panel);
                Require(!canvas.gameObject.activeSelf && !handle.IsDragging,
                    "Ending the session hides the panel and releases its drag");
                head.transform.SetPositionAndRotation(new Vector3(.61f, 1.39f, -.43f), Quaternion.Euler(-18f, -103f, 51f));
                SetPanelSession(state, true);
                TickPanel(panel);
                Require(Near(panel.transform.position, ExpectedSessionPosition(head.transform), .0001f) &&
                        !Near(panel.transform.position, draggedPosition, .01f),
                    "A new session resets placement from the current head pose");

                ValidatePanelPointerRelease(handle, source);
                Debug.Log("DOFFY session panel pose/capture smoke passed: fixed position, horizontal facing, fresh session placement, bar isolation and safe cancellation; XR acquisition requires device validation.");
            }
            finally
            {
                handle.CancelDrag();
                WriteField(state, "<State>k__BackingField", originalState);
                WriteField(state, "<IsStreaming>k__BackingField", originalStreaming);
                WriteField(source, "head", originalHead);
                WriteField(source, "handsActive", originalHands);
                WriteField(source, "controllerModeActive", originalController);
                WriteField(source, "rightInputTracked", originalTracked);
                WriteField(panel, "poseInitialized", originalPoseInitialized);
                WriteField(panel, "lastHorizontalForward", originalForward);
                panel.transform.SetPositionAndRotation(originalPosition, originalRotation);
                canvas.gameObject.SetActive(originalVisible);
                UnityEngine.Object.DestroyImmediate(head);
            }
            Require(!AppManager.Instance.IsStreaming, "Presentation fixture restores the idle robot session before yielding");
        }

        internal static void ValidatePanelPointerRelease(TeleopPanelDragHandle handle, WristInteractionSource source)
        {
            foreach (bool hands in new[] { false, true })
            {
                WriteField(source, "handsActive", hands);
                WriteField(source, "controllerModeActive", !hands);
                int accepted = hands ? source.RightPoke.Identifier : source.RightRay.Identifier;
                int rejected = hands ? source.RightRay.Identifier : source.RightPoke.Identifier;
                PointerEventData pointer = new PointerEventData(EventSystem.current) { pointerId = accepted };
                handle.OnInitializePotentialDrag(pointer);
                Require(!pointer.useDragThreshold, "Selected Meta pointer disables the drag threshold: " + (hands ? "poke" : "ray"));
                PointerEventData other = new PointerEventData(EventSystem.current) { pointerId = rejected };
                handle.OnInitializePotentialDrag(other);
                Require(other.useDragThreshold, "Inactive Meta modality cannot configure panel dragging");
                handle.OnPointerDown(pointer);
                Require(!handle.IsDragging, "Untracked input cannot acquire panel capture");
                SeedCapture(handle, hands, accepted);
                handle.OnPointerUp(other);
                Require(handle.IsDragging, "Another pointer cannot release the active panel capture");
                handle.OnPointerUp(pointer);
                Require(!handle.IsDragging, "Owner pointer releases the capture safely: " + (hands ? "poke" : "ray"));
            }

            SeedCapture(handle, false, source.RightRay.Identifier);
            Invoke(handle, "LateUpdate");
            Require(!handle.IsDragging, "Lost tracking/input availability cancels captured panel movement");
            SeedCapture(handle, true, source.RightPoke.Identifier);
            Invoke(handle, "OnApplicationFocus", false);
            Require(!handle.IsDragging, "Application focus loss cancels capture immediately");
            SeedCapture(handle, false, source.RightRay.Identifier);
            Invoke(handle, "OnApplicationPause", true);
            Require(!handle.IsDragging, "Application pause cancels capture immediately");
            SeedCapture(handle, true, source.RightPoke.Identifier);
            handle.enabled = false;
            Require(!handle.IsDragging, "Disabling the drag bar cannot leave a held capture");
            handle.enabled = true;
        }

        private static void SeedCapture(TeleopPanelDragHandle handle, bool hands, int pointer)
        {
            WriteField(handle, "handCapture", hands);
            WriteField(handle, "pointerId", pointer);
            WriteField(handle, "<IsDragging>k__BackingField", true);
        }

        private static void SetPanelSession(object state, bool open)
        {
            WriteField(state, "<IsStreaming>k__BackingField", open);
            WriteField(state, "<State>k__BackingField", open ? TeleopSessionState.Starting : TeleopSessionState.Idle);
        }

        private static void TickPanel(TeleopRecordPanel panel) { Invoke(panel, "Update"); Invoke(panel, "LateUpdate"); }
        private static void FacePanel(TeleopRecordPanel panel)
        {
            Require(Time.unscaledDeltaTime > 0f, "Play provides a positive unscaled frame interval for smooth facing");
            // Batchmode can run thousands of frames per second. Accumulate three seconds
            // of the production blend rather than assuming a fixed display frame rate.
            int steps = Mathf.CeilToInt(3f / Time.unscaledDeltaTime);
            for (int i = 0; i < steps; i++) Invoke(panel, "LateUpdate");
        }
        private static Vector3 ExpectedSessionPosition(Transform head) => head.position +
            Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized * .43f + Vector3.down * .22f;
        private static bool FacesHeadHorizontally(Transform panel, Transform head) =>
            Vector3.Dot(panel.up, Vector3.up) > .9999f && Quaternion.Angle(panel.rotation,
                Quaternion.LookRotation(Vector3.ProjectOnPlane(panel.position - head.position, Vector3.up), Vector3.up)) < .1f;

        private static Rect BoundsInCanvas(RectTransform child, RectTransform canvas)
        {
            Vector3[] corners = new Vector3[4];
            child.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (Vector3 corner in corners)
            {
                Vector2 local = canvas.InverseTransformPoint(corner);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static object ReadField(object owner, string name)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Inspectable fixture field: " + owner.GetType().Name + "." + name);
            return field.GetValue(owner);
        }
        private static void WriteField(object owner, string name, object value)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Writable fixture field: " + owner.GetType().Name + "." + name);
            field.SetValue(owner, value);
        }
        private static void Invoke(object owner, string name, params object[] arguments)
        {
            MethodInfo method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null, "Inspectable fixture method: " + owner.GetType().Name + "." + name);
            method.Invoke(owner, arguments);
        }

        private static void ValidateModeLayout(WorkspaceShell shell, WristInteractionSource source,
            Transform rotor, BraceletRotator rotator, WristUISettings settings)
        {
            FieldInfo hands = typeof(WristInteractionSource).GetField("handsActive", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo apply = typeof(WorkspaceShell).GetMethod("ApplyInputLayout", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(hands != null && apply != null, "Input-specific layout can be exercised");
            bool original = (bool)hands.GetValue(source);
            try
            {
                foreach (bool handMode in new[] { false, true, false })
                {
                    hands.SetValue(source, handMode);
                    apply.Invoke(shell, null);
                    float scale = handMode ? settings.handBraceletScale : 1f;
                    Require(Near(rotor.localScale, Vector3.one * scale), "Cuff visual scale follows the current input mode");
                    Vector3 tilePosition = shell.transform.InverseTransformPoint(rotor.Find("Bracelet control 0").position);
                    Require(Near(new Vector2(tilePosition.x, tilePosition.y).magnitude, rotator.Radius),
                        "Finger/ray drag radius matches the actual scaled cuff in both input modes");
                    Require(Near(rotator.Radius, settings.radius * scale), "Configured drag radius uses metres after scaling");
                    Transform page = shell.transform.Find("Wrist presentation/Wrist details");
                    Require(page != null && Near(page.localScale.x, handMode ? settings.handPageScale : settings.pageScale, .000001f),
                        "Details and nested keyboard share the compact input-mode scale");
                }
            }
            finally
            {
                hands.SetValue(source, original);
                apply.Invoke(shell, null);
            }
        }

        private static void ValidateSessionPresentationStates()
        {
            var state = new TeleopSessionStateMachine();
            Require(!WristInteractionSource.IsSessionOpen(state.State, state.IsStreaming), "Idle shows the bracelet");
            state.BeginStart();
            Require(WristInteractionSource.IsSessionOpen(state.State, state.IsStreaming), "Start immediately requests the session panel");
            state.BeginVideoConnecting();
            Require(WristInteractionSource.IsSessionOpen(state.State, state.IsStreaming), "Panel remains during connection negotiation");
            state.MarkStreaming();
            Require(WristInteractionSource.IsSessionOpen(state.State, state.IsStreaming), "Panel persists without reading controller grip");
            state.MarkTrackingLost("test", true);
            state.MarkRecalibrationFailed("test");
            Require(WristInteractionSource.IsSessionOpen(state.State, state.IsStreaming), "Quit remains reachable during a paused session with an error");
            state.BeginStopping();
            Require(!WristInteractionSource.IsSessionOpen(state.State, state.IsStreaming), "Stopping closes the session panel");
            state.CompleteStop();
            state.SetError("failed start");
            Require(!WristInteractionSource.IsSessionOpen(state.State, state.IsStreaming), "Failed idle startup permits the bracelet again");
        }

        private static void ValidateReadingAnchor(WorkspaceShell shell, WristInteractionSource source)
        {
            WristDetailAnchor anchor = shell.GetComponentInChildren<WristDetailAnchor>(true);
            Require(anchor != null, "Detail pages have an independent reading orientation anchor");
            FieldInfo poseField = typeof(WristInteractionSource).GetField("leftPose", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo trackedField = typeof(WristInteractionSource).GetField("leftTracked", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo headField = typeof(WristInteractionSource).GetField("head", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo settingsField = typeof(WorkspaceShell).GetField("wristSettings", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(poseField != null && trackedField != null && headField != null && settingsField != null,
                "Reading-anchor validation can access its tracked pose and settings");

            Pose original = (Pose)poseField.GetValue(source);
            bool tracked = (bool)trackedField.GetValue(source);
            Transform originalHead = (Transform)headField.GetValue(source);
            WristUISettings settings = (WristUISettings)settingsField.GetValue(shell);
            Require(settings != null, "Reading-anchor settings are available");

            Quaternion originalShellRotation = shell.transform.rotation;
            Transform braceletRotor = shell.transform.Find("Wrist presentation/Bracelet rotor");
            Require(braceletRotor != null, "Independent bracelet rotor exists beside the details canvas");
            Quaternion originalRotorRotation = braceletRotor.localRotation;
            bool originalActive = anchor.gameObject.activeSelf;
            Vector3 originalAnchorPosition = anchor.transform.position;
            Quaternion originalAnchorRotation = anchor.transform.rotation;
            GameObject headFixture = new GameObject("Reading anchor validation head");
            try
            {
                trackedField.SetValue(source, true);
                // The fixture deliberately makes the old head-to-wrist opening direction
                // disagree with the hand's +65-degree absolute yaw.
                Vector3 wristPosition = new Vector3(-.37f, .81f, 1.24f);
                headFixture.transform.SetPositionAndRotation(
                    wristPosition + new Vector3(.31f, .47f, -.22f), Quaternion.Euler(25f, 153f, 17f));
                headField.SetValue(source, headFixture.transform);

                const float openingYaw = 65f;
                Pose wrist = ReadingTestPose(wristPosition, openingYaw);
                poseField.SetValue(source, wrist);
                anchor.gameObject.SetActive(false);
                anchor.gameObject.SetActive(true);
                anchor.CaptureReadingPose();
                anchor.RefreshPose();

                Quaternion openingRotation = ExpectedReadingRotation(openingYaw);
                Vector3 openingPosition = ExpectedReadingPosition(
                    wristPosition, openingYaw, settings, source.HandsActive);
                Require(Quaternion.Angle(openingRotation, anchor.transform.rotation) < .05f,
                    "Opening immediately uses the hand's absolute +65-degree yaw with a fixed 45-degree pitch and zero roll");
                Require(Near(openingPosition, anchor.transform.position, .0001f),
                    "Opening attachment offset uses the same absolute yaw as the reading panel");

                // Neither the headset pose nor rotations in the bracelet hierarchy can seed
                // a separate reading direction or move the world-space attachment offset.
                headFixture.transform.SetPositionAndRotation(
                    wristPosition + new Vector3(-.8f, .1f, .6f), Quaternion.Euler(70f, 12f, 95f));
                anchor.RefreshPose();
                Require(Quaternion.Angle(openingRotation, anchor.transform.rotation) < .05f &&
                        Near(openingPosition, anchor.transform.position, .0001f),
                    "Moving and rotating the headset does not change reading orientation or attachment offset");
                headField.SetValue(source, null);
                anchor.RefreshPose();
                Require(Quaternion.Angle(openingRotation, anchor.transform.rotation) < .05f &&
                        Near(openingPosition, anchor.transform.position, .0001f),
                    "Reading orientation and attachment remain valid without a headset transform");

                braceletRotor.localRotation = Quaternion.Euler(35f, 90f, 70f);
                shell.transform.rotation = Quaternion.Euler(110, 240, 155);
                anchor.RefreshPose();
                Require(Quaternion.Angle(openingRotation, anchor.transform.rotation) < .05f &&
                        Near(openingPosition, anchor.transform.position, .0001f),
                    "Independent bracelet and parent rotations cannot steer the reading panel or its offset");

                poseField.SetValue(source, ReadingTestPose(wristPosition, openingYaw, 38f, 0f));
                anchor.RefreshPose();
                Require(Quaternion.Angle(openingRotation, anchor.transform.rotation) < .05f,
                    "Wrist pitch leaves the absolute horizontal reading yaw unchanged");
                poseField.SetValue(source, ReadingTestPose(wristPosition, openingYaw, 0f, 143f));
                anchor.RefreshPose();
                Require(Quaternion.Angle(openingRotation, anchor.transform.rotation) < .05f &&
                        Near(openingPosition, anchor.transform.position, .0001f),
                    "Wrist roll leaves the absolute horizontal reading yaw and attachment offset unchanged");

                Vector3 turnedPosition = wristPosition + new Vector3(.13f, -.04f, .09f);
                const float turnedYaw = 125f;
                poseField.SetValue(source, ReadingTestPose(turnedPosition, turnedYaw));
                anchor.RefreshPose();
                Quaternion turnedRotation = ExpectedReadingRotation(turnedYaw);
                Vector3 turnedAnchorPosition = ExpectedReadingPosition(
                    turnedPosition, turnedYaw, settings, source.HandsActive);
                Require(Quaternion.Angle(turnedRotation, anchor.transform.rotation) < .05f &&
                        Near(turnedAnchorPosition, anchor.transform.position, .0001f),
                    "A reliable wrist turn immediately updates both absolute yaw and attachment offset");

                Vector3 verticalPosition = turnedPosition + new Vector3(-.04f, .06f, .11f);
                poseField.SetValue(source, ReadingTestPose(verticalPosition, -70f, 85f));
                anchor.RefreshPose();
                Vector3 verticalAnchorPosition = ExpectedReadingPosition(
                    verticalPosition, turnedYaw, settings, source.HandsActive);
                Require(Quaternion.Angle(turnedRotation, anchor.transform.rotation) < .05f &&
                        Near(verticalAnchorPosition, anchor.transform.position, .0001f),
                    "Near-vertical wrist holds the last valid yaw while its position continues to follow");

                const float recoveredYaw = -40f;
                Vector3 recoveredPosition = verticalPosition + new Vector3(.02f, -.03f, -.07f);
                poseField.SetValue(source, ReadingTestPose(recoveredPosition, recoveredYaw));
                anchor.RefreshPose();
                Quaternion recoveredRotation = ExpectedReadingRotation(recoveredYaw);
                Vector3 recoveredAnchorPosition = ExpectedReadingPosition(
                    recoveredPosition, recoveredYaw, settings, source.HandsActive);
                Require(Quaternion.Angle(recoveredRotation, anchor.transform.rotation) < .05f &&
                        Near(recoveredAnchorPosition, anchor.transform.position, .0001f),
                    "Leaving vertical immediately resynchronizes to absolute yaw without rebasing an offset");

                // A new open interval starts at its own current hand yaw, not the prior view yaw.
                anchor.gameObject.SetActive(false);
                const float reopenedYaw = -145f;
                Vector3 reopenedPosition = recoveredPosition + new Vector3(-.09f, .12f, .04f);
                poseField.SetValue(source, ReadingTestPose(reopenedPosition, reopenedYaw));
                anchor.gameObject.SetActive(true);
                anchor.CaptureReadingPose();
                anchor.RefreshPose();
                Require(Quaternion.Angle(ExpectedReadingRotation(reopenedYaw), anchor.transform.rotation) < .05f &&
                        Near(ExpectedReadingPosition(reopenedPosition, reopenedYaw, settings, source.HandsActive),
                            anchor.transform.position, .0001f),
                    "Reopening synchronizes immediately to the current absolute hand yaw and matching offset");
            }
            finally
            {
                anchor.gameObject.SetActive(false);
                poseField.SetValue(source, original);
                trackedField.SetValue(source, tracked);
                headField.SetValue(source, originalHead);
                shell.transform.rotation = originalShellRotation;
                braceletRotor.localRotation = originalRotorRotation;
                anchor.transform.SetPositionAndRotation(originalAnchorPosition, originalAnchorRotation);
                anchor.gameObject.SetActive(originalActive);
                if (originalActive)
                    anchor.RefreshPose();
                UnityEngine.Object.DestroyImmediate(headFixture);
            }
        }

        private static Pose ReadingTestPose(Vector3 position, float yaw, float pitch = 0f, float roll = 0f)
        {
            Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Euler(pitch, 0f, roll);
            return new Pose(position, rotation);
        }

        private static Quaternion ExpectedReadingRotation(float yaw) =>
            Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Euler(45f, 0f, 0f);

        private static Vector3 ExpectedReadingPosition(Vector3 wristPosition, float yaw,
            WristUISettings settings, bool handsActive)
        {
            float modeScale = handsActive ? settings.handBraceletScale : 1f;
            Vector3 planarOffset = Quaternion.AngleAxis(yaw, Vector3.up) *
                                   new Vector3(settings.pageOffset.x * modeScale, 0f,
                                       settings.pageOffset.y * modeScale);
            Vector3 verticalOffset = Vector3.up * (settings.radius * modeScale + .10f);
            return wristPosition + planarOffset + verticalOffset;
        }

        private static void ExerciseDetailPagesAndKeypad(WorkspaceShell shell)
        {
            string[] names = { "Teleop Config", "Camera", "WRM Setting", "System Setting" };
            foreach (string name in names)
            {
                Button navigation = FindButton(shell, name);
                Require(navigation != null, "Retained detail page control exists: " + name);
                navigation.onClick.Invoke();
            }
            ValidateExitConfirmation(shell);
            FindButton(shell, "Teleop Config").onClick.Invoke();
            Button alignment = FindButton(shell, "Alignment");
            Require(alignment != null && alignment.gameObject.activeInHierarchy,
                "Alignment remains accessible inside Teleop Config");
            alignment.onClick.Invoke();
            Require(FindButton(shell, "Reset reference")?.gameObject.activeInHierarchy == true &&
                    FindButton(shell, "Place / replace base")?.gameObject.activeInHierarchy == true &&
                    FindButton(shell, "Adjust axes")?.gameObject.activeInHierarchy == true &&
                    FindButton(shell, "Confirm alignment")?.gameObject.activeInHierarchy == true,
                "Nested Alignment retains the existing capture, placement, axes and resume actions");
            FindButton(shell, "Connection & input").onClick.Invoke();

            TMP_InputField field = null;
            foreach (TMP_InputField candidate in shell.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (candidate.name == "Workspace host input" && candidate.gameObject.activeInHierarchy)
                {
                    field = candidate;
                    break;
                }
            }
            Require(field != null, "Host input preserved inside Teleop Config");
            string original = field.text;
            // Previous pages are destroyed at frame end. Select the opener belonging
            // to this live input rather than a deactivated page awaiting destruction.
            Button editIp = FindButton(field.transform.parent, "Edit IP");
            Require(editIp != null, "IP entry has an explicit Meta-compatible edit action");
            editIp.onClick.Invoke();
            Require(WorkspaceKeypad.IsOpen, "Edit IP opens an active keypad");
            WorkspaceKeypad keypad = shell.GetComponentInChildren<WorkspaceKeypad>(true);
            Require(keypad != null && keypad.GetComponentInChildren<WristCanvasSurface>(true) != null,
                "IPv4 keyboard owns an independent touch surface");
            Button close = FindButton(shell, "Close");
            Require(close != null && close.GetComponent<WristCanvasSurface>() != null &&
                    close.GetComponent<WristCanvasSurface>().enabled,
                "Header Close retains its dedicated enabled interaction helper while the keypad is open");

            FindButton(keypad, "Clear").onClick.Invoke();
            FindButton(keypad, "1").onClick.Invoke();
            Require(field.text == "1", "Keyboard text-entry callback is preserved");
            FindButton(keypad, "Back").onClick.Invoke();
            Require(field.text.Length == 0, "Keyboard backspace callback is preserved");
            FindButton(keypad, "Clear").onClick.Invoke();
            foreach (char digit in "192.168.1.42")
                FindButton(keypad, digit.ToString()).onClick.Invoke();
            Require(field.text == "192.168.1.42", "IPv4 digits and dots reach the actual input field");
            int commits = 0;
            UnityEngine.Events.UnityAction<string> commitCounter = _ => commits++;
            field.onEndEdit.AddListener(commitCounter);
            FindButton(keypad, "Done").onClick.Invoke();
            Require(commits == 1, "Done commits the edited address exactly once");
            field.onEndEdit.RemoveListener(commitCounter);
            Require(!WorkspaceKeypad.IsOpen && !close.GetComponent<WristCanvasSurface>().enabled,
                "Done dismisses the keypad and restores the regular page interaction scope");
            shell.OpenKeypad(field);
            Require(close.GetComponent<WristCanvasSurface>().enabled, "Reopening restores the header close surface");
            close.onClick.Invoke();
            Require(!WorkspaceKeypad.IsOpen && !close.GetComponent<WristCanvasSurface>().enabled,
                "Header Close dismisses the keypad and the detail page without an ancestor button");
            field.text = original;
            WorkspaceKeypad.Close();
        }

        private static void ValidateExitConfirmation(WorkspaceShell shell)
        {
            int quitRequests = 0;
            Func<bool> rejectQuit = () => { quitRequests++; return false; };
            Application.wantsToQuit += rejectQuit;
            try
            {
                Transform ring = shell.transform.Find("Wrist presentation/Bracelet rotor/Bracelet control 5");
                Button opener = ring.GetComponentInChildren<Button>(true);
                Transform detail = shell.transform.Find("Wrist presentation/Wrist details");
                opener.onClick.Invoke();
                Require(detail.gameObject.activeSelf && quitRequests == 0,
                    "Exit APP opens a confirmation without requesting application quit");
                Button cancel = FindButton(detail, "Cancel");
                Button confirm = FindButton(detail, "Confirm exit app");
                Require(cancel != null && confirm != null && cancel.gameObject.activeInHierarchy &&
                        confirm.gameObject.activeInHierarchy && confirm != opener &&
                        confirm.GetComponentInChildren<TMP_Text>().text == "Exit APP",
                    "Exit confirmation has independent Cancel and Exit APP controls");
                cancel.onClick.Invoke();
                Require(!detail.gameObject.activeSelf && quitRequests == 0,
                    "Cancel closes the exit confirmation without requesting quit");
                opener.onClick.Invoke();
                Require(detail.gameObject.activeSelf && quitRequests == 0,
                    "Exit confirmation can be reopened safely after cancellation");
                FindButton(detail, "Close").onClick.Invoke();
                Require(!detail.gameObject.activeSelf && quitRequests == 0 && !AppManager.Instance.IsStreaming,
                    "Header Close dismisses exit confirmation and keeps teleoperation idle");
            }
            finally
            {
                Application.wantsToQuit -= rejectQuit;
            }
        }

        private static Button FindButton(Component root, string name)
        {
            Button inactive = null;
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.name == name)
                {
                    if (button.gameObject.activeInHierarchy) return button;
                    if (inactive == null) inactive = button;
                }
            return inactive;
        }

        private static bool Near(float actual, float expected, float tolerance = PositionTolerance) =>
            Mathf.Abs(actual - expected) <= tolerance;

        private static bool Near(Vector3 actual, Vector3 expected, float tolerance = PositionTolerance) =>
            (actual - expected).sqrMagnitude <= tolerance * tolerance;

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Bracelet validation: " + description);
        }
    }
}
