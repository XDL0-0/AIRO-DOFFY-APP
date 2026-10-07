using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Doffy.UI;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Doffy.Editor
{
    /// <summary>Isolated presentation regressions; no robot, calibration, signaling or recording callbacks.</summary>
    internal static class WristWorkspaceRegressionSmoke
    {
        public static void ValidateAxes()
        {
            GameObject fixture = new GameObject("Wrist axes regression fixture");
            fixture.SetActive(false); // Keep singleton/service Awake and Start outside the fixture.
            HashSet<Material> originalMaterials = new HashSet<Material>(Resources.FindObjectsOfTypeAll<Material>());
            List<Material> generatedMaterials = new List<Material>();
            try
            {
                GameObject coord = new GameObject("Serialized Coord");
                coord.transform.SetParent(fixture.transform, false);
                Transform visual = new GameObject("Visual").transform;
                visual.SetParent(coord.transform, false);
                foreach (string name in new[] { "X Axis", "Y Axis", "Z Axis", "Origin Sphere", "Base Cylinder" })
                {
                    GameObject part = GameObject.CreatePrimitive(name == "Origin Sphere" ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
                    part.name = name;
                    part.transform.SetParent(visual, false);
                }
                CalibrationGizmo gizmo = coord.AddComponent<CalibrationGizmo>();
                Renderer[] serializedRenderers = coord.GetComponentsInChildren<Renderer>(true);
                for (int repeat = 0; repeat < 20; repeat++)
                {
                    // Scene/domain reload loses caches while keeping the serialized geometry.
                    Write(gizmo, "_visualsReady", false);
                    Write(gizmo, "_visualRoot", null);
                    gizmo.EnsureVisuals();
                    Require(coord.transform.childCount == 1 && ReferenceEquals(coord.transform.GetChild(0), visual),
                        "Repeated axis generation adopts the same serialized Visual wrapper");
                    AssertSameRenderers(coord.transform, serializedRenderers,
                        "Repeated axis generation retains exactly the original five renderers");
                }

                GameObject duplicate = new GameObject("Duplicate Coord");
                duplicate.transform.SetParent(fixture.transform, false);
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    Transform wrapper = new GameObject("Visual").transform;
                    wrapper.SetParent(duplicate.transform, false);
                }
                CalibrationGizmo duplicateGizmo = duplicate.AddComponent<CalibrationGizmo>();
                duplicateGizmo.EnsureVisuals();
                int liveWrappers = 0;
                foreach (Transform wrapper in duplicate.transform)
                    if (wrapper.name == "Visual" && wrapper.gameObject.activeSelf) liveWrappers++;
                Require(liveWrappers == 1 && duplicate.GetComponentsInChildren<Renderer>(true).Length == 5,
                    "Previously duplicated wrappers collapse to one visible five-part gizmo");

                GameObject worldObject = new GameObject("Isolated TeleopWorld");
                worldObject.transform.SetParent(fixture.transform, false);
                TeleopWorld world = worldObject.AddComponent<TeleopWorld>();
                Transform anchor = world.CreateRobotBaseAnchor();
                Transform axes = anchor.Find("RobotBaseAxes");
                Require(axes != null, "World fixture creates committed axes");
                Renderer[] worldRenderers = axes.GetComponentsInChildren<Renderer>(true);
                Require(worldRenderers.Length == 3, "Committed robot base contains one three-axis set");
                GameObject owner = new GameObject("Axes edit owner");
                owner.transform.SetParent(fixture.transform, false);
                for (int repeat = 0; repeat < 20; repeat++)
                {
                    Require(ReferenceEquals(world.CreateRobotBaseAnchor(), anchor) && worldObject.transform.childCount == 1,
                        "Repeated base creation reuses one anchor");
                    world.SetAxesVisible(true);
                    world.BeginAxesEdit(owner);
                    Require(world.AxesVisible && !axes.gameObject.activeSelf,
                        "Editing hides committed axes while preserving requested visibility");
                    world.EndAxesEdit(fixture);
                    Require(!axes.gameObject.activeSelf, "Another owner cannot expose axes during editing");
                    world.EndAxesEdit(owner);
                    Require(axes.gameObject.activeSelf, "Ending the owning edit restores committed axes");
                    world.SetAxesVisible(false);
                    world.BeginAxesEdit(owner);
                    world.EndAxesEdit(owner);
                    Require(!world.AxesVisible && !axes.gameObject.activeSelf,
                        "Ending axes edit preserves the user's hidden-axis setting");
                    world.SetRobotBasePose(new Vector3(repeat * .01f, .8f, -.2f), Quaternion.Euler(30f, repeat * 9f, 20f));
                    Require(anchor.childCount == 1 && ReferenceEquals(anchor.GetChild(0), axes),
                        "Repeated pose changes keep one committed axis root");
                    AssertSameRenderers(axes, worldRenderers, "Repeated edit/toggle/placement retains the same three axes");
                }

                GameObject placementObject = new GameObject("Isolated placement tool");
                placementObject.transform.SetParent(fixture.transform, false);
                RobotBasePlacementTool placement = placementObject.AddComponent<RobotBasePlacementTool>();
                Invoke(placement, "CreatePreview");
                Transform preview = (Transform)Read(placement, "_previewRoot");
                Require(preview != null && preview.parent == placement.transform,
                    "Placement preview is owned by the placement tool hierarchy");
                Renderer[] previewRenderers = preview.GetComponentsInChildren<Renderer>(true);
                Require(previewRenderers.Length == 3, "Placement preview has one three-axis set");
                for (int repeat = 0; repeat < 20; repeat++)
                {
                    Invoke(placement, "CreatePreview");
                    Require(ReferenceEquals(Read(placement, "_previewRoot"), preview) && placement.transform.childCount == 1,
                        "Repeated preview generation does not accumulate axis roots");
                    preview.gameObject.SetActive(true);
                    placement.EndPlacementEdit();
                    Require(!preview.gameObject.activeSelf, "Ending placement hides the existing preview immediately");
                    AssertSameRenderers(preview, previewRenderers, "Repeated preview generation retains three renderers");
                }
                preview.gameObject.SetActive(true);
                Invoke(placement, "OnDisable");
                Require(!preview.gameObject.activeSelf, "Disabling the placement owner hides its axes immediately");
                Debug.Log("DOFFY axes regression passed: serialized gizmo adoption, duplicate cleanup, repeated world anchors/edit/toggles/poses and owned reusable placement preview.");
            }
            finally
            {
                foreach (Renderer renderer in fixture.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null && !originalMaterials.Contains(material) && !generatedMaterials.Contains(material))
                            generatedMaterials.Add(material);
                UnityEngine.Object.DestroyImmediate(fixture);
                foreach (Material material in generatedMaterials)
                    if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        public static IEnumerator ExerciseCameras(WorkspaceShell shell, WristInteractionSource source,
            Action<WristCanvasSurface, RectTransform, bool> route)
        {
            Require(!AppManager.Instance.IsStreaming, "Camera smoke starts with an idle robot session");
            VideoStreamManager video = UnityEngine.Object.FindAnyObjectByType<VideoStreamManager>();
            UdpWindowManager windows = UnityEngine.Object.FindAnyObjectByType<UdpWindowManager>();
            Require(video != null && windows != null && windows.udpWindowPrefab != null,
                "Actual WebRTC manager and UDP prefab are available");
            video.InitializeCameraPanels();
            List<RawImage> panels = new List<RawImage>();
            int expectedGroupSize = 1;
            foreach (string field in new[] { "singlePanels", "dualPanels", "triPanels" })
            {
                RawImage[] group = (RawImage[])Read(video, field);
                Require(group != null && group.Length == expectedGroupSize++,
                    "Single, Dual and Tri retain one, two and three serialized panels");
                panels.AddRange(group);
            }
            Require(panels.Count == 6 && new HashSet<RawImage>(panels).Count == 6,
                "All six independent WebRTC panel references are retained");

            Transform originalHead = source.Head;
            bool originalHands = source.HandsActive;
            bool originalController = source.ControllerModeActive;
            bool originalTracked = source.RightInputTracked;
            bool originalSourceEnabled = source.enabled;
            GameObject head = new GameObject("Camera regression head");
            GameObject inactiveParent = new GameObject("Inactive UDP smoke parent");
            inactiveParent.SetActive(false);
            GameObject udpObject = null;
            Dictionary<GameObject, bool> originalActive = new Dictionary<GameObject, bool>();
            try
            {
                Write(source, "head", head.transform);
                Write(source, "rightInputTracked", false);
                head.transform.SetPositionAndRotation(new Vector3(-.2f, 1.5f, -.3f), Quaternion.Euler(20f, 35f, -17f));
                foreach (RawImage panel in panels)
                {
                    Require(panel != null, "Serialized WebRTC panel reference is not missing");
                    originalActive[panel.gameObject] = panel.gameObject.activeSelf;
                    panel.gameObject.SetActive(false);
                }
                // Instantiate the real prefab inactive, disable its receiver before activating it,
                // and retain the actual serialized parent relationship for SDK canvas routing.
                udpObject = UnityEngine.Object.Instantiate(windows.udpWindowPrefab, inactiveParent.transform);
                foreach (UdpSocketMultiHD receiver in udpObject.GetComponentsInChildren<UdpSocketMultiHD>(true))
                    receiver.enabled = false;
                udpObject.transform.SetParent(windows.windowContainer, false);
                udpObject.SetActive(true);
                VideoWindowController udp = udpObject.GetComponent<VideoWindowController>();
                Require(udp != null, "Real UDP prefab retains its controller");
                udp.InitializeCameraInteraction();
                CameraPanelInteraction udpCamera = udpObject.GetComponent<CameraPanelInteraction>();
                Require(udpCamera != null, "UDP Awake/initialization wires camera interaction");
                for (int repeat = 0; repeat < 20; repeat++) udp.InitializeCameraInteraction();
                ValidateCameraStructure(udpCamera, source);
                foreach (UdpSocketMultiHD receiver in udpObject.GetComponentsInChildren<UdpSocketMultiHD>(true))
                    Require(Read(receiver, "receiver") == null && !receiver.enabled,
                        "UDP presentation initialization never opens the disabled receiver");
                IEnumerator udpSteps = ExerciseCamera(udpCamera, source, head.transform, route, originalActive);
                while (udpSteps.MoveNext()) yield return udpSteps.Current;
                udpObject.SetActive(false);

                // Rebinding all transports repeatedly must keep one bar and two scopes per view.
                for (int repeat = 0; repeat < 20; repeat++) video.InitializeCameraPanels();
                foreach (RawImage panel in panels)
                {
                    CameraPanelInteraction camera = panel.GetComponent<CameraPanelInteraction>();
                    Require(camera != null, "Every serialized WebRTC view is initialized even while its group is hidden");
                    ValidateCameraStructure(camera, source);
                    IEnumerator panelSteps = ExerciseCamera(camera, source, head.transform, route, originalActive);
                    while (panelSteps.MoveNext()) yield return panelSteps.Current;
                    panel.gameObject.SetActive(false);
                }
                Require(video.CurrentState == VideoStreamManager.SessionState.Idle && !AppManager.Instance.IsStreaming,
                    "Camera presentation, routing and lifecycle checks leave signaling and teleoperation idle");
                Debug.Log("DOFFY camera regression passed: real UDP prefab and all six WebRTC views, repeat initialization, isolated text-free bars, real Meta ray/poke graphic routing, fixed position, hold/release facing and safe lifecycle cancellation; no camera transport starts.");
            }
            finally
            {
                // Restore descendants before parents so no temporary visibility state escapes.
                List<GameObject> objects = new List<GameObject>(originalActive.Keys);
                objects.Sort((a, b) => Depth(b.transform).CompareTo(Depth(a.transform)));
                foreach (GameObject obj in objects) if (obj != null) obj.SetActive(originalActive[obj]);
                Write(source, "head", originalHead);
                Write(source, "handsActive", originalHands);
                Write(source, "controllerModeActive", originalController);
                Write(source, "rightInputTracked", originalTracked);
                source.enabled = originalSourceEnabled;
                if (udpObject != null) UnityEngine.Object.DestroyImmediate(udpObject);
                UnityEngine.Object.DestroyImmediate(inactiveParent);
                UnityEngine.Object.DestroyImmediate(head);
            }
        }

        private static IEnumerator ExerciseCamera(CameraPanelInteraction camera, WristInteractionSource source,
            Transform head, Action<WristCanvasSurface, RectTransform, bool> route,
            Dictionary<GameObject, bool> originalActive)
        {
            Transform panel = camera.transform;
            Vector3 position = panel.position;
            Quaternion rotation = panel.rotation;
            Vector3 scale = panel.localScale;
            bool enabled = camera.enabled;
            try
            {
                // Each view starts independently. The prior view's explicit vertical-
                // head case must not become this view's normal-facing starting pose.
                head.SetPositionAndRotation(new Vector3(-.2f, 1.5f, -.3f), Quaternion.Euler(20f, 35f, -17f));
                for (Transform parent = panel; parent != null; parent = parent.parent)
                {
                    if (!originalActive.ContainsKey(parent.gameObject))
                        originalActive.Add(parent.gameObject, parent.gameObject.activeSelf);
                    parent.gameObject.SetActive(true);
                }
                camera.enabled = false; // Actual surface routing continues; automatic pose updates are fixture-driven.
                panel.SetPositionAndRotation(new Vector3(.1f, .3f, 1f), Quaternion.identity);
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                Require(!camera.CanInteract, "Untracked/disabled presentation cannot acquire live camera capture");

                WristCanvasSurface barSurface = camera.BarRect.GetComponent<WristCanvasSurface>();
                CheckPointerRoute(barSurface, camera.BarRect, route,
                    "Real ray and poke events reach this camera's independent drag bar");
                VideoWindowController udp = panel.GetComponent<VideoWindowController>();
                Graphic contentGraphic = udp != null && udp.resolutionButton != null
                    ? udp.resolutionButton.targetGraphic : panel.GetComponent<RawImage>();
                Require(contentGraphic != null && contentGraphic.enabled && contentGraphic.gameObject.activeInHierarchy,
                    "Actual camera content/control is an active graphic");
                bool originalRaycast = contentGraphic.raycastTarget;
                contentGraphic.raycastTarget = true;
                try
                {
                    WristCanvasSurface controls = panel.GetComponent<WristCanvasSurface>();
                    CheckPointerRoute(controls, contentGraphic.rectTransform, route,
                        "Real ray and poke events reach the camera content without capturing its bar");
                    Require(!camera.DragHandle.IsDragging, "Pressing camera content cannot capture panel movement");
                }
                finally { contentGraphic.raycastTarget = originalRaycast; }
                // A view may end up on either side of the user after dragging/facing.
                // SDK graphic routing must remain valid when it opposes the legacy canvas.
                panel.rotation = Quaternion.Euler(0f, 180f, 0f);
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                CheckPointerRoute(barSurface, camera.BarRect, route,
                    "Actual camera drag bar keeps real ray/poke routing at opposite-facing yaw");
                panel.rotation = Quaternion.identity;
                yield return null;
                yield return null;
                if (udp != null)
                {
                    IEnumerator keypadSteps = ExerciseCameraKeypad(camera, source, route);
                    while (keypadSteps.MoveNext()) yield return keypadSteps.Current;
                }

                panel.SetPositionAndRotation(new Vector3(.4f, 1.1f, .8f), Quaternion.Euler(31f, -120f, 57f));
                Vector3 chosenPosition = panel.position;
                FaceCamera(camera);
                Require(Near(panel.position, chosenPosition) && FacesHead(panel, head),
                    $"Camera converges to upright user-facing yaw at its existing world position: " +
                    $"chosen={chosenPosition:F4}, actual={panel.position:F4}, head={head.position:F4}, " +
                    $"rotation={panel.eulerAngles:F2}, horizontalAway={Vector3.ProjectOnPlane(panel.position - head.position, Vector3.up):F4}");
                Quaternion heldRotation = Quaternion.Euler(0f, -134f, 0f);
                panel.rotation = heldRotation;
                SeedCapture(camera.DragHandle, false, source.RightRay.Identifier);
                head.position += new Vector3(.5f, .2f, -.3f);
                FaceCamera(camera);
                Require(Near(panel.position, chosenPosition) && Quaternion.Angle(panel.rotation, heldRotation) < .001f,
                    "Held camera drag suspends automatic facing and keeps the captured pose");
                camera.DragHandle.CancelDrag();
                FaceCamera(camera);
                Require(Near(panel.position, chosenPosition) && FacesHead(panel, head),
                    "Release resumes facing while preserving the chosen camera position");
                Require(Near(panel.localScale, scale), "Camera facing and capture preserve its authored scale");
                Quaternion lastYaw = panel.rotation;
                head.position = panel.position + Vector3.up * .6f;
                FaceCamera(camera);
                Require(Quaternion.Angle(panel.rotation, lastYaw) < .001f,
                    "Vertically aligned head preserves the last valid camera yaw");
                WristUIValidation.ValidatePanelPointerRelease(camera.DragHandle, source);
                ValidateHandleHighlight(camera, source);
                SeedCapture(camera.DragHandle, true, source.RightPoke.Identifier);
                panel.gameObject.SetActive(false);
                Require(!camera.DragHandle.IsDragging, "Hiding a camera panel cancels its held capture");
                panel.gameObject.SetActive(true);
                // The production helper refreshes nested event cameras in LateUpdate.
                // This fixture drives that method explicitly while the helper is disabled.
                Invoke(camera, "LateUpdate");
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                CheckPointerRoute(barSurface, camera.BarRect, route,
                    "Camera drag routing survives hide/re-enable without duplicate surfaces");
            }
            finally
            {
                camera.DragHandle.CancelDrag();
                panel.SetPositionAndRotation(position, rotation);
                panel.localScale = scale;
                camera.enabled = enabled;
            }
        }

        private static IEnumerator ExerciseCameraKeypad(CameraPanelInteraction camera, WristInteractionSource source,
            Action<WristCanvasSurface, RectTransform, bool> route)
        {
            // A field without a transport listener exercises the real modal/commit path
            // while its callback reinitializes the actual UDP presentation exactly once.
            GameObject fieldObject = new GameObject("Isolated camera port input", typeof(RectTransform), typeof(TMP_InputField));
            fieldObject.transform.SetParent(camera.transform, false);
            TMP_InputField field = fieldObject.GetComponent<TMP_InputField>();
            field.SetTextWithoutNotify("8000");
            field.readOnly = false;
            field.shouldHideSoftKeyboard = false;
            field.shouldActivateOnSelect = true;
            // TMP forces soft-keyboard hiding on desktop platforms regardless of the
            // requested setter value. Restoration must preserve the observed input state.
            bool originalReadOnly = field.readOnly;
            bool originalHideKeyboard = field.shouldHideSoftKeyboard;
            bool originalActivateOnSelect = field.shouldActivateOnSelect;
            Vector3 barPosition = camera.BarRect.localPosition;
            Vector2 barSize = camera.BarRect.sizeDelta;
            int commits = 0;
            field.onEndEdit.AddListener(value =>
            {
                commits++;
                Require(value == "8123", "Camera keypad commits the edited port value");
                camera.Initialize((RectTransform)camera.transform, source);
                Require(Near(camera.BarRect.localPosition, barPosition) && camera.BarRect.sizeDelta == barSize,
                    "Port commit/reinitialization ignores flyout graphics and keeps the bottom bar bounds");
            });
            try
            {
                WorkspaceKeypad.Show((RectTransform)camera.transform, field, null, source, () => true);
                Require(WorkspaceKeypad.IsOpen && !camera.CanInteract,
                    "Camera keypad opens its modal while underlying camera capture is gated");
                yield return null;
                yield return null;
                WorkspaceKeypad keypad = camera.GetComponentInChildren<WorkspaceKeypad>();
                Require(keypad != null && keypad.transform.parent == camera.transform &&
                        keypad.GetComponent<Canvas>() == null,
                    "Camera port keypad keeps the camera host and registered root canvas");
                WristCanvasSurface surface = keypad.GetComponentInChildren<WristCanvasSurface>();
                route(surface, (RectTransform)FindButton(keypad, "Clear").transform, false);
                int index = 0;
                foreach (char digit in "8123")
                    route(surface, (RectTransform)FindButton(keypad, digit.ToString()).transform, index++ % 2 == 1);
                Require(field.text == "8123", "Real camera keypad ray/poke hits enter all port digits");
                Quaternion beforeModalFacing = camera.transform.rotation;
                FaceCamera(camera);
                Require(Quaternion.Angle(camera.transform.rotation, beforeModalFacing) < .001f,
                    "Camera orientation remains stable while entering a port");
                route(surface, (RectTransform)FindButton(keypad, "Done").transform, true);
                bool modalOpen = WorkspaceKeypad.IsOpen;
                bool restoredReadOnly = field.readOnly;
                bool restoredHideKeyboard = field.shouldHideSoftKeyboard;
                bool restoredActivateOnSelect = field.shouldActivateOnSelect;
                Require(!modalOpen && commits == 1 && restoredReadOnly == originalReadOnly &&
                        restoredHideKeyboard == originalHideKeyboard &&
                        restoredActivateOnSelect == originalActivateOnSelect,
                    $"Poke Done commits exactly once, closes camera modal and restores input flags: " +
                    $"IsOpen={modalOpen}, commits={commits}, readOnly={restoredReadOnly} (original {originalReadOnly}), " +
                    $"hideKeyboard={restoredHideKeyboard} (original {originalHideKeyboard}), " +
                    $"activateOnSelect={restoredActivateOnSelect} (original {originalActivateOnSelect})");
                Require(Near(camera.BarRect.localPosition, barPosition) && camera.BarRect.sizeDelta == barSize,
                    "Closing camera keypad retains the drag bar position and dimensions");
                yield return null;
                ValidateCameraStructure(camera, source);
            }
            finally
            {
                WorkspaceKeypad.Close();
                UnityEngine.Object.DestroyImmediate(fieldObject);
            }
        }

        private static Button FindButton(Component root, string name)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>())
                if (button.name == name) return button;
            throw new InvalidOperationException("Camera regression active button missing: " + name);
        }

        private static void ValidateHandleHighlight(CameraPanelInteraction camera, WristInteractionSource source)
        {
            TeleopPanelDragHandle handle = camera.DragHandle;
            RoundedPanel background = handle.GetComponent<RoundedPanel>();
            bool sourceEnabled = source.enabled;
            bool hands = source.HandsActive, controller = source.ControllerModeActive;
            Func<bool> availability = (Func<bool>)Read(handle, "interactionAvailable");
            try
            {
                // Only the availability gate is supplied by the fixture. No tracker or
                // session fields are made live and no hardware poses are sampled here.
                source.enabled = true;
                Write(source, "handsActive", false);
                Write(source, "controllerModeActive", true);
                handle.Initialize(camera.transform, source, () => true);
                PointerEventData pointer = new PointerEventData(EventSystem.current) { pointerId = source.RightRay.Identifier };
                PointerEventData other = new PointerEventData(EventSystem.current) { pointerId = source.RightPoke.Identifier };
                handle.OnPointerEnter(other);
                Require(background.color == WorkspaceTheme.Raised, "Inactive pointer cannot highlight a camera bar");
                handle.OnPointerEnter(pointer);
                Require(background.color == WorkspaceTheme.Muted, "Selected right pointer highlights its camera drag bar");
                SeedCapture(handle, false, pointer.pointerId);
                Invoke(handle, "RefreshColor");
                Require(background.color == WorkspaceTheme.Accent, "Held camera capture uses the selected bar color");
                handle.CancelDrag();
                Require(background.color == WorkspaceTheme.Muted, "Release restores the current hover highlight");
                Write(source, "handsActive", true);
                Write(source, "controllerModeActive", false);
                Invoke(handle, "LateUpdate");
                Require(background.color == WorkspaceTheme.Raised, "Changing input mode clears stale camera bar highlight");
            }
            finally
            {
                Write(source, "handsActive", hands);
                Write(source, "controllerModeActive", controller);
                source.enabled = sourceEnabled;
                handle.Initialize(camera.transform, source, availability);
            }
        }

        private static void ValidateCameraStructure(CameraPanelInteraction camera, WristInteractionSource source)
        {
            Require(camera.GetComponents<CameraPanelInteraction>().Length == 1 && camera.DragHandle != null &&
                    camera.GetComponentsInChildren<TeleopPanelDragHandle>(true).Length == 1,
                "Each camera owns exactly one initialized interaction helper and drag bar");
            Require(ReferenceEquals(Read(camera.DragHandle, "target"), camera.transform) &&
                    ReferenceEquals(Read(camera.DragHandle, "source"), source),
                "Camera bar moves its own panel through the selected Meta source");
            Require(camera.BarRect.GetComponentsInChildren<TMP_Text>(true).Length == 0 &&
                    camera.BarRect.GetComponent<Graphic>()?.raycastTarget == true &&
                    camera.BarRect.GetComponentInParent<Button>() == null &&
                    camera.BarRect.GetComponentsInChildren<Button>(true).Length == 0,
                "Camera bottom bar is text-free, highlighted and independent of content controls");
            WristCanvasSurface barSurface = camera.BarRect.GetComponent<WristCanvasSurface>();
            WristCanvasSurface controlsSurface = camera.GetComponent<WristCanvasSurface>();
            Require(camera.Canvas != null && camera.Canvas.renderMode == RenderMode.WorldSpace &&
                    camera.Canvas.GetComponent<GraphicRaycaster>() != null &&
                    camera.Canvas.GetComponent<PointableCanvas>() != null &&
                    barSurface != null && controlsSurface != null && barSurface != controlsSurface,
                "Each camera has independent bar and content surfaces on its registered root canvas");
            Require(ReferenceEquals(((PointableCanvas)barSurface.Ray.PointableElement).Canvas, camera.Canvas) &&
                    ReferenceEquals(((PointableCanvas)controlsSurface.Ray.PointableElement).Canvas, camera.Canvas),
                "Both independent camera surfaces route through the actual registered root canvas");
            Require(camera.GetComponentsInChildren<WristCanvasSurface>(true).Length == 2,
                "Repeated initialization keeps exactly one content and one bar interaction scope");
            Rect barBounds = Bounds(camera.BarRect, (RectTransform)camera.transform);
            foreach (Graphic graphic in camera.GetComponentsInChildren<Graphic>(true))
                if (!graphic.transform.IsChildOf(camera.BarRect))
                    Require(barBounds.yMax < Bounds(graphic.rectTransform, (RectTransform)camera.transform).yMin,
                        "Camera drag bar remains below every existing content graphic: " + graphic.name);
            foreach (DraggableWindowWorld legacy in camera.GetComponentsInChildren<DraggableWindowWorld>(true))
                Require(!legacy.enabled, "Video content cannot keep the former whole-window movement path");
            foreach (WindowJoystickHoldMove legacy in camera.GetComponentsInChildren<WindowJoystickHoldMove>(true))
                Require(!legacy.enabled, "Joystick movement cannot bypass the camera bar capture");
        }

        private static void CheckPointerRoute(WristCanvasSurface surface, RectTransform rect,
            Action<WristCanvasSurface, RectTransform, bool> route, string message)
        {
            EventTrigger trigger = rect.GetComponent<EventTrigger>();
            bool added = trigger == null;
            if (added) trigger = rect.gameObject.AddComponent<EventTrigger>();
            List<EventTrigger.Entry> original = trigger.triggers;
            int downs = 0, ups = 0;
            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => downs++);
            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => ups++);
            trigger.triggers = new List<EventTrigger.Entry> { down, up };
            try
            {
                route(surface, rect, false);
                int rayDowns = downs, rayUps = ups;
                route(surface, rect, true);
                Require(downs == 2 && ups == 2,
                    $"{message}: downs={downs}, ups={ups}; ray downs/ups={rayDowns}/{rayUps}, " +
                    $"poke downs/ups={downs - rayDowns}/{ups - rayUps}; " + PointerRouteDiagnostics(surface, rect));
            }
            finally
            {
                if (added) UnityEngine.Object.DestroyImmediate(trigger);
                else trigger.triggers = original;
            }
        }

        private static string PointerRouteDiagnostics(WristCanvasSurface surface, RectTransform rect)
        {
            Graphic graphic = rect.GetComponent<Graphic>();
            CameraPanelInteraction camera = rect.GetComponentInParent<CameraPanelInteraction>();
            PointableCanvas pointable = surface.Ray.PointableElement as PointableCanvas;
            PointableCanvasModule module = UnityEngine.Object.FindAnyObjectByType<PointableCanvasModule>();
            Camera eventCamera = typeof(PointableCanvasModule).GetField("_pointerEventCamera",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(module) as Camera;
            object registered = pointable != null ? typeof(PointableCanvas).GetField("_registered",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pointable) : null;
            List<string> canvases = new List<string>();
            if (camera != null)
                foreach (Canvas canvas in camera.GetComponentsInChildren<Canvas>(true))
                    canvases.Add($"{Identity(canvas)} active={canvas.isActiveAndEnabled}, root={Identity(canvas.rootCanvas)}, " +
                        $"worldCamera={CameraPose(canvas.worldCamera)}, raycasterCamera={CameraPose(canvas.GetComponent<GraphicRaycaster>()?.eventCamera)}");
            return $"rect={Identity(rect)} active={rect.gameObject.activeInHierarchy}, position={rect.position:F4}, " +
                $"rotation={rect.eulerAngles:F2}, forward={rect.forward:F4}, scale={rect.lossyScale:F5}; " +
                $"graphic enabled={graphic?.enabled}, depth={graphic?.depth}, cull={graphic?.canvasRenderer.cull}, " +
                $"canvas={Identity(graphic?.canvas)}, root={Identity(graphic?.canvas?.rootCanvas)}; " +
                $"panel={Identity(camera)} position={camera?.transform.position:F4}, rotation={camera?.transform.eulerAngles:F2}, enabled={camera?.enabled}; " +
                $"pointable={Identity(pointable)} registered={registered}, boundCanvas={Identity(pointable?.Canvas)}, " +
                $"boundForward={pointable?.Canvas?.transform.forward:F4}, boundCamera={CameraPose(pointable?.Canvas?.worldCamera)}; " +
                $"SDK eventCamera={CameraPose(eventCamera)}; nested=[{string.Join(" | ", canvases)}]";
        }

        private static string Identity(UnityEngine.Object value) => value == null ? "null" : value.name;
        private static string CameraPose(Camera camera) => camera == null ? "null" :
            $"{Identity(camera)} position={camera.transform.position:F4}, rotation={camera.transform.eulerAngles:F2}";

        private static void FaceCamera(CameraPanelInteraction camera)
        {
            Require(Time.unscaledDeltaTime > 0f, "Play provides a positive interval for camera facing");
            int steps = Mathf.CeilToInt(3f / Time.unscaledDeltaTime);
            for (int i = 0; i < steps; i++) Invoke(camera, "LateUpdate");
        }
        private static void SeedCapture(TeleopPanelDragHandle handle, bool hands, int pointer)
        {
            Write(handle, "handCapture", hands);
            Write(handle, "pointerId", pointer);
            Write(handle, "<IsDragging>k__BackingField", true);
        }
        private static bool FacesHead(Transform panel, Transform head) => Vector3.Dot(panel.up, Vector3.up) > .9999f &&
            Quaternion.Angle(panel.rotation, Quaternion.LookRotation(
                Vector3.ProjectOnPlane(panel.position - head.position, Vector3.up), Vector3.up)) < .1f;
        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .0001f * .0001f;
        private static int Depth(Transform transform)
        {
            int depth = 0;
            while (transform.parent != null) { depth++; transform = transform.parent; }
            return depth;
        }
        private static Rect Bounds(RectTransform child, RectTransform parent)
        {
            Vector3[] corners = new Vector3[4];
            child.GetWorldCorners(corners);
            Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
            foreach (Vector3 corner in corners)
            {
                Vector2 local = parent.InverseTransformPoint(corner);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void AssertSameRenderers(Transform root, Renderer[] expected, string message)
        {
            Renderer[] current = root.GetComponentsInChildren<Renderer>(true);
            Require(current.Length == expected.Length, message);
            foreach (Renderer renderer in expected)
                Require(Array.IndexOf(current, renderer) >= 0, message);
        }

        private static object Read(object owner, string name) => Field(owner, name).GetValue(owner);
        private static void Write(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);
        private static FieldInfo Field(object owner, string name)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Fixture field exists: " + owner.GetType().Name + "." + name);
            return field;
        }
        private static void Invoke(object owner, string name, params object[] args)
        {
            MethodInfo method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null, "Fixture method exists: " + owner.GetType().Name + "." + name);
            method.Invoke(owner, args);
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Workspace regression: " + message);
        }
    }
}
