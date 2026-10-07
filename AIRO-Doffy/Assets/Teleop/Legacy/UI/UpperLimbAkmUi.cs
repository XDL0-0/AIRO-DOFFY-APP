using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// AKM 状态 UI 刷新器（按钮本体在场景中，onClick 直接连到 UpperLimbAkmManager）。
///
/// 场景结构（挂在 Main Canvas 的 Panel 下）：
/// - "WRM_enable Button"：WRM 主开关，onClick → ToggleWrm；启用时变绿。
/// - "AKM Calibration Button"：onClick → OnCalibrationClicked；初始置灰，WRM 启用后解锁。
/// - "AKM_StatusText"：标定流程与实时 elbow_alpha/confidence 状态文本。
///
/// 本脚本（由自举创建）只负责按名字查找场景控件并刷新显示，不做任何创建。
/// </summary>
public class UpperLimbAkmUi : MonoBehaviour
{
    private static readonly Color ButtonNormal = new Color(0.13f, 0.17f, 0.23f, 0.9f);
    private static readonly Color WrmOnColor = new Color(0.10f, 0.46f, 0.42f, 0.95f);

    private UpperLimbAkmManager _manager;
    private TextMeshProUGUI _statusText;
    private Button _wrmButton;
    private Image _wrmImage;
    private Button _calButton;
    private TextMeshProUGUI _calLabel;
    private bool _handModeActive;

    private void Start()
    {
        _manager = FindAnyObjectByType<UpperLimbAkmManager>();
        if (_manager == null)
        {
            LogManager.Log("UpperLimbAKM", "UpperLimbAkmManager not found, AKM UI cannot refresh");
            return;
        }

        RectTransform panel = FindPanel();
        if (panel == null)
        {
            LogManager.Log("UpperLimbAKM", "Main Canvas/Panel not found, AKM UI cannot refresh");
            return;
        }

        FindSceneControls(panel);
    }

    private RectTransform FindPanel()
    {
        GameObject mainCanvas = GameObject.Find("Main Canvas");
        if (mainCanvas == null) return null;

        Transform panel = mainCanvas.transform.Find("Panel");
        return panel != null ? panel as RectTransform : null;
    }

    private void FindSceneControls(RectTransform panel)
    {
        Transform wrm = panel.Find("WRM_enable Button");
        if (wrm != null)
        {
            _wrmButton = wrm.GetComponent<Button>();
            _wrmImage = wrm.GetComponent<Image>();
        }

        Transform cal = panel.Find("AKM Calibration Button");
        if (cal != null)
        {
            _calButton = cal.GetComponent<Button>();
            _calLabel = cal.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        Transform status = panel.Find("AKM_StatusText");
        if (status != null)
            _statusText = status.GetComponent<TextMeshProUGUI>();

        if (_wrmButton == null || _calButton == null || _statusText == null)
            LogManager.Log("UpperLimbAKM",
                "Scene missing full AKM controls (WRM_enable Button / AKM Calibration Button / AKM_StatusText), partial refresh inactive");
    }

    private void Update()
    {
        if (_manager == null)
        {
            _manager = FindAnyObjectByType<UpperLimbAkmManager>();
            if (_manager == null) return;
        }

        RefreshHandModeGate();
        RefreshWrmVisual();
        RefreshCalibrationButton();
        RefreshStatusText();
    }

    /// <summary>
    /// 手部控制模式（TrackingModeManager → PlayerPrefs "cfg_trackingMode"=1）下
    /// 禁用 WRM_enable 按钮，并自动关闭 WRM（避免手部模式下手势与肘部映射并存）。
    /// 只读检测现有状态，不改任何现有脚本。
    /// </summary>
    private void RefreshHandModeGate()
    {
        if (_wrmButton == null) return;

        bool hands = PlayerPrefs.GetInt("cfg_trackingMode", 0) == 1;
        if (hands == _handModeActive) return;

        _handModeActive = hands;
        if (hands && _manager.WrmEnabled)
            _manager.SetWrmEnabled(false);
        LogManager.Log("UpperLimbAKM", hands ? "Hand tracking mode: WRM disabled" : "Controller mode: WRM available");
    }

    private void RefreshWrmVisual()
    {
        if (_wrmImage == null || _wrmButton == null) return;

        _wrmImage.color = _manager.WrmEnabled ? WrmOnColor : ButtonNormal;
        _wrmButton.interactable = !_handModeActive; // 手部控制模式禁用
    }

    private void RefreshCalibrationButton()
    {
        if (_calButton == null) return;

        _calButton.interactable = _manager.WrmEnabled;
        if (_calLabel != null)
            _calLabel.text = _manager.IsCalibrated ? "recalibrate" : "Calibration";
    }

    private void RefreshStatusText()
    {
        if (_statusText == null) return;

        if (_handModeActive)
        {
            _statusText.text = "Hand tracking mode: WRM disabled";
            return;
        }

        if (!_manager.WrmEnabled)
        {
            _statusText.text = "Click WRM_enable to enable upper limb teleop";
            return;
        }

        switch (_manager.State)
        {
            case UpperLimbAkmManager.CalibrationState.Idle:
                _statusText.text = "Click Calibration to start elbow calibration";
                break;
            case UpperLimbAkmManager.CalibrationState.WaitingDown:
                _statusText.text = "Press right-hand A for elbow down calibration";
                break;
            case UpperLimbAkmManager.CalibrationState.CalibratingDown:
            case UpperLimbAkmManager.CalibrationState.CalibratingHorizontal:
                _statusText.text = "calibrating.....";
                break;
            case UpperLimbAkmManager.CalibrationState.WaitingHorizontal:
                _statusText.text = "Press right-hand A for side raise calibration";
                break;
            case UpperLimbAkmManager.CalibrationState.Calibrated:
                _statusText.text =
                    $"elbow_alpha: {_manager.ElbowAlpha:F3}    confidence: {_manager.Confidence:F2}" +
                    (_manager.IsTrackingValid ? "" : "    (tracking invalid)");
                break;
        }
    }
}
