using UnityEngine;

public static class TeleopReferenceFrame
{
    private static bool _isCalibrated;
    private static Quaternion _yawInverse = Quaternion.identity;
    private static Vector3 _worldOrigin = Vector3.zero;

    // 手动校准(CalibrationTool):完整 6DoF 变换,优先级高于自动 yaw 校准
    private static bool _useManual;
    private static Quaternion _manualRotationInverse = Quaternion.identity;
    private static Vector3 _manualOrigin = Vector3.zero;

    public static bool IsCalibrated => _isCalibrated;

    public static bool IsManualCalibrationActive => _useManual;

    /// <summary>
    /// 设置手动校准(由 CalibrationTool 在退出校准模式时调用)。
    /// 语义与 CalibrationTool.GetPosition/GetRotation 一致:
    ///   p' = rotation^-1 * (p - origin)
    ///   q' = rotation^-1 * q
    /// </summary>
    public static void SetManualCalibration(Quaternion rotation, Vector3 origin)
    {
        _manualRotationInverse = Quaternion.Inverse(rotation);
        _manualOrigin = origin;
        _useManual = true;
        _isCalibrated = true;
    }

    /// <summary>清除手动校准,回到自动 yaw 校准(或未校准状态)。</summary>
    public static void ClearManualCalibration()
    {
        _useManual = false;
    }

    public static bool Calibrate()
    {
        Transform head = Camera.main != null ? Camera.main.transform : null;
        Quaternion referenceRotation = head != null ? head.rotation : Quaternion.identity;

        _worldOrigin = head != null ? head.position : Vector3.zero;
        _yawInverse = Quaternion.Inverse(ExtractYaw(referenceRotation));
        _isCalibrated = true;

        // 显式触发自动校准(流启动/跟踪丢失后重校准)时,清除手动校准覆盖,
        // 否则手动校准的优先级会导致自动校准按钮无效
        _useManual = false;

        LogManager.Log("Tracking", "Teleop reference frame calibrated");
        return true;
    }

    public static void Clear()
    {
        _isCalibrated = false;
        _yawInverse = Quaternion.identity;
        _worldOrigin = Vector3.zero;
        _useManual = false;
        _manualRotationInverse = Quaternion.identity;
        _manualOrigin = Vector3.zero;
    }

    public static void TransformControllerPose(ref Vector3 position, ref Quaternion rotation)
    {
        if (!_isCalibrated) return;

        if (_useManual)
        {
            // 手动校准:完整 6DoF,含原点平移
            position = _manualRotationInverse * (position - _manualOrigin);
            rotation = _manualRotationInverse * rotation;
            return;
        }

        position = _yawInverse * position;
        rotation = _yawInverse * rotation;
    }

    public static Vector3 TransformWorldPoint(Vector3 position)
    {
        if (!_isCalibrated) return position;

        if (_useManual)
            return _manualRotationInverse * (position - _manualOrigin);

        return _yawInverse * (position - _worldOrigin);
    }

    public static Quaternion TransformWorldRotation(Quaternion rotation)
    {
        if (!_isCalibrated) return rotation;

        if (_useManual)
            return _manualRotationInverse * rotation;

        return _yawInverse * rotation;
    }

    private static Quaternion ExtractYaw(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
}
