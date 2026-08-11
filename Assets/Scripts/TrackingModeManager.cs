using TMPro;
using UnityEngine;

/// <summary>
/// 在手部追踪与控制器追踪之间切换，保证同一时刻只通过 8001 通道发送一种姿态数据。
/// 新增: 集成 AppManager，切换时自动持久化到 PlayerPrefs。
/// </summary>
public class TrackingModeManager : MonoBehaviour
{
    public enum TrackingMode { Controllers, Hands }

    [Header("Senders")]
    [SerializeField] private DualControllerSender dualControllerSender;
    [SerializeField] private HandTrackingSender   handTrackingSender;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI modeText;

    [Header("Default")]
    [SerializeField] private TrackingMode currentMode = TrackingMode.Controllers;

    private void Start()
    {
        int saved = PlayerPrefs.GetInt("cfg_trackingMode", (int)currentMode);
        currentMode = (TrackingMode)saved;
        ApplyMode();
    }

    public void ToggleTrackingMode()
    {
        currentMode = currentMode == TrackingMode.Controllers
            ? TrackingMode.Hands
            : TrackingMode.Controllers;
        ApplyMode();
        PlayerPrefs.SetInt("cfg_trackingMode", (int)currentMode);
        PlayerPrefs.Save();
    }

    public void SetControllersMode() { currentMode = TrackingMode.Controllers; ApplyMode(); }
    public void SetHandsMode()       { currentMode = TrackingMode.Hands;       ApplyMode(); }

    private void ApplyMode()
    {
        if (dualControllerSender != null)
            dualControllerSender.SetSendingEnabled(currentMode == TrackingMode.Controllers);
        if (handTrackingSender != null)
            handTrackingSender.SetSendingEnabled(currentMode == TrackingMode.Hands);
        if (modeText != null)
            modeText.text = currentMode == TrackingMode.Controllers ? "Controllers" : "Hands";
    }
}
