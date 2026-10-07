using UnityEngine;

/// <summary>
/// 应用级控制：退出应用。
/// 绑定 UI 按钮 OnClick → QuitApp()
/// </summary>
public class AppController : MonoBehaviour
{
    public void QuitApp()
    {
        Application.Quit();
    }
}
