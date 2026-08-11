using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TeleopControlModeManager : MonoBehaviour
{
    public enum ControlMode
    {
        Mirror,
        View
    }

    public static TeleopControlModeManager Instance { get; private set; }
    public static ControlMode CurrentMode { get; private set; } = ControlMode.Mirror;

    [Header("References")]
    [SerializeField] private TeleopWorld teleopWorld;
    [SerializeField] private Transform trackingSpace;
    [SerializeField] private RobotBasePlacementTool placementTool;
    [SerializeField] private TeleopViewModeMapper viewModeMapper;

    [Header("UI")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private TextMeshProUGUI modeLabel;

    [Header("Controller Shortcut")]
    [SerializeField] private bool enableControllerShortcut = false;
    [SerializeField] private OVRInput.Controller shortcutController = OVRInput.Controller.LTouch;
    [SerializeField] private OVRInput.Button toggleShortcut = OVRInput.Button.Two;

    [Header("Mode Tools")]
    [SerializeField] private bool enableViewToolsOnlyInViewMode = true;
    [SerializeField] private bool autoCreateViewModeTools = true;

    private const string ModePrefsKey = "cfg_teleopControlMode";
    private bool _warnedMissingAnchor;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (teleopWorld == null)
            teleopWorld = FindAnyObjectByType<TeleopWorld>();

        if (teleopWorld == null && autoCreateViewModeTools)
            teleopWorld = new GameObject("TeleopWorld").AddComponent<TeleopWorld>();

        if (trackingSpace == null)
        {
            OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null)
                trackingSpace = cameraRig.trackingSpace;
        }

        EnsureViewModeTools();
    }

    private void Start()
    {
        int savedMode = PlayerPrefs.GetInt(ModePrefsKey, (int)CurrentMode);
        CurrentMode = (ControlMode)Mathf.Clamp(savedMode, 0, 1);

        if (toggleButton != null)
            toggleButton.onClick.AddListener(ToggleControlMode);

        ApplyMode();
    }

    private void OnDestroy()
    {
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(ToggleControlMode);

        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (enableControllerShortcut && OVRInput.GetDown(toggleShortcut, shortcutController))
            ToggleControlMode();
    }

    public void ToggleControlMode()
    {
        SetControlMode(CurrentMode == ControlMode.Mirror ? ControlMode.View : ControlMode.Mirror);
    }

    public void SetMirrorMode()
    {
        SetControlMode(ControlMode.Mirror);
    }

    public void SetViewMode()
    {
        SetControlMode(ControlMode.View);
    }

    public void SetControlMode(ControlMode mode)
    {
        if (CurrentMode == mode)
            return;

        CurrentMode = mode;
        PlayerPrefs.SetInt(ModePrefsKey, (int)CurrentMode);
        PlayerPrefs.Save();
        _warnedMissingAnchor = false;
        ApplyMode();

        LogManager.Log("Teleop", $"Control mode: {CurrentMode}");
    }

    public static void TransformControllerPose(OVRInput.Controller controller, ref Vector3 position, ref Quaternion rotation)
    {
        TeleopControlModeManager manager = Instance;
        if (manager == null || CurrentMode == ControlMode.Mirror)
        {
            TeleopReferenceFrame.TransformControllerPose(ref position, ref rotation);
            return;
        }

        manager.TransformControllerPoseForViewMode(ref position, ref rotation);
    }

    private void TransformControllerPoseForViewMode(ref Vector3 position, ref Quaternion rotation)
    {
        TeleopWorld world = teleopWorld != null ? teleopWorld : TeleopWorld.Instance;
        if (world == null || !world.HasRobotBaseAnchor)
        {
            WarnMissingAnchorOnce();
            TeleopReferenceFrame.TransformControllerPose(ref position, ref rotation);
            return;
        }

        Vector3 worldPosition = trackingSpace != null
            ? trackingSpace.TransformPoint(position)
            : position;
        Quaternion worldRotation = trackingSpace != null
            ? trackingSpace.rotation * rotation
            : rotation;

        position = world.WorldPointToControllerPacket(worldPosition);
        rotation = world.WorldRotationToControllerPacket(worldRotation);
    }

    private void ApplyMode()
    {
        EnsureViewModeTools();

        if (modeLabel != null)
            modeLabel.text = CurrentMode == ControlMode.Mirror
                ? "Control Mode\nMirror"
                : "Control Mode\nView";

        if (enableViewToolsOnlyInViewMode)
        {
            bool viewMode = CurrentMode == ControlMode.View;
            if (placementTool != null)
                placementTool.enabled = viewMode;
            if (viewModeMapper != null)
                viewModeMapper.enabled = viewMode;
        }

        // 坐标轴(机器人底座坐标系)仅 View 模式显示,Mirror 模式隐藏
        if (teleopWorld != null)
            teleopWorld.SetAxesVisible(CurrentMode == ControlMode.View);
    }

    private void EnsureViewModeTools()
    {
        if (!autoCreateViewModeTools)
            return;

        if (placementTool == null)
            placementTool = FindAnyObjectByType<RobotBasePlacementTool>();

        if (placementTool == null)
        {
            GameObject placementObject = new GameObject("RobotBasePlacementTool");
            placementTool = placementObject.AddComponent<RobotBasePlacementTool>();
        }

        if (viewModeMapper == null)
            viewModeMapper = FindAnyObjectByType<TeleopViewModeMapper>();

        if (viewModeMapper == null)
        {
            GameObject mapperObject = new GameObject("TeleopViewModeMapper");
            viewModeMapper = mapperObject.AddComponent<TeleopViewModeMapper>();
        }
    }

    private void WarnMissingAnchorOnce()
    {
        if (_warnedMissingAnchor)
            return;

        _warnedMissingAnchor = true;
        LogManager.Log("Teleop", "View mode needs RobotBaseAnchor; falling back to mirror frame");
    }
}
