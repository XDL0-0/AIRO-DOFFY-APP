using UnityEngine;
using UnityEngine.UI;

public class WindowToggle : MonoBehaviour
{
    public CanvasGroup UserManual;
    public Button toggleButton;

    private bool isVisible = false;

    void Start()
    {

        HidePanel();

        toggleButton.onClick.AddListener(ToggleWindow);


    }

    void ToggleWindow()
    {
        isVisible = !isVisible;
        if (isVisible)
        {
            ShowPanel();
        }
        else
        {
            HidePanel();
        }
    }

    void ShowPanel()
    {
        UserManual.alpha = 1f;  // 显示
        UserManual.blocksRaycasts = true;  // 阻挡后面物体交互
    }

    void HidePanel()
    {
        UserManual.alpha = 0f;  // 隐藏
        UserManual.blocksRaycasts = false;  // 允许后面物体交互
    }
}
