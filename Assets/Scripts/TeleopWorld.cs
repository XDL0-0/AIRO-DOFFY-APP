using UnityEngine;

public class TeleopWorld : MonoBehaviour
{
    public static TeleopWorld Instance { get; private set; }

    [Header("Anchors")]
    [SerializeField] private Transform robotBaseAnchor;
    [SerializeField] private bool createRobotBaseAnchorOnStart = false;

    [Header("Visuals")]
    [SerializeField] private bool showRobotBaseAxes = true;
    [SerializeField] private float axisLength = 0.25f;
    [SerializeField] private float axisRadius = 0.01f;

    private GameObject _axisVisual;
    private bool _axesVisibleByMode = true; // 由 TeleopControlModeManager 控制:坐标轴仅 View 模式显示

    public bool HasRobotBaseAnchor => robotBaseAnchor != null;
    public Transform RobotBaseAnchor => robotBaseAnchor;

    /// <summary>当前坐标轴是否应可见(由模式管理器/校准工具控制)。</summary>
    public bool AxesVisible => _axesVisibleByMode;

    /// <summary>按控制模式显隐坐标轴(Mirror 模式下隐藏)。</summary>
    public void SetAxesVisible(bool visible)
    {
        _axesVisibleByMode = visible;
        RefreshVisual();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (robotBaseAnchor == null && createRobotBaseAnchorOnStart)
            CreateRobotBaseAnchor();

        RefreshVisual();
    }

    private void OnValidate()
    {
        axisLength = Mathf.Max(0.01f, axisLength);
        axisRadius = Mathf.Max(0.001f, axisRadius);
    }

    public Transform CreateRobotBaseAnchor()
    {
        GameObject anchorObject = new GameObject("RobotBaseAnchor");
        anchorObject.transform.SetParent(transform, false);
        robotBaseAnchor = anchorObject.transform;
        RefreshVisual();
        return robotBaseAnchor;
    }

    public void SetRobotBasePose(Vector3 position, Quaternion rotation)
    {
        if (robotBaseAnchor == null)
            CreateRobotBaseAnchor();

        robotBaseAnchor.SetPositionAndRotation(position, FlattenYaw(rotation));
        RefreshVisual();
    }

    public Vector3 WorldPointToRobotBase(Vector3 worldPoint)
    {
        if (robotBaseAnchor == null)
            return worldPoint;

        Vector3 delta = worldPoint - robotBaseAnchor.position;
        return new Vector3(
            Vector3.Dot(delta, robotBaseAnchor.forward),
            Vector3.Dot(delta, robotBaseAnchor.right),
            Vector3.Dot(delta, robotBaseAnchor.up));
    }

    public Vector3 RobotBasePointToWorld(Vector3 robotBasePoint)
    {
        if (robotBaseAnchor == null)
            return robotBasePoint;

        return robotBaseAnchor.position
            + robotBaseAnchor.forward * robotBasePoint.x
            + robotBaseAnchor.right * robotBasePoint.y
            + robotBaseAnchor.up * robotBasePoint.z;
    }

    public Vector3 WorldDeltaToRobotBase(Vector3 worldDelta)
    {
        if (robotBaseAnchor == null)
            return worldDelta;

        return new Vector3(
            Vector3.Dot(worldDelta, robotBaseAnchor.forward),
            Vector3.Dot(worldDelta, robotBaseAnchor.right),
            Vector3.Dot(worldDelta, robotBaseAnchor.up));
    }

    public Vector3 RobotBaseDeltaToWorld(Vector3 robotBaseDelta)
    {
        if (robotBaseAnchor == null)
            return robotBaseDelta;

        return robotBaseAnchor.forward * robotBaseDelta.x
            + robotBaseAnchor.right * robotBaseDelta.y
            + robotBaseAnchor.up * robotBaseDelta.z;
    }

    public Quaternion WorldRotationToRobotBase(Quaternion worldRotation)
    {
        if (robotBaseAnchor == null)
            return worldRotation;

        Vector3 localUp = WorldDeltaToRobotBase(worldRotation * Vector3.up);
        Vector3 localForward = WorldDeltaToRobotBase(worldRotation * Vector3.forward);

        if (localForward.sqrMagnitude < 0.0001f || localUp.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        return Quaternion.LookRotation(localForward.normalized, localUp.normalized);
    }

    public Quaternion RobotBaseRotationToWorld(Quaternion robotBaseRotation)
    {
        if (robotBaseAnchor == null)
            return robotBaseRotation;

        Vector3 worldForward = RobotBaseDeltaToWorld(robotBaseRotation * Vector3.forward);
        Vector3 worldUp = RobotBaseDeltaToWorld(robotBaseRotation * Vector3.up);

        if (worldForward.sqrMagnitude < 0.0001f || worldUp.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        return Quaternion.LookRotation(worldForward.normalized, worldUp.normalized);
    }

    public Vector3 WorldPointToControllerPacket(Vector3 worldPoint)
    {
        return RobotBaseToControllerPacket(WorldPointToRobotBase(worldPoint));
    }

    public Quaternion WorldRotationToControllerPacket(Quaternion worldRotation)
    {
        Quaternion robotRotation = WorldRotationToRobotBase(worldRotation);
        return new Quaternion(
            robotRotation.x,
            -robotRotation.z,
            robotRotation.y,
            robotRotation.w);
    }

    private static Vector3 RobotBaseToControllerPacket(Vector3 robotBasePoint)
    {
        return new Vector3(-robotBasePoint.x, robotBasePoint.z, -robotBasePoint.y);
    }

    private void RefreshVisual()
    {
        if (!showRobotBaseAxes || !_axesVisibleByMode || robotBaseAnchor == null)
        {
            if (_axisVisual != null)
                _axisVisual.SetActive(false);
            return;
        }

        if (_axisVisual == null)
        {
            _axisVisual = new GameObject("RobotBaseAxes");
            _axisVisual.transform.SetParent(robotBaseAnchor, false);

            CreateAxis("X", Vector3.forward, Color.red);
            CreateAxis("Y", Vector3.right, Color.green);
            CreateAxis("Z", Vector3.up, Color.blue);
        }

        _axisVisual.SetActive(true);
    }

    private void CreateAxis(string axisName, Vector3 direction, Color color)
    {
        GameObject axis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        axis.name = axisName + " Axis";
        axis.transform.SetParent(_axisVisual.transform, false);

        axis.transform.localScale = new Vector3(axisRadius, axisLength * 0.5f, axisRadius);
        axis.transform.localPosition = direction.normalized * (axisLength * 0.5f);
        axis.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);

        Renderer renderer = axis.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material = CreateMaterial(color);

        Collider collider = axis.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private static Quaternion FlattenYaw(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
}
