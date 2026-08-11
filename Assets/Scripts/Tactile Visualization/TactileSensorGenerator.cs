using UnityEngine;
using System.Collections.Generic;

public class TactileSensorGenerator : MonoBehaviour
{
    [Header("Prefab Settings")]
    public TactileArrow arrowPrefab;
    public Transform container;

    [Header("Layout Dimensions")]
    public float spacingY = 0.015f; // 调小一点间距适应手柄
    public float spacingZ = 0.015f;
    public float planeOffset = 0.015f;

    [Header("Matrix Config")]
    public int mainRows = 4;
    public int mainCols = 8;
    public int extraPointsCount = 3;
    [Header("Data Settings")]
    [Tooltip("将原始 int 数值转换为 Unity 力大小的系数。例如原始值 100 * 0.01 = 1.0f")]
    public float forceSensitivity = 0.0001f;

    [SerializeField]
    private List<TactileArrow> allArrows = new List<TactileArrow>();

    private void Start()
    {
        // 游戏运行第一帧时会自动调用这里
        GenerateLayout();
    }

    // --- 数据更新 ---
    //public void UpdateSensorData(List<Vector3> forceData)
    //{
    //    if (forceData != null) UpdateSensorData(forceData.ToArray());
    //}
    
    public void UpdateRawSensorData(int[,] rawData)
    {
        // 卫语句：基本检查
        if (rawData == null || allArrows.Count == 0) return;

        // 获取数据行数，通常是 41
        int dataCount = rawData.GetLength(0); 
        
        // 安全检查：防止数组越界（以防硬件发来的数据少于41个）
        int loopCount = Mathf.Min(dataCount, allArrows.Count);

        for (int i = 0; i < loopCount; i++)
        {
            // 1. 获取原始 xyz
            // 假设传感器数据顺序是 x, y, z (根据你的硬件可能需要调整顺序，如 z, y, x)
            float rawX = rawData[i, 0];
            float rawY = rawData[i, 1];
            float rawZ = rawData[i, 2];

            // 2. 组装成局部向量 (Local Sensor Force)
            // 这一步非常重要：这个 Vector3 代表"相对于传感器表面"的力
            Vector3 localForce = new Vector3(rawX, rawY, rawZ) * forceSensitivity;

            // 3. 直接更新对应的箭头
            // 不需要再做 convertedForces 数组中转，直接驱动
            allArrows[i].UpdateVisuals(localForce);
        }
    }


    public void UpdateSensorData(Vector3[] forceData)
    {
        if (allArrows.Count == 0 || forceData == null) return;
        int count = Mathf.Min(allArrows.Count, forceData.Length);
        for (int i = 0; i < count; i++)
        {
            if (allArrows[i] != null) allArrows[i].UpdateVisuals(forceData[i]);
        }
    }

    // --- 生成逻辑 ---
    [ContextMenu("Generate Sensor Layout")]
    public void GenerateLayout()
    {
        if (arrowPrefab == null) { Debug.LogError("请先赋值 Arrow Prefab"); return; }
        if (container == null) container = transform;

        // 清理旧物体
        var tempChildren = new List<GameObject>();
        foreach (Transform child in container) tempChildren.Add(child.gameObject);
        foreach (GameObject child in tempChildren) DestroyImmediate(child);
        allArrows.Clear();

        // 定义四个方向的旋转四元数
        Quaternion rotRight = Quaternion.Euler(0, 90, 0);   // 主平面：向右 (X轴)
        Quaternion rotForward = Quaternion.identity;          // 前平面：向前 (Z轴)
        Quaternion rotUp = Quaternion.Euler(-90, 0, 0);  // 上平面：向上 (Y轴)
        Quaternion rotDown = Quaternion.Euler(90, 0, 0);   // 下平面：向下 (-Y轴)

        // 1. 生成主平面 (Main Plane 4x8) - 垂直于YZ平面 -> 指向X
        for (int r = 0; r < mainCols; r++)
        {
            for (int c = 0; c < mainRows; c++)
            {
                // 注意：这里r对应Y轴高度，c对应Z轴长度
                Vector3 pos = new Vector3(0, c * spacingZ, r * spacingY);
                SpawnArrow($"Arrow_{r*4+c}", pos, rotRight);
            }
        }

        // 计算主平面的边界尺寸
        float mainHeight = (mainRows - 1) * spacingZ;
        float mainLength = (mainCols - 1) * spacingY;


        // 2. 生成前平面 (Front Plane 1x3) - 在最前端，垂直于自己 -> 指向Z
        float frontZ = mainLength + planeOffset;
        for (int i = 0; i < extraPointsCount; i++)
        {
            float yPos = (i / (float)(extraPointsCount - 1)) * mainHeight;
            if (extraPointsCount <= 1) yPos = mainHeight / 2;

            // 位置：X=0, Y=分布, Z=前端偏移
            Vector3 pos = new Vector3(0, yPos, frontZ);
            SpawnArrow($"Arrow_{mainCols * mainRows + extraPointsCount*2+i}", pos, rotForward);
        }

        // 计算Z轴分布点 (前、中、后)
        float[] zPositions = new float[extraPointsCount];



        for (int i = 0; i < extraPointsCount; i++)
        {
            float t = i / (float)(extraPointsCount - 1);
            if (extraPointsCount <= 1) t = 0.5f;
            // 假设 extraPoints 顺序是从后到前 (0 -> Length)
            zPositions[i] = t * mainLength;
        }

        // 3. 生成上平面 (Top Plane 1x3) - 在最上面，垂直于自己 -> 指向Y
        float topY = mainHeight + planeOffset;
        for (int i = 0; i < extraPointsCount; i++)
        {
            Vector3 pos = new Vector3(0, topY, zPositions[i]);
            SpawnArrow($"Arrow_{mainCols * mainRows + extraPointsCount * 2 - i-1}", pos, rotUp);
        }

        // 4. 生成下平面 (Bottom Plane 1x3) - 在最下面，垂直于自己 -> 指向-Y
        float bottomY = -planeOffset;
        for (int i = 0; i < extraPointsCount; i++)
        {
            Vector3 pos = new Vector3(0, bottomY, zPositions[i]);
            SpawnArrow($"Arrow_{mainCols * mainRows + extraPointsCount * 1 - i-1}", pos, rotDown);
        }

        allArrows.Sort((a, b) => {
            int idA = int.Parse(a.name.Split('_')[1]);
            int idB = int.Parse(b.name.Split('_')[1]);
            return idA.CompareTo(idB);
        });

        for (int i = 0; i < allArrows.Count; i++)
        {
            // SetSiblingIndex 会改变物体在 Hierarchy 里的上下位置
            allArrows[i].transform.SetSiblingIndex(i);
        }

        Debug.Log($"生成完毕: 总共 {allArrows.Count} 个传感器点。");
    }

    private void SpawnArrow(string name, Vector3 localPosition, Quaternion rotation)
    {
        TactileArrow newArrow = Instantiate(arrowPrefab, container);
        newArrow.name = name;
        newArrow.transform.localPosition = localPosition;

        // 关键：调用 SetDefaultRotation 而不是直接赋值
        // 这样 Arrow 脚本会记住这个“休息状态”的朝向
        newArrow.SetDefaultRotation(rotation);

        allArrows.Add(newArrow);
    }

    //public void UpdateRawSensorData(int[,] rawData)
    //{
    //    if (rawData == null || allArrows.Count == 0) return;

    //    int rows = rawData.GetLength(0); // 应该是 41

    //    // 确保不会越界（取数据长度和箭头数量的最小值）
    //    int count = Mathf.Min(rows, allArrows.Count);

    //    // 准备一个 Vector3 数组传给现有的逻辑
    //    Vector3[] convertedForces = new Vector3[count];

    //    for (int i = 0; i < count; i++)
    //    {
    //        // 1. 获取原始 xyz (假设数据顺序是 x, y, z)
    //        // 你可能需要根据实际传感器的轴向调整这里的顺序，比如 (z, y, x)
    //        float rawX = rawData[i, 0];
    //        float rawY = rawData[i, 1];
    //        float rawZ = rawData[i, 2];

    //        // 2. 转换为 Vector3 并应用灵敏度系数
    //        Vector3 force = new Vector3(rawX, rawY, rawZ) * forceSensitivity;

    //        convertedForces[i] = force;
    //    }

    //    // 3. 调用你现有的方法更新视觉
    //    UpdateSensorData(convertedForces);
    //}
}