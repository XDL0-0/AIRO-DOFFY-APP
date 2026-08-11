using UnityEngine;

public class TactileArrow : MonoBehaviour
{
    [Header("Components")]
    public Transform stickTransform;
    public Transform ballTransform;
    public Renderer[] arrowRenderers;

    [Header("Settings")]
    public float maxForce = 0.01f;
    [Tooltip("力每增加1单位，棍子增长多少")]
    public float lengthMultiplier = 0.5f;

    [Tooltip("没有力的时候，棍子的基础长度 (Resting Length)")]
    public float minLength = 0.0f;

    public float baseWidth = 0.002f;
    private const float CYLINDER_DEFAULT_HEIGHT = 2.0f;

    private MaterialPropertyBlock propBlock;
    private Quaternion defaultLocalRotation; // 记录生成时的默认朝向
    private bool isInitialized = false;

    void Awake()
    {
        propBlock = new MaterialPropertyBlock();
        // 暂时记录一下，Start的时候会覆盖为Generator生成的朝向
        defaultLocalRotation = transform.localRotation;
    }

    void Start()
    {
        if (!isInitialized)
        {
            // 如果Generator没有显式调用初始化，我们把当前的当作默认
            defaultLocalRotation = transform.localRotation;
            isInitialized = true;
        }
        // 初始化时强制更新一次视觉，确保000状态下有棍子
        UpdateVisuals(Vector3.zero);
    }

    // 供Generator调用，用来锁定它的“正前方”
    public void SetDefaultRotation(Quaternion rot)
    {
        transform.localRotation = rot;
        defaultLocalRotation = rot;
        isInitialized = true;
    }

    public void UpdateVisuals(Vector3 forceVector)
    {
        float magnitude = forceVector.magnitude;

        // --- 1. 旋转控制 (核心修改部分) ---
        if (magnitude > 0.001f)
        {
            // [修复原理]: 
            // 假设 forceVector 是相对于"传感器自身"的 (例如 (0,0,1) 代表垂直压入表面)。
            // 我们需要用 defaultLocalRotation * forceVector 将这个局部力转换到父物体的坐标系方向。

            Vector3 alignedForceDirection = defaultLocalRotation * forceVector;

            // 让箭头看向这个“修正后”的方向
            transform.localRotation = Quaternion.LookRotation(alignedForceDirection);
        }
        else
        {
            // 没有力的时候，回到默认朝向
            transform.localRotation = defaultLocalRotation;
        }

        // --- 2. 形状/长度控制 ---
        float targetLength = minLength + (magnitude * lengthMultiplier);

        if (stickTransform != null)
        {
            float yScale = targetLength / CYLINDER_DEFAULT_HEIGHT;
            stickTransform.localScale = new Vector3(baseWidth, yScale, baseWidth);
            stickTransform.localPosition = new Vector3(0, 0, targetLength / 2.0f);
        }

        if (ballTransform != null)
        {
            ballTransform.localScale = Vector3.one * (baseWidth * 2.0f);
            ballTransform.localPosition = new Vector3(0, 0, targetLength);
        }

        // --- 3. 颜色控制 ---
        float t = Mathf.Clamp01(magnitude / maxForce);
        Color targetColor = Color.Lerp(Color.blue, Color.red, t);

        if (arrowRenderers != null)
        {
            foreach (var rend in arrowRenderers)
            {
                if (rend == null) continue;
                rend.GetPropertyBlock(propBlock);
                propBlock.SetColor("_Color", targetColor);
                propBlock.SetColor("_BaseColor", targetColor);
                rend.SetPropertyBlock(propBlock);
            }
        }
    }

    //public void UpdateVisuals(Vector3 forceVector)
    //{
    //    float magnitude = forceVector.magnitude;

    //    // --- 1. 旋转控制 ---
    //    if (magnitude > 0.001f)
    //    {
    //        // 有力的时候，指向力的方向
    //        transform.localRotation = Quaternion.LookRotation(forceVector);
    //    }
    //    else
    //    {
    //        // 没有力的时候，回到默认朝向（垂直于表面）
    //        transform.localRotation = defaultLocalRotation;
    //    }

    //    // --- 2. 形状/长度控制 ---
    //    // 目标长度 = 基础长度 + (力的大小 * 倍率)
    //    float targetLength = minLength + (magnitude * lengthMultiplier);

    //    // A. 控制棍子 (Stick)
    //    if (stickTransform != null)
    //    {
    //        // 计算 Y 轴缩放 (目标长度 / 默认高度2)
    //        float yScale = targetLength / CYLINDER_DEFAULT_HEIGHT;
    //        stickTransform.localScale = new Vector3(baseWidth, yScale , baseWidth);
    //        // 移动位置，确保底部不动 (向Z前移长度的一半)
    //        stickTransform.localPosition = new Vector3(0, 0, targetLength / 2.0f);
    //    }

    //    // B. 控制球 (Ball)
    //    if (ballTransform != null)
    //    {
    //        ballTransform.localScale = Vector3.one * (baseWidth * 2.0f);
    //        // 球始终顶在最前端
    //        ballTransform.localPosition = new Vector3(0, 0, targetLength);
    //    }

    //    // --- 3. 颜色控制 ---
    //    // 归一化颜色强度
    //    float t = Mathf.Clamp01(magnitude / maxForce);
    //    Color targetColor = Color.Lerp(Color.blue, Color.red, t);

    //    if (arrowRenderers != null)
    //    {
    //        foreach (var rend in arrowRenderers)
    //        {
    //            if (rend == null) continue;
    //            rend.GetPropertyBlock(propBlock);
    //            propBlock.SetColor("_Color", targetColor);
    //            propBlock.SetColor("_BaseColor", targetColor);
    //            rend.SetPropertyBlock(propBlock);
    //        }
    //    }
    //}
}