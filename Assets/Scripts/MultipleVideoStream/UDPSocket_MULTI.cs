//using System;
//using System.Diagnostics;
//using System.Net;
//using System.Net.Sockets;
//using System.Text;
//using System.Threading;
//using TMPro;
//using UnityEngine;

//public class UdpSocketMulti : MonoBehaviour
//{
//    //public bool isTxStarted = false;

//    UdpClient client;
//    IPEndPoint ReceiveEndPoint;
//    Thread receiveThread;
//    public TextMeshProUGUI debug;
//    private Texture2D texture;
//    private byte[] imageData;
//    private bool newImageReady = false;
//    private int rxPort;
//    [SerializeField] string IP;
//    public void Initialize(string ip, int receivePort)
//    {
//        IP = ip;
//        rxPort = receivePort;
//        SetupUDP();
//    }

//    private void SetupUDP()
//    {
//        if (receiveThread != null)
//            receiveThread.Abort();

//        if (client != null)
//        {
//            client.Close();
//        }

//        client = new UdpClient(rxPort);
//        receiveThread = new Thread(new ThreadStart(ReceiveData));
//        receiveThread.IsBackground = true;
//        receiveThread.Start();
//    }


//    void Start()
//    {
//        texture = new Texture2D(640, 480, TextureFormat.RGB24, false);
//        GetComponent<Renderer>().material.mainTexture = texture;

//    }

//    private void ReceiveData()
//    {
//        while (true)
//        {
//            debug.text = "WAITTTTTTINGGGG";

//            try
//            {
//                ReceiveEndPoint = new IPEndPoint(IPAddress.Parse(IP), rxPort);
//                imageData = client.Receive(ref ReceiveEndPoint);
//                debug.text = imageData.ToString();
//                newImageReady = true;
//            }
//            catch (Exception err)
//            {
//                debug.text = err.ToString();
//                //Debug.LogError(err.ToString());
//            }
//        }
//    }

//    void Update()
//    {
//        if (newImageReady && imageData != null)
//        {
//            lock (this)
//            {
//                if (texture.LoadImage(imageData))
//                {
//                    debug.text = "imageDATEEEEEE";
//                    texture.Apply();
//                }
//                else
//                {
//                    debug.text = "Image cannot load";
//                    //Debug.LogError("Image cannot load");
//                }
//                newImageReady = false;
//            }
//        }

//    }


//    void OnDisable()
//    {
//        if (receiveThread != null)
//            receiveThread.Abort();
//        if (client != null)
//        {
//            client.Close();
//        }
//    }
//}

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TMPro;
using UnityEngine;

public class UdpSocketMulti : MonoBehaviour
{
    private UdpClient client;
    private Thread receiveThread;

    // 使用 volatile 确保多线程间的可见性
    private volatile bool isRunning = false;

    // 用于线程同步的锁对象
    private readonly object lockObject = new object();

    private Texture2D texture;
    private byte[] pendingImageData = null; // 待处理的数据
    private bool hasNewData = false;

    private int rxPort;
    [SerializeField] string IP; // 注意：UDP接收通常只需要端口，IP主要用于发送或过滤
    public void Initialize(string ip, int receivePort)
    {
        IP = ip;
        rxPort = receivePort;
        SetupUDP();
    }

    // 如果是通过 Inspector 设置的，直接在 Start 启动也可以
    void Start()
    {
        // 如果没有外部调用 Initialize，可以在这里给个默认值测试

        // 初始大小不重要，LoadImage 会自动调整大小，但格式最好保持一致
        texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        GetComponent<Renderer>().material.mainTexture = texture;

        SetupUDP();
    }

    private void SetupUDP()
    {
        Cleanup(); // 先清理旧的连接

        try
        {
            client = new UdpClient(rxPort);
            // 解决 Windows 下 UDP 连接断开导致的 SocketException
            //client.Client.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null);

            isRunning = true;
            receiveThread = new Thread(new ThreadStart(ReceiveData));
            receiveThread.IsBackground = true;
            receiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to start UDP: {e.Message}");
        }
    }

    private void ReceiveData()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, rxPort);
        //IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(IP), rxPort);
        while (isRunning)
        {

            try
            {
                // 只有当有数据时才读取，避免阻塞导致无法退出循环
                if (client.Available > 0)
                {
                    byte[] receivedBytes = client.Receive(ref remoteEndPoint);

                    // 加锁：将数据存入缓存区
                    lock (lockObject)
                    {
                        pendingImageData = receivedBytes;
                        hasNewData = true;
                    }
                }
                else
                {
                    // 稍微休眠一下，避免空转占用 CPU
                    Thread.Sleep(10);
                }
            }
            catch (SocketException sockEx)
            {
                // 忽略非阻塞或关闭时的错误
                if (sockEx.SocketErrorCode != SocketError.Interrupted)
                    Debug.LogWarning(sockEx.Message);
            }
            catch (Exception err)
            {
                Debug.LogError(err.ToString());
            }
        }
    }

    void Update()
    {
        if (hasNewData)
        {
            byte[] dataToLoad = null;

            // 加锁：快速取出数据，尽量缩短锁住的时间
            lock (lockObject)
            {
                if (hasNewData && pendingImageData != null)
                {
                    dataToLoad = pendingImageData;
                    pendingImageData = null; // 清空引用
                    hasNewData = false;
                }
            }

            // 解锁后再执行耗时的 LoadImage，避免阻塞接收线程
            if (dataToLoad != null)
            {
                // LoadImage 必须在主线程
                if (texture.LoadImage(dataToLoad))
                {
                    texture.Apply();
                }
                //texture.LoadImage(dataToLoad);

            }
        }
    }

    private void Cleanup()
    {
        isRunning = false;

        // 等待线程安全结束（可选）
        // if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(500);

        if (client != null)
        {
            client.Close();
            client = null;
        }
    }

    void OnDisable()
    {
        Cleanup();
    }

    void OnApplicationQuit()
    {
        Cleanup();
    }
}

