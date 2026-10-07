using UnityEngine;

public class VirtualUrdfJointDriver : MonoBehaviour
{
    [SerializeField]
    private string[] jointNames =
    {
        "shoulder_pan_joint",
        "shoulder_lift_joint",
        "elbow_joint",
        "wrist_1_joint",
        "wrist_2_joint",
        "wrist_3_joint"
    };

    [SerializeField] private ArticulationBody[] joints = new ArticulationBody[6];
    [SerializeField] private float[] direction = { 1f, 1f, 1f, 1f, 1f, 1f };
    [SerializeField] private float[] offsetDegrees = { 0f, 0f, 0f, 0f, 0f, 0f };
    [SerializeField] private bool autoFindJoints = true;
    [SerializeField] private bool configureDrive = true;
    [SerializeField] private float stiffness = 10000f;
    [SerializeField] private float damping = 1000f;
    [SerializeField] private float forceLimit = 1000f;

    private void Awake()
    {
        if (autoFindJoints)
            FindJoints();
    }

    [ContextMenu("Find URDF Joints")]
    public void FindJoints()
    {
        if (jointNames == null)
            return;

        if (joints == null || joints.Length != jointNames.Length)
            joints = new ArticulationBody[jointNames.Length];

        ArticulationBody[] bodies = GetComponentsInChildren<ArticulationBody>(true);
        for (int i = 0; i < jointNames.Length; i++)
        {
            joints[i] = null;
            foreach (ArticulationBody body in bodies)
            {
                if (body.name.Contains(jointNames[i]))
                {
                    joints[i] = body;
                    break;
                }
            }
        }
    }

    public void ApplyJointsRadians(float[] radians)
    {
        if (radians == null || joints == null)
            return;

        int count = Mathf.Min(radians.Length, joints.Length);
        for (int i = 0; i < count; i++)
            ApplyJointRadians(i, radians[i]);
    }

    private void ApplyJointRadians(int index, float radians)
    {
        ArticulationBody body = joints[index];
        if (body == null)
            return;

        float sign = ValueAt(direction, index, 1f);
        float offset = ValueAt(offsetDegrees, index, 0f);
        ArticulationDrive drive = body.xDrive;
        drive.target = radians * Mathf.Rad2Deg * sign + offset;

        if (configureDrive)
        {
            drive.stiffness = stiffness;
            drive.damping = damping;
            drive.forceLimit = forceLimit;
        }

        body.xDrive = drive;
    }

    private static float ValueAt(float[] values, int index, float fallback)
    {
        return values != null && index >= 0 && index < values.Length ? values[index] : fallback;
    }
}
