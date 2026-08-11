using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// 通过 UDP 接收力/力矩传感器数据并更新力箭头(移植自 TactAR 的 ServerForce/UpdateForce)。
///
/// 数据格式(默认端口 8012,原 tactile 口,工作站改发 6D 力):
///   二进制 6×int32 小端(24 bytes):Fx Fy Fz Mx My Mz
///   显示长度 = raw × forceSensitivity(米),与原 tactile 的 0.0001 缩放一致
///   力矩 M 暂不显示(预留)
///
/// 备选格式(TactAR JSON,listenPort 改回 8014 时):
/// {
///   "device_id": "left",                 // "left" 或 "right"
///   "arrow": {
///     "start": [x,y,z],                  // 力的起点(传感器安装位置,局部坐标)
///     "end":   [x,y,z]                   // 力的终点(方向 + 大小)
///   },
///   "scale": [headScale, shaftRadius, 0] // 箭头头部球缩放 / 杆身半径
/// }
///
/// 力箭头挂在对应 TCP 变换下(随末端运动);不配置时自动在 LeftTCP/RightTCP 下创建。
/// </summary>
public class ForceSensorReceiver : MonoBehaviour
{
    [Serializable]
    public class Arrow
    {
        public float[] start;
        public float[] end;
    }

    [Serializable]
    public class ForceSensorMessage
    {
        public string device_id;
        public Arrow arrow;
        public float[] scale;
    }

    [Header("UDP")]
    [Tooltip("监听端口。默认 8012(原 tactile 口,工作站发 6D 力二进制);用 TactAR JSON 格式时改回 8014")]
    [SerializeField] private int listenPort = 8012;

    [Header("6D Force (binary, 8012)")]
    [Tooltip("int32 缩放:显示长度(米) = raw × forceSensitivity。建议 0.00001, 即 raw=100000 → 1 米箭头")]
    public float forceSensitivity = 0.00001f;
    [Tooltip("6D 力显示到哪个箭头(6D 数据不含左右标识)")]
    public ForceTarget forceTarget = ForceTarget.Right;

    public enum ForceTarget { Left, Right, Both }

    [Header("References")]
    [Tooltip("左臂力箭头挂载点(不赋值则自动创建)")]
    public Transform leftForce;
    [Tooltip("右臂力箭头挂载点(不赋值则自动创建)")]
    public Transform rightForce;

    [Header("TCP 挂载")]
    [Tooltip("左臂 TCP(取 TCPPoseReceiver 的,不赋值则自动查找/创建)")]
    public Transform leftTCP;
    [Tooltip("右臂 TCP(取 TCPPoseReceiver 的,不赋值则自动查找/创建)")]
    public Transform rightTCP;

    private readonly object _lock = new object();
    private UdpClient _client;
    private Thread _thread;
    private volatile bool _running;

    private ForceSensorMessage _pending;
    private bool _hasPending;

    /// <summary>6D 力(二进制):force 已乘 forceSensitivity,torque 预留。</summary>
    private struct Force6D
    {
        public Vector3 force;
        public Vector3 torque;
    }

    private Force6D _pending6d;
    private bool _hasPending6d;

    private ForceArrow _leftArrow;
    private ForceArrow _rightArrow;

    private void OnEnable()
    {
        EnsureReferences();
        _running = true;
        try
        {
            _client = new UdpClient(listenPort);
        }
        catch (SocketException e)
        {
            Debug.LogError($"[ForceSensorReceiver] Failed to bind UDP {listenPort}: {e.Message}" +
                           " — 检查 UdpWindowManager.receiveTactile 是否已关闭");
            _running = false;
            return;
        }
        _thread = new Thread(ReceiveLoop) { IsBackground = true };
        _thread.Start();
        Debug.Log($"[ForceSensorReceiver] Listening on UDP {listenPort}");
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
        // ---- 6D 力(二进制 8012):线性力部分驱动箭头 ----
        bool has6d;
        Force6D f6d;
        lock (_lock)
        {
            has6d = _hasPending6d;
            f6d = _pending6d;
            _pending6d = default;
            _hasPending6d = false;
        }
        if (has6d)
        {
            if (forceTarget == ForceTarget.Left || forceTarget == ForceTarget.Both)
            {
                ForceArrow fArrow = EnsureArrow(ref _leftArrow, leftForce, "LeftForce");
                if (fArrow != null) fArrow.UpdateForce(f6d.force);
            }
            if (forceTarget == ForceTarget.Right || forceTarget == ForceTarget.Both)
            {
                ForceArrow fArrow = EnsureArrow(ref _rightArrow, rightForce, "RightForce");
                if (fArrow != null) fArrow.UpdateForce(f6d.force);
            }
        }

        // ---- TactAR JSON(备选)----
        ForceSensorMessage msg;
        lock (_lock)
        {
            if (!_hasPending)
                return;
            msg = _pending;
            _pending = null;
            _hasPending = false;
        }

        ForceArrow arrow = ResolveArrow(msg.device_id);
        if (arrow == null)
            return;

        Vector3 start = Vector3.zero;
        Vector3 end = Vector3.zero;
        if (msg.arrow != null)
        {
            if (msg.arrow.start != null && msg.arrow.start.Length >= 3)
                start = new Vector3(msg.arrow.start[0], msg.arrow.start[1], msg.arrow.start[2]);
            if (msg.arrow.end != null && msg.arrow.end.Length >= 3)
                end = new Vector3(msg.arrow.end[0], msg.arrow.end[1], msg.arrow.end[2]);
        }

        Vector3 fLocal = end - start;

        arrow.UpdateForce(fLocal);
    }

    private ForceArrow ResolveArrow(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return null;

        if (deviceId.Contains("left", StringComparison.OrdinalIgnoreCase))
            return EnsureArrow(ref _leftArrow, leftForce, "LeftForce");
        if (deviceId.Contains("right", StringComparison.OrdinalIgnoreCase))
            return EnsureArrow(ref _rightArrow, rightForce, "RightForce");

        return null;
    }

    private ForceArrow EnsureArrow(ref ForceArrow arrow, Transform mount, string defaultName)
    {
        if (arrow != null)
            return arrow;

        Transform root = mount != null ? mount : transform.Find(defaultName);
        if (root == null)
        {
            GameObject obj = new GameObject(defaultName);
            obj.transform.SetParent(transform, false);
            root = obj.transform;
        }

        arrow = root.GetComponent<ForceArrow>();
        if (arrow == null)
            arrow = root.gameObject.AddComponent<ForceArrow>();

        return arrow;
    }

    private void EnsureReferences()
    {
        TCPPoseReceiver receiver = FindAnyObjectByType<TCPPoseReceiver>();

        if (leftTCP == null)
            leftTCP = receiver != null ? receiver.leftTCP : null;
        if (rightTCP == null)
            rightTCP = receiver != null ? receiver.rightTCP : null;

        if (leftForce == null && leftTCP != null)
        {
            Transform existing = leftTCP.Find("LeftForce");
            leftForce = existing != null ? existing : CreateMount(leftTCP, "LeftForce");
        }
        if (rightForce == null && rightTCP != null)
        {
            Transform existing = rightTCP.Find("RightForce");
            rightForce = existing != null ? existing : CreateMount(rightTCP, "RightForce");
        }
    }

    private static Transform CreateMount(Transform parent, string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        return obj.transform;
    }

    private void ReceiveLoop()
    {
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                byte[] data = _client.Receive(ref endpoint);
                ParsePacket(data);
            }
            catch (SocketException)
            {
                if (_running)
                    Debug.LogWarning("[ForceSensorReceiver] UDP receive failed.");
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ForceSensorReceiver] Parse error: {e.Message}");
            }
        }
    }

    private void ParsePacket(byte[] data)
    {
        // ---- 二进制 6D 力:6×int32 小端(24 bytes),Fx Fy Fz Mx My Mz ----
        if (data.Length >= 6 * 4 && data[0] != (byte)'{')
        {
            Force6D f;
            f.force = new Vector3(
                BitConverter.ToInt32(data, 0),
                BitConverter.ToInt32(data, 4),
                BitConverter.ToInt32(data, 8)) * forceSensitivity;
            f.torque = new Vector3(
                BitConverter.ToInt32(data, 12),
                BitConverter.ToInt32(data, 16),
                BitConverter.ToInt32(data, 20)) * forceSensitivity;

            lock (_lock)
            {
                _pending6d = f;
                _hasPending6d = true;
            }
            return;
        }

        // ---- TactAR JSON 格式(备选)----
        ForceSensorMessage msg = JsonUtility.FromJson<ForceSensorMessage>(Encoding.UTF8.GetString(data));
        if (msg == null)
            return;

        lock (_lock)
        {
            _pending = msg;
            _hasPending = true;
        }
    }

    private void OnValidate()
    {
        listenPort = Mathf.Clamp(listenPort, 1024, 65535);
    }
}
