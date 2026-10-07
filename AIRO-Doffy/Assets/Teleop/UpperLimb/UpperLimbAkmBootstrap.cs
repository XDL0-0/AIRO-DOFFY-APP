using UnityEngine;

/// <summary>
/// AKM 启动自举：场景加载后自动创建 UpperLimbAKM 管理器与 UI（幂等，已存在则跳过）。
/// 遵循项目"运行时自建 manager"的先例（TeleopControlModeManager 等），
/// 无需改动任何场景文件或现有脚本。
/// </summary>
public static class UpperLimbAkmBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Object.FindAnyObjectByType<UpperLimbAkmManager>() == null)
        {
            new GameObject("UpperLimbAKM").AddComponent<UpperLimbAkmManager>();
        }

        if (Object.FindAnyObjectByType<UpperLimbAkmUi>() == null)
        {
            new GameObject("UpperLimbAKM UI").AddComponent<UpperLimbAkmUi>();
        }
    }
}
