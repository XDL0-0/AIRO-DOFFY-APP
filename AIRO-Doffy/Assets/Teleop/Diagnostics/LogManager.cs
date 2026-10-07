using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局日志管理器（单例）。
/// 按来源分类存储日志，方便 HUD 分屏显示。
/// 用法: LogManager.Log("Right", "UDP Ready");
/// </summary>
public class LogManager : MonoBehaviour
{
    public static LogManager Instance { get; private set; }

    private readonly Dictionary<string, List<string>> _logs = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>向指定来源追加一条日志（同时输出到 Console）。</summary>
    public static void Log(string source, string message)
    {
        if (Instance != null)
        {
            if (!Instance._logs.ContainsKey(source))
                Instance._logs[source] = new List<string>();
            Instance._logs[source].Add(message);
        }
        Debug.Log($"[{source}] {message}");
    }

    /// <summary>获取某个来源的全部日志。</summary>
    public List<string> GetMessages(string source)
    {
        return _logs.TryGetValue(source, out var list) ? list : new List<string>();
    }
}
