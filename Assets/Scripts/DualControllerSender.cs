using System.Diagnostics;
using UnityEngine;
using TMPro;

/// <summary>
/// 在同一个 Update() 中同时采集左右手 Controller 数据，
/// 打上高精度单调时间戳后通过 UDP 发送给下位机。
///
/// 格式: C,<frameId>,<timestamp_ns>,<左手数据>,<右手数据>
/// 左/右手数据字段顺序:
///   px,py,pz, rx,ry,rz,rw, jx,jy, trigger, grip, buttonAX, buttonBY, joystickPress
///
/// 改动一览:
///   - Time.time → Stopwatch 纳秒级单调时钟
///   - 集成 AppManager 全局串流开关
///   - 附加 frameId 方便 Python 端匹配 / 丢帧检测
///   - 固定 100 Hz 发送:按单调时钟节流,帧率不足时单帧补发(上限 3 包)
/// </summary>
public class DualControllerSender : MonoBehaviour
{
    [Header("UDP")]
    [SerializeField] private UdpSocket udpSocket;

    [Header("发送开关")]
    [SerializeField] private bool stateSending = true;
    [SerializeField] private TextMeshProUGUI sendingStateText;

    // Stopwatch 高精度时钟
    private static readonly double TicksToNs = 1_000_000_000.0 / Stopwatch.Frequency;

    private const ulong SendIntervalNs = 10_000_000;    // 100 Hz 固定发送间隔(10ms)
    private const int    MaxSendPerFrame = 3;           // 掉帧时单帧最多补发 3 包,丢弃过期数据
    private ulong _nextSendNs;                          // 下一次发送的计划时刻(单调时钟)

    private uint _frameId;

    public void ToggleSending()
    {
        stateSending = !stateSending;
        UpdateSendingUI();
    }

    public void SetSendingEnabled(bool enabled)
    {
        stateSending = enabled;
        UpdateSendingUI();
    }

    private void Start()
    {
        if (udpSocket == null)
            udpSocket = FindAnyObjectByType<UdpSocket>();
        _nextSendNs = GetMonotonicNs();
    }

    private void Update()
    {
        if (udpSocket == null || !stateSending) return;
        if (AppManager.Instance != null && !AppManager.Instance.CanSendTeleopData) return;

        ulong nowNs = GetMonotonicNs();
        if (nowNs < _nextSendNs) return; // 未到发送时刻,本帧跳过

        // 积压超过 30ms(长暂停后恢复等):直接对齐到当前时刻,不补发过期数据
        if (nowNs - _nextSendNs >= SendIntervalNs * MaxSendPerFrame)
        {
            _nextSendNs = nowNs + SendIntervalNs;
            return;
        }

        // 每帧只采样一次(OVRInput 同帧内数据不变,补发包复用同一份数据)
        string leftData  = SampleController(OVRInput.Controller.LTouch);
        string rightData = SampleController(OVRInput.Controller.RTouch);

        // 固定 100 Hz:帧率不足 100fps 时按计划时刻补发,维持平均速率
        int sent = 0;
        do
        {
            _frameId++;
            // 时间戳用计划发送时刻:相邻包严格间隔 10ms,方便 Python 端丢帧检测
            string packet = $"C,{_frameId},{_nextSendNs},{leftData},{rightData}";
            udpSocket.SendData8001(packet);
            _nextSendNs += SendIntervalNs;
        } while (++sent < MaxSendPerFrame && _nextSendNs <= nowNs);

        // 积压超过 30ms:丢弃过期帧,重新对齐到当前时刻,避免突发大量旧数据
        if (_nextSendNs <= nowNs)
            _nextSendNs = nowNs + SendIntervalNs;
    }

    /// <summary>采集单侧 Controller 所有字段。</summary>
    private static string SampleController(OVRInput.Controller ctrl)
    {
        Vector3    pos  = OVRInput.GetLocalControllerPosition(ctrl);
        Quaternion rot  = OVRInput.GetLocalControllerRotation(ctrl);
        TeleopControlModeManager.TransformControllerPose(ctrl, ref pos, ref rot);

        Vector2    joy  = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, ctrl);
        float      trig = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, ctrl);
        int grip     = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, ctrl) ? 1 : 0;
        int buttonAX = OVRInput.Get(OVRInput.Button.One,               ctrl) ? 1 : 0;
        int buttonBY = OVRInput.Get(OVRInput.Button.Two,               ctrl) ? 1 : 0;
        int joyPress = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, ctrl) ? 1 : 0;

        return $"{pos.x:F6},{pos.y:F6},{pos.z:F6}," +
               $"{rot.x:F6},{rot.y:F6},{rot.z:F6},{rot.w:F6}," +
               $"{joy.x:F6},{joy.y:F6}," +
               $"{trig:F6},{grip},{buttonAX},{buttonBY},{joyPress}";
    }

    private static ulong GetMonotonicNs()
    {
        return (ulong)(Stopwatch.GetTimestamp() * TicksToNs);
    }

    private void UpdateSendingUI()
    {
        if (sendingStateText != null)
            sendingStateText.text = stateSending
                ? "Posture Transmission\r\nON"
                : "Posture Transmission\r\nOFF";
    }
}
