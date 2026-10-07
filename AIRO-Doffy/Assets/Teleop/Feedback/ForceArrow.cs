using UnityEngine;

/// <summary>
/// 力箭头可视化: TCP 标记球(白) + 力终点球(彩色) + 杆身圆柱连接二者。
/// 挂在 TCP 变换下,箭头随末端自动运动。
/// </summary>
public class ForceArrow : MonoBehaviour
{
    [Header("Visual")]
    public float tcpMarkerRadius = 0.02f;
    public float headRadius = 0.02f;
    public float shaftRadius = 0.004f;

    [Header("Color")]
    public Color markerColor = Color.white;
    public Color lowForceColor = new Color(0.2f, 0.9f, 0.2f);
    public Color highForceColor = new Color(0.95f, 0.15f, 0.15f);

    private Transform _tcpMarker;
    private Transform _head;
    private Transform _shaft;
    private Material _markerMat;
    private Material _forceMat;
    private bool _built;

    private void Awake() { BuildVisuals(); }

    public void BuildVisuals()
    {
        if (_built) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard");

        // TCP 标记球(白色,始终可见)
        _tcpMarker = CreatePrimitive("TcpMarker", PrimitiveType.Sphere, transform,
            Vector3.zero, tcpMarkerRadius * 2f, markerColor, shader);

        _markerMat = new Material(shader);
        _markerMat.color = markerColor;
        _tcpMarker.GetComponent<Renderer>().material = _markerMat;

        // 力终点球(彩色,随力变化)
        _forceMat = new Material(shader);
        _head = CreatePrimitive("Head", PrimitiveType.Sphere, transform,
            Vector3.zero, headRadius * 2f, lowForceColor, shader);
        _head.GetComponent<Renderer>().material = _forceMat;

        // 杆身圆柱(连接 TCP → 力终点)
        _shaft = CreatePrimitive("Shaft", PrimitiveType.Cylinder, transform,
            Vector3.zero, 1f, lowForceColor, shader);
        _shaft.GetComponent<Renderer>().material = _forceMat;

        // 初始隐藏力和杆,等数据到了再显示
        _head.gameObject.SetActive(false);
        _shaft.gameObject.SetActive(false);

        _built = true;
    }

    public void UpdateForce(Vector3 forceLocal)
    {
        BuildVisuals();

        float length = forceLocal.magnitude;
        if (length < 0.0001f)
        {
            _head.gameObject.SetActive(false);
            _shaft.gameObject.SetActive(false);
            return;
        }

        _head.gameObject.SetActive(true);
        _shaft.gameObject.SetActive(true);

        Vector3 dir = forceLocal / length;

        // TCP 标记球始终在原点
        _tcpMarker.localPosition = Vector3.zero;

        // 力终点球 = forceLocal
        _head.localPosition = forceLocal;

        // 杆身:中点在 forceLocal/2, 朝向 dir, 长度 = length
        _shaft.localPosition = forceLocal * 0.5f;
        _shaft.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
        _shaft.localScale = new Vector3(shaftRadius, length * 0.5f, shaftRadius);

        // 颜色随力大小渐变
        Color c = Color.Lerp(lowForceColor, highForceColor, Mathf.Clamp01(length * 10f));
        _forceMat.color = c;
    }

    private static Transform CreatePrimitive(string name, PrimitiveType type,
        Transform parent, Vector3 pos, float scale, Color color, Shader shader)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = pos;
        if (type == PrimitiveType.Cylinder)
            obj.transform.localScale = Vector3.one * scale;
        else
            obj.transform.localScale = Vector3.one * scale;
        Collider c = obj.GetComponent<Collider>();
        if (c != null) c.enabled = false;
        Material mat = new Material(shader);
        mat.color = color;
        obj.GetComponent<Renderer>().material = mat;
        return obj.transform;
    }

    private void OnDestroy()
    {
        if (_markerMat != null) Destroy(_markerMat);
        if (_forceMat != null) Destroy(_forceMat);
    }
}
