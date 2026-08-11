using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// 通过 UDP 8012 接收机器人 TCP 位姿 + 6D 力(单端口,合并传输)。
///
/// JSON 格式:
/// {
///   "rightTCP": {
///     "position": [x,y,z],
///     "rotation": [w,x,y,z],
///     "force": [fx,fy,fz],
///     "torque": [mx,my,mz]
///   }
/// }
///
/// position/rotation 为 TCP 在机器人基座坐标系下的位姿(Unity 左手系, w 在前)。
/// force/torque 为牛顿/牛·米,直接驱动 ForceArrow 显示。
///
/// 单臂配置: 只接 RightTCP + RightForce, leftTCP/leftForce 置空即可。
/// </summary>
public class TCPPoseReceiver : MonoBehaviour
{
    [Serializable]
    public class TcpPose
    {
        public float[] position;
        public float[] rotation;
        public float[] force;
        public float[] torque;
    }

    [Serializable]
    public class BimanualTcpMessage
    {
        public TcpPose leftTCP;
        public TcpPose rightTCP;
    }

    [Header("UDP")]
    [SerializeField] private int listenPort = 8012;

    [Header("TCP References")]
    [Tooltip("左臂 TCP 变换(单臂置空)")]
    public Transform leftTCP;
    [Tooltip("右臂 TCP 变换")]
    public Transform rightTCP;

    [Header("Force Arrow References")]
    [Tooltip("左臂力箭头(单臂置空)")]
    public Transform leftForce;
    [Tooltip("右臂力箭头")]
    public Transform rightForce;

    [Header("Force Display")]
    [Tooltip("力箭头显示缩放: 显示长度(米) = 力(N) × displayScale")]
    public float forceDisplayScale = 0.01f;

    [Header("Auto Create")]
    public bool autoCreateTcpObjects = true;

    private readonly object _lock = new object();
    private UdpClient _client;
    private Thread _thread;
    private volatile bool _running;

    private BimanualTcpMessage _pending;
    private bool _hasPending;
    private bool _hasReceived;

    private ForceArrow _rightArrow;
    private ForceArrow _leftArrow;

    /// <summary>是否收到过至少一帧 TCP 数据。</summary>
    public bool HasReceivedData => _hasReceived;

    private void OnEnable()
    {
        EnsureTcpObjects();
        CacheForceArrows();
        _running = true;
        _client = new UdpClient(listenPort);
        _thread = new Thread(ReceiveLoop) { IsBackground = true };
        _thread.Start();
        Debug.Log($"[TCPPoseReceiver] Listening on UDP {listenPort}");
    }

    private void OnDisable()
    {
        _running = false;
        try { _client?.Close(); } catch { }
        if (_thread != null && _thread.IsAlive)
            _thread.Join(250);
    }

    private void Update()
    {
        BimanualTcpMessage msg;
        lock (_lock)
        {
            if (!_hasPending)
                return;
            msg = _pending;
            _pending = null;
            _hasPending = false;
        }

        if (msg.leftTCP != null)
        {
            ApplyPose(leftTCP, msg.leftTCP);
            ApplyForce(ref _leftArrow, leftForce, msg.leftTCP);
        }
        if (msg.rightTCP != null)
        {
            ApplyPose(rightTCP, msg.rightTCP);
            ApplyForce(ref _rightArrow, rightForce, msg.rightTCP);
        }
    }

    private static void ApplyPose(Transform target, TcpPose pose)
    {
        if (target == null || pose == null)
            return;

        if (pose.position != null && pose.position.Length >= 3)
            target.localPosition = new Vector3(
                pose.position[0], pose.position[1], pose.position[2]);

        if (pose.rotation != null && pose.rotation.Length >= 4)
            target.localRotation = new Quaternion(
                pose.rotation[1], pose.rotation[2], pose.rotation[3], pose.rotation[0]);
    }

    private void ApplyForce(ref ForceArrow cachedArrow, Transform forceTransform, TcpPose pose)
    {
        if (pose.force == null || pose.force.Length < 3)
            return;

        if (cachedArrow == null && forceTransform != null)
            cachedArrow = forceTransform.GetComponent<ForceArrow>();

        if (cachedArrow == null)
            return;

        Vector3 forceLocal = new Vector3(
            pose.force[0], pose.force[1], pose.force[2]) * forceDisplayScale;

        cachedArrow.UpdateForce(forceLocal);
    }

    private void CacheForceArrows()
    {
        if (leftForce != null) _leftArrow = leftForce.GetComponent<ForceArrow>();
        if (rightForce != null) _rightArrow = rightForce.GetComponent<ForceArrow>();
    }

    private void ReceiveLoop()
    {
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                byte[] data = _client.Receive(ref endpoint);
                ParsePacket(Encoding.UTF8.GetString(data));
            }
            catch (SocketException)
            {
                if (_running)
                    Debug.LogWarning("[TCPPoseReceiver] UDP receive failed.");
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TCPPoseReceiver] Parse error: {e.Message}");
            }
        }
    }

    private void ParsePacket(string packet)
    {
        BimanualTcpMessage msg = JsonUtility.FromJson<BimanualTcpMessage>(packet);
        if (msg == null)
            return;

        lock (_lock)
        {
            _pending = msg;
            _hasPending = true;
            _hasReceived = true;
        }
    }

    private void EnsureTcpObjects()
    {
        if (!autoCreateTcpObjects)
            return;

        if (leftTCP == null)
        {
            Transform existing = transform.Find("LeftTCP");
            leftTCP = existing != null ? existing : CreateTcpObject("LeftTCP");
        }
        if (rightTCP == null)
        {
            Transform existing = transform.Find("RightTCP");
            rightTCP = existing != null ? existing : CreateTcpObject("RightTCP");
        }
    }

    private Transform CreateTcpObject(string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(transform, false);
        return obj.transform;
    }

    private void OnValidate()
    {
        listenPort = Mathf.Clamp(listenPort, 1024, 65535);
    }
}
