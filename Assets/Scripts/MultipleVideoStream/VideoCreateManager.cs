using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VideoWindowController : MonoBehaviour
{
    public Button closeButton;
    public TMP_InputField portInputField;
    public Button resolutionButton;
    public TextMeshProUGUI resolutionButtonLabel;
    public TextMeshProUGUI textStatus;
    private UdpSocketMultiHD udpSocket;
    private GameObject currentWindow;
    [SerializeField] string IP;
    [SerializeField] int port;
    private UdpWindowManager udpWindowManager;
    private VideoWindowController selfname;

    public string IpAddress => IP;
    public int Port => port;
    void Awake()
    {
        Transform planeTransform = transform.Find("Plane");
        if (planeTransform == null)
        {
            textStatus.text = "cannot find plane";
        }
        udpSocket = transform.Find("Plane").GetComponent<UdpSocketMultiHD>();


        closeButton.onClick.AddListener(CloseWindow);
        resolutionButton.onClick.AddListener(ChangeResolution);
        portInputField.onEndEdit.AddListener(UpdatePort);

        // udpWindowManager = FindObjectOfType<UdpWindowManager>();
        udpWindowManager = FindAnyObjectByType<UdpWindowManager>();

        if (udpWindowManager != null)
        {
            // ������������� UdpWindowManager �еı���
            //Debug.Log("Found UdpWindowManager.");
        }
        else
        {
            //Debug.LogError("UdpWindowManager not found.");
        }
    }

    public void Initialize(string ip, int receivePort, VideoWindowController name)
    {
        if (udpSocket != null)
        {
            IP = ip;
            port = receivePort;
            udpSocket.Initialize(ip, receivePort);
            textStatus.text = IP + ": " + receivePort;
            portInputField.text = receivePort.ToString();
            selfname = name;
        }
        else
        {
            textStatus.text = "udpsocket is null";
        }
    }

    void UpdatePort(string newPort)
    {
        if (int.TryParse(newPort, out int parsed))
        {
            port = parsed;
            udpSocket.Initialize(IP, port);
            textStatus.text = IP + ": " + port;
        }
        else
        {
            textStatus.text = "not valid";
        }
    }


    //void CloseWindow()
    //{
    //    udpWindowManager.windowControllers.Remove(selfname);
    //    Destroy(gameObject);
    //}

    void CloseWindow()
    {

        if (udpWindowManager != null)
        {
            // �ѡ��Լ����������������ù�����ȥ�����ͷŶ˿ں����ٵ��߼�
            udpWindowManager.CloseWindow(this);
        }
        else
        {
            // ����Ҳ�����������Ϊ�˷�ֹ������ֻ��ǿ������
            Debug.LogWarning("UdpWindowManager missing, destroying window without freeing port.");
            Destroy(gameObject);
        }
    }

    void ChangeResolution()
    {
        if(resolutionButtonLabel.text == "x1.0")
        {
            resolutionButtonLabel.text = "x1.5";
        }
        else if(resolutionButtonLabel.text =="x1.5")
        {
            resolutionButtonLabel.text = "x2.0";
        }
        else if (resolutionButtonLabel.text == "x2.0")
        {
            resolutionButtonLabel.text = "x1.0";
        }
    }

    public void UpdateIpAddress(string newIP)
    {
        if (System.Net.IPAddress.TryParse(newIP, out System.Net.IPAddress ip))
        {
            IP = newIP;
            udpSocket.Initialize(IP, port);
            textStatus.text = IP + ": " + port;
            //textStatus.text = "IP : " + newIP;
        }
        else
        {
            textStatus.text = "not valid";
        }
    }
}