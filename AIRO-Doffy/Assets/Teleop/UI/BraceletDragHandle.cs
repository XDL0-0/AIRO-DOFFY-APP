using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Pointer-event bridge for one of the bracelet's tangential drag-band
/// Graphics. All segment handles share the same rotation/capture owner; hand
/// capture is maintained from fingertip geometry across segment pointer changes.
/// </summary>
public sealed class BraceletDragHandle : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
{
    private BraceletRotator _owner;

    public BraceletRotator Owner => _owner;

    /// <summary>Initializes the pointer-event target for the bracelet drag band.</summary>
    public void Initialize(BraceletRotator owner)
    {
        if (_owner == owner)
            return;

        if (_owner != null)
            _owner.HandleDisabled(this);

        _owner = owner;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (_owner != null && _owner.AcceptsPointer(eventData))
            eventData.useDragThreshold = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_owner != null)
            _owner.BeginPointerInteraction(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        // The rotator samples the Meta interactor every LateUpdate. This event
        // only keeps Unity's EventSystem capture active for the controller path.
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (_owner != null)
            _owner.PointerReleased(this, eventData);
    }

    private void OnDisable()
    {
        if (_owner != null)
            _owner.HandleDisabled(this);
    }
}
