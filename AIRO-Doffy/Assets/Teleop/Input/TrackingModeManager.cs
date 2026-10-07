using TMPro;
using UnityEngine;

/// <summary>
/// Selects exactly one 8001 posture sender. The mode is explicit state shared
/// with AppManager, persisted under the classic key, and never read from UI text.
/// </summary>
public class TrackingModeManager : MonoBehaviour
{
    public enum TrackingMode { Controllers, Hands }

    [Header("Senders")]
    [SerializeField] private DualControllerSender dualControllerSender;
    [SerializeField] private HandTrackingSender handTrackingSender;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI modeText;

    [Header("Default")]
    [SerializeField] private TrackingMode currentMode = TrackingMode.Controllers;

    public TrackingMode CurrentMode => currentMode;

    private void Start()
    {
        int saved = PlayerPrefs.GetInt("cfg_trackingMode", (int)currentMode);
        if (AppManager.Instance != null)
            saved = AppManager.Instance.TrackingMode;
        SetTrackingModeInternal(saved, false);
    }

    public void ToggleTrackingMode()
    {
        SetTrackingMode(currentMode == TrackingMode.Controllers ? 1 : 0);
    }

    public void SetControllersMode() { SetTrackingMode(0); }
    public void SetHandsMode() { SetTrackingMode(1); }

    /// <summary>Public integer API used by the new workspace UI.</summary>
    public void SetTrackingMode(int mode)
    {
        SetTrackingModeInternal(mode, true);
    }

    private void SetTrackingModeInternal(int mode, bool persist)
    {
        if ((int)currentMode != TeleopRuntimeSettings.ClampTrackingMode(mode))
            AppManager.Instance?.HandleTrackingLost("Input mode changed");
        currentMode = (TrackingMode)TeleopRuntimeSettings.ClampTrackingMode(mode);
        ApplyMode();

        if (persist)
        {
            PlayerPrefs.SetInt("cfg_trackingMode", (int)currentMode);
            PlayerPrefs.Save();
        }

        // Avoid calling AppManager.SetTrackingMode here: that would recurse back
        // into this component. This adoption hook updates only its snapshot.
        AppManager.Instance?.AdoptTrackingModeFromLegacy((int)currentMode, persist);
    }

    private void ApplyMode()
    {
        bool controllers = currentMode == TrackingMode.Controllers;
        if (dualControllerSender != null)
            dualControllerSender.SetSendingEnabled(controllers);
        if (handTrackingSender != null)
            handTrackingSender.SetSendingEnabled(!controllers);

        if (currentMode == TrackingMode.Hands)
        {
            UpperLimbAkmManager wrm = FindAnyObjectByType<UpperLimbAkmManager>();
            if (wrm != null)
                wrm.SetWrmEnabled(false);
        }

        if (modeText != null)
            modeText.text = controllers ? "Controllers" : "Hands";
    }
}
