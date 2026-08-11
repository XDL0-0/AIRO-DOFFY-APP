using UnityEngine;

/// <summary>
/// 创建并管理校准坐标系的 3D gizmo(球 + 圆柱 + X/Y/Z 轴)。
/// 参考 TactAR 的 coord 结构:白色球标记 TCP 原点,红/绿/蓝圆柱表示 X/Y/Z 轴。
/// 所有部件程序化创建,材质半透明,便于在 passthrough 中看到真实场景。
///
/// 层级:本组件挂在 "Coord" 上,所有视觉部件放在 "Visual" wrapper 下,
/// 整体缩放只改 wrapper,避免破坏圆柱的长宽比例。
/// </summary>
public class CalibrationGizmo : MonoBehaviour
{
    [Header("Visual Config")]
    public float axisLength = 0.25f;
    public float axisRadius = 0.008f;
    public float sphereRadius = 0.03f;
    public float baseRadius = 0.015f;
    public float baseHeight = 0.04f;

    public Color xColor = new Color(0.9f, 0.2f, 0.2f, 0.65f);
    public Color yColor = new Color(0.2f, 0.8f, 0.2f, 0.65f);
    public Color zColor = new Color(0.2f, 0.3f, 0.9f, 0.65f);
    public Color sphereColor = new Color(1f, 1f, 1f, 0.85f);

    private Transform _visualRoot;
    private bool _visualsReady;

    private void Awake()
    {
        EnsureVisuals();
    }

    /// <summary>确保 gizmo 子物体已创建。可重复调用。</summary>
    public void EnsureVisuals()
    {
        if (_visualsReady) return;

        GameObject visualObject = new GameObject("Visual");
        visualObject.transform.SetParent(transform, false);
        _visualRoot = visualObject.transform;

        CreateAxis("X", Vector3.forward, xColor);
        CreateAxis("Y", Vector3.left, yColor);
        CreateAxis("Z", Vector3.up, zColor);
        CreateSphere(sphereRadius, sphereColor);
        CreateBase(baseRadius, baseHeight);

        _visualsReady = true;
    }

    /// <summary>当前 gizmo 外观缩放(Visual wrapper 的 localScale)。</summary>
    public float VisualScale => _visualRoot != null ? _visualRoot.localScale.x : 1f;

    /// <summary>整体缩放 gizmo 外观(A/X 缩小、B/Y 放大时使用)。</summary>
    public void SetVisualScale(float scale)
    {
        if (_visualRoot != null)
            _visualRoot.localScale = Vector3.one * Mathf.Max(0.05f, scale);
    }

    private void CreateAxis(string axisName, Vector3 direction, Color color)
    {
        GameObject axis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        axis.name = axisName + " Axis";
        axis.transform.SetParent(_visualRoot, false);

        // 圆柱默认高度 2(单位圆柱),以轴中心为原点,一半长度向正方向延伸
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

    private void CreateSphere(float radius, Color color)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Origin Sphere";
        sphere.transform.SetParent(_visualRoot, false);
        sphere.transform.localScale = Vector3.one * (radius * 2f);

        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material = CreateMaterial(color);

        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
    }

    private void CreateBase(float radius, float height)
    {
        GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObj.name = "Base Cylinder";
        baseObj.transform.SetParent(_visualRoot, false);
        baseObj.transform.localScale = new Vector3(radius, height * 0.5f, radius);

        Renderer renderer = baseObj.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material = CreateMaterial(new Color(0.6f, 0.6f, 0.6f, 0.5f));

        Collider collider = baseObj.GetComponent<Collider>();
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

        // 半透明混合,让真实场景(实验台/机器人)透过 gizmo 可见
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        material.renderQueue = 3000;

        return material;
    }
}
