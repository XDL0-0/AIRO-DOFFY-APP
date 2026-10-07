using UnityEngine;
using UnityEngine.EventSystems;

namespace Doffy.UI
{
    public sealed class WorkspaceDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Transform Target { get; set; }
        private Vector3 initialPointer, initialPosition, normal;
        private bool dragging;

        public void OnPointerDown(PointerEventData data)
        {
            if (Target == null || !data.pointerCurrentRaycast.isValid) return;
            initialPointer = data.pointerCurrentRaycast.worldPosition;
            initialPosition = Target.position;
            normal = Target.forward;
            dragging = true;
            AppManager.Instance?.HandleTrackingLost("Workspace repositioning");
        }

        public void OnDrag(PointerEventData data)
        {
            if (!dragging || !data.pointerCurrentRaycast.isValid) return;
            Vector3 offset = Vector3.ProjectOnPlane(data.pointerCurrentRaycast.worldPosition - initialPointer, normal);
            Target.position = initialPosition + Vector3.ClampMagnitude(offset, 1.5f);
        }

        public void OnPointerUp(PointerEventData data) => dragging = false;
    }
}
