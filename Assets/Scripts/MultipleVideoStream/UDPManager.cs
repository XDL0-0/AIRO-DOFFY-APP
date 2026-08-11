using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;



public class UdpWindowManager : MonoBehaviour
{


    UdpSocket udpSocket;
    TactileUIManager tactileUIManager;
    private UdpSocketMulti udptoolfortactile;
    public GameObject udpWindowPrefab;
    public Transform windowContainer;
    //public Button createWindowButton;
    public Button FocusModeButton;
    public TextMeshProUGUI FocusModeButtonLabel;
    public TMP_InputField ipInputField;
    // string tempStr = "";
    bool FocusMode = false;
    private string IP = "10.10.130.107";
    private int basePort = 8000;
    private int TactilePort = 8012;
    private List<int> activePorts = new List<int>();
    public List<VideoWindowController> windowControllers = new List<VideoWindowController>();

    private int value = 0;
    private int optionCount = 3;


    private StringBuilder _sb = new StringBuilder(128);   // new
    private string _lastMsg = null;                       // new

    public float sendInterval = 0.05f; // new
    private float _sendTimer = 0f;     // new

    private Thread _receiveThread;
    private UdpClient _tactileUdpClient;
    private bool _isReceiving = false;
    private string _targetIP;

    public int[,] TactileSensorData;

    private object _dataLock = new object();

    [Header("Tactile")]
    [Tooltip("是否监听 8012 tactile 数据。8012 已改发 6D 力并由 ForceSensorReceiver 接收时请关闭,避免端口冲突")]
    public bool receiveTactile = true;

    [Header("Visualization References")]
    public TactileSensorGenerator tactileGenerator;

    [Header("Debug")]
    [SerializeField] private bool logTactilePreview = false;
    [SerializeField] private float controllerResetTriggerThreshold = 0.8f;

    // [Header("WebRTC Control Elements")]
    // public Button webRtcButton;               // Enable WebRTC Camera 按钮本身
    // public TextMeshProUGUI webRtcButtonText;  // 该按钮上的文字组件
    // public Button addUdpCameraButton;         // ADD UDP CAMERA 按钮
    // public GameObject webRtcScreen1;          // 隐藏的屏幕1
    // public GameObject webRtcScreen2;          // 隐藏的屏幕2

    // private bool isWebRtcEnabled = false;     // 用于记录当前是否开启了 WebRTC

    void Start()
    {
        //createWindowButton.onClick.AddListener(CreateUdpWindow);
        if (AppManager.Instance != null)
        {
            IP = AppManager.Instance.ServerIP;
            basePort = AppManager.Instance.UdpVideoBasePort;
            TactilePort = AppManager.Instance.TactilePort;
        }

        if (ipInputField != null)
            ipInputField.onValueChanged.AddListener(OnIpAddressChanged);
        // if (webRtcButton != null)
        // {
        //     webRtcButton.onClick.AddListener(ToggleWebRtcCamera);
        // }
        //FocusModeButton.onClick.AddListener(FocusControl);
        udpSocket = FindAnyObjectByType<UdpSocket>();


        if (tactileUIManager == null)
        {
            tactileUIManager = FindAnyObjectByType<TactileUIManager>();
        }
        _targetIP = ipInputField != null ? ipInputField.text.Trim() : IP;
        if (string.IsNullOrEmpty(_targetIP)) _targetIP = IP;

        // 8012 已改发 6D 力并由 ForceSensorReceiver 接收时,自动跳过 tactile 绑定,避免端口冲突
        if (receiveTactile && FindAnyObjectByType<ForceSensorReceiver>() != null)
        {
            Debug.LogWarning("[UdpWindowManager] ForceSensorReceiver 已在监听 8012,跳过 tactile 接收(如不再需要可关闭 receiveTactile)");
            receiveTactile = false;
        }

        if (receiveTactile)
            StartTactileReceiver();
    }
    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            FocusControl();
        }
        bool resetComboPressed =
            OVRInput.GetDown(OVRInput.RawButton.RThumbstick) &&
            OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) >= controllerResetTriggerThreshold &&
            !RobotBasePlacementTool.ShouldSuppressRightThumbstickReset;

        if (resetComboPressed)
        {
            foreach (var windowController in windowControllers)
            {
                windowController.resolutionButtonLabel.text = "x1.0";
            }
            if (FocusModeButtonLabel != null)
                FocusModeButtonLabel.text = "Fine Control Mode\nOFF";
        }

        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
        {
            value = (value + 1) % optionCount;
            CanvasMove();
        }
        _sendTimer += Time.deltaTime;
        if (_sendTimer >= sendInterval)
        {
            _sendTimer = 0f;
            Resolution_loop();
        }


        int[,] tactileSnapshot = null;
        lock (_dataLock)
        {
            if (TactileSensorData != null)
                tactileSnapshot = (int[,])TactileSensorData.Clone();
        }

        if (tactileSnapshot != null)
        {
            if (tactileUIManager != null)
            {
                tactileUIManager.EnableVisualizationButton();
            }

            if (logTactilePreview)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("TactileSensorData preview:");

                for (int i = 0; i < tactileSnapshot.GetLength(0); i++)
                    sb.Append($"[Row {i}] {tactileSnapshot[i, 0]}, {tactileSnapshot[i, 1]}, {tactileSnapshot[i, 2]}\n");

                Debug.Log(sb.ToString());
            }
            if (tactileGenerator != null)
            {
                tactileGenerator.UpdateRawSensorData(tactileSnapshot);
            }

        }
        
    }
    void CanvasMove()
    {
        RectTransform rect = windowContainer.GetComponent<RectTransform>();

        switch (value)
        {
            case 0: // FRONT
                rect.localPosition = new Vector3(0f, 0f, 14f);
                rect.localEulerAngles = new Vector3(0f, 0f, 0f);
                break;

            case 1: // LEFT
                rect.localPosition = new Vector3(-5f, 0f, 10f);
                rect.localEulerAngles = new Vector3(0f, -45f, 0f);
                break;

            case 2: // RIGHT
                rect.localPosition = new Vector3(5f, 0f, 10f);
                rect.localEulerAngles = new Vector3(0f, 45f, 0f);
                break;
        }

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void Resolution_loop()
    {
        if (udpSocket == null) return;

        _sb.Clear();

        foreach (var windowController in windowControllers)
        {
            _sb.Append(windowController.Port);
            _sb.Append(',');
            _sb.Append(windowController.resolutionButtonLabel.text);
            _sb.Append(';');
        }

        string focusModeLabel = FocusModeButtonLabel != null
            ? FocusModeButtonLabel.text.Replace("\n", ",")
            : "Fine Control Mode,OFF";
        _sb.Append(focusModeLabel);
        _sb.Append(';');

        string msg = _sb.ToString();

        if (msg == _lastMsg) return;
        _lastMsg = msg;

        udpSocket.SendData8005(msg);
    }
    public void CreateUdpWindow()
    {
        int newPort = GetAvailablePort();
        if (newPort == -1)
        {
            return;
        }

        GameObject newWindow = Instantiate(udpWindowPrefab, windowContainer);
        newWindow.transform.SetSiblingIndex(14);

        VideoWindowController videoWindowController = newWindow.GetComponent<VideoWindowController>();
        if (videoWindowController != null)
        {
            string ip = ipInputField != null ? ipInputField.text.Trim() : _targetIP;
            if (string.IsNullOrEmpty(ip))
                ip = IP;

            if (string.IsNullOrEmpty(ip))
            {
                return;
            }

            int receivePort = newPort;
            videoWindowController.Initialize(ip, receivePort, videoWindowController);
            RuntimeUITheme.ApplyTo(newWindow.transform);
            windowControllers.Add(videoWindowController);
            activePorts.Add(newPort);
        }
    }

    void FocusControl()
    {
 

        if (FocusMode)
        {
            FocusMode = false;
            foreach (var windowController in windowControllers)
            {
                windowController.resolutionButtonLabel.text = "x1.0";
            }
            if (FocusModeButtonLabel != null)
                FocusModeButtonLabel.text = "Fine Control Mode\nOFF";
        }
        else
        {
            FocusMode = true;
            foreach (var windowController in windowControllers)
            {
                windowController.resolutionButtonLabel.text = "x1.5";
            }
            if (FocusModeButtonLabel != null)
                FocusModeButtonLabel.text = "Fine Control Mode\nON";
        }

    }


    int GetAvailablePort()
    {
        for (int i = 0; i < 5; i++)
        {
            int port = basePort + i * 2;
            if (!activePorts.Contains(port)) return port;
        }
        return -1;
    }


    public void CloseWindow(VideoWindowController windowToRemove)
    {
        if (windowToRemove == null) return;

        if (activePorts.Contains(windowToRemove.Port))
        {
            activePorts.Remove(windowToRemove.Port);
            Debug.Log($"Port {windowToRemove.Port} released");
        }

        if (windowControllers.Contains(windowToRemove))
        {
            windowControllers.Remove(windowToRemove);
        }

        Destroy(windowToRemove.gameObject);

        Resolution_loop();
    }

    public void CloseAllWindows()
    {
        if (windowControllers.Count == 0) return;

        foreach (var window in windowControllers)
        {
            if (window != null)
            {
                Destroy(window.gameObject);
            }
        }

        activePorts.Clear();
        windowControllers.Clear();
        Debug.Log("所有窗口已销毁，端口池已全部清空。");

        Resolution_loop();
    }
    // public void ToggleWebRtcCamera()
    // {
    //     // 每次点击，状态反转 (true 变 false，false 变 true)
    //     isWebRtcEnabled = !isWebRtcEnabled;
    //
    //     if (isWebRtcEnabled)
    //     {
    //         // ===== 状态 1：开启 WebRTC =====
    //
    //         // 1. 文字变成 Disable WebRTC Camera
    //         if (webRtcButtonText != null) webRtcButtonText.text = "Disable WebRTC Camera";
    //
    //         // 2. 使 ADD UDP CAMERA Button 变灰无法点击
    //         if (addUdpCameraButton != null) addUdpCameraButton.interactable = false;
    //
    //         // 3. 将 UDP window 全部关闭
    //         CloseAllWindows();
    //
    //         // 4. 将隐藏的两个屏幕设为激活
    //         if (webRtcScreen1 != null) webRtcScreen1.SetActive(true);
    //         if (webRtcScreen2 != null) webRtcScreen2.SetActive(true);
    //
    //         Debug.Log("WebRTC Camera 已启用，UDP 窗口已清空。");
    //     }
    //     else
    //     {
    //         // ===== 状态 2：关闭 WebRTC =====
    //
    //         // 1. 文字变回 Enable WebRTC Camera
    //         if (webRtcButtonText != null) webRtcButtonText.text = "Enable WebRTC Camera";
    //
    //         // 2. 使 ADD UDP CAMERA Button 变得可以点击
    //         if (addUdpCameraButton != null) addUdpCameraButton.interactable = true;
    //
    //         // 3. 将两个屏幕设为隐藏
    //         if (webRtcScreen1 != null) webRtcScreen1.SetActive(false);
    //         if (webRtcScreen2 != null) webRtcScreen2.SetActive(false);
    //
    //         Debug.Log("WebRTC Camera 已禁用，可以继续添加 UDP 窗口。");
    //     }
    // }


    void OnDestroy()
    {
        StopTactileReceiver();
    }

    void OnIpAddressChanged(string newIP)
    {
        _targetIP = string.IsNullOrWhiteSpace(newIP) ? IP : newIP.Trim();

        foreach (var windowController in windowControllers)
        {
            windowController.UpdateIpAddress(_targetIP);
        }

        if (udptoolfortactile != null && System.Net.IPAddress.TryParse(_targetIP, out System.Net.IPAddress ip))
        {
            udptoolfortactile.Initialize(_targetIP, TactilePort);
        }

    }



    void StartTactileReceiver()
    {
        if (_isReceiving) return;

        _isReceiving = true;
        _receiveThread = new Thread(ReceiveDataLoop);
        _receiveThread.IsBackground = true;
        _receiveThread.Start();
    }

    void StopTactileReceiver()
    {
        _isReceiving = false;
        if (_tactileUdpClient != null)
        {
            _tactileUdpClient.Close();
            _tactileUdpClient = null;
        }
        if (_receiveThread != null && _receiveThread.IsAlive)
        {
            if (!_receiveThread.Join(250))
                Debug.LogWarning("Tactile receive thread did not stop within 250 ms.");
        }
        _receiveThread = null;
    }

    private void ReceiveDataLoop()
    {
        try
        {
            _tactileUdpClient = new UdpClient(TactilePort);

            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (_isReceiving)
            {
                try
                {
                    byte[] receivedBytes = _tactileUdpClient.Receive(ref remoteEndPoint);

                    ParseByteStreamToTable(receivedBytes);
                }
                catch (Exception ex)
                {
                    if (_isReceiving) Debug.LogWarning($"UDP Receive Error: {ex.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to bind port {TactilePort}: {e.Message}");
        }
    }

    private void ParseByteStreamToTable(byte[] data)
    {
        int sensorCount = 41;
        int axisCount = 3;
        int bytesPerValue = 4;

        int expectedLen = sensorCount * axisCount * bytesPerValue;

        if (logTactilePreview)
            Debug.LogWarning($"Received byte length: {data.Length}");

        if (data.Length < expectedLen)
        {
            Debug.LogWarning($"Byte length insufficient, received {data.Length} need {expectedLen}");
            return;
        }
        if (TactileSensorData == null)
        {
            TactileSensorData = new int[41, 3];
        }

        lock (_dataLock)
        {
            int byteIndex = 0;

            for (int i = 0; i < sensorCount; i++)
            {
                for (int j = 0; j < axisCount; j++)
                {
                    if (byteIndex + 4 > data.Length)
                    {
                        Debug.LogWarning($"Index out of bounds byteIndex = {byteIndex}");
                        return;
                    }

                    int val = BitConverter.ToInt32(data, byteIndex);

                    TactileSensorData[i, j] = val;

                    byteIndex += bytesPerValue;
                }
            }
        }
    }

    public float GetSensorValue(int sensorId, int axisIndex)
    {
        if (sensorId < 0 || sensorId >= 41 || axisIndex < 0 || axisIndex >= 3) return 0f;

        lock (_dataLock)
        {
            if (TactileSensorData == null) return 0f;
            return TactileSensorData[sensorId, axisIndex];
        }
    }



    

}
