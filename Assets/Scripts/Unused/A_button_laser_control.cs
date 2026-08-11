using UnityEngine;
using Oculus.Interaction.Input; // 确保使用 Oculus SDK 的命名空间

public class AButtonLaserControl : MonoBehaviour
{
    public GameObject rayInteractor; // 绑定你的 Ray 对象（通常是手柄上的 Line Renderer）

    void Update()
    {
        // 监听 A 按键 (右手控制器)
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
        {
            rayInteractor.SetActive(false); // 禁用射线
        }

        if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
        {
            rayInteractor.SetActive(true); // 重新启用射线
        }
    }
}
