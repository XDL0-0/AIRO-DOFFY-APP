using System;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Doffy.UI
{
    /// <summary>Captures a floating panel's dedicated drag bar, independently of its buttons.</summary>
    [DefaultExecutionOrder(20)]
    public sealed class TeleopPanelDragHandle : MonoBehaviour, IPointerDownHandler,
        IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        private Transform target;
        private WristInteractionSource source;
        private RoundedPanel background;
        private Func<bool> interactionAvailable;
        private Vector3 initialPosition, initialPointer, dragNormal;
        private float rayDistance;
        private int pointerId;
        private bool handCapture, hovered, hoverHands;

        public bool IsDragging { get; private set; }

        public void Initialize(Transform panel, WristInteractionSource interactionSource,
            Func<bool> availability = null)
        {
            CancelDrag();
            target = panel;
            source = interactionSource;
            // Session callers retain their recording-only gate. Camera panels supply a
            // scope that permits the same right input before and during teleoperation.
            interactionAvailable = availability;
            background = GetComponent<RoundedPanel>();
            RefreshColor();
        }

        private bool Available => isActiveAndEnabled && target != null &&
            target.gameObject.activeInHierarchy && source != null && source.isActiveAndEnabled &&
            source.Head != null && (interactionAvailable?.Invoke() ?? source.CanInteractForRecording);

        private bool AcceptsPointer(PointerEventData data)
        {
            if (data == null || source == null) return false;
            return source.HandsActive
                ? source.RightPoke != null && data.pointerId == source.RightPoke.Identifier
                : source.ControllerModeActive && source.RightRay != null && data.pointerId == source.RightRay.Identifier;
        }

        public void OnInitializePotentialDrag(PointerEventData data)
        {
            if (AcceptsPointer(data)) data.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (IsDragging || !Available || !AcceptsPointer(data) ||
                data.pointerPressRaycast.gameObject != gameObject || !data.pointerCurrentRaycast.isValid)
                return;

            handCapture = source.HandsActive;
            if (handCapture)
            {
                var poke = source.RightPoke;
                if (!poke.isActiveAndEnabled || poke.State != InteractorState.Select) return;
                initialPointer = poke.Origin;
            }
            else
            {
                var ray = source.RightRay;
                if (!ray.isActiveAndEnabled || ray.State != InteractorState.Select) return;
                rayDistance = Vector3.Dot(data.pointerCurrentRaycast.worldPosition - ray.Origin,
                    ray.Forward.normalized);
                if (!IsFinite(rayDistance) || rayDistance < 0f) return;
                initialPointer = ray.Origin + ray.Forward.normalized * rayDistance;
            }
            if (!IsFinite(initialPointer)) return;
            initialPosition = target.position;
            dragNormal = target.forward;
            pointerId = data.pointerId;
            IsDragging = true;
            RefreshColor();
        }

        public void OnDrag(PointerEventData data)
        {
            // Meta poses keep capture moving even when the UI raycast misses the bar.
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (!IsDragging || data == null || data.pointerId != pointerId) return;
            if (handCapture || !Available || !IsTriggerHeld()) CancelDrag();
        }

        private void LateUpdate()
        {
            if (!Available)
            {
                hovered = false;
                CancelDrag();
                return;
            }
            if (hovered && hoverHands != source.HandsActive)
            {
                hovered = false;
                RefreshColor();
            }
            if (!IsDragging) return;
            if (handCapture != source.HandsActive)
            {
                CancelDrag();
                return;
            }

            Vector3 pointer;
            if (handCapture)
            {
                var poke = source.RightPoke;
                if (poke == null || !poke.isActiveAndEnabled || poke.Identifier != pointerId ||
                    poke.State != InteractorState.Select)
                {
                    CancelDrag();
                    return;
                }
                // Keep the contact plane fixed in depth so lifting the finger releases
                // the poke instead of pulling the panel along with the fingertip.
                pointer = initialPointer + Vector3.ProjectOnPlane(poke.Origin - initialPointer, dragNormal);
            }
            else
            {
                var ray = source.RightRay;
                if (ray == null || !ray.isActiveAndEnabled || ray.Identifier != pointerId ||
                    ray.State == InteractorState.Disabled || !IsTriggerHeld())
                {
                    CancelDrag();
                    return;
                }
                pointer = ray.Origin + ray.Forward.normalized * rayDistance;
            }

            if (!IsFinite(pointer)) { CancelDrag(); return; }
            target.position = initialPosition + pointer - initialPointer;
        }

        public void CancelDrag()
        {
            IsDragging = false;
            RefreshColor();
        }

        public void OnPointerEnter(PointerEventData data)
        {
            if (!Available || !AcceptsPointer(data)) return;
            hovered = true;
            hoverHands = source.HandsActive;
            RefreshColor();
        }

        public void OnPointerExit(PointerEventData data)
        {
            if (!AcceptsPointer(data)) return;
            hovered = false;
            RefreshColor();
        }

        private void RefreshColor()
        {
            if (background != null)
                background.color = IsDragging ? WorkspaceTheme.Accent : hovered ? WorkspaceTheme.Muted : WorkspaceTheme.Raised;
        }

        private static bool IsTriggerHeld() =>
            OVRInput.Get(OVRInput.RawButton.RIndexTrigger) ||
            OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) >= 0.5f;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private void OnDisable()
        {
            hovered = false;
            CancelDrag();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            hovered = false;
            CancelDrag();
        }
        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            hovered = false;
            CancelDrag();
        }
    }
}
