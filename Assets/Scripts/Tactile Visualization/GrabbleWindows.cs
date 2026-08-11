using UnityEngine;
using UnityEngine.EventSystems;

public class DraggableWindowWorld : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    // 建议指向“窗口根节点”，比如子 Canvas 外面包的一个空物体
    public Transform target;

    private Transform _parent;
    private Vector3 _startLocalTargetPos;
    private Vector3 _startLocalPointerPos;
    private bool _dragging = false;

    void Awake()
    {
        if (target == null)
            target = transform;      // 没填就移动自己

        _parent = target.parent;     // 在父物体坐标系下移动
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_parent == null || !eventData.pointerCurrentRaycast.isValid)
            return;

        // 只在“从这个背景对象本身按下”时才允许拖动
        // 点到子节点（X 按钮、其他 UI）的情况直接忽略
        if (eventData.pointerPressRaycast.gameObject != gameObject)
        {
            _dragging = false;
            return;
        }

        _startLocalTargetPos = target.localPosition;
        _startLocalPointerPos = _parent.InverseTransformPoint(
            eventData.pointerCurrentRaycast.worldPosition
        );

        _dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || _parent == null || !eventData.pointerCurrentRaycast.isValid)
            return;

        // 当前命中点在父物体局部坐标里的位置
        Vector3 curLocalPointerPos = _parent.InverseTransformPoint(
            eventData.pointerCurrentRaycast.worldPosition
        );

        Vector3 deltaLocal = curLocalPointerPos - _startLocalPointerPos;

        // 只在父物体平面内移动，锁 z
        deltaLocal.z = 0;

        target.localPosition = _startLocalTargetPos + deltaLocal;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _dragging = false;
    }
}
