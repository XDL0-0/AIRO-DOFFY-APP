using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Doffy.UI
{
    /// <summary>
    /// Adds transport-independent camera controls and a dedicated bottom drag bar.
    /// Initialization only creates presentation/input surfaces; it never opens a receiver or session.
    /// </summary>
    [DefaultExecutionOrder(30)]
    public sealed class CameraPanelInteraction : MonoBehaviour
    {
        private const float FacingSharpness = 10f;
        private RectTransform panelRect;
        private WristInteractionSource source;
        private Canvas canvas;
        private RectTransform barRect;
        private TeleopPanelDragHandle dragHandle;
        private Rect lastPanelRect;
        private bool initialized;
        private readonly Vector3[] corners = new Vector3[4];

        public Canvas Canvas => canvas;
        public RectTransform BarRect => barRect;
        public TeleopPanelDragHandle DragHandle => dragHandle;

        /// <summary>Camera controls share the tracked right input in idle and open teleoperation.</summary>
        public bool CanInteract => HasCameraInput() && !WorkspaceKeypad.IsOpen;

        private bool HasCameraInput() => isActiveAndEnabled && panelRect != null &&
            panelRect.gameObject.activeInHierarchy && source != null && source.isActiveAndEnabled &&
            source.CanInteractForFloatingPanels;

        /// <summary>Adds or rebinds one camera panel without touching its transport or visibility.</summary>
        public static CameraPanelInteraction Ensure(RectTransform panel, WristInteractionSource source = null)
        {
            if (panel == null) throw new System.ArgumentNullException(nameof(panel));
            var interaction = panel.GetComponent<CameraPanelInteraction>();
            if (interaction == null) interaction = panel.gameObject.AddComponent<CameraPanelInteraction>();
            interaction.Initialize(panel, source);
            return interaction;
        }

        public void Initialize(RectTransform panel, WristInteractionSource interactionSource = null)
        {
            if (panel == null) throw new System.ArgumentNullException(nameof(panel));
            if (panel.gameObject != gameObject)
                throw new System.ArgumentException("Attach camera interaction to the camera panel rectangle.", nameof(panel));
            panelRect = panel;
            source = interactionSource == null ? WristInteractionSource.Ensure() : interactionSource;

            // Root-canvas orientation drives Meta's graphic projection. Independent
            // camera yaw therefore needs one actual root canvas per view, rather than
            // a nested canvas beneath the retired legacy UI canvas.
            Transform parent = panel.parent;
            Transform floatingParent = CameraPanelContainer.Ensure(parent);
            if (parent != floatingParent) panel.SetParent(floatingParent, true);

            // The old UDP prefab lets its video image and joystick script move the same root.
            // Keep every camera movement path on the dedicated bar instead.
            foreach (var legacy in panel.GetComponentsInChildren<DraggableWindowWorld>(true)) legacy.enabled = false;
            foreach (var legacy in panel.GetComponentsInChildren<WindowJoystickHoldMove>(true)) legacy.enabled = false;

            canvas = EnsureCanvas(panel);
            var pointable = EnsurePointable(canvas);
            WristCanvasSurface.Create(panel, pointable, source, true, false, IsInteractionAvailable);

            if (!initialized)
            {
                var barObject = new GameObject("Camera panel drag bar", typeof(RectTransform));
                barObject.SetActive(false);
                barObject.layer = gameObject.layer;
                barRect = barObject.GetComponent<RectTransform>();
                barRect.SetParent(panel, false);
                barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(.5f, .5f);
                var background = barObject.AddComponent<RoundedPanel>();
                background.color = WorkspaceTheme.Raised;
                background.raycastTarget = true;
                dragHandle = barObject.AddComponent<TeleopPanelDragHandle>();
                initialized = true;
            }

            RefreshBarLayout();
            dragHandle.Initialize(panel, source, IsInteractionAvailable);
            WristCanvasSurface.Create(barRect, pointable, source,
                true, false, IsInteractionAvailable);
            barRect.gameObject.SetActive(true);
        }

        private bool IsInteractionAvailable() => CanInteract;

        private static Canvas EnsureCanvas(RectTransform rect)
        {
            // The parent layout group is canvas-free; this canvas is both the camera's
            // render root and its registered Meta input root, at any world rotation.
            var canvas = rect.GetComponent<Canvas>();
            if (canvas == null)
            {
                Vector3 position = rect.localPosition;
                Quaternion rotation = rect.localRotation;
                Vector3 scale = rect.localScale;
                Vector2 anchorsMin = rect.anchorMin, anchorsMax = rect.anchorMax;
                Vector2 size = rect.sizeDelta, pivot = rect.pivot;
                canvas = rect.gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                rect.anchorMin = anchorsMin;
                rect.anchorMax = anchorsMax;
                rect.pivot = pivot;
                rect.sizeDelta = size;
                rect.localPosition = position;
                rect.localRotation = rotation;
                rect.localScale = scale;
            }
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.enabled = true;
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null) raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            raycaster.enabled = true;
            return canvas;
        }

        private static PointableCanvas EnsurePointable(Canvas canvas)
        {
            var pointable = canvas.GetComponent<PointableCanvas>();
            if (pointable == null) pointable = canvas.gameObject.AddComponent<PointableCanvas>();
            pointable.InjectCanvas(canvas);
            pointable.enabled = true;
            return pointable;
        }

        /// <summary>Edits this camera port on its own surface, including during teleoperation.</summary>
        public void OpenPortKeypad(TMP_InputField field)
        {
            if (field == null || !CanInteract) return;
            dragHandle?.CancelDrag();
            WorkspaceKeypad.Show(panelRect, field, null, source, HasCameraInput);
        }

        private void RefreshBarLayout()
        {
            // Include graphics extending below the UDP prefab rectangle, especially its
            // video image. The bar has its own bounded surface below every existing control.
            Rect rect = panelRect.rect;
            float bottom = rect.yMin;
            foreach (var graphic in panelRect.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.transform.IsChildOf(barRect) ||
                    graphic.GetComponentInParent<WorkspaceKeypad>() != null) continue;
                graphic.rectTransform.GetWorldCorners(corners);
                for (int i = 0; i < corners.Length; i++)
                    bottom = Mathf.Min(bottom, panelRect.InverseTransformPoint(corners[i]).y);
            }
            float height = Mathf.Clamp(rect.height * .085f, 32f, 50f);
            float gap = Mathf.Max(12f, height * .25f);
            barRect.sizeDelta = new Vector2(rect.width * .6f, height);
            barRect.localPosition = new Vector3(rect.center.x, bottom - gap - height * .5f, 0f);
            barRect.localRotation = Quaternion.identity;
            barRect.localScale = Vector3.one;
            lastPanelRect = rect;
        }

        private void LateUpdate()
        {
            if (!initialized || panelRect == null) return;
            if (panelRect.rect != lastPanelRect) RefreshBarLayout();
            if (canvas != null && canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            if (source == null || source.Head == null || WorkspaceKeypad.IsOpen ||
                (dragHandle != null && dragHandle.IsDragging)) return;

            // Unity UI fronts point along local -Z. Preserve the selected world position
            // and smoothly face the user with world up, independently for each view.
            Vector3 away = Vector3.ProjectOnPlane(panelRect.position - source.Head.position, Vector3.up);
            if (away.sqrMagnitude < .0001f) return;
            float blend = 1f - Mathf.Exp(-FacingSharpness * Time.unscaledDeltaTime);
            panelRect.rotation = Quaternion.Slerp(panelRect.rotation,
                Quaternion.LookRotation(away, Vector3.up), blend);
        }

        private void OnDisable() => dragHandle?.CancelDrag();
    }
}
