using UnityEngine;

public class TeleopViewModeMapper : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TeleopWorld teleopWorld;
    [SerializeField] private Transform trackingSpace;

    [Header("Input")]
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;
    [SerializeField] private OVRInput.Button clutchButton = OVRInput.Button.PrimaryHandTrigger;

    [Header("Mapping")]
    [SerializeField] private float movementScale = 1f;
    [SerializeField] private float deadZoneMeters = 0.002f;

    private Vector3 _controllerStartWorld;
    private bool _wasClutched;

    public bool IsClutched { get; private set; }
    public Vector3 WorldDelta { get; private set; }
    public Vector3 RobotBaseDelta { get; private set; }

    private void Awake()
    {
        if (teleopWorld == null)
            teleopWorld = FindAnyObjectByType<TeleopWorld>();

        if (trackingSpace == null)
        {
            OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null)
                trackingSpace = cameraRig.trackingSpace;
        }
    }

    private void Update()
    {
        IsClutched = OVRInput.Get(clutchButton, controller);
        if (!TryGetControllerWorldPosition(out Vector3 controllerWorld))
        {
            WorldDelta = Vector3.zero;
            RobotBaseDelta = Vector3.zero;
            _wasClutched = false;
            return;
        }

        if (IsClutched && !_wasClutched)
        {
            _controllerStartWorld = controllerWorld;
            WorldDelta = Vector3.zero;
            RobotBaseDelta = Vector3.zero;
        }

        if (IsClutched)
        {
            Vector3 rawWorldDelta = (controllerWorld - _controllerStartWorld) * movementScale;
            if (rawWorldDelta.magnitude < deadZoneMeters)
                rawWorldDelta = Vector3.zero;

            WorldDelta = rawWorldDelta;
            RobotBaseDelta = teleopWorld != null
                ? teleopWorld.WorldDeltaToRobotBase(rawWorldDelta)
                : rawWorldDelta;
        }
        else
        {
            WorldDelta = Vector3.zero;
            RobotBaseDelta = Vector3.zero;
        }

        _wasClutched = IsClutched;
    }

    private bool TryGetControllerWorldPosition(out Vector3 position)
    {
        if (!OVRInput.GetControllerPositionTracked(controller))
        {
            position = Vector3.zero;
            return false;
        }

        Vector3 localPosition = OVRInput.GetLocalControllerPosition(controller);
        position = trackingSpace != null
            ? trackingSpace.TransformPoint(localPosition)
            : localPosition;
        return true;
    }
}
