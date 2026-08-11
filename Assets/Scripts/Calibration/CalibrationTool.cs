using UnityEngine;

/// <summary>
/// 交互式坐标系校准工具(移植自 TactAR 的 Calibration.cs)。
///
/// 用途:把虚拟坐标系(Quest 世界空间)手动对齐到真实机器人的 TCP 位置/朝向,
/// 使 AR 可视化(触觉箭头、力箭头、相机画面)与真实实验场景重合。
///
/// 操作方式(与 TactAR 一致):
///   - X + A 同时按下        : 进入/退出校准模式(显示/隐藏坐标 gizmo)
///   - 右扳机按住并移动手柄  : 绕垂直轴旋转坐标 gizmo(yaw)
///   - 左扳机按住并移动手柄  : 平移坐标 gizmo 原点
///   - A / X                 : 缩小 gizmo 外观
///   - B / Y                 : 放大 gizmo 外观
///   - 任意摇杆按下          : 重置到原点
///
/// 校准结果的应用:
///   - 写入手柄姿态发送链路(TeleopReferenceFrame.SetManualCalibration),
///     Mirror 模式下手部姿态自动按校准坐标系变换
///   - 同步到 TeleopWorld.RobotBaseAnchor,View 模式同样生效
///
/// 坐标语义:校准变换 (origin O, rotation R) 将 Quest 世界点映射到校准空间:
///   p' = R^-1 * (p - O) , q' = R^-1 * q
/// </summary>
public class CalibrationTool : MonoBehaviour
{
    public static CalibrationTool instance;

    [Header("References")]
    [Tooltip("坐标系 gizmo 根节点(不赋值则运行时自动创建)")]
    public Transform coord;

    [Header("Gizmo")]
    public bool autoCreateCoord = true;
    [Tooltip("进入校准模式时 gizmo 是否可见")]
    public bool showGizmoWhileCalibrating = true;

    [Header("Input")]
    public bool enableControllerShortcut = true;

    private bool _running;
    private Vector3 _startPositionRight;
    private Vector3 _startPositionLeft;
    private Vector3 _startPositionTransform;
    private CalibrationGizmo _gizmo;

    public bool IsCalibrating => _running;
    public static CalibrationTool Instance => instance;

    private void Awake()
    {
        instance = this;
        _running = false;

        EnsureCoord();
        if (coord != null)
            coord.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>进入/退出校准模式(显示/隐藏坐标 gizmo)。</summary>
    public void SwitchAlign(bool run)
    {
        _running = run;

        TeleopWorld world = TeleopWorld.Instance;
        if (world == null)
            world = FindAnyObjectByType<TeleopWorld>();

        if (run)
        {
            // 从当前 View 模式锚点(机器人底座)状态开始,避免每次从原点重调
            if (world != null && world.RobotBaseAnchor != null)
            {
                transform.SetPositionAndRotation(world.RobotBaseAnchor.position, world.RobotBaseAnchor.rotation);
            }

            // 校准 gizmo 与 View 坐标轴位于同一位置,同时显示会互相遮挡
            // (gizmo 半透明后渲染,盖住 RobotBaseAxes → 看起来坐标"消失")。
            // 校准模式下隐藏 View 坐标轴,退出时按模式恢复。
            if (world != null)
            {
                _axesVisibleBeforeCalibration = world.AxesVisible;
                world.SetAxesVisible(false);
            }
        }

        if (coord != null)
        {
            coord.gameObject.SetActive(run && showGizmoWhileCalibrating);
            if (_gizmo != null)
                _gizmo.EnsureVisuals();
        }

        // 退出校准时把结果应用到姿态发送链路与 View 模式锚点,并恢复坐标轴
        if (!run)
        {
            ApplyCalibration();
            if (world != null)
                world.SetAxesVisible(_axesVisibleBeforeCalibration);
        }
    }

    private bool _axesVisibleBeforeCalibration = true;

    private void Update()
    {
        // 校准模式切换必须在校准分支之前处理,否则退出模式后无法再进入
        HandleToggleShortcut();

        if (!_running) return;

        // ---- 旋转:右扳机(仅 yaw,与 TactAR 一致)----
        if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
        {
            _startPositionRight = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        }
        if (OVRInput.Get(OVRInput.RawButton.RIndexTrigger))
        {
            Vector3 offset = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch) - _startPositionRight;
            offset.y = 0;
            if (offset.sqrMagnitude > 0.0001f)
                transform.LookAt(transform.position + offset);
        }

        // ---- 平移:左扳机 ----
        if (OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger))
        {
            _startPositionTransform = transform.position;
            _startPositionLeft = OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch);
        }
        if (OVRInput.Get(OVRInput.RawButton.LIndexTrigger))
        {
            Vector3 offset = OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch) - _startPositionLeft;
            transform.position = _startPositionTransform + offset;
        }

        // ---- 缩放 gizmo 外观:A/X 缩小,B/Y 放大 ----
        if (_gizmo != null)
        {
            if (OVRInput.GetDown(OVRInput.RawButton.A) || OVRInput.GetDown(OVRInput.RawButton.X))
                _gizmo.SetVisualScale(_gizmo.VisualScale - 0.1f);
            if (OVRInput.GetDown(OVRInput.RawButton.B) || OVRInput.GetDown(OVRInput.RawButton.Y))
                _gizmo.SetVisualScale(_gizmo.VisualScale + 0.1f);
        }

        // ---- 重置已禁用 ----
    }

    /// <summary>把当前校准变换应用到本地帧链(TeleopReferenceFrame + TeleopWorld 锚点)。</summary>
    public void ApplyCalibration()
    {
        TeleopReferenceFrame.SetManualCalibration(transform.rotation, transform.position);

        TeleopWorld world = TeleopWorld.Instance;
        if (world == null)
            world = FindAnyObjectByType<TeleopWorld>();
        if (world == null)
            return;

        Transform anchor = world.RobotBaseAnchor;
        if (anchor == null)
            anchor = world.CreateRobotBaseAnchor();

        anchor.SetPositionAndRotation(transform.position, transform.rotation);
    }

    // ---- 世界坐标 -> 校准空间坐标(与 TactAR 语义一致) ----
    /// <summary>将世界位置转换到校准空间。</summary>
    public Vector3 GetPosition(Vector3 worldPosition)
    {
        return Quaternion.Inverse(transform.rotation) * (worldPosition - transform.position);
    }

    /// <summary>将世界欧拉角转换到校准空间。</summary>
    public Vector3 GetEuler(Vector3 worldEuler)
    {
        return (Quaternion.Inverse(transform.rotation) * Quaternion.Euler(worldEuler)).eulerAngles;
    }

    /// <summary>将世界四元数转换到校准空间。</summary>
    public Quaternion GetRotation(Quaternion worldRotation)
    {
        return Quaternion.Inverse(transform.rotation) * worldRotation;
    }

    // ---- 输入:校准模式切换(X + A 同时按下,带 500ms 防抖) ----
    private float _toggleCooldownUntil;

    private void HandleToggleShortcut()
    {
        if (!enableControllerShortcut) return;
        if (Time.time < _toggleCooldownUntil) return;

        bool xPressed = OVRInput.Get(OVRInput.RawButton.X);
        bool aPressed = OVRInput.Get(OVRInput.RawButton.A);
        bool xDown = OVRInput.GetDown(OVRInput.RawButton.X);
        bool aDown = OVRInput.GetDown(OVRInput.RawButton.A);

        bool chord = (xDown && aPressed) || (aDown && xPressed);
        if (!chord) return;

        _toggleCooldownUntil = Time.time + 0.5f;
        SwitchAlign(!_running);
    }

    private void EnsureCoord()
    {
        if (coord == null)
        {
            Transform existing = transform.Find("Coord");
            if (existing != null)
            {
                coord = existing;
            }
            else if (autoCreateCoord)
            {
                GameObject coordObject = new GameObject("Coord");
                coordObject.transform.SetParent(transform, false);
                coord = coordObject.transform;
            }
        }

        if (coord != null && _gizmo == null)
        {
            _gizmo = coord.GetComponent<CalibrationGizmo>();
            if (_gizmo == null)
                _gizmo = coord.gameObject.AddComponent<CalibrationGizmo>();
        }
    }
}
