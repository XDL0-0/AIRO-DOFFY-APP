using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// 挂到 udpWindowPrefab（有 RectTransform）
/// 需求：光标指到该窗口上时，按住右手食指扳机(RIndexTrigger)即可用右手摇杆移动；松开停止。
public class WindowJoystickHoldMove : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("移动参数")]
    [Tooltip("像素/秒（UI 坐标系速度）")]
    public float moveSpeed = 800f;
    [Tooltip("按住A键(OVR Button.One)加速倍数")]
    public float fastMultiplier = 1.8f;
    [Tooltip("摇杆死区")]
    public float deadZone = 0.15f;
    [Tooltip("限制在父容器内")]
    public bool clampToParent = true;

    [Header("视觉反馈（可选）")]
    public Graphic highlightWhileGrab;   // 抓取时显示/加粗的外框或Image
    public Graphic highlightWhileHover;  // 仅悬停时显示的浅高亮

    RectTransform rect;
    RectTransform parentRect;
    Canvas rootCanvas;

    bool isHover = false;   // 光标是否在窗口上
    bool isGrab = false;   // 是否正被扳机“抓住”

    // 全局互斥：保证一次只“抓住”一个窗口
    static WindowJoystickHoldMove currentGrab = null;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        if (transform.parent != null) parentRect = transform.parent as RectTransform;
        rootCanvas = GetComponentInParent<Canvas>();
        SetHover(false);
        SetGrab(false);
    }

    public void OnPointerEnter(PointerEventData eventData) => SetHover(true);
    public void OnPointerExit(PointerEventData eventData) => SetHover(false);

    void Update()
    {
        // 开始抓取：只有当光标在该窗口上，且当前没有别的窗口被抓住，且按下R扳机
        if (!isGrab && isHover && (currentGrab == null) && OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
        {
            BeginGrab();
        }

        // 维持抓取：必须一直按住R扳机；松开则结束
        if (isGrab)
        {
            if (!OVRInput.Get(OVRInput.RawButton.RIndexTrigger))
            {
                EndGrab();
                return;
            }

            // 用右手摇杆移动
            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
            if (stick.magnitude >= deadZone)
            {
                float speed = moveSpeed * Time.unscaledDeltaTime;
                if (OVRInput.Get(OVRInput.Button.One)) speed *= fastMultiplier; // A键加速

                float scale = (rootCanvas != null) ? rootCanvas.scaleFactor : 1f;
                Vector2 delta = stick * speed / Mathf.Max(0.0001f, scale);
                rect.anchoredPosition += delta;

                if (clampToParent && parentRect != null) ClampToParent();
            }
        }
        else
        {
            // 没抓住时，如果玩家在别处按下扳机，不影响本窗口
            // 悬停高亮已在 SetHover() 里处理
        }
    }

    void BeginGrab()
    {
        isGrab = true;
        currentGrab = this;
        SetGrab(true);
    }

    void EndGrab()
    {
        isGrab = false;
        if (currentGrab == this) currentGrab = null;
        SetGrab(false);
    }

    void SetHover(bool on)
    {
        isHover = on;
        if (!isGrab && highlightWhileHover != null)
            highlightWhileHover.enabled = on;
    }

    void SetGrab(bool on)
    {
        if (highlightWhileGrab != null) highlightWhileGrab.enabled = on;
        // 抓取时取消仅悬停高亮，避免叠加
        if (highlightWhileHover != null) highlightWhileHover.enabled = (!on && isHover);
    }

    void ClampToParent()
    {
        Vector2 pos = rect.anchoredPosition;
        Vector2 size = rect.rect.size * rect.lossyScale; // 保险起见考虑缩放
        Vector2 half = size * 0.5f;

        Vector2 parentSize = parentRect.rect.size;
        Vector2 parentHalf = parentSize * 0.5f;

        float minX = -parentHalf.x + half.x;
        float maxX = parentHalf.x - half.x;
        float minY = -parentHalf.y + half.y;
        float maxY = parentHalf.y - half.y;

        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        rect.anchoredPosition = pos;
    }
}
