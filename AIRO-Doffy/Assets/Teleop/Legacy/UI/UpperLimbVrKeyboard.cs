using System.Collections.Generic;
using System.Net;
using Doffy.UI;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// VR 浮空数字键盘 —— 3×5（1-9 / 0 . Back / 最后一行全宽 Enter）。
///
/// 交互方式：聚焦输入框时，以当下头部（眼睛）位置为锚点生成一个独立的
/// WorldSpace 画布键盘（不在 Main Canvas 内）：位置 = 眼睛前方 distanceInFront
/// （默认 1.0m，水平化视线方向）+ 下方 heightBelowEye（默认 0.15m）、朝向 =
/// 与 Main Canvas 平行（不随玩家/相机转动，玩家以看场景 UI 的视角看键盘即为
/// 正）。每次显示时重新取锚点放置一次，显示期间固定不动（不跟随、不旋转），
/// 用右控制器射线或右手直接戳击按键。
///
/// 收起规则：键盘显示期间，射线或戳击在键盘以外的任何 UI 上按下（如 Main Canvas、
/// 场景按钮等）即收起；按下键盘按键或输入框本体则保持。
///
/// 命中链路（自包含，不依赖 Main Canvas 的任何交互组件）：
///   射线或手部戳击 → 键盘有限 RayInteractable/PokeInteractable 表面（由
///   WristCanvasSurface 提供）→ 自建 PointableCanvas →
///   GraphicRaycaster RaycastAll → 按键 Image（raycastTarget = true）→
///   IPointerClickHandler。
/// 键盘隐藏时整棵子树 SetActive(false)，射线/戳击交互随之注销，不产生干扰。
///
/// 背景：Unity 6 的 Android 入口换成 GameActivity 后，TouchScreenKeyboard 对
/// Quest 的系统键盘 overlay 永远等不到可见性回报（引擎原生层 UGASoftKeyboard.cpp
/// 在 GameActivity_showSoftInput 后同步等待 400ms，等不到就判失败，见 UUM-92151
/// 系列），因此一律 shouldHideSoftKeyboard = true 并用场景内键盘。
///
/// 交互模型：输入框只用于"聚焦显示 + 读取最终文本"，打字过程不进入 TMP 编辑态
/// （Quest 上编辑态显示刷新不可靠：实测打字后内容不即时显示，点击其它文本框
/// 才刷新；且重新聚焦触发 OnFocus→SelectAll 全选高亮，表现为"Enter 后仍选中"）。
/// 因此每次按键直接写 field.text（SetText→UpdateLabel 非编辑态即时显示），
/// 不调用 Select/ActivateInputField。
/// Enter 提交：显式 onEndEdit.Invoke（合法 IP 存入 PlayerPrefs（cfg_ip，
/// UdpSocket 启动时读取））→ 键盘隐藏。
///
/// 约束：只新建脚本；不改任何现有 .cs / 场景文件；运行时自建（同
/// UpperLimbAkmBootstrap 先例）。
/// </summary>
public class UpperLimbVrKeyboard : MonoBehaviour
{
    [Header("Input Fields")]
    [Tooltip("需要绑定键盘的输入框物体名（场景内 GameObject 名）")]
    public string[] fieldNames = { "IP Input", "num_WebRTC Input" };
    [Tooltip("IP 输入框名：编辑结束时把合法 IP 存入 PlayerPrefs（UdpSocket 启动时读取 cfg_ip）")]
    public string ipFieldName = "IP Input";
    [Tooltip("PlayerPrefs 键名，与 UdpSocket.cs 的 cfg_ip 保持一致")]
    public string ipPrefsKey = "cfg_ip";

    [Header("Anchored Position (head)")]
    [Tooltip("键盘在眼睛正前方的距离（水平方向，略低于视线 heightBelowEye）。聚焦时以当下头部位置为锚点放置，显示期间固定不动，重新显示时再以当下头部位置为准")]
    public float distanceInFront = 1.0f;
    [Tooltip("键盘中心相对眼睛的下移量（世界 y 方向）")]
    public float heightBelowEye = 0.15f;

    [Header("Keyboard Layout")]
    [Tooltip("按键宽/高（画布像素单位）")]
    public float keyWidth = 80f;
    public float keyHeight = 80f;
    public float keyGap = 10f;
    public float margin = 16f;
    [Tooltip("画布缩放：按键世界尺寸 = 像素 × 该缩放")]
    public float worldScale = 0.0012f;

    [Header("Appearance")]
    public Color panelColor = new Color(0.05f, 0.07f, 0.11f, 0.85f);
    public Color keyColor = new Color(0.18f, 0.21f, 0.28f, 0.95f);
    public Color funcKeyColor = new Color(0.13f, 0.16f, 0.22f, 0.95f);
    public Color textColor = Color.white;
    public float fontSize = 44f;

    // ===== 内部状态 =====
    private readonly List<TMP_InputField> _fields = new List<TMP_InputField>();
    private readonly List<UpperLimbVrKey> _keys = new List<UpperLimbVrKey>();
    private readonly List<IPointableElement> _sceneElements = new List<IPointableElement>();
    private TMP_InputField _ipField;
    private TMP_InputField _activeField;
    private RectTransform _keyboard;
    private RayInteractable _rayInteractable;   // 键盘自己的交互表面（不参与"按下收起"判定）
    private Transform _eyeAnchor;
    private Transform _canvasAlignment;   // Main Canvas 朝向：键盘平面与之平行
    private bool _built;
    private bool _firstUpdate = true;

    private void Awake()
    {
        // 绑定输入框；一律关系统键盘（Quest + GameActivity 上引擎 400ms 等待必超时）
        foreach (var field in Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include))
        {
            foreach (var name in fieldNames)
            {
                if (field.gameObject.name != name) continue;
                _fields.Add(field);
                field.shouldHideSoftKeyboard = true;
                if (name == ipFieldName) _ipField = field;
                field.onEndEdit.AddListener(_ => OnFieldEditEnd(field));
                break;
            }
        }

        if (_fields.Count == 0)
        {
            LogManager.Log("UpperLimbVRKB", "No input fields found, VR keyboard disabled");
            return;
        }

        BuildKeyboard();
        _built = true;
        LogManager.Log("UpperLimbVRKB", $"Fixed VR keypad built for {_fields.Count} field(s)");

        // 收起检测：监听场景所有 RayInteractable 的 Select（按下）事件 —— 除键盘自身
        // 表面外，任何可交互表面被按下（Main Canvas、场景按钮等）即收起键盘。
        // （早先尝试订阅 PointableCanvasModule.WhenPointerStarted：Pointer 在场景
        // 启动时射线首次 hover 就创建，早于本脚本 Awake，事件被错过，点击不收起。）
        foreach (var ri in Object.FindObjectsByType<RayInteractable>(FindObjectsInactive.Exclude))
        {
            if (ri == _rayInteractable) continue;
            var element = ri.PointableElement;
            if (element == null) continue;
            element.WhenPointerEventRaised += OnSceneSelectableSelect;
            _sceneElements.Add(element);
        }
    }

    private void OnDestroy()
    {
        foreach (var element in _sceneElements)
            element.WhenPointerEventRaised -= OnSceneSelectableSelect;
    }

    /// <summary>场景任一非键盘交互表面被按下（Select 边沿事件）→ 收起键盘并提交输入框。</summary>
    private void OnSceneSelectableSelect(PointerEvent evt)
    {
        if (evt.Type != PointerEventType.Select) return;
        if (_activeField == null) return;
        var field = _activeField;
        CommitAndHide(field);
        LogManager.Log("UpperLimbVRKB", "keypad hidden: select on scene interactable, committed");
    }

    private void Update()
    {
        if (!_built) return;

        // 延迟一帧再隐藏：保证面板子树各组件的 Start（RayInteractable / PointableCanvas
        // 注册等）先执行
        if (_firstUpdate)
        {
            _firstUpdate = false;
            _keyboard.gameObject.SetActive(false);
        }

        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;

        TMP_InputField selField = null;
        if (sel != null)
        {
            selField = sel.GetComponentInParent<TMP_InputField>();
            if (selField != null && !_fields.Contains(selField)) selField = null;
        }

        if (selField != null)
        {
            _activeField = selField;
        }
        else if (sel != null && sel.GetComponentInParent<UpperLimbVrKey>() == null)
        {
            if (_activeField != null)        // 点选了其它 UI（如 WRM_enable）→ 提交并关闭键盘
                CommitAndHide(_activeField);
        }
        // sel == null：保持当前激活字段。按键点击后 PointableCanvasModule 会清空选中、
        // Enter 提交后也会清空选中，不能因此让键盘消失；点选其它 Selectable UI 走上方
        // 分支关闭，点选非 Selectable 交互表面由 OnSceneSelectableSelect 收起。

        bool show = _activeField != null;
        if (_keyboard.gameObject.activeSelf != show)
        {
            if (show)
                PlaceAtHead();   // 每次显示时以当下头部（眼睛）位置为锚点
            _keyboard.gameObject.SetActive(show);
            LogManager.Log("UpperLimbVRKB", show
                ? "keypad shown at " + _keyboard.position.ToString("F2")
                : "keypad hidden");
        }
    }

    /// <summary>输入框编辑结束（失焦/Enter 提交）：IP 框存入 PlayerPrefs（仅合法 IP）。</summary>
    private void OnFieldEditEnd(TMP_InputField field)
    {
        if (field != _ipField) return;
        if (IPAddress.TryParse(field.text, out _))
        {
            PlayerPrefs.SetString(ipPrefsKey, field.text);
            LogManager.Log("UpperLimbVRKB", "IP saved to PlayerPrefs: " + field.text);
        }
    }

    /// <summary>由按键点击回调（UpperLimbVrKey.OnPointerClick）。</summary>
    public void OnKeyPressed(UpperLimbVrKey key)
    {
        var field = _activeField;
        if (field == null) return;

        switch (key.Kind)
        {
            case UpperLimbVrKey.KeyKind.Char:
                field.text = field.text + key.Char;
                field.MoveTextEnd(false);
                break;
            case UpperLimbVrKey.KeyKind.Backspace:
                if (field.text.Length > 0)
                {
                    field.text = field.text.Substring(0, field.text.Length - 1);
                    field.MoveTextEnd(false);
                }
                break;
            case UpperLimbVrKey.KeyKind.Enter:
                CommitField(field);
                return;
        }

        // 刻意不调用 field.Select()/ActivateInputField()：输入框保持非编辑态，
        // 内容经 SetText→UpdateLabel 即时显示（编辑态下 Quest 显示刷新不可靠，
        // 且重新聚焦触发 SelectAll 全选高亮，表现为"总是选中状态"）。
        LogManager.Log("UpperLimbVRKB",
            $"key pressed: {key.Kind} on {field.gameObject.name}, text=\"{field.text}\"");
    }

    /// <summary>Enter：提交并关闭键盘。显式触发 onEndEdit → 保存 IP。</summary>
    private void CommitField(TMP_InputField field)
    {
        CommitAndHide(field);
        LogManager.Log("UpperLimbVRKB",
            $"enter committed on {field.gameObject.name}, text=\"{field.text}\"");
    }

    /// <summary>
    /// 提交输入框并收起键盘（Enter 与"点击键盘外收起"共用）。
    /// 取消 EventSystem 选中 → 退出编辑态 → 显式清 m_SelectionStillActive（光标
    /// CaretBlink 协程 `while (isFocused || m_SelectionStillActive)` 依赖它退出）→
    /// 显式补发 onEndEdit（打字期间输入框非编辑态、未选中，失焦路径不会触发
    /// onEndEdit，必须显式补发才能保存 IP；幂等，OnFieldEditEnd 只做合法 IP
    /// 校验与 PlayerPrefs 写入，重复调用无副作用）。
    /// </summary>
    private void CommitAndHide(TMP_InputField field)
    {
        _activeField = null;
        var es = EventSystem.current;
        if (es != null)
            es.SetSelectedGameObject(null);   // 取消 EventSystem 选中 → 输入框 OnDeselect
        field.DeactivateInputField(true);     // 退出编辑态（编辑态时走 ReleaseSelection）
        field.ReleaseSelection();             // 清 m_SelectionStillActive → 光标协程退出
        field.onEndEdit.Invoke(field.text);   // 补发 onEndEdit → 保存 IP
    }

    // ===== 键盘构建 =====

    private void BuildKeyboard()
    {
        float innerW = 3 * keyWidth + 2 * keyGap;                  // 数字三列宽度
        float panelW = innerW + 2 * margin;
        float panelH = 5 * keyHeight + 4 * keyGap + 2 * margin;

        // 独立 WorldSpace 画布（不在 Main Canvas 内）
        var rootGO = new GameObject("VR Keyboard", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
            typeof(PointableCanvas));
        var rootRt = (RectTransform)rootGO.transform;
        rootRt.SetParent(transform, false);
        rootRt.sizeDelta = new Vector2(panelW, panelH);
        rootRt.localScale = Vector3.one * worldScale;
        _keyboard = rootRt;

        // 面板背景（不抢射线）
        var bgGO = new GameObject("Panel", typeof(RectTransform));
        var bgRt = (RectTransform)bgGO.transform;
        bgRt.SetParent(rootRt, false);
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bg = bgGO.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = false;

        var pc = rootGO.GetComponent<PointableCanvas>();
        pc.InjectCanvas(rootGO.GetComponent<Canvas>());
        WristInteractionSource source = GetComponentInParent<WristInteractionSource>();
        if (source == null) source = WristInteractionSource.Ensure();
        _rayInteractable = WristCanvasSurface.Create(rootRt, pc, source, true).Ray;

        float topY = panelH / 2f - margin - keyHeight / 2f;

        // 3×5：1-9 / 0 . Back / 最后一行全宽 Enter
        BuildRow(rootRt, 0, topY, new[] { "1", "2", "3" }, null);
        BuildRow(rootRt, 1, topY, new[] { "4", "5", "6" }, null);
        BuildRow(rootRt, 2, topY, new[] { "7", "8", "9" }, null);
        BuildRow(rootRt, 3, topY, new[] { "0", ".", "Back" }, null);
        BuildRow(rootRt, 4, topY, new[] { "Enter" }, new[] { innerW });
    }

    /// <summary>
    /// 以当下头部（眼睛）位置为锚点放置键盘：眼睛前方 distanceInFront（水平化
    /// 视线方向）+ 下方 heightBelowEye；平面与 Main Canvas 平行。仅在每次显示时
    /// 调用一次，显示期间固定不动。
    /// </summary>
    private void PlaceAtHead()
    {
        if (_eyeAnchor == null)
        {
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            _eyeAnchor = rig != null ? rig.centerEyeAnchor : null;
            if (_eyeAnchor == null)
                _eyeAnchor = Camera.main != null ? Camera.main.transform : null;
            if (_eyeAnchor == null)
                LogManager.Log("UpperLimbVRKB", "center eye anchor not found, keypad placement skipped");
        }

        if (_eyeAnchor != null)
        {
            // 取 forward 的水平分量（忽略俯仰）：键盘只随头部转身移动，
            // 低头看输入框时键盘不会贴到脸上。
            Vector3 fwd = _eyeAnchor.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
                fwd = Vector3.forward;   // 视线垂直上/下时无水平分量，兜底
            fwd.Normalize();
            _keyboard.position = _eyeAnchor.position
                + fwd * distanceInFront
                + Vector3.down * heightBelowEye;
        }

        // 键盘平面与 Main Canvas 平行（朝向不随玩家/相机转动，玩家以看场景 UI
        // 的视角看键盘即为正）。早期用 LookRotation 面向相机，视线接近垂直时
        // up 退化绕 Z 乱转；后改为"法线取视线水平分量"，玩家转身后键盘朝向
        // 固定不动仍显不正 —— 现直接复制 Main Canvas 朝向。
        if (_canvasAlignment == null)
            _canvasAlignment = GameObject.Find("Main Canvas")?.transform;
        _keyboard.rotation = _canvasAlignment != null ? _canvasAlignment.rotation : Quaternion.identity;
    }

    private void BuildRow(RectTransform parent, int row, float topY, string[] labels, float[] widths)
    {
        if (widths == null)
        {
            widths = new float[labels.Length];
            for (int i = 0; i < widths.Length; i++) widths[i] = keyWidth;
        }
        float rowWidth = 0f;
        for (int i = 0; i < labels.Length; i++) rowWidth += widths[i];
        rowWidth += (labels.Length - 1) * keyGap;

        float y = topY - row * (keyHeight + keyGap);
        float x = -rowWidth / 2f;
        for (int i = 0; i < labels.Length; i++)
        {
            x += widths[i] / 2f;
            CreateKey(parent, labels[i], x, y, widths[i]);
            x += widths[i] / 2f + keyGap;
        }
    }

    private void CreateKey(RectTransform parent, string label, float x, float y, float w)
    {
        var go = new GameObject("Key " + label, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, keyHeight);

        var key = go.AddComponent<UpperLimbVrKey>();
        key.Owner = this;
        key.SetLabel(label);
        go.AddComponent<WorkspaceKeyPressDepth>();

        var img = go.AddComponent<Image>();
        img.color = key.Kind == UpperLimbVrKey.KeyKind.Char ? keyColor : funcKeyColor;

        // 文字作为子物体拉伸铺满按键
        var txtGO = new GameObject("Text", typeof(RectTransform));
        var txtRt = (RectTransform)txtGO.transform;
        txtRt.SetParent(rt, false);
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;
        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        key.BindLabel(tmp);

        _keys.Add(key);
    }
}

/// <summary>单个按键：只实现 IPointerClickHandler（不实现 ISelectHandler，避免抢走输入框焦点）。</summary>
public class UpperLimbVrKey : MonoBehaviour, IPointerClickHandler
{
    public enum KeyKind { Char, Backspace, Enter }

    public KeyKind Kind { get; private set; } = KeyKind.Char;
    public char Char { get; private set; }
    public UpperLimbVrKeyboard Owner { get; set; }

    /// <summary>解析按键标签 → 类型与字符。</summary>
    public void SetLabel(string label)
    {
        if (label == "Back")
        {
            Kind = KeyKind.Backspace;
        }
        else if (label == "Enter")
        {
            Kind = KeyKind.Enter;
        }
        else
        {
            Kind = KeyKind.Char;
            Char = label[0];
        }
    }

    public void BindLabel(TextMeshProUGUI label)
    {
        switch (Kind)
        {
            case KeyKind.Char: label.text = Char.ToString(); break;
            case KeyKind.Backspace: label.text = "Back"; break;
            case KeyKind.Enter: label.text = "Enter"; break;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Owner?.OnKeyPressed(this);
    }
}

/// <summary>启动自举：场景加载后自动创建键盘（幂等，已存在则跳过）。</summary>
public static class UpperLimbVrKeyboardBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Object.FindAnyObjectByType<UpperLimbVrKeyboard>() == null)
        {
            new GameObject("UpperLimbVRKeyboard").AddComponent<UpperLimbVrKeyboard>();
        }
    }
}
