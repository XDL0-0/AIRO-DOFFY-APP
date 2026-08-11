using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

/// <summary>
/// 手部关节追踪 UDP 发送器 —— 基于 OVRHand + OVRSkeleton API。
///
/// 适用于使用 OVRHand / OVRSkeleton 组件的 Quest 手部追踪项目。
/// 如果你的 GameObject 上挂载的是 OVR Hand (Script) + OVR Skeleton (Script)，
/// 而非 Interaction SDK 的 Hand (IHand) 组件，请使用本脚本。
///
/// 挂载方式:
///   创建空 GameObject，挂载本脚本，在 Inspector 中手动拖入：
///     - leftOVRHand / rightOVRHand   (OVRHand 组件)
///     - leftSkeleton / rightSkeleton (OVRSkeleton 组件)
///   以及 UdpSocket。
///
/// 骨骼顺序 (24 bones, OVRSkeleton.BoneId):
///   0  WristRoot
///   1  ForearmStub
///   2  Thumb0      3  Thumb1      4  Thumb2      5  Thumb3
///   6  Index1      7  Index2      8  Index3
///   9  Middle1    10  Middle2    11  Middle3
///  12  Ring1      13  Ring2      14  Ring3
///  15  Pinky0     16  Pinky1     17  Pinky2     18  Pinky3
///  19  ThumbTip   20  IndexTip   21  MiddleTip  22  RingTip  23  PinkyTip
///
/// 文本协议:
///   H,&lt;side&gt;,&lt;frameId&gt;,&lt;timestamp_ns&gt;,
///     wpx,wpy,wpz,wrx,wry,wrz,wrw,          ← 手腕位姿 (pos + quat)
///     b0x,b0y,b0z, b1x,b1y,b1z, ...         ← 24 个骨骼世界坐标
///
/// 二进制协议:
///   头部 8 字节: [0]='H', [1]=side, [2..3]=bone_count(uint16 LE), [4..7]=frameId(uint32 LE)
///   之后每个骨骼 12 字节: float x, float y, float z (little-endian)
/// </summary>
public class HandTrackingSender : MonoBehaviour
{
    // ================================================================
    // Inspector 参数
    // ================================================================
    [Header("UDP")]
    [SerializeField] private UdpSocket udpSocket;

    [Header("发送开关")]
    [SerializeField] private bool stateSending = true;

    [Header("OVRHand 引用")]
    [Tooltip("左手 OVRHand 组件")]
    [SerializeField] private OVRHand leftOVRHand;
    [Tooltip("右手 OVRHand 组件")]
    [SerializeField] private OVRHand rightOVRHand;

    [Header("OVRSkeleton 引用")]
    [Tooltip("左手 OVRSkeleton 组件")]
    [SerializeField] private OVRSkeleton leftSkeleton;
    [Tooltip("右手 OVRSkeleton 组件")]
    [SerializeField] private OVRSkeleton rightSkeleton;

    [Header("发送设置")]
    [Tooltip("每秒发送帧数上限（0 = 每帧都发）")]
    [SerializeField] private float sendRateHz = 30f;

    [Tooltip("true = 二进制协议, false = 文本协议")]
    [SerializeField] private bool useBinaryProtocol = true;
    
    [Tooltip("只在手部被追踪时才发送")]
    [SerializeField] private bool onlySendWhenTracked = true;

    [Tooltip("持握 Controller 时跳过对应手的 Hand 数据（自动检测）")]
    [SerializeField] private bool skipWhenControllerActive = true;

    // ================================================================
    // 私有字段
    // ================================================================
    private float _sendInterval;
    private float _sendTimer;

    private const int BONE_COUNT      = 26;
    private const int BYTES_PER_BONE  = 12;   // 3 × float32
    private const int HEADER_BYTES    = 8;
    private readonly byte[] _binaryBuf = new byte[HEADER_BYTES + BONE_COUNT * BYTES_PER_BONE];

    private readonly StringBuilder _sb = new StringBuilder(1024);

    private static readonly double TicksToNs = 1_000_000_000.0 / Stopwatch.Frequency;
    private uint _frameId;

    // ================================================================
    void Start()
    {
        _sendInterval = sendRateHz > 0f ? 1f / sendRateHz : 0f;

        if (udpSocket == null)
            udpSocket = FindAnyObjectByType<UdpSocket>();

        if (leftOVRHand  == null) UnityEngine.Debug.LogWarning("[HandTrackingSender] 左手 OVRHand 未设置");
        if (rightOVRHand == null) UnityEngine.Debug.LogWarning("[HandTrackingSender] 右手 OVRHand 未设置");
        if (leftSkeleton  == null) UnityEngine.Debug.LogWarning("[HandTrackingSender] 左手 OVRSkeleton 未设置");
        if (rightSkeleton == null) UnityEngine.Debug.LogWarning("[HandTrackingSender] 右手 OVRSkeleton 未设置");
    }

    public void SetSendingEnabled(bool enabled) => stateSending = enabled;

    // ================================================================
    void Update()
    {
        if (udpSocket == null || !stateSending) return;
        if (AppManager.Instance != null && !AppManager.Instance.CanSendTeleopData) return;

        if (_sendInterval > 0f)
        {
            _sendTimer += Time.deltaTime;
            if (_sendTimer < _sendInterval) return;
            _sendTimer = 0f;
        }

        if (leftOVRHand != null && leftSkeleton != null)
            SendHandData(leftOVRHand, leftSkeleton, 'L', OVRInput.Controller.LTouch);
        if (rightOVRHand != null && rightSkeleton != null)
            SendHandData(rightOVRHand, rightSkeleton, 'R', OVRInput.Controller.RTouch);
    }

    // ================================================================
    private void SendHandData(OVRHand hand, OVRSkeleton skeleton, char side, OVRInput.Controller controller)
    {
        if (onlySendWhenTracked && !hand.IsTracked) return;

        // 当对应侧 controller 处于活跃状态时，跳过 hand 数据发送
        if (skipWhenControllerActive && OVRInput.IsControllerConnected(controller)) return;

        IList<OVRBone> bones = skeleton.Bones;

        if (bones == null || bones.Count < BONE_COUNT) return;

        _frameId++;
        ulong tsNs = GetMonotonicNs();

        Transform wristTf = bones[0].Transform;
        Pose wrist = new Pose(
            TeleopReferenceFrame.TransformWorldPoint(wristTf.position),
            TeleopReferenceFrame.TransformWorldRotation(wristTf.rotation));

        if (useBinaryProtocol)
            SendBinary(bones, side);
        else
            SendText(bones, wrist, side, tsNs);
    }

    // ================================================================
    // 文本协议
    // ================================================================
    private void SendText(IList<OVRBone> bones, Pose wrist, char side, ulong tsNs)
    {
        _sb.Clear();
        _sb.Append("H,").Append(side)
           .Append(',').Append(_frameId)
           .Append(',').Append(tsNs);

        AppendVector3(_sb, wrist.position);
        AppendQuaternion(_sb, wrist.rotation);

        int count = Math.Min(bones.Count, BONE_COUNT);
        for (int i = 0; i < count; i++)
        {
            AppendVector3(_sb, TeleopReferenceFrame.TransformWorldPoint(bones[i].Transform.position));
        }
        _sb.Append('\n');

        udpSocket.SendData8001(_sb.ToString());
    }

    // ================================================================
    // 二进制协议
    // [0]='H', [1]=side, [2..3]=boneCount(LE), [4..7]=frameId(LE), [8..] bones
    // ================================================================
    private void SendBinary(IList<OVRBone> bones, char side)
    {
        int count = Math.Min(bones.Count, BONE_COUNT);

        _binaryBuf[0] = 0x48; // 'H'
        _binaryBuf[1] = (byte)side;
        _binaryBuf[2] = (byte)(count & 0xFF);
        _binaryBuf[3] = (byte)((count >> 8) & 0xFF);
        WriteUInt32LE(_binaryBuf, 4, _frameId);

        int offset = HEADER_BYTES;
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = TeleopReferenceFrame.TransformWorldPoint(bones[i].Transform.position);
            WriteFloat(_binaryBuf, offset,     pos.x);
            WriteFloat(_binaryBuf, offset + 4, pos.y);
            WriteFloat(_binaryBuf, offset + 8, pos.z);
            offset += BYTES_PER_BONE;
        }

        int totalBytes = HEADER_BYTES + count * BYTES_PER_BONE;
        string encoded = Convert.ToBase64String(_binaryBuf, 0, totalBytes);
        udpSocket.SendData8001($"HB,{encoded}");
    }

    // ================================================================
    // 辅助方法
    // ================================================================
    private static ulong GetMonotonicNs()
    {
        return (ulong)(Stopwatch.GetTimestamp() * TicksToNs);
    }

    private static void AppendVector3(StringBuilder sb, Vector3 v)
    {
        sb.Append(',').Append(v.x.ToString("F4"))
          .Append(',').Append(v.y.ToString("F4"))
          .Append(',').Append(v.z.ToString("F4"));
    }

    private static void AppendQuaternion(StringBuilder sb, Quaternion q)
    {
        sb.Append(',').Append(q.x.ToString("F3"))
          .Append(',').Append(q.y.ToString("F3"))
          .Append(',').Append(q.z.ToString("F3"))
          .Append(',').Append(q.w.ToString("F3"));
    }

    private static void WriteFloat(byte[] buf, int offset, float value)
    {
        byte[] b = BitConverter.GetBytes(value);
        buf[offset]     = b[0];
        buf[offset + 1] = b[1];
        buf[offset + 2] = b[2];
        buf[offset + 3] = b[3];
    }

    private static void WriteUInt32LE(byte[] buf, int offset, uint value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
