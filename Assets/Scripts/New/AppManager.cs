using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AppManager : MonoBehaviour
{
    public static AppManager Instance { get; private set; }

    [Header("Runtime Config")]
    [SerializeField] private TeleopConfig teleopConfig;

    [Header("Network UI")]
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private TMP_InputField numWebRTCInputField;

    [Header("Status UI")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI sessionStateText;
    [SerializeField] private Button btnStart;
    [SerializeField] private Button recalibrateButton;
    [SerializeField] private Button controlModeButton;
    [SerializeField] private TextMeshProUGUI controlModeButtonText;
    [SerializeField] private Button repositionButton;

    [Header("Options")]
    [SerializeField] private Toggle debugInfoToggle;

    [Header("WebRTC Video")]
    [SerializeField] private Button webRTCButton;
    [SerializeField] private TextMeshProUGUI webRTCButtonText;
    [SerializeField] private GameObject webRTCSinglePanel;
    [SerializeField] private GameObject[] webRTCDualPanels;
    [SerializeField] private GameObject[] webRTCTriPanels;

    [Header("UDP Camera Control")]
    [SerializeField] private Button addUdpCameraButton;
    [SerializeField] private UdpWindowManager udpWindowManager;

    [Header("Build Info")]
    [SerializeField] private TextMeshProUGUI versionText;

    [Header("Menu")]
    [SerializeField] private GameObject menuPanel;

    public string ServerIP { get; private set; } = "10.10.131.72";
    public int PosePort { get; private set; } = 8001;
    public int ControlPort { get; private set; } = 8005;
    public int TactilePort { get; private set; } = 8012;
    public int SignalingPort { get; private set; } = 8765;
    public int UdpVideoBasePort { get; private set; } = 8000;

    public int TrackingMode { get; private set; }
    public float SendRateHz { get; private set; } = 30f;
    public bool UseBinaryProtocol { get; private set; } = true;
    public bool IsStreaming { get; private set; }
    public bool IsWebRTCEnabled { get; private set; }
    public int NumWebRTC { get; private set; } = 1;
    public bool NeedsRecalibration { get; private set; }
    public TeleopSessionState SessionState { get; private set; } = TeleopSessionState.Idle;
    public bool ShowDebugInfo => debugInfoToggle != null && debugInfoToggle.isOn;

    public bool CanSendTeleopData =>
        IsStreaming &&
        !NeedsRecalibration &&
        SessionState != TeleopSessionState.TrackingLost &&
        SessionState != TeleopSessionState.Stopping;

    private UdpSocket _udpSocket;
    private VideoStreamManager _videoStreamManager;
    private TeleopTrackingGuard _trackingGuard;
    private TeleopControlModeManager _controlModeManager;
    private string _lastCtrlMsg;
    private int _previousSleepTimeout;
    private TeleopControlModeManager.ControlMode _lastControlMode;

    private bool PreventSleep => teleopConfig == null || teleopConfig.preventSleep;
    private bool PauseControlOnTrackingLoss => teleopConfig == null || teleopConfig.pauseControlOnTrackingLoss;
    private bool RequireRecalibrateAfterTrackingLoss => teleopConfig == null || teleopConfig.requireRecalibrateAfterTrackingLoss;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ApplyConfigDefaults();
    }

    private void Start()
    {
        _previousSleepTimeout = Screen.sleepTimeout;
        if (PreventSleep)
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

        if (versionText != null)
            versionText.text = $"v{Application.version}";

        _udpSocket = FindAnyObjectByType<UdpSocket>();
        _videoStreamManager = FindAnyObjectByType<VideoStreamManager>();
        _controlModeManager = EnsureControlModeManager();

        EnsureTrackingGuard();
        LoadConfig();

        if (ipInputField != null) ipInputField.onValueChanged.AddListener(_ => ClearError());
        if (numWebRTCInputField != null) numWebRTCInputField.onEndEdit.AddListener(OnNumWebRTCChanged);
        if (webRTCButton != null) webRTCButton.onClick.AddListener(ToggleWebRTC);
        if (recalibrateButton != null) recalibrateButton.onClick.AddListener(RecalibrateTeleopFrame);
        if (controlModeButton != null) controlModeButton.onClick.AddListener(ToggleTeleopControlMode);
        if (repositionButton != null) repositionButton.onClick.AddListener(BeginRobotBaseReposition);

        SetSessionState(TeleopSessionState.Idle, "System Ready", Color.green);
        UpdateWebRTCButtonLabel();
        UpdateControlModeButtonLabel();
        // RuntimeUITheme.ApplySceneTheme();
    }

    private void Update()
    {
        if (IsStreaming && OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
            StopStreaming();

        if (IsStreaming &&
            NeedsRecalibration &&
            OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.RTouch))
        {
            RecalibrateTeleopFrame();
        }

        if (_lastControlMode != TeleopControlModeManager.CurrentMode)
            UpdateControlModeButtonLabel();
    }

    private void OnDestroy()
    {
        if (controlModeButton != null)
            controlModeButton.onClick.RemoveListener(ToggleTeleopControlMode);
        if (repositionButton != null)
            repositionButton.onClick.RemoveListener(BeginRobotBaseReposition);

        if (PreventSleep)
            Screen.sleepTimeout = _previousSleepTimeout;

        if (Instance == this)
            Instance = null;
    }

    public void SaveConfig()
    {
        PlayerPrefs.SetString("cfg_ip", ServerIP);
        PlayerPrefs.SetInt("cfg_trackingMode", TrackingMode);
        PlayerPrefs.SetFloat("cfg_sendRateHz", SendRateHz);
        PlayerPrefs.SetInt("cfg_useBinary", UseBinaryProtocol ? 1 : 0);
        PlayerPrefs.SetInt("cfg_numWebRTC", NumWebRTC);
        PlayerPrefs.Save();
        LogManager.Log("App", "Config saved");
    }

    private void LoadConfig()
    {
        ServerIP = PlayerPrefs.GetString("cfg_ip", ServerIP);
        TrackingMode = PlayerPrefs.GetInt("cfg_trackingMode", TrackingMode);
        SendRateHz = PlayerPrefs.GetFloat("cfg_sendRateHz", SendRateHz);
        UseBinaryProtocol = PlayerPrefs.GetInt("cfg_useBinary", UseBinaryProtocol ? 1 : 0) == 1;

        int defaultTrackCount = teleopConfig != null ? teleopConfig.defaultWebRTCTrackCount : NumWebRTC;
        NumWebRTC = Mathf.Clamp(PlayerPrefs.GetInt("cfg_numWebRTC", defaultTrackCount), 1, 3);

        if (ipInputField != null)
            ipInputField.text = ServerIP;

        if (numWebRTCInputField != null)
            numWebRTCInputField.text = NumWebRTC.ToString();

        ApplyWebRTCTrackCount(NumWebRTC);

        LogManager.Log("App",
            $"Config loaded IP:{ServerIP} Pose:{PosePort} Ctrl:{ControlPort} " +
            $"Tactile:{TactilePort} Signal:{SignalingPort} WebRTC:{NumWebRTC}");
    }

    public async void OnStartStreaming()
    {
        ClearError();

        if (!ReadTextField(ipInputField, out string ip))
        {
            SetStatus("Error: invalid IP", Color.red);
            return;
        }

        ServerIP = ip;
        NumWebRTC = SanitizeWebRTCTrackCount(numWebRTCInputField != null ? numWebRTCInputField.text : null);
        SaveConfig();

        IsStreaming = true;
        NeedsRecalibration = false;
        TeleopReferenceFrame.Calibrate();

        SetStartButtonText("Stop Streaming");
        if (menuPanel != null) menuPanel.SetActive(false);

        LogManager.Log("App",
            $"Stream started IP:{ServerIP} Pose:{PosePort} Ctrl:{ControlPort} " +
            $"Tactile:{TactilePort} Signal:{SignalingPort} Mode:{TrackingMode}");

        if (IsWebRTCEnabled)
        {
            SetSessionState(TeleopSessionState.VideoConnecting, "Teleop Active - Video Connecting", Color.yellow);
            bool ok = await StartWebRTCVideoSession();
            if (!IsStreaming) return;

            SetSessionState(
                TeleopSessionState.Streaming,
                ok ? "Streaming Active" : "Streaming Active - Video Unavailable",
                ok ? Color.green : Color.yellow);
        }
        else
        {
            SetSessionState(TeleopSessionState.Streaming, "Streaming Active", Color.green);
        }
    }

    public void StopStreaming()
    {
        if (!IsStreaming && SessionState == TeleopSessionState.Idle) return;

        SetSessionState(TeleopSessionState.Stopping, "Stopping Streaming", Color.yellow);
        IsStreaming = false;
        NeedsRecalibration = false;
        TeleopReferenceFrame.Clear();
        ClearError();

        if (menuPanel != null) menuPanel.SetActive(true);

        VideoStreamManager mgr = GetVideoStreamManager();
        if (mgr != null) _ = mgr.StopVideoSession("user_stop");

        SetStartButtonText("Start Streaming");
        SetSessionState(TeleopSessionState.Idle, "System Ready", Color.green);
        LogManager.Log("App", "Streaming stopped by user");
    }

    public void HandleDisconnection(string reason)
    {
        if (!IsStreaming) return;

        IsStreaming = false;
        NeedsRecalibration = false;
        TeleopReferenceFrame.Clear();

        if (menuPanel != null) menuPanel.SetActive(true);

        SetStartButtonText("Start Streaming");
        SetSessionState(TeleopSessionState.Idle, $"Disconnected: {reason}", Color.yellow);
        LogManager.Log("App", $"Disconnected: {reason}");
    }

    public void HandleVideoDisconnection(string reason)
    {
        if (!IsStreaming) return;

        SetSessionState(TeleopSessionState.Streaming, $"Video issue: {reason}", Color.yellow);
        LogManager.Log("App", $"Video issue while teleop remains active: {reason}");
    }

    public void HandleTrackingLost(string reason)
    {
        if (!IsStreaming || !PauseControlOnTrackingLoss) return;

        NeedsRecalibration = RequireRecalibrateAfterTrackingLoss;
        SetSessionState(TeleopSessionState.TrackingLost, $"Tracking paused: {reason}. Recalibrate.", Color.yellow);
        LogManager.Log("Tracking", $"Control paused: {reason}");
    }

    public void HandleTrackingAvailable(string reason)
    {
        if (!IsStreaming || SessionState != TeleopSessionState.TrackingLost) return;

        if (NeedsRecalibration)
        {
            SetStatus($"Tracking back: {reason}. Recalibrate to resume.", Color.yellow);
            UpdateStateUi();
            return;
        }

        SetSessionState(TeleopSessionState.Streaming, "Streaming Active", Color.green);
    }

    public void RecalibrateTeleopFrame()
    {
        if (!TeleopReferenceFrame.Calibrate())
        {
            NeedsRecalibration = true;
            SetSessionState(TeleopSessionState.TrackingLost, "Recalibration failed", Color.red);
            return;
        }

        NeedsRecalibration = false;

        if (IsStreaming)
            SetSessionState(TeleopSessionState.Streaming, "Streaming Active", Color.green);
        else
            SetSessionState(TeleopSessionState.Idle, "Teleop Frame Calibrated", Color.green);
    }

    public void SendControlMessage(string msg)
    {
        if (_udpSocket == null || string.IsNullOrEmpty(msg)) return;
        if (msg == _lastCtrlMsg) return;

        _lastCtrlMsg = msg;
        _udpSocket.SendData8005(msg);
    }

    public async void ToggleWebRTC()
    {
        if (!IsWebRTCEnabled)
        {
            int trackCount = SanitizeWebRTCTrackCount(numWebRTCInputField != null ? numWebRTCInputField.text : null);
            ApplyWebRTCTrackCount(trackCount);
            SetActiveWebRTCPanels(trackCount);

            if (udpWindowManager != null) udpWindowManager.CloseAllWindows();
            if (addUdpCameraButton != null) addUdpCameraButton.interactable = false;

            IsWebRTCEnabled = true;
            UpdateWebRTCButtonLabel();
            LogManager.Log("Video", "WebRTC enabled; UDP video windows closed. Tactile receiver remains active.");

            if (IsStreaming)
            {
                SetSessionState(TeleopSessionState.VideoConnecting, "Teleop Active - Video Connecting", Color.yellow);
                bool ok = await StartWebRTCVideoSession();
                if (!IsStreaming) return;

                SetSessionState(
                    TeleopSessionState.Streaming,
                    ok ? "Streaming Active" : "Streaming Active - Video Unavailable",
                    ok ? Color.green : Color.yellow);
            }
        }
        else
        {
            IsWebRTCEnabled = false;
            UpdateWebRTCButtonLabel();
            SetActiveWebRTCPanels(0);

            if (addUdpCameraButton != null) addUdpCameraButton.interactable = true;

            VideoStreamManager mgr = GetVideoStreamManager();
            if (mgr != null) await mgr.StopVideoSession("user_disable");

            if (IsStreaming && SessionState != TeleopSessionState.TrackingLost)
                SetSessionState(TeleopSessionState.Streaming, "Streaming Active", Color.green);
        }
    }

    public void ToggleStreaming()
    {
        if (IsStreaming)
            StopStreaming();
        else
            OnStartStreaming();
    }

    public void ToggleTeleopControlMode()
    {
        TeleopControlModeManager manager = EnsureControlModeManager();
        if (manager == null) return;

        manager.ToggleControlMode();
        UpdateControlModeButtonLabel();
    }

    /// <summary>
    /// 重新定位机器人底座坐标系:自动切到 View 模式并进入摆放编辑流程。
    /// 流程:手柄移动定位置 → 右手摇杆按下确认 → 转动手柄定朝向 → 再次确认 → 锁定。
    /// </summary>
    public void BeginRobotBaseReposition()
    {
        TeleopControlModeManager manager = EnsureControlModeManager();
        if (manager == null) return;

        if (TeleopControlModeManager.CurrentMode != TeleopControlModeManager.ControlMode.View)
        {
            manager.SetViewMode();
            UpdateControlModeButtonLabel();
        }

        RobotBasePlacementTool tool = FindAnyObjectByType<RobotBasePlacementTool>();
        if (tool == null) return;

        tool.BeginPlacementEdit();
        LogManager.Log("Teleop", "Robot base repositioning started");
    }

    private async System.Threading.Tasks.Task<bool> StartWebRTCVideoSession()
    {
        VideoStreamManager mgr = GetVideoStreamManager();
        if (mgr == null)
        {
            LogManager.Log("Video", "VideoStreamManager missing");
            return false;
        }

        bool ok = await mgr.StartVideoSession(ServerIP, SignalingPort, "720p30");
        if (!ok) LogManager.Log("Video", "Video session failed to start");
        return ok;
    }

    private void OnNumWebRTCChanged(string value)
    {
        ApplyWebRTCTrackCount(SanitizeWebRTCTrackCount(value));
        SaveConfig();
    }

    private int SanitizeWebRTCTrackCount(string value)
    {
        if (!int.TryParse(value?.Trim(), out int count))
            count = 1;

        return Mathf.Clamp(count, 1, 3);
    }

    private void ApplyWebRTCTrackCount(int count)
    {
        NumWebRTC = Mathf.Clamp(count, 1, 3);

        if (numWebRTCInputField != null && numWebRTCInputField.text != NumWebRTC.ToString())
            numWebRTCInputField.text = NumWebRTC.ToString();

        WebRTCVideoReceiver receiver = FindAnyObjectByType<WebRTCVideoReceiver>();
        if (receiver != null)
            receiver.ExpectedTrackCount = NumWebRTC;
    }

    private void SetActiveWebRTCPanels(int count)
    {
        if (webRTCSinglePanel != null)
            webRTCSinglePanel.SetActive(count == 1);

        SetPanelGroupActive(webRTCDualPanels, count == 2);
        SetPanelGroupActive(webRTCTriPanels, count == 3);
    }

    private void SetPanelGroupActive(GameObject[] panels, bool active)
    {
        if (panels == null) return;

        foreach (GameObject panel in panels)
        {
            if (panel != null)
                panel.SetActive(active);
        }
    }

    private void SetSessionState(TeleopSessionState state, string status, Color color)
    {
        SessionState = state;
        SetStatus(status, color);
        UpdateStateUi();
        LogManager.Log("Session", $"{state}: {status}");
    }

    private void UpdateStateUi()
    {
        if (sessionStateText != null)
            sessionStateText.text = SessionState.ToString();

        if (recalibrateButton != null)
            recalibrateButton.gameObject.SetActive(IsStreaming && NeedsRecalibration);
    }

    private void UpdateWebRTCButtonLabel()
    {
        if (webRTCButtonText != null)
            webRTCButtonText.text = IsWebRTCEnabled ? "Disable WebRTC" : "Enable WebRTC";
    }

    private void UpdateControlModeButtonLabel()
    {
        _lastControlMode = TeleopControlModeManager.CurrentMode;

        if (controlModeButtonText != null)
            controlModeButtonText.text = _lastControlMode == TeleopControlModeManager.ControlMode.Mirror
                ? "Control Mode\nMirror"
                : "Control Mode\nView";

        // 重新定位按钮仅 View 模式可用,Mirror 模式下置灰
        if (repositionButton != null)
            repositionButton.interactable = _lastControlMode == TeleopControlModeManager.ControlMode.View;
    }

    private void ClearError()
    {
        if (btnStart != null)
            btnStart.interactable = true;
    }

    private void SetStatus(string msg, Color color)
    {
        if (statusText != null)
        {
            statusText.text = msg;
            statusText.color = color;
        }

        if (btnStart != null)
            btnStart.interactable = color != Color.red;
    }

    private void SetStartButtonText(string text)
    {
        if (btnStart == null) return;

        TextMeshProUGUI label = btnStart.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = text;
    }

    private void ApplyConfigDefaults()
    {
        if (teleopConfig == null) return;

        ServerIP = teleopConfig.defaultServerIP;
        PosePort = teleopConfig.posePort;
        ControlPort = teleopConfig.controlPort;
        TactilePort = teleopConfig.tactilePort;
        SignalingPort = teleopConfig.signalingPort;
        UdpVideoBasePort = teleopConfig.udpVideoBasePort;
        SendRateHz = teleopConfig.sendRateHz;
        UseBinaryProtocol = teleopConfig.useBinaryProtocol;
        NumWebRTC = Mathf.Clamp(teleopConfig.defaultWebRTCTrackCount, 1, 3);
    }

    private void EnsureTrackingGuard()
    {
        _trackingGuard = FindAnyObjectByType<TeleopTrackingGuard>();
        if (_trackingGuard == null)
            _trackingGuard = gameObject.AddComponent<TeleopTrackingGuard>();

        _trackingGuard.Initialize(this);
    }

    private VideoStreamManager GetVideoStreamManager()
    {
        if (_videoStreamManager == null)
            _videoStreamManager = FindAnyObjectByType<VideoStreamManager>();

        return _videoStreamManager;
    }

    private TeleopControlModeManager EnsureControlModeManager()
    {
        if (_controlModeManager == null)
            _controlModeManager = FindAnyObjectByType<TeleopControlModeManager>();

        if (_controlModeManager == null)
            _controlModeManager = new GameObject("TeleopControlModeManager").AddComponent<TeleopControlModeManager>();

        return _controlModeManager;
    }

    private static bool ReadTextField(TMP_InputField field, out string value)
    {
        value = "";
        if (field == null) return false;

        value = field.text.Trim();
        return !string.IsNullOrEmpty(value);
    }
}
