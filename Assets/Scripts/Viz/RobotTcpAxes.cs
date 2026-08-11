using UnityEngine;

/// <summary>
/// TCP 坐标轴可视化: 直接在 TCP 上绘制 X(红), Y(绿), Z(蓝)。
/// 使用 Cylinder 实体,Quest 可见。
/// </summary>
[ExecuteAlways]
public class RobotTcpAxes : MonoBehaviour
{
    [Header("Axes")]
    public float axisLength = 0.1f;
    public float axisRadius = 0.004f;

    public Color xColor = Color.red;
    public Color yColor = Color.green;
    public Color zColor = Color.blue;

    private bool _built;

    private void Awake() { Build(); }
    private void OnEnable() { Build(); }

    private void Build()
    {
        if (_built) return;

        // 清理历史上所有版本的旧轴子节点
        foreach (Transform child in transform)
        {
            var name = child.name;
            if (name == "X" || name == "Y" || name == "Z"
                || name.StartsWith("AxisVisualizer")
                || name.StartsWith("TcpAxis"))
                DestroyImmediate(child.gameObject);
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard");

        CreateAxis("X", Vector3.forward, xColor, shader);
        CreateAxis("Y", Vector3.left,   yColor, shader);
        CreateAxis("Z", Vector3.up,     zColor, shader);

        _built = true;
    }

    private void CreateAxis(string name, Vector3 direction, Color color, Shader shader)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.name = name;
        obj.transform.SetParent(transform, false);
        obj.transform.localPosition = direction * (axisLength * 0.5f);
        obj.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
        obj.transform.localScale = new Vector3(axisRadius, axisLength * 0.5f, axisRadius);

        var c = obj.GetComponent<Collider>();
        if (c != null) c.enabled = false;

        var mat = new Material(shader);
        mat.color = color;
        obj.GetComponent<Renderer>().material = mat;
    }
}
