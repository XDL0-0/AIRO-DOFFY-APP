using System;
using System.Net;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Coordinates the teleoperation session and projects its state to the legacy
/// scene UI. Settings, validation, state transitions and video lifetime live in
/// narrow helpers so no command path needs to parse a button or input label.
/// </summary>
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

    // These property names and values are part of the classic public contract.
    public string ServerIP { get; private set; } = "10.10.131.72";
    public int PosePort { get; private set; } = 8001;
    public int ControlPort { get; private set; } = 8005;
    public int TactilePort { get; private set; } = 8012;
    public int SignalingPort { get; private set; } = 8765;
    public int UdpVideoBasePort { get; private set; } = 8000;
    public int TrackingMode { get; private set; }
    public float SendRateHz { get; private set; } = 30f;
    public bool UseBinaryProtocol { get; private set; } = true;
    public bool IsStreaming => _session != null && _session.IsStreaming;
    public bool IsWebRTCEnabled { get; private set; }
    public int NumWebRTC { get; private set; } = 1;
    public bool NeedsRecalibration => _session != null && _session.NeedsRecalibration;
    public TeleopSessionState SessionState => _session != null ? _session.State : TeleopSessionState.Idle;
    public bool ShowDebugInfo => debugInfoToggle != null ? debugInfoToggle.isOn : _showDebugInfo;
    public bool CanSendTeleopData => _session != null && _session.CanSend && TrackingAvailable() && !AlignmentEditing();
    public bool IsTeleoperationActive => CanSendTeleopData &&
        (TrackingMode == 1 ||
         OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch) ||
         OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) ||
         (_upperLimbManager != null && _upperLimbManager.WrmEnabled &&
          OVRInput.Get(_upperLimbManager.clutchButton, _upperLimbManager.controller)));

    /// <summary>Stable state for the new workspace UI.</summary>
    public string StatusText => _session != null ? _session.StatusText : "System Ready";
    public string LastError => _session != null ? _session.LastError : string.Empty;
    public event Action Changed;

    private TeleopRuntimeSettings _settings = TeleopRuntimeSettings.Defaults();
    private readonly TeleopSettingsStore _settingsStore = new TeleopSettingsStore();
    private TeleopSessionCoordinator _session;
    private TeleopLegacyUiBinding _legacyUi;

    private UdpSocket _udpSocket;
    private VideoStreamManager _videoStreamManager;
    private RecordingController _recordingController;
    private TeleopTrackingGuard _trackingGuard;
    private UpperLimbAkmManager _upperLimbManager;
    private TeleopControlModeManager _controlModeManager;
    private TrackingModeManager _trackingModeManager;
    private RobotBasePlacementTool _placementTool;
    private string _lastCtrlMsg;
    private int _previousSleepTimeout;
    private TeleopControlModeManager.ControlMode _lastControlMode;
    private bool _showDebugInfo;
    private bool _started;
    private bool _destroyed;
    private int _webRtcGeneration;

    private bool PreventSleep => _settings.PreventSleep;
    private bool PauseControlOnTrackingLoss => _settings.PauseControlOnTrackingLoss;
    // Retained as a named setting for compatibility/documentation. Tracking
    // callbacks always require explicit recalibration for safety.
    private bool RequireRecalibrateAfterTrackingLoss => _settings.RequireRecalibrateAfterTrackingLoss;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ApplyConfigDefaults();
        LoadConfig();
        _showDebugInfo = debugInfoToggle != null && debugInfoToggle.isOn;
        _legacyUi = new TeleopLegacyUiBinding(
            ipInputField, numWebRTCInputField, statusText, sessionStateText, btnStart,
            recalibrateButton, controlModeButton, controlModeButtonText, repositionButton,
            debugInfoToggle, webRTCButton, webRTCButtonText, webRTCSinglePanel,
            webRTCDualPanels, webRTCTriPanels, addUdpCameraButton, versionText, menuPanel);
        _session = new TeleopSessionCoordinator(
            GetVideoStreamManager,
            () => ServerIP,
            () => SignalingPort,
            () => IsWebRTCEnabled,
            () => _destroyed,
            GetRecordingController,
            message => LogManager.Log("Session", message));
        _session.Changed += OnSessionChanged;
    }

    private void Start()
    {
        _started = true;
        _previousSleepTimeout = Screen.sleepTimeout;
        if (PreventSleep)
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

        _legacyUi?.SetVersion($"v{Application.version}");

        _udpSocket = FindAnyObjectByType<UdpSocket>();
        _videoStreamManager = FindAnyObjectByType<VideoStreamManager>();
        _recordingController = FindAnyObjectByType<RecordingController>();
        _upperLimbManager = FindAnyObjectByType<UpperLimbAkmManager>();
        _trackingModeManager = FindAnyObjectByType<TrackingModeManager>();
        _controlModeManager = EnsureControlModeManager();
        EnsureTrackingGuard();

        ApplyTransportSettings();
        ApplyWebRTCTrackCount(NumWebRTC);
        ApplyTrackingModeToServices();
        SetActiveWebRTCPanels(IsWebRTCEnabled ? NumWebRTC : 0);
        _legacyUi?.SyncInputs(ServerIP, NumWebRTC);
        AttachLegacyListeners();

        _session.CompleteInitialStop();
        UpdateWebRTCButtonLabel();
        UpdateControlModeButtonLabel();
        LogManager.Log("App", $"Config loaded IP:{ServerIP} Pose:{PosePort} Ctrl:{ControlPort} " +
            $"Tactile:{TactilePort} Signal:{SignalingPort} WebRTC:{NumWebRTC}");
    }

    private void Update()
    {
        if (_destroyed) return;

        if (IsStreaming && OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
            StopStreaming();

        if (IsStreaming && NeedsRecalibration &&
            OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.RTouch))
            RecalibrateTeleopFrame();

        if (_lastControlMode != TeleopControlModeManager.CurrentMode)
            UpdateControlModeButtonLabel();
    }

    private void OnDestroy()
    {
        _destroyed = true;
        DetachLegacyListeners();
        if (_session != null)
            _session.Changed -= OnSessionChanged;
        // Destruction is also a session shutdown path in Unity scene reloads;
        // do not leave a legacy recorder requesting frames after its facade is
        // gone.
        GetRecordingController()?.StopRecording();
        TeleopReferenceFrame.Clear();
        _session?.CancelVideo();
        _ = _session?.StopVideoAsync("app_destroy");

        if (_started && PreventSleep)
            Screen.sleepTimeout = _previousSleepTimeout;
        if (Instance == this)
            Instance = null;
    }

    // ---------------------------------------------------------------------
    // Settings and transport coordination
    // ---------------------------------------------------------------------
    public void SaveConfig()
    {
        CopyPublicPropertiesToSettings();
        _settingsStore.Save(_settings, new UnityPlayerPrefsStore());
        LogManager.Log("App", "Config saved");
    }

    private void LoadConfig()
    {
        TeleopRuntimeSettings loaded = _settingsStore.Load(_settings, new UnityPlayerPrefsStore());
        ApplySettingsSnapshot(loaded);
    }

    /// <summary>
    /// Applies the new workspace connection form. It is the single entrypoint
    /// for host and WebRTC track settings and never reads hidden legacy fields.
    /// </summary>
    public bool ConfigureConnection(string host, int tracks)
    {
        if (!TeleopConnectionValidator.TryValidate(host, tracks, out string error))
        {
            _session?.SetError(error);
            return false;
        }

        string normalizedHost = host.Trim();
        bool hostChanged = !string.Equals(ServerIP, normalizedHost, StringComparison.OrdinalIgnoreCase);
        bool tracksChanged = NumWebRTC != tracks;
        if (hostChanged && IsStreaming)
            GetRecordingController()?.StopRecording();

        ServerIP = normalizedHost;
        NumWebRTC = TeleopRuntimeSettings.ClampTrackCount(tracks);
        _settings.ServerHost = ServerIP;
        _settings.WebRtcTrackCount = NumWebRTC;
        ApplyTransportSettings();
        ApplyWebRTCTrackCount(NumWebRTC);
        SaveConfig();
        _session?.ClearError();

        if (_session == null)
        {
            NotifyChanged();
            return true;
        }

        if (!IsStreaming)
            _session.MarkConnectionReady();
        else
        {
            if (hostChanged)
                _session.MarkConnectionChanged();
            if (IsWebRTCEnabled && (hostChanged || tracksChanged))
                _ = RestartVideoSessionAsync("connection_changed");
        }

        NotifyChanged();
        return true;
    }

    /// <summary>Called only by the legacy UdpSocket IP input listener.</summary>
    internal void ApplyLegacyHost(string host)
    {
        ConfigureConnection(host, NumWebRTC);
    }

    /// <summary>New workspace tracking mode API; 0=controllers, 1=hands.</summary>
    public void SetTrackingMode(int mode)
    {
        int normalized = TeleopRuntimeSettings.ClampTrackingMode(mode);
        if (TrackingMode != normalized) HandleTrackingLost("Input mode changed");
        TrackingMode = normalized;
        _settings.TrackingMode = normalized;
        SaveConfig();
        ApplyTrackingModeToServices();
        NotifyChanged();
    }

    /// <summary>Updates all video consumers and restarts an active WebRTC session.</summary>
    public void SetWebRTCTrackCount(int count)
    {
        int normalized = TeleopRuntimeSettings.ClampTrackCount(count);
        bool changed = NumWebRTC != normalized;
        NumWebRTC = normalized;
        _settings.WebRtcTrackCount = normalized;
        ApplyWebRTCTrackCount(normalized);
        SaveConfig();

        if (changed && IsStreaming && IsWebRTCEnabled)
            _ = RestartVideoSessionAsync("track_count_changed");
        NotifyChanged();
    }

    /// <summary>Explicit debug state used by the workspace; no label parsing.</summary>
    public void SetDebugVisible(bool visible)
    {
        _showDebugInfo = visible;
        _legacyUi?.SetDebugVisible(visible);
        NotifyChanged();
    }

    // Compatibility with the old AppManager implementation.
    public void OnNumWebRTCChanged(string value)
    {
        SetWebRTCTrackCount(SanitizeWebRTCTrackCount(value));
    }

    internal void AdoptTrackingModeFromLegacy(int mode, bool persist)
    {
        TrackingMode = TeleopRuntimeSettings.ClampTrackingMode(mode);
        _settings.TrackingMode = TrackingMode;
        if (persist)
            SaveConfig();
        NotifyChanged();
    }

    // ---------------------------------------------------------------------
    // Session lifecycle and safety
    // ---------------------------------------------------------------------
    public async void OnStartStreaming()
    {
        if (_session == null) return;
        if (!TrackingAvailable() || AlignmentEditing())
        {
            _session.SetError("Restore tracking and finish alignment first");
            return;
        }
        if (!TeleopConnectionValidator.TryValidate(ServerIP, NumWebRTC, out string error))
        {
            _session.SetError(error);
            return;
        }
        ApplyTransportSettings();
        SaveConfig();
        await _session.StartAsync(TeleopReferenceFrame.PrepareSession);
    }

    public void StopStreaming()
    {
        _session?.Stop("user_stop");
        _lastCtrlMsg = null;
    }

    public void HandleDisconnection(string reason)
    {
        _session?.HandleDisconnected(reason);
    }

    public void HandleVideoDisconnection(string reason)
    {
        _session?.HandleVideoDisconnection(reason);
    }

    public void HandleTrackingLost(string reason)
    {
        _session?.HandleTrackingLost(reason, PauseControlOnTrackingLoss);
    }

    public void HandleTrackingAvailable(string reason)
    {
        // Returning focus/tracking never reopens the send gate by itself.
        _session?.HandleTrackingAvailable(reason);
    }

    public void RecalibrateTeleopFrame()
    {
        if (!TrackingAvailable() || AlignmentEditing())
        {
            _session?.SetError("Restore tracking and finish alignment first");
            return;
        }
        _session?.Recalibrate(TeleopReferenceFrame.Calibrate);
    }

    public void ConfirmAlignment()
    {
        if (!TrackingAvailable() || AlignmentEditing())
        {
            _session?.SetError("Finish alignment and restore tracking before resuming");
            return;
        }
        // Accept the manual frame without replacing it with an automatic head-yaw frame.
        _session?.Recalibrate(() => TeleopReferenceFrame.IsCalibrated);
    }

    private bool AlignmentEditing()
    {
        if (CalibrationTool.Instance != null && CalibrationTool.Instance.IsCalibrating) return true;
        if (_placementTool == null) _placementTool = FindAnyObjectByType<RobotBasePlacementTool>();
        return _placementTool != null && _placementTool.IsPlacementEditing;
    }

    public void SendControlMessage(string msg)
    {
        if (_udpSocket == null || string.IsNullOrEmpty(msg)) return;
        if (msg == _lastCtrlMsg) return;
        _lastCtrlMsg = msg;
        _udpSocket.SendData8005(msg);
    }

    // ---------------------------------------------------------------------
    // Video selection and race-safe lifetime
    // ---------------------------------------------------------------------
    public async void ToggleWebRTC()
    {
        int request = ++_webRtcGeneration;
        if (!IsWebRTCEnabled)
        {
            IsWebRTCEnabled = true;
            SetWebRTCTrackCount(NumWebRTC);
            SetActiveWebRTCPanels(NumWebRTC);
            GetUdpWindowManager()?.CloseAllWindows();
            _legacyUi?.SetAddUdpInteractable(false);
            UpdateWebRTCButtonLabel();
            LogManager.Log("Video", "WebRTC enabled; UDP video windows closed. Tactile receiver remains active.");

            if (!IsStreaming)
            {
                NotifyChanged();
                return;
            }
            _session.BeginVideoConnecting();
            await RestartVideoSessionAsync("enable");
            if (request != _webRtcGeneration || !IsStreaming || !IsWebRTCEnabled) return;
        }
        else
        {
            IsWebRTCEnabled = false;
            SetActiveWebRTCPanels(0);
            _legacyUi?.SetAddUdpInteractable(true);
            UpdateWebRTCButtonLabel();
            if (_session != null) await _session.StopVideoAsync("user_disable");
            if (request != _webRtcGeneration) return;
            if (IsStreaming && SessionState != TeleopSessionState.TrackingLost)
                _session.MarkStreaming();
        }
        NotifyChanged();
    }

    public void ToggleStreaming()
    {
        if (IsStreaming || (_session != null && _session.IsStarting) ||
            SessionState == TeleopSessionState.VideoConnecting)
            StopStreaming();
        else
            OnStartStreaming();
    }

    private async Task<bool> RestartVideoSessionAsync(string reason)
    {
        if (_session == null) return false;
        bool ok = await _session.RestartVideoAsync(reason);
        _session.CompleteVideoRestart(ok, reason);
        return ok;
    }

    // ---------------------------------------------------------------------
    // Existing control/reposition APIs
    // ---------------------------------------------------------------------
    public void ToggleTeleopControlMode()
    {
        TeleopControlModeManager manager = EnsureControlModeManager();
        if (manager == null) return;
        manager.ToggleControlMode();
        UpdateControlModeButtonLabel();
    }

    /// <summary>
    /// 重新定位机器人底座坐标系:自动切到 View 模式并进入摆放编辑流程。
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

    // ---------------------------------------------------------------------
    // Legacy UI projection and setup
    // ---------------------------------------------------------------------
    private void OnSessionChanged()
    {
        if (_destroyed) return;
        _legacyUi?.SetStatus(_session.StatusText, _session.Status);
        _legacyUi?.SetState(SessionState, IsStreaming, NeedsRecalibration,
            IsStreaming || (_session != null && _session.IsStarting)
                ? "Stop Streaming" : "Start Streaming");
        _legacyUi?.SetMenuVisible(!IsStreaming && (_session == null || !_session.IsStarting));
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        try { Changed?.Invoke(); } catch (Exception error) { Debug.LogException(error, this); }
    }

    private void UpdateWebRTCButtonLabel()
    {
        _legacyUi?.SetWebRtcEnabled(IsWebRTCEnabled);
    }

    private void UpdateControlModeButtonLabel()
    {
        _lastControlMode = TeleopControlModeManager.CurrentMode;
        _legacyUi?.SetControlMode(_lastControlMode);
    }

    private void ClearError()
    {
        if (_session == null) return;
        _session.ClearError();
        _legacyUi?.SetStatus(_session.StatusText, _session.Status);
    }

    private void SetStartButtonText(string text)
    {
        _legacyUi?.SetStartText(text);
    }

    private void ApplyConfigDefaults()
    {
        _settings = teleopConfig != null ? teleopConfig.CreateRuntimeSettings() : TeleopRuntimeSettings.Defaults();
        _settings.Normalize();
        if (!IsIpv4(_settings.ServerHost))
            _settings.ServerHost = TeleopRuntimeSettings.Defaults().ServerHost;
        ApplySettingsSnapshot(_settings);
    }

    private void ApplySettingsSnapshot(TeleopRuntimeSettings snapshot)
    {
        _settings = snapshot == null ? TeleopRuntimeSettings.Defaults() : snapshot.Clone();
        _settings.Normalize();
        if (!IsIpv4(_settings.ServerHost))
            _settings.ServerHost = TeleopRuntimeSettings.Defaults().ServerHost;

        ServerIP = _settings.ServerHost;
        PosePort = _settings.PosePort;
        ControlPort = _settings.ControlPort;
        TactilePort = _settings.TactilePort;
        SignalingPort = _settings.SignalingPort;
        UdpVideoBasePort = _settings.UdpVideoBasePort;
        TrackingMode = _settings.TrackingMode;
        SendRateHz = _settings.SendRateHz;
        UseBinaryProtocol = _settings.UseBinaryProtocol;
        NumWebRTC = _settings.WebRtcTrackCount;
    }

    private void CopyPublicPropertiesToSettings()
    {
        _settings.ServerHost = ServerIP;
        _settings.PosePort = PosePort;
        _settings.ControlPort = ControlPort;
        _settings.TactilePort = TactilePort;
        _settings.SignalingPort = SignalingPort;
        _settings.UdpVideoBasePort = UdpVideoBasePort;
        _settings.TrackingMode = TrackingMode;
        _settings.SendRateHz = SendRateHz;
        _settings.UseBinaryProtocol = UseBinaryProtocol;
        _settings.WebRtcTrackCount = NumWebRTC;
    }

    private void ApplyTransportSettings()
    {
        if (_udpSocket == null)
            _udpSocket = FindAnyObjectByType<UdpSocket>();
        if (_udpSocket != null && !_udpSocket.ApplySettings(ServerIP, PosePort, ControlPort))
            _session?.SetError("Error: invalid UDP endpoint configuration");

        UdpWindowManager windows = GetUdpWindowManager();
        if (windows != null)
            windows.UpdateHost(ServerIP);
    }

    private void ApplyTrackingModeToServices()
    {
        TrackingMode = TeleopRuntimeSettings.ClampTrackingMode(TrackingMode);
        _settings.TrackingMode = TrackingMode;

        if (_trackingModeManager == null)
            _trackingModeManager = FindAnyObjectByType<TrackingModeManager>();
        if (_trackingModeManager != null && (int)_trackingModeManager.CurrentMode != TrackingMode)
            _trackingModeManager.SetTrackingMode(TrackingMode);

        DualControllerSender controllers = FindAnyObjectByType<DualControllerSender>();
        HandTrackingSender hands = FindAnyObjectByType<HandTrackingSender>();
        controllers?.SetSendingEnabled(TrackingMode == 0);
        hands?.SetSendingEnabled(TrackingMode == 1);

        if (TrackingMode == 1)
            FindAnyObjectByType<UpperLimbAkmManager>()?.SetWrmEnabled(false);
    }

    private void ApplyWebRTCTrackCount(int count)
    {
        NumWebRTC = TeleopRuntimeSettings.ClampTrackCount(count);
        _settings.WebRtcTrackCount = NumWebRTC;
        _legacyUi?.SyncInputs(ServerIP, NumWebRTC);

        WebRTCVideoReceiver receiver = FindAnyObjectByType<WebRTCVideoReceiver>();
        if (receiver != null)
            receiver.ExpectedTrackCount = NumWebRTC;
        if (IsWebRTCEnabled)
            SetActiveWebRTCPanels(NumWebRTC);
    }

    private void SetActiveWebRTCPanels(int count)
    {
        _legacyUi?.SetPanels(count);
    }

    private void AttachLegacyListeners()
    {
        _legacyUi?.Attach(OnIpFieldChanged, OnNumWebRTCChanged, ToggleWebRTC,
            RecalibrateTeleopFrame, ToggleTeleopControlMode, BeginRobotBaseReposition);
    }

    private void DetachLegacyListeners()
    {
        _legacyUi?.Detach(OnIpFieldChanged, OnNumWebRTCChanged, ToggleWebRTC,
            RecalibrateTeleopFrame, ToggleTeleopControlMode, BeginRobotBaseReposition);
    }

    private void OnIpFieldChanged(string value)
    {
        ClearError();
    }

    private int SanitizeWebRTCTrackCount(string value)
    {
        return int.TryParse(value == null ? string.Empty : value.Trim(), out int count)
            ? TeleopRuntimeSettings.ClampTrackCount(count) : 1;
    }

    private void EnsureTrackingGuard()
    {
        _trackingGuard = FindAnyObjectByType<TeleopTrackingGuard>();
        if (_trackingGuard == null)
            _trackingGuard = gameObject.AddComponent<TeleopTrackingGuard>();
        _trackingGuard.Initialize(this);
    }

    private bool TrackingAvailable()
    {
        return _trackingGuard == null || _trackingGuard.IsAvailable;
    }

    private VideoStreamManager GetVideoStreamManager()
    {
        if (_videoStreamManager == null)
            _videoStreamManager = FindAnyObjectByType<VideoStreamManager>();
        return _videoStreamManager;
    }

    private RecordingController GetRecordingController()
    {
        if (_recordingController == null)
            _recordingController = FindAnyObjectByType<RecordingController>();
        return _recordingController;
    }

    private static bool IsIpv4(string host)
    {
        return IPAddress.TryParse(host, out IPAddress address) &&
               address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    private UdpWindowManager GetUdpWindowManager()
    {
        if (udpWindowManager == null)
            udpWindowManager = FindAnyObjectByType<UdpWindowManager>();
        return udpWindowManager;
    }

    private TeleopControlModeManager EnsureControlModeManager()
    {
        if (_controlModeManager == null)
            _controlModeManager = FindAnyObjectByType<TeleopControlModeManager>();
        if (_controlModeManager == null)
            _controlModeManager = new GameObject("TeleopControlModeManager").AddComponent<TeleopControlModeManager>();
        return _controlModeManager;
    }

    private sealed class UnityPlayerPrefsStore : ITeleopPreferenceStore
    {
        public string GetString(string key, string fallback) => PlayerPrefs.GetString(key, fallback);
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
        public float GetFloat(string key, float fallback) => PlayerPrefs.GetFloat(key, fallback);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);
        public void Save() => PlayerPrefs.Save();
    }
}
