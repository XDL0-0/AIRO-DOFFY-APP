

using UnityEngine;
using System.Collections;
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TMPro;

public class UdpSocket : MonoBehaviour
{
    [HideInInspector] public bool isTxStarted = false;


    [SerializeField] int txPort_8001 = 8001;
    [SerializeField] int txPort_8003 = 8003;
    [SerializeField] int txPort_8005 = 8005;

    [Header("UI References")]
    [SerializeField] TextMeshProUGUI TextshowinUnity = null;
    [SerializeField] TextMeshProUGUI IPaddress = null;
    [SerializeField] TextMeshProUGUI ChangeIPState = null;
    [SerializeField] TMP_InputField ipInputField;

    private string newIP = "10.10.131.72";
    string IP;

    UdpClient client;
    IPEndPoint remoteEndPoint_8001;
    IPEndPoint remoteEndPoint_8003;
    IPEndPoint remoteEndPoint_8005;
    Thread receiveThread;
    private volatile bool _isReceiving;

    private byte[] imageData;

    // --- 修复关键点 1: 添加线程锁和中间变量 ---
    private object _dataLock = new object(); // 线程锁
    private string _pendingLogMessage = null; // 待显示的日志信息
    private string _pendingErrorMessage = null; // 待显示的错误信息
    private bool _shouldUpdateUI = false; // 标记是否需要更新UI
    // ----------------------------------------

    public void SendData8001(string message) { SendData(message, remoteEndPoint_8001); }
    public void SendData8003(string message) { SendData(message, remoteEndPoint_8003); }
    public void SendData8005(string message) { SendData(message, remoteEndPoint_8005); }

    // 封装发送逻辑，避免重复代码
    private void SendData(string message, IPEndPoint endpoint)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.Send(data, data.Length, endpoint);
        }
        catch (Exception err)
        {
            if (TextshowinUnity != null)
                TextshowinUnity.text = $"Send error: {err.GetType().Name}";
            // TextshowinUnity.text = err.ToString(); // 主线程调用的函数可以直接改UI
        }
    }

    public void UpdateIP(string input)
    {
        System.Net.IPAddress ip;
        if (System.Net.IPAddress.TryParse(input, out ip))
        {
            newIP = ip.ToString();
            if (ChangeIPState != null)
                ChangeIPState.text = "Target IP will be updated to: " + newIP;
        }
        else
        {
            if (ChangeIPState != null)
                ChangeIPState.text = "Not a correct IP address!";
        }
    }

    void Awake()
    {
        IP = newIP;
        RebuildEndpoints();

        client = new UdpClient();

        _isReceiving = true;
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        if (TextshowinUnity != null)
            TextshowinUnity.text = "UDP Comms Initialised";
        if (IPaddress != null)
            IPaddress.text = "Default PC IP Address:" + IP;
    }

    // private void Start()
    // {
    //     if (ipInputField != null)
    //     {
    //         ipInputField.onEndEdit.AddListener(UpdateIP);
    //     }
    //     else
    //     {
    //         TextshowinUnity.text = "IP InputField not available!";
    //     }
    // }

private void Start()
{
    if (AppManager.Instance != null)
    {
        newIP = AppManager.Instance.ServerIP;
        txPort_8001 = AppManager.Instance.PosePort;
        txPort_8005 = AppManager.Instance.ControlPort;
    }

    // 从 PlayerPrefs 恢复上次保存的 IP
    string savedIP = PlayerPrefs.GetString("cfg_ip", newIP);
    if (IPAddress.TryParse(savedIP, out _))
        newIP = savedIP;

    IP = newIP;
    RebuildEndpoints();

    if (ipInputField != null)
    {
        ipInputField.text = newIP;           // UI 也同步显示
        ipInputField.onEndEdit.AddListener(UpdateIP);
    }
}

    // --- 修复关键点 2: 修改接收线程逻辑 ---
    // 这里的代码绝对不能碰 UI 组件
    private void ReceiveData()
    {
        while (_isReceiving)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                imageData = client.Receive(ref anyIP); // 阻塞等待数据

                string msg = "Receive data from: " + anyIP.Address.ToString();

                // 仅仅是把消息存起来，不要赋值给 ChangeIPState.text
                lock (_dataLock)
                {
                    _pendingLogMessage = msg;
                    _shouldUpdateUI = true;
                }
            }
            catch (Exception err)
            {
                if (!_isReceiving) break;

                // 错误信息也存起来
                lock (_dataLock)
                {
                    // _pendingErrorMessage = err.ToString();
                    _pendingErrorMessage = $"Recv error: {err.GetType().Name}";
                    _shouldUpdateUI = true;
                }
            }
        }
    }

    void OnDisable()
    {
        _isReceiving = false;
        if (client != null) client.Close();
        if (receiveThread != null && receiveThread.IsAlive)
        {
            if (!receiveThread.Join(250))
                Debug.LogWarning("UDP receive thread did not stop within 250 ms.");
        }
    }

    void Update()
    {
        // IP 更新逻辑
        if (IP != newIP)
        {
            IP = newIP;
            RebuildEndpoints();
            if (IPaddress != null)
                IPaddress.text = "Target IP Address:" + IP;
            if (ChangeIPState != null)
                ChangeIPState.text = "";
        }

        // --- 修复关键点 3: 在主线程更新 UI ---
        if (_shouldUpdateUI)
        {
            lock (_dataLock)
            {
                // 处理普通消息
                if (!string.IsNullOrEmpty(_pendingLogMessage))
                {
                    if (ChangeIPState != null)
                        ChangeIPState.text = _pendingLogMessage;
                    _pendingLogMessage = null; // 清空
                }

                // 处理错误消息
                if (!string.IsNullOrEmpty(_pendingErrorMessage))
                {
                    if (TextshowinUnity != null)
                        TextshowinUnity.text = _pendingErrorMessage;
                    _pendingErrorMessage = null; // 清空
                }

                _shouldUpdateUI = false;
            }
        }
    }

    private void RebuildEndpoints()
    {
        remoteEndPoint_8001 = new IPEndPoint(IPAddress.Parse(IP), txPort_8001);
        remoteEndPoint_8003 = new IPEndPoint(IPAddress.Parse(IP), txPort_8003);
        remoteEndPoint_8005 = new IPEndPoint(IPAddress.Parse(IP), txPort_8005);
    }
}
