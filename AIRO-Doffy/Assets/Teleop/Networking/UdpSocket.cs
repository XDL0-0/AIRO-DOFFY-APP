using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;

/// <summary>
/// Legacy UDP sender for pose, recording and control channels. The public
/// SendData800x methods are retained for all classic feature scripts; endpoint
/// configuration is now applied as one validated snapshot by AppManager.
/// </summary>
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
    private string IP;
    private UdpClient client;
    private IPEndPoint remoteEndPoint_8001;
    private IPEndPoint remoteEndPoint_8003;
    private IPEndPoint remoteEndPoint_8005;
    private Thread receiveThread;
    private volatile bool _isReceiving;
    private byte[] imageData;

    private readonly object _dataLock = new object();
    private string _pendingLogMessage;
    private string _pendingErrorMessage;
    private bool _shouldUpdateUI;

    public void SendData8001(string message) { SendData(message, remoteEndPoint_8001); }
    public void SendData8003(string message) { SendData(message, remoteEndPoint_8003); }
    public void SendData8005(string message) { SendData(message, remoteEndPoint_8005); }

    /// <summary>
    /// Applies host and sender ports atomically from the session coordinator.
    /// All three endpoint objects are replaced together, so a caller cannot
    /// observe one sender using the new host while another still uses the old.
    /// Invalid values leave the previous endpoint intact.
    /// </summary>
    public bool ApplySettings(string host, int posePort, int controlPort)
    {
        string trimmed = host == null ? string.Empty : host.Trim();
        if (!IPAddress.TryParse(trimmed, out IPAddress address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            !IsValidPort(posePort) || !IsValidPort(txPort_8003) ||
            !IsValidPort(controlPort))
            return false;

        IPEndPoint poseEndpoint = new IPEndPoint(address, posePort);
        IPEndPoint recordingEndpoint = new IPEndPoint(address, txPort_8003);
        IPEndPoint controlEndpoint = new IPEndPoint(address, controlPort);

        newIP = trimmed;
        IP = trimmed;
        txPort_8001 = posePort;
        txPort_8005 = controlPort;
        remoteEndPoint_8001 = poseEndpoint;
        remoteEndPoint_8003 = recordingEndpoint;
        remoteEndPoint_8005 = controlEndpoint;
        return true;
    }

    public void UpdateIP(string input)
    {
        string trimmed = input == null ? string.Empty : input.Trim();
        if (!IPAddress.TryParse(trimmed, out IPAddress address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            if (ChangeIPState != null)
                ChangeIPState.text = "Not a correct IP address!";
            return;
        }

        if (!ApplySettings(trimmed, txPort_8001, txPort_8005))
            return;

        if (ChangeIPState != null)
            ChangeIPState.text = "Target IP will be updated to: " + newIP;

        // A legacy input edit is still a supported connection entrypoint. Keep
        // the app snapshot and UDP endpoints in sync instead of updating only
        // this sender.
        AppManager.Instance?.ApplyLegacyHost(trimmed);
    }

    private void Awake()
    {
        AppManager app = AppManager.Instance;
        if (app != null)
        {
            newIP = app.ServerIP;
            txPort_8001 = app.PosePort;
            txPort_8005 = app.ControlPort;
        }

        IP = newIP;
        if (!RebuildEndpoints())
        {
            IP = "10.10.131.72";
            newIP = IP;
            RebuildEndpoints();
        }

        client = new UdpClient();
        isTxStarted = true;
        _isReceiving = true;
        receiveThread = new Thread(ReceiveData) { IsBackground = true };
        receiveThread.Start();

        if (TextshowinUnity != null)
            TextshowinUnity.text = "UDP Comms Initialised";
        if (IPaddress != null)
            IPaddress.text = "Default PC IP Address:" + IP;
    }

    private void Start()
    {
        AppManager app = AppManager.Instance;
        if (app != null)
        {
            // AppManager loads PlayerPrefs before Start. Do not read a second
            // hidden preference value here and overwrite a configured session.
            ApplySettings(app.ServerIP, app.PosePort, app.ControlPort);
        }
        else
        {
            string savedIP = PlayerPrefs.GetString("cfg_ip", newIP);
            if (IPAddress.TryParse(savedIP, out _))
                newIP = savedIP.Trim();
        }

        if (ipInputField != null)
        {
            ipInputField.text = newIP;
            ipInputField.onEndEdit.AddListener(UpdateIP);
        }
    }

    private void ReceiveData()
    {
        while (_isReceiving)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                imageData = client.Receive(ref anyIP);
                string msg = "Receive data from: " + anyIP.Address;
                lock (_dataLock)
                {
                    _pendingLogMessage = msg;
                    _shouldUpdateUI = true;
                }
            }
            catch (Exception error)
            {
                if (!_isReceiving) break;
                lock (_dataLock)
                {
                    _pendingErrorMessage = "Recv error: " + error.GetType().Name;
                    _shouldUpdateUI = true;
                }
            }
        }
    }

    private void OnDisable()
    {
        _isReceiving = false;
        isTxStarted = false;
        if (client != null)
            client.Close();

        if (receiveThread != null && receiveThread.IsAlive && !receiveThread.Join(250))
            Debug.LogWarning("UDP receive thread did not stop within 250 ms.");
    }

    private void Update()
    {
        if (IP != newIP)
        {
            if (IPAddress.TryParse(newIP, out IPAddress address) &&
                address.AddressFamily == AddressFamily.InterNetwork)
            {
                IP = newIP;
                if (RebuildEndpoints())
                {
                    if (IPaddress != null) IPaddress.text = "Target IP Address:" + IP;
                    if (ChangeIPState != null) ChangeIPState.text = string.Empty;
                }
            }
        }

        if (!_shouldUpdateUI) return;
        lock (_dataLock)
        {
            if (!string.IsNullOrEmpty(_pendingLogMessage))
            {
                if (ChangeIPState != null) ChangeIPState.text = _pendingLogMessage;
                _pendingLogMessage = null;
            }

            if (!string.IsNullOrEmpty(_pendingErrorMessage))
            {
                if (TextshowinUnity != null) TextshowinUnity.text = _pendingErrorMessage;
                _pendingErrorMessage = null;
            }

            _shouldUpdateUI = false;
        }
    }

    private void SendData(string message, IPEndPoint endpoint)
    {
        if (client == null || endpoint == null || string.IsNullOrEmpty(message)) return;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.Send(data, data.Length, endpoint);
        }
        catch (Exception error)
        {
            if (TextshowinUnity != null)
                TextshowinUnity.text = "Send error: " + error.GetType().Name;
        }
    }

    private bool RebuildEndpoints()
    {
        if (!IPAddress.TryParse(IP, out IPAddress address) ||
            address.AddressFamily != AddressFamily.InterNetwork) return false;
        if (!IsValidPort(txPort_8001) || !IsValidPort(txPort_8003) || !IsValidPort(txPort_8005)) return false;

        remoteEndPoint_8001 = new IPEndPoint(address, txPort_8001);
        remoteEndPoint_8003 = new IPEndPoint(address, txPort_8003);
        remoteEndPoint_8005 = new IPEndPoint(address, txPort_8005);
        return true;
    }

    private static bool IsValidPort(int port)
    {
        return port >= 1 && port <= 65535;
    }
}
