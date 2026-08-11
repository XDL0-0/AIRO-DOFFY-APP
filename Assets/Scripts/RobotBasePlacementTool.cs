using UnityEngine;

public class RobotBasePlacementTool : MonoBehaviour
{
    private static RobotBasePlacementTool _activeTool;
    private static int _suppressRightThumbstickResetFrame = -1;

    private enum CalibrationStep
    {
        SetPosition,
        SetDirection,
        Locked
    }

    [Header("References")]
    [SerializeField] private TeleopWorld teleopWorld;
    [SerializeField] private Transform trackingSpace;

    [Header("Input")]
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;
    [SerializeField] private bool enableControllerConfirm = true;
    [SerializeField] private OVRInput.Button placeButton = OVRInput.Button.PrimaryThumbstick;
    [SerializeField] private float confirmBlockedByTriggerThreshold = 0.8f;
    [SerializeField] private OVRInput.Axis2D yawAdjustAxis = OVRInput.Axis2D.PrimaryThumbstick;

    [Header("Touch Calibration")]
    // 预览坐标系沿手柄朝向(局部 +Z)前移 0.25m,便于把坐标原点对准真实机器人底座
    [SerializeField] private Vector3 positionProbeLocalOffset = new Vector3(0f, 0f, 0.25f);
    [SerializeField] private bool alignYawToController = true;
    [SerializeField] private bool lockAfterPlacement = true;
    [SerializeField] private float yawAdjustDegreesPerSecond = 90f;
    [SerializeField] private float yawAdjustDeadZone = 0.2f;

    [Header("Visuals")]
    [SerializeField] private bool showPreview = true;
    [SerializeField] private float previewAxisLength = 0.18f;
    [SerializeField] private float previewAxisRadius = 0.008f;

    private Transform _previewRoot;
    private Pose _candidatePose;
    private bool _hasCandidate;
    private float _manualYawOffsetDegrees;
    private Vector3 _capturedBasePosition;
    private CalibrationStep _calibrationStep = CalibrationStep.SetPosition;

    public bool IsPlacementEditing => isActiveAndEnabled && _calibrationStep != CalibrationStep.Locked;

    public static bool ShouldSuppressRightThumbstickReset
    {
        get
        {
            bool confirmedThisFrame = _suppressRightThumbstickResetFrame == Time.frameCount;
            return confirmedThisFrame;
        }
    }

    private void Awake()
    {
        if (teleopWorld == null)
            teleopWorld = FindAnyObjectByType<TeleopWorld>();

        if (teleopWorld == null)
            teleopWorld = new GameObject("TeleopWorld").AddComponent<TeleopWorld>();

        if (trackingSpace == null)
        {
            OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null)
                trackingSpace = cameraRig.trackingSpace;
        }
    }

    private void OnEnable()
    {
        _activeTool = this;
    }

    private void OnDisable()
    {
        if (_activeTool == this)
            _activeTool = null;

        HideVisuals();
    }

    private void Start()
    {
        if (showPreview)
            CreatePreview();
    }

    private void Update()
    {
        if (TeleopControlModeManager.CurrentMode != TeleopControlModeManager.ControlMode.View)
        {
            HideVisuals();
            return;
        }

        if (_calibrationStep == CalibrationStep.Locked)
        {
            HideVisuals();
            return;
        }

        UpdateManualYawOffset();
        UpdateCandidate();
        UpdateVisuals();

        if (enableControllerConfirm &&
            _hasCandidate &&
            OVRInput.GetDown(placeButton, controller) &&
            OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) < confirmBlockedByTriggerThreshold)
        {
            ConfirmPlacement();
        }
    }

    public void BeginPlacementEdit()
    {
        _calibrationStep = CalibrationStep.SetPosition;
        _manualYawOffsetDegrees = 0f;
        _hasCandidate = false;
    }

    public void ConfirmPlacement()
    {
        if (!_hasCandidate)
            return;

        if (_calibrationStep == CalibrationStep.SetPosition)
        {
            _capturedBasePosition = _candidatePose.position;
            _calibrationStep = CalibrationStep.SetDirection;
            _suppressRightThumbstickResetFrame = Time.frameCount;
            return;
        }

        if (_calibrationStep == CalibrationStep.SetDirection)
        {
            teleopWorld.SetRobotBasePose(_capturedBasePosition, _candidatePose.rotation);
            _calibrationStep = lockAfterPlacement ? CalibrationStep.Locked : CalibrationStep.SetPosition;
        }

        _suppressRightThumbstickResetFrame = Time.frameCount;
    }

    private void UpdateCandidate()
    {
        if (!TryGetControllerWorldPose(out Vector3 controllerPosition, out Quaternion controllerRotation))
        {
            _hasCandidate = false;
            return;
        }

        if (_calibrationStep == CalibrationStep.SetPosition)
        {
            Vector3 probePosition = controllerPosition + controllerRotation * positionProbeLocalOffset;
            _candidatePose = new Pose(probePosition, CandidateRotation(controllerRotation, Vector3.up));
            _hasCandidate = true;
            return;
        }

        if (_calibrationStep == CalibrationStep.SetDirection)
        {
            _candidatePose = new Pose(_capturedBasePosition, CandidateRotation(controllerRotation, Vector3.up));
            _hasCandidate = true;
            return;
        }

        _hasCandidate = false;
    }

    private Quaternion CandidateRotation(Quaternion controllerRotation, Vector3 surfaceNormal)
    {
        Quaternion baseRotation;
        Vector3 normal = surfaceNormal.normalized;

        if (!alignYawToController)
        {
            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.right, normal);

            baseRotation = Quaternion.LookRotation(forward.normalized, normal);
        }
        else
        {
            Vector3 forward = controllerRotation * Vector3.forward;
            forward = Vector3.ProjectOnPlane(forward, normal);

            if (forward.sqrMagnitude < 0.0001f)
            {
                Transform head = Camera.main != null ? Camera.main.transform : null;
                forward = head != null
                    ? Vector3.ProjectOnPlane(head.forward, normal)
                    : Vector3.forward;
            }

            if (forward.sqrMagnitude < 0.0001f)
                return Quaternion.identity;

            baseRotation = Quaternion.LookRotation(forward.normalized, normal);
        }

        return Quaternion.AngleAxis(_manualYawOffsetDegrees, normal) * baseRotation;
    }

    private void UpdateVisuals()
    {
        if (_previewRoot != null)
        {
            _previewRoot.gameObject.SetActive(_hasCandidate);
            if (_hasCandidate)
                _previewRoot.SetPositionAndRotation(_candidatePose.position, _candidatePose.rotation);
        }
    }

    private void HideVisuals()
    {
        if (_previewRoot != null)
            _previewRoot.gameObject.SetActive(false);
    }

    private bool TryGetControllerWorldPose(out Vector3 position, out Quaternion rotation)
    {
        bool positionTracked = OVRInput.GetControllerPositionTracked(controller);
        bool rotationTracked = OVRInput.GetControllerOrientationTracked(controller);

        if (!positionTracked || !rotationTracked)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        Vector3 localPosition = OVRInput.GetLocalControllerPosition(controller);
        Quaternion localRotation = OVRInput.GetLocalControllerRotation(controller);

        if (trackingSpace == null)
        {
            position = localPosition;
            rotation = localRotation;
            return true;
        }

        position = trackingSpace.TransformPoint(localPosition);
        rotation = trackingSpace.rotation * localRotation;
        return true;
    }

    private void UpdateManualYawOffset()
    {
        Vector2 yawInput = OVRInput.Get(yawAdjustAxis, controller);
        float x = Mathf.Abs(yawInput.x) > yawAdjustDeadZone ? yawInput.x : 0f;

        if (Mathf.Approximately(x, 0f))
            return;

        _manualYawOffsetDegrees += x * yawAdjustDegreesPerSecond * Time.deltaTime;
        _manualYawOffsetDegrees = Mathf.Repeat(_manualYawOffsetDegrees + 180f, 360f) - 180f;
    }

    private void CreatePreview()
    {
        if (_previewRoot != null)
            return;

        GameObject previewObject = new GameObject("RobotBasePlacementPreview");
        _previewRoot = previewObject.transform;

        CreatePreviewAxis("X", Vector3.forward, Color.red);
        CreatePreviewAxis("Y", Vector3.right, Color.green);
        CreatePreviewAxis("Z", Vector3.up, Color.blue);

        _previewRoot.gameObject.SetActive(false);
    }

    private void CreatePreviewAxis(string axisName, Vector3 direction, Color color)
    {
        GameObject axis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        axis.name = axisName + " Preview Axis";
        axis.transform.SetParent(_previewRoot, false);
        axis.transform.localScale = new Vector3(previewAxisRadius, previewAxisLength * 0.5f, previewAxisRadius);
        axis.transform.localPosition = direction.normalized * (previewAxisLength * 0.5f);
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
        material.color = new Color(color.r, color.g, color.b, 0.65f);
        return material;
    }
}
