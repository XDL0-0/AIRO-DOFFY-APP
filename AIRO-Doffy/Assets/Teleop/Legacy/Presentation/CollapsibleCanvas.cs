using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CollapsibleCanvas : MonoBehaviour
{
    // —— 配置 —— //
    public enum HideMode { SetActive, CanvasGroup }

    [Header("Wiring")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private RectTransform headerArea;   // 可选：始终可见
    [SerializeField] private RectTransform bodyArea;     // 必填：折叠/展开的主体

    [Header("Collapsed State")]
    [Tooltip("折叠时仍需显示的元素（必须是 BodyArea 的后代，可任意层级）。")]
    [SerializeField] private List<GameObject> showWhenCollapsed = new();
    [SerializeField] private bool startCollapsed = false;

    [Header("Button Label (TMP 可选)")]
    [SerializeField] private TextMeshProUGUI tmpButtonLabel; // 按钮文字（TMP）

    [Header("Hiding Mode")]
    [SerializeField] private HideMode hideMode = HideMode.CanvasGroup; // 推荐 CanvasGroup（软隐藏）

    private bool _collapsed;

    // —— 状态缓存 —— //
    // 初始状态：用于第一次展开时的兜底
    private struct NodeState
    {
        public bool active;
        public bool hadCanvasGroup;
        public float cgAlpha;
        public bool cgInteractable;
        public bool cgBlocksRaycasts;
    }

    private readonly Dictionary<Transform, NodeState> _initial = new();
    private bool _cachedInitial;

    // 最近一次“可见状态”的快照：在每次折叠之前记录，展开时恢复到它
    private readonly Dictionary<Transform, NodeState> _preCollapseSnapshot = new();

    // —— 生命周期 —— //
    private void Reset()
    {
        if (!toggleButton) toggleButton = GetComponentInChildren<Button>(true);
        if (!bodyArea)
        {
            var rt = GetComponent<RectTransform>();
            if (rt && rt.childCount > 0)
                bodyArea = rt.GetChild(rt.childCount - 1) as RectTransform;
        }
    }

    private void Awake()
    {
        if (toggleButton) toggleButton.onClick.AddListener(Toggle);
        else Debug.LogWarning("CollapsibleCanvas: Toggle Button is not assigned.");
    }

    private void Start()
    {
        CacheInitialStatesIfNeeded(); // 记录初始（兜底）
        ApplyState(startCollapsed);
    }

    private void OnDestroy()
    {
        if (toggleButton) toggleButton.onClick.RemoveListener(Toggle);
    }

    // —— 对外 API —— //
    public void SetCollapsed(bool collapsed) => ApplyState(collapsed);
    public void Toggle() => ApplyState(!_collapsed);

    // —— 主逻辑 —— //
    private void ApplyState(bool collapsed)
    {
        // 在任何隐藏/显示之前确保 bodyArea
        if (!bodyArea)
        {
            Debug.LogWarning("CollapsibleCanvas: BodyArea is not assigned.");
            return;
        }

        // —— 关键改动：若本次目标为“折叠”，先对当前可见状态做快照 —— //
        if (collapsed)
            SnapshotCurrentVisibleState(bodyArea, _preCollapseSnapshot);

        _collapsed = collapsed;

        // 确保已有初始兜底
        CacheInitialStatesIfNeeded();

        // BodyArea 自身保持激活（CanvasGroup 模式需要）
        if (!bodyArea.gameObject.activeSelf) bodyArea.gameObject.SetActive(true);

        // 可选兜底：若按钮/文字在 BodyArea 下，则自动加入白名单
        if (toggleButton && IsUnder(toggleButton.transform, bodyArea) &&
            !showWhenCollapsed.Contains(toggleButton.gameObject))
            showWhenCollapsed.Add(toggleButton.gameObject);

        if (tmpButtonLabel && IsUnder(tmpButtonLabel.transform, bodyArea) &&
            !showWhenCollapsed.Contains(tmpButtonLabel.gameObject))
            showWhenCollapsed.Add(tmpButtonLabel.gameObject);

        // 1) 先隐藏整棵 BodyArea 子树
        if (hideMode == HideMode.SetActive) HideAllDescendants_SetActive(bodyArea);
        else HideAllDescendants_CanvasGroup(bodyArea);

        // 2) 再根据状态恢复
        if (!collapsed)
        {
            // 展开：优先恢复到“上一次折叠前”的快照；若缺失则退回初始状态
            if (hideMode == HideMode.SetActive)
            {
                if (!RestoreFromSnapshot_SetActive(bodyArea, _preCollapseSnapshot))
                    RestoreActiveRecursive(bodyArea);
            }
            else
            {
                if (!RestoreFromSnapshot_CanvasGroup(bodyArea, _preCollapseSnapshot))
                    RestoreCanvasGroupRecursive(bodyArea);
            }
        }
        else
        {
            // 折叠：只恢复白名单及其父链为可见/可交互
            foreach (var go in showWhenCollapsed)
            {
                if (!go || !IsUnder(go.transform, bodyArea)) continue;

                var t = go.transform;
                while (t && t != bodyArea)
                {
                    if (hideMode == HideMode.SetActive)
                    {
                        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    }
                    else
                    {
                        var cgA = EnsureCanvasGroup(t.gameObject);
                        cgA.alpha = 1f; cgA.interactable = true; cgA.blocksRaycasts = true;
                    }
                    t = t.parent;
                }

                if (hideMode == HideMode.SetActive)
                    go.SetActive(true);
                else
                {
                    var cg = EnsureCanvasGroup(go);
                    cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true;
                }
            }
        }

        UpdateButtonLabel();
        RefreshLayout();
    }

    // —— 初始/快照 缓存 与 恢复 —— //
    private void CacheInitialStatesIfNeeded()
    {
        if (_cachedInitial || !bodyArea) return;
        _initial.Clear();
        CacheInitialRecursive(bodyArea);
        _cachedInitial = true;
    }

    private void CacheInitialRecursive(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            _initial[c] = CaptureNodeState(c);
            CacheInitialRecursive(c);
        }
    }

    private NodeState CaptureNodeState(Transform t)
    {
        var st = new NodeState
        {
            active = t.gameObject.activeSelf,
            hadCanvasGroup = false,
            cgAlpha = 1f,
            cgInteractable = true,
            cgBlocksRaycasts = true,
        };
        var cg = t.GetComponent<CanvasGroup>();
        if (cg)
        {
            st.hadCanvasGroup = true;
            st.cgAlpha = cg.alpha;
            st.cgInteractable = cg.interactable;
            st.cgBlocksRaycasts = cg.blocksRaycasts;
        }
        return st;
    }

    private void SnapshotCurrentVisibleState(Transform root, Dictionary<Transform, NodeState> dst)
    {
        dst.Clear();
        SnapshotRecursive(root, dst);
    }

    private void SnapshotRecursive(Transform root, Dictionary<Transform, NodeState> dst)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            dst[c] = CaptureNodeState(c);
            SnapshotRecursive(c, dst);
        }
    }

    private void RestoreActiveRecursive(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (_initial.TryGetValue(c, out var st))
                if (c.gameObject.activeSelf != st.active)
                    c.gameObject.SetActive(st.active);
            RestoreActiveRecursive(c);
        }
    }

    private void RestoreCanvasGroupRecursive(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (_initial.TryGetValue(c, out var st))
            {
                var cg = c.GetComponent<CanvasGroup>();
                if (st.hadCanvasGroup)
                {
                    if (!cg) cg = c.gameObject.AddComponent<CanvasGroup>();
                    cg.alpha = st.cgAlpha;
                    cg.interactable = st.cgInteractable;
                    cg.blocksRaycasts = st.cgBlocksRaycasts;
                }
                else if (cg)
                {
                    Destroy(cg);
                }
            }
            RestoreCanvasGroupRecursive(c);
        }
    }

    private bool RestoreFromSnapshot_SetActive(Transform root, Dictionary<Transform, NodeState> snap)
    {
        bool hadAny = snap.Count > 0;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (snap.TryGetValue(c, out var st))
                c.gameObject.SetActive(st.active);
            RestoreFromSnapshot_SetActive(c, snap);
        }
        return hadAny;
    }

    private bool RestoreFromSnapshot_CanvasGroup(Transform root, Dictionary<Transform, NodeState> snap)
    {
        bool hadAny = snap.Count > 0;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (snap.TryGetValue(c, out var st))
            {
                var cg = c.GetComponent<CanvasGroup>();
                if (st.hadCanvasGroup)
                {
                    if (!cg) cg = c.gameObject.AddComponent<CanvasGroup>();
                    cg.alpha = st.cgAlpha;
                    cg.interactable = st.cgInteractable;
                    cg.blocksRaycasts = st.cgBlocksRaycasts;
                }
                else if (cg)
                {
                    Destroy(cg);
                }
            }
            RestoreFromSnapshot_CanvasGroup(c, snap);
        }
        return hadAny;
    }

    // —— 小工具 —— //
    private static bool IsUnder(Transform child, RectTransform root)
    {
        var t = child;
        while (t != null)
        {
            if (t == root) return true;
            t = t.parent;
        }
        return false;
    }

    private void UpdateButtonLabel()
    {
        if (!tmpButtonLabel) return;
        if (tmpButtonLabel.isActiveAndEnabled)
            tmpButtonLabel.text = _collapsed ? "+" : "−";
    }

    private void RefreshLayout()
    {
        var rt = GetComponent<RectTransform>();
        if (rt)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            var p = rt.parent as RectTransform;
            if (p) LayoutRebuilder.ForceRebuildLayoutImmediate(p);
        }
    }

    // —— SetActive 模式 —— //
    private static void HideAllDescendants_SetActive(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.gameObject.activeSelf) c.gameObject.SetActive(false);
            HideAllDescendants_SetActive(c);
        }
    }

    // 按初始 active 状态恢复
    // （注意：真正的恢复逻辑在 RestoreActiveRecursive 中执行）
    private static void ShowAllDescendants_SetActive(Transform root)
    {
        // 已废弃：保持空实现，避免误用。
    }

    // —— CanvasGroup 模式（软隐藏） —— //
    private static CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private static void HideAllDescendants_CanvasGroup(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            var cg = EnsureCanvasGroup(c.gameObject);
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
            HideAllDescendants_CanvasGroup(c);
        }
    }

    // 展开时不直接“全开”，而是走 RestoreCanvasGroupRecursive 恢复到缓存值
    private static void ShowAllDescendants_CanvasGroup(Transform root) { }
}
