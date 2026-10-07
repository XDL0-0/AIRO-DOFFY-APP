using UnityEngine;

public class Robotiq2F85MimicJointDriver : MonoBehaviour
{
    [SerializeField] private string[] jointNames =
    {
        "finger_joint",
        "left_inner_knuckle_joint",
        "left_inner_finger_joint",
        "right_outer_knuckle_joint",
        "right_inner_knuckle_joint",
        "right_inner_finger_joint"
    };

    [SerializeField] private float[] multipliers = { 1f, 1f, -1f, 1f, 1f, -1f };
    [SerializeField] private float[] offsetsRadians = { 0f, 0f, 0f, 0f, 0f, 0f };
    [SerializeField] private ArticulationBody[] joints;
    [SerializeField] private bool autoFindJoints = true;
    [SerializeField] private bool configureDrive = true;
    [SerializeField] private float openAngleRadians = 0f;
    [SerializeField] private float closedAngleRadians = 0.8f;
    [SerializeField] private float stiffness = 4000f;
    [SerializeField] private float damping = 400f;
    [SerializeField] private float forceLimit = 1000f;

    private void Awake()
    {
        if (autoFindJoints)
            FindJoints();
    }

    [ContextMenu("Find Gripper Joints")]
    public void FindJoints()
    {
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

    public void ApplyNormalized(float normalizedClosed)
    {
        if (joints == null)
            return;

        normalizedClosed = Mathf.Clamp01(normalizedClosed);
        float master = Mathf.Lerp(openAngleRadians, closedAngleRadians, normalizedClosed);

        for (int i = 0; i < joints.Length; i++)
        {
            ArticulationBody body = joints[i];
            if (body == null)
                continue;

            float radians = master * ValueAt(multipliers, i, 1f) + ValueAt(offsetsRadians, i, 0f);
            ArticulationDrive drive = body.xDrive;
            drive.target = radians * Mathf.Rad2Deg;

            if (configureDrive)
            {
                drive.stiffness = stiffness;
                drive.damping = damping;
                drive.forceLimit = forceLimit;
            }

            body.xDrive = drive;
        }
    }

    private static float ValueAt(float[] values, int index, float fallback)
    {
        return values != null && index >= 0 && index < values.Length ? values[index] : fallback;
    }
}
