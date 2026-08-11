using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// TactAR 功能场景配置工具:
///   1. 删除 V0.6.0 场景中的 42 点触觉可视化
///   2. 关闭 VideoWindowManager 的 8012 tactile 接收
///   3. 创建 TactAR 风格校准层级:
///      Calibration(CalibrationTool + TCPPoseReceiver)
///      ├── Coord (CalibrationGizmo: 白球 + 三色轴)
///      ├── LeftTCP  → LeftForce  (ForceArrow)
///      └── RightTCP → RightForce (ForceArrow)
///      ForceSensorReceiver (独立根, 监听 8012)
///      TCP/力箭头随校准自动移动(因为是 Calibration 子物体)
///
/// 用法:Unity 菜单 Tools > TactAR Features > Configure V0.6.0 Scene
/// 或批处理:Unity -batchmode -quit -projectPath <path> -executeMethod TactARSceneSetup.ConfigureV06
/// </summary>
public static class TactARSceneSetup
{
    private const string ScenePath = "Assets/Scenes/V0.6.0 Realtime_Force.unity";

    private static readonly string[] TactileObjectsToDelete =
    {
        "TactileSensorRoot",
        "TactileUIManager",
        "Tactile UI Button",
        "Tactile IP"
    };

    [MenuItem("Tools/TactAR Features/Configure V0.6.0 Scene")]
    public static void ConfigureV06()
    {
        // 1. 打开场景(已打开则复用)
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 2. 删除 42 点触觉可视化相关物体
        DeleteTactileObjects();

        // 3. 8012 已改发 6D 力,关闭 VideoWindowManager 的 tactile 接收(避免端口冲突)
        DisableWindowManagerTactile();

        // 3.1 ForceSensorReceiver 备份端口改为 8013(主端口 8012 已被 TCPPoseReceiver 占用)
        ForceReceiverTo8013();

        // 4. 创建校准层级
        CreateCalibrationHierarchy();

        // 5. 创建 TCP 随动层级 + 力箭头
        CreateTcpAndForceHierarchy();

        // 5. 保存
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[TactARSceneSetup] V0.6.0 scene configured.");
    }

    private static void DeleteTactileObjects()
    {
        foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (obj.scene.IsValid() && obj.scene.isLoaded && obj.hideFlags == HideFlags.None &&
                TactileObjectsToDelete.Contains(obj.name))
            {
                Debug.Log($"[TactARSceneSetup] Deleting {obj.name}");
                Object.DestroyImmediate(obj);
            }
        }
    }

    /// <summary>
    /// 8012 口已改发 6D 力并由 ForceSensorReceiver 接收,关闭 VideoWindowManager 的
    /// tactile 接收,避免两个 UdpClient 绑定同一端口(幂等)。
    /// </summary>
    private static void DisableWindowManagerTactile()
    {
        UdpWindowManager wm = Object.FindAnyObjectByType<UdpWindowManager>();
        if (wm == null)
            return;

        SerializedObject so = new SerializedObject(wm);
        SerializedProperty p = so.FindProperty("receiveTactile");
        if (p != null && p.boolValue)
        {
            p.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[TactARSceneSetup] VideoWindowManager.receiveTactile disabled (8012 → ForceSensorReceiver).");
        }
    }

    /// <summary>ForceSensorReceiver 备份端口设为 8013(主端口 8012 已被 TCPPoseReceiver 使用;幂等)。</summary>
    private static void ForceReceiverTo8013()
    {
        ForceSensorReceiver receiver = Object.FindAnyObjectByType<ForceSensorReceiver>();
        if (receiver == null)
            return;

        SerializedObject so = new SerializedObject(receiver);
        SerializedProperty p = so.FindProperty("listenPort");
        if (p != null && p.intValue != 8013)
        {
            p.intValue = 8013;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[TactARSceneSetup] ForceSensorReceiver.listenPort → 8013 (backup, main is 8012).");
        }
    }

    /// <summary>
    /// 创建或重建力箭头挂载点(幂等,自动清理旧结构)。
    /// </summary>
    private static GameObject EnsureForceArrow(GameObject tcpParent, string name)
    {
        Transform existing = tcpParent.transform.Find(name);
        if (existing != null)
        {
            // 旧结构没有 TcpMarker/Arrow 容器 → 重建
            if (existing.Find("TcpMarker") == null)
            {
                Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }
            else
            {
                existing.gameObject.SetActive(true);
                return existing.gameObject;
            }
        }

        GameObject forceObj = new GameObject(name);
        forceObj.transform.SetParent(tcpParent.transform, false);
        ForceArrow arrow = forceObj.AddComponent<ForceArrow>();
        arrow.BuildVisuals();
        return forceObj;
    }

    private static void CreateCalibrationHierarchy()
    {
        GameObject calibration = GameObject.Find("Calibration");
        if (calibration == null)
        {
            calibration = new GameObject("Calibration");
        }

        CalibrationTool tool = calibration.GetComponent<CalibrationTool>();
        if (tool == null)
            tool = calibration.AddComponent<CalibrationTool>();

        // 强制重建 Coord,确保用最新的轴方向(X=红水平,Y=绿水平,Z=蓝垂直)
        Transform oldCoord = calibration.transform.Find("Coord");
        if (oldCoord != null) Object.DestroyImmediate(oldCoord.gameObject);

        GameObject coord = new GameObject("Coord");
        coord.transform.SetParent(calibration.transform, false);
        CalibrationGizmo gizmo = coord.AddComponent<CalibrationGizmo>();

        SerializedObject so = new SerializedObject(tool);
        so.FindProperty("coord").objectReferenceValue = coord.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        gizmo.EnsureVisuals();
        coord.SetActive(false);

        Debug.Log("[TactARSceneSetup] Calibration hierarchy created (Coord rebuilt with updated axes).");
    }

    /// <summary>
    /// 创建 TactAR 风格层级:LeftTCP/RightTCP 作为 Calibration 的直接子物体,
    /// TCPPoseReceiver 挂在 Calibration 上,校准后 TCP/力箭头自动跟随。
    /// 幂等、支持从旧 RobotTCP 结构迁移。
    /// </summary>
    private static void CreateTcpAndForceHierarchy()
    {
        GameObject calibration = GameObject.Find("Calibration");
        if (calibration == null)
        {
            Debug.LogError("[TactARSceneSetup] Calibration not found — run CreateCalibrationHierarchy first.");
            return;
        }

        // 先清理可能存在的旧左臂对象(单臂不需要)
        Transform oldLeftTcp = calibration.transform.Find("LeftTCP");
        if (oldLeftTcp != null) Object.DestroyImmediate(oldLeftTcp.gameObject);

        // 幂等:RightTCP 已正确配置则跳过(检查 TcpMarker 是否存在)
        Transform existingRight = calibration.transform.Find("RightTCP");
        if (existingRight != null && existingRight.Find("TcpMarker") != null)
        {
            Debug.Log("[TactARSceneSetup] TCP hierarchy already configured, skipping.");
            return;
        }

        // --- 清理旧 RobotTCP 结构(从 V0.6.0 初版迁移)---
        GameObject oldRobotTcp = GameObject.Find("RobotTCP");
        if (oldRobotTcp != null)
        {
            // 将子物体移到 Calibration 下再删除主干
            foreach (Transform child in oldRobotTcp.transform)
            {
                if (child.name == "LeftTCP" || child.name == "RightTCP")
                    child.SetParent(calibration.transform, false);
            }
            Object.DestroyImmediate(oldRobotTcp);
            Debug.Log("[TactARSceneSetup] Removed old RobotTCP root; TCP objects moved under Calibration.");
        }

        // --- 确保 RightTCP 存在(Calibration 下,单臂只用右侧)---
        GameObject rightTcp = calibration.transform.Find("RightTCP")?.gameObject;
        if (rightTcp == null)
        {
            rightTcp = new GameObject("RightTCP");
            rightTcp.transform.SetParent(calibration.transform, false);
        }

        // --- 力箭头挂载点(在 TCP 下, ForceArrow 自己创建 TcpMarker)---
        // 清理旧 LeftForce
        Transform oldLeftForce = rightTcp.transform.Find("LeftForce");
        if (oldLeftForce != null) Object.DestroyImmediate(oldLeftForce.gameObject);
        // 清理旧版独立 TcpMarker(现在由 ForceArrow 内部创建)
        Transform oldMarker = rightTcp.transform.Find("TcpMarker");
        if (oldMarker != null) Object.DestroyImmediate(oldMarker.gameObject);

        // TCP 坐标轴可视化(红=+X, 绿=+Y, 蓝=+Z, 通过 AxisVisualizer 子节点 +Z 90° 修正)
        var oldAxes = rightTcp.GetComponent<RobotTcpAxes>();
        if (oldAxes != null) Object.DestroyImmediate(oldAxes);
        rightTcp.AddComponent<RobotTcpAxes>();

        GameObject rightForce = EnsureForceArrow(rightTcp, "RightForce");

        // --- TCPPoseReceiver 挂在 Calibration 上(同时接管 TCP + Force)---
        TCPPoseReceiver tcpReceiver = calibration.GetComponent<TCPPoseReceiver>();
        if (tcpReceiver == null)
            tcpReceiver = calibration.AddComponent<TCPPoseReceiver>();

        // 接线(单臂:只接右侧,TCP+Force 同在 8012)
        SerializedObject tcpSo = new SerializedObject(tcpReceiver);
        tcpSo.FindProperty("listenPort").intValue = 8012;
        tcpSo.FindProperty("leftTCP").objectReferenceValue = null;
        tcpSo.FindProperty("rightTCP").objectReferenceValue = rightTcp.transform;
        tcpSo.FindProperty("leftForce").objectReferenceValue = null;
        tcpSo.FindProperty("rightForce").objectReferenceValue = rightForce.transform;
        tcpSo.ApplyModifiedPropertiesWithoutUndo();

        // --- ForceSensorReceiver(独立根,8013 JSON 备选 / 8014 TactAR 格式)---
        GameObject forceReceiver = GameObject.Find("ForceSensorReceiver");
        ForceSensorReceiver forceScript;
        if (forceReceiver == null)
        {
            forceReceiver = new GameObject("ForceSensorReceiver");
            forceScript = forceReceiver.AddComponent<ForceSensorReceiver>();
        }
        else
        {
            forceScript = forceReceiver.GetComponent<ForceSensorReceiver>();
            if (forceScript == null)
                forceScript = forceReceiver.AddComponent<ForceSensorReceiver>();
        }
        forceReceiver.SetActive(false); // 主流程走 8012,备选端口默认关闭

        SerializedObject forceSo = new SerializedObject(forceScript);
        forceSo.FindProperty("leftForce").objectReferenceValue = null;
        forceSo.FindProperty("rightForce").objectReferenceValue = rightForce.transform;
        forceSo.FindProperty("leftTCP").objectReferenceValue = null;
        forceSo.FindProperty("rightTCP").objectReferenceValue = rightTcp.transform;
        forceSo.FindProperty("listenPort").intValue = 8013;
        forceSo.FindProperty("forceTarget").intValue = (int)ForceSensorReceiver.ForceTarget.Right;
        forceSo.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log("[TactARSceneSetup] TCP + Force hierarchy created (TactAR-style: under Calibration).");
    }
}
