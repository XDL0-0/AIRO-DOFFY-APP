using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Doffy.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Owns floating UDP views and their layout. Optional tactile acquisition is isolated.</summary>
public class UdpWindowManager : MonoBehaviour
{
    public GameObject udpWindowPrefab;
    public Transform windowContainer;
    public Button FocusModeButton;
    public TextMeshProUGUI FocusModeButtonLabel;
    public TMP_InputField ipInputField;
    public List<VideoWindowController> windowControllers = new List<VideoWindowController>();
    public float sendInterval = .05f;
    public bool receiveTactile;
    public TactileSensorGenerator tactileGenerator;
    public int[,] TactileSensorData;
    [SerializeField] private bool logTactilePreview;
    [SerializeField] private float controllerResetTriggerThreshold = .8f;
    private UdpSocket sender;
    private TactileUIManager tactileUi;
    private TactileArrayStream tactile;
    private UpperLimbAkmManager upperLimb;
    private string host;
    private int basePort = 8000, layout;
    private float nextControlSend;
    private bool focused;
    private readonly StringBuilder message = new StringBuilder(256);
    private string lastControl;

    private void Start()
    {
        windowContainer = Doffy.UI.CameraPanelContainer.Ensure(windowContainer);
        var app = AppManager.Instance;
        host = app != null ? app.ServerIP : PlayerPrefs.GetString("cfg_ip", "10.10.131.72");
        basePort = app != null ? app.UdpVideoBasePort : 8000;
        sender = FindAnyObjectByType<UdpSocket>();
        tactileUi = FindAnyObjectByType<TactileUIManager>();
        upperLimb = FindAnyObjectByType<UpperLimbAkmManager>();
        if (ipInputField != null) ipInputField.onEndEdit.AddListener(UpdateHost);
        if (!receiveTactile) return;
        int tactilePort = app != null ? app.TactilePort : 8012;
        var tcp = FindAnyObjectByType<TCPPoseReceiver>();
        var force = FindAnyObjectByType<ForceSensorReceiver>();
        if ((tcp != null && tcp.isActiveAndEnabled && tcp.ListenPort == tactilePort) ||
            (force != null && force.isActiveAndEnabled && force.ListenPort == tactilePort))
        {
            Debug.LogWarning("Legacy tactile stream is disabled because the TCP/wrench channel owns its port.", this);
            receiveTactile = false;
            return;
        }
        try { tactile = new TactileArrayStream(tactilePort); }
        catch (Exception error) { Debug.LogWarning("Tactile receiver: " + error.Message, this); }
    }

    private void Update()
    {
        bool mappingActive = upperLimb != null && upperLimb.WrmEnabled;
        if (!mappingActive && FocusModeButtonLabel != null && OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            SetFocus(!focused);
        if (OVRInput.GetDown(OVRInput.RawButton.RThumbstick) &&
            OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) >= controllerResetTriggerThreshold &&
            !RobotBasePlacementTool.ShouldSuppressRightThumbstickReset) SetFocus(false);
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) CycleLayout();
        if (Time.unscaledTime >= nextControlSend)
        {
            nextControlSend = Time.unscaledTime + Mathf.Max(.02f, sendInterval);
            Resolution_loop();
        }
        int[,] sample = tactile?.TakeLatest();
        if (sample == null) return;
        TactileSensorData = sample;
        tactileUi?.EnableVisualizationButton();
        if (tactileGenerator != null) tactileGenerator.UpdateRawSensorData(sample);
        if (logTactilePreview) Debug.Log($"Tactile: {sample[0, 0]}, {sample[0, 1]}, {sample[0, 2]}", this);
    }

    public void UpdateHost(string value)
    {
        if (!IPAddress.TryParse(value?.Trim(), out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return;
        host = value.Trim();
        foreach (var window in windowControllers) if (window != null) window.UpdateIpAddress(host);
        lastControl = null;
    }

    public void CreateUdpWindow()
    {
        if (AppManager.Instance != null && AppManager.Instance.IsWebRTCEnabled) return;
        windowControllers.RemoveAll(window => window == null);
        if (windowControllers.Count >= 5 || udpWindowPrefab == null || windowContainer == null) return;
        windowContainer = Doffy.UI.CameraPanelContainer.Ensure(windowContainer);
        int port = AvailablePort();
        if (port < 0 || string.IsNullOrWhiteSpace(host)) return;
        GameObject instance = Instantiate(udpWindowPrefab, windowContainer);
        var controller = instance.GetComponent<VideoWindowController>();
        if (controller == null) { Destroy(instance); return; }
        controller.Initialize(host, port, controller);
        RuntimeUITheme.ApplyTo(instance.transform);
        windowControllers.Add(controller);
    }

    public bool IsPortAvailable(int port, VideoWindowController excluding = null)
    {
        foreach (var window in windowControllers)
            if (window != null && window != excluding && window.Port == port) return false;
        return true;
    }

    private int AvailablePort()
    {
        for (int i = 0; i < 5; i++) if (IsPortAvailable(basePort + 2 * i)) return basePort + 2 * i;
        return -1;
    }

    public void CloseWindow(VideoWindowController window)
    {
        if (window == null) return;
        windowControllers.Remove(window);
        Destroy(window.gameObject);
        lastControl = null;
    }

    public void CloseAllWindows()
    {
        foreach (var window in windowControllers) if (window != null) Destroy(window.gameObject);
        windowControllers.Clear();
        lastControl = null;
    }

    public void CycleLayout()
    {
        if (windowContainer == null) return;
        windowContainer = Doffy.UI.CameraPanelContainer.Ensure(windowContainer);
        layout = (layout + 1) % 3;
        Vector3[] positions = { new Vector3(0, 0, 14), new Vector3(-5, 0, 10), new Vector3(5, 0, 10) };
        float[] headings = { 0, -45, 45 };
        windowContainer.localPosition = positions[layout];
        windowContainer.localEulerAngles = new Vector3(0, headings[layout], 0);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public void SetFocus(bool enabled)
    {
        focused = enabled;
        foreach (var window in windowControllers) if (window != null) window.SetResolution(enabled ? "x1.5" : "x1.0");
        if (FocusModeButtonLabel != null) FocusModeButtonLabel.text = "Fine Control Mode\n" + (enabled ? "ON" : "OFF");
    }

    public void Resolution_loop()
    {
        // WRM owns 8005 while active. Window zoom remains local in that mode.
        if (sender == null || FocusModeButtonLabel == null || (upperLimb != null && upperLimb.WrmEnabled)) return;
        message.Clear();
        foreach (var window in windowControllers)
            if (window != null) message.Append(window.Port).Append(',').Append(window.Resolution).Append(';');
        message.Append("Fine Control Mode,").Append(focused ? "ON;" : "OFF;");
        string value = message.ToString();
        if (lastControl == value) return;
        sender.SendData8005(value);
        lastControl = value;
    }

    public float GetSensorValue(int sensorId, int axisIndex) =>
        TactileSensorData != null && sensorId >= 0 && sensorId < 41 && axisIndex >= 0 && axisIndex < 3
            ? TactileSensorData[sensorId, axisIndex] : 0;

    private void OnDestroy()
    {
        if (ipInputField != null) ipInputField.onEndEdit.RemoveListener(UpdateHost);
        tactile?.Dispose();
        CloseAllWindows();
    }
}
