using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Doffy.Protocol;
using UnityEngine;

/// <summary>Sample Meta hand skeletons and send classic H/HB packets through the session gate.</summary>
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
    private readonly Vector3[] _bonePositions = new Vector3[BONE_COUNT];

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
        if (hand == null || skeleton == null) return;
        if (onlySendWhenTracked && !hand.IsTracked) return;

        // 当对应侧 controller 处于活跃状态时，跳过 hand 数据发送
        if (skipWhenControllerActive && OVRInput.IsControllerConnected(controller)) return;

        IList<OVRBone> bones = skeleton.Bones;

        if (bones == null || bones.Count < BONE_COUNT) return;

        // Never emit a prefix of the skeleton. Python accepts only complete
        // 26-bone H/HB frames, so validate and transform every wire bone before
        // touching either the text builder or the shared binary buffer.
        for (int i = 0; i < BONE_COUNT; i++)
        {
            OVRBone bone = bones[i];
            if (bone == null || bone.Transform == null) return;

            Vector3 position = TeleopReferenceFrame.TransformWorldPoint(bone.Transform.position);
            if (!IsFinite(position)) return;
            _bonePositions[i] = position;
        }

        Pose wrist = default(Pose);
        ulong tsNs = 0;
        if (!useBinaryProtocol)
        {
            Transform wristTf = bones[0].Transform;
            Quaternion wristRotation = TeleopReferenceFrame.TransformWorldRotation(wristTf.rotation);
            if (!IsUsable(wristRotation)) return;
            wrist = new Pose(_bonePositions[0], wristRotation);
            tsNs = GetMonotonicNs();
        }

        _frameId++;
        if (useBinaryProtocol)
            SendBinary(side);
        else
            SendText(_bonePositions, wrist, side, tsNs);
    }

    // ================================================================
    // 文本协议
    // ================================================================
    private void SendText(Vector3[] bonePositions, Pose wrist, char side, ulong tsNs)
    {
        _sb.Clear();
        _sb.Append("H,").Append(side)
           .Append(',').Append(_frameId)
           .Append(',').Append(tsNs);

        AppendVector3(_sb, wrist.position);
        AppendQuaternion(_sb, wrist.rotation);

        for (int i = 0; i < BONE_COUNT; i++)
            AppendVector3(_sb, bonePositions[i]);
        _sb.Append('\n');

        udpSocket.SendData8001(_sb.ToString());
    }

    // ================================================================
    // 二进制协议
    // [0]='H', [1]=side, [2..3]=boneCount(LE), [4..7]=frameId(LE), [8..] bones
    // ================================================================
    private void SendBinary(char side)
    {
        _binaryBuf[0] = 0x48; // 'H'
        _binaryBuf[1] = (byte)side;
        _binaryBuf[2] = (byte)(BONE_COUNT & 0xFF);
        _binaryBuf[3] = (byte)((BONE_COUNT >> 8) & 0xFF);
        WriteUInt32LE(_binaryBuf, 4, _frameId);

        int offset = HEADER_BYTES;
        for (int i = 0; i < BONE_COUNT; i++)
        {
            Vector3 pos = _bonePositions[i];
            WriteFloat(_binaryBuf, offset,     pos.x);
            WriteFloat(_binaryBuf, offset + 4, pos.y);
            WriteFloat(_binaryBuf, offset + 8, pos.z);
            offset += BYTES_PER_BONE;
        }

        int totalBytes = HEADER_BYTES + BONE_COUNT * BYTES_PER_BONE;
        string encoded = Convert.ToBase64String(_binaryBuf, 0, totalBytes);
        udpSocket.SendData8001($"HB,{encoded}");
    }

    private static bool IsFinite(Vector3 value) =>
        IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsUsable(Quaternion value) =>
        IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w) &&
        (Math.Abs(value.x) + Math.Abs(value.y) + Math.Abs(value.z) + Math.Abs(value.w) > 0.000001f);

    // ================================================================
    // 辅助方法
    // ================================================================
    private static ulong GetMonotonicNs()
    {
        return (ulong)(Stopwatch.GetTimestamp() * TicksToNs);
    }

    private static void AppendVector3(StringBuilder sb, Vector3 v)
    {
        TeleopWireFormat.AppendFloat(sb, v.x, "F4");
        TeleopWireFormat.AppendFloat(sb, v.y, "F4");
        TeleopWireFormat.AppendFloat(sb, v.z, "F4");
    }

    private static void AppendQuaternion(StringBuilder sb, Quaternion q)
    {
        TeleopWireFormat.AppendFloat(sb, q.x, "F3");
        TeleopWireFormat.AppendFloat(sb, q.y, "F3");
        TeleopWireFormat.AppendFloat(sb, q.z, "F3");
        TeleopWireFormat.AppendFloat(sb, q.w, "F3");
    }

    private static void WriteFloat(byte[] buf, int offset, float value) =>
        TeleopWireFormat.WriteFloatLittleEndian(buf, offset, value);

    private static void WriteUInt32LE(byte[] buf, int offset, uint value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
