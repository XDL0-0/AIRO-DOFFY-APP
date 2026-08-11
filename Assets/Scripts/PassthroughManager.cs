using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR; // 使用 Unity 的 XR 命名空间

public class PassthroughToggle : MonoBehaviour
{
    public OVRPassthroughLayer passthroughLayer; // Oculus Passthrough Layer
    public GameObject passthroughScreen;          // 用来显示 Passthrough 的虚拟屏幕
    public Button toggleButton;                   // UI 按钮
    public float distanceFromCamera = 2f;         // 屏幕距离用户的距离

    private bool isPassthroughActive = false;

    void Start()
    {
        // 确保 Passthrough 一开始是关闭的
        passthroughLayer.enabled = false;
        passthroughScreen.SetActive(false);

        // 绑定按钮点击事件
        toggleButton.onClick.AddListener(TogglePassthrough);
    }

    void Update()
    {
        if (isPassthroughActive)
        {
            // 让虚拟屏幕跟随头盔
            Vector3 headPosition = Camera.main.transform.position; // 获取头部位置
            Vector3 forwardDirection = Camera.main.transform.forward; // 获取头部朝向
            passthroughScreen.transform.position = headPosition + forwardDirection * distanceFromCamera;
            passthroughScreen.transform.rotation = Camera.main.transform.rotation; // 让屏幕与头部方向对齐
        }
    }

    void TogglePassthrough()
    {
        // 切换 Passthrough 的显示状态
        isPassthroughActive = !isPassthroughActive;
        passthroughLayer.enabled = isPassthroughActive;
        passthroughScreen.SetActive(isPassthroughActive);
    }
}
