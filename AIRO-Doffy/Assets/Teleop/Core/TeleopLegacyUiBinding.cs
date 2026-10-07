using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Presentation adapter for serialized classic scene references. Keeping these
/// references in a helper preserves their AppManager field names/scene wiring
/// while keeping session decisions independent from UI text.
/// </summary>
public sealed class TeleopLegacyUiBinding
{
    private readonly TMP_InputField ipInput;
    private readonly TMP_InputField trackInput;
    private readonly TextMeshProUGUI status;
    private readonly TextMeshProUGUI sessionState;
    private readonly Button startButton;
    private readonly Button recalibrateButton;
    private readonly Button controlButton;
    private readonly TextMeshProUGUI controlLabel;
    private readonly Button repositionButton;
    private readonly Toggle debugToggle;
    private readonly Button webRtcButton;
    private readonly TextMeshProUGUI webRtcLabel;
    private readonly GameObject singlePanel;
    private readonly GameObject[] dualPanels;
    private readonly GameObject[] triPanels;
    private readonly Button addUdpButton;
    private readonly TextMeshProUGUI versionLabel;
    private readonly GameObject menu;

    public TeleopLegacyUiBinding(TMP_InputField ipInput, TMP_InputField trackInput,
        TextMeshProUGUI status, TextMeshProUGUI sessionState, Button startButton,
        Button recalibrateButton, Button controlButton, TextMeshProUGUI controlLabel,
        Button repositionButton, Toggle debugToggle, Button webRtcButton,
        TextMeshProUGUI webRtcLabel, GameObject singlePanel, GameObject[] dualPanels,
        GameObject[] triPanels, Button addUdpButton, TextMeshProUGUI versionLabel,
        GameObject menu)
    {
        this.ipInput = ipInput;
        this.trackInput = trackInput;
        this.status = status;
        this.sessionState = sessionState;
        this.startButton = startButton;
        this.recalibrateButton = recalibrateButton;
        this.controlButton = controlButton;
        this.controlLabel = controlLabel;
        this.repositionButton = repositionButton;
        this.debugToggle = debugToggle;
        this.webRtcButton = webRtcButton;
        this.webRtcLabel = webRtcLabel;
        this.singlePanel = singlePanel;
        this.dualPanels = dualPanels;
        this.triPanels = triPanels;
        this.addUdpButton = addUdpButton;
        this.versionLabel = versionLabel;
        this.menu = menu;
    }

    public void Attach(UnityAction<string> ipChanged, UnityAction<string> trackChanged,
        UnityAction webRtcClicked, UnityAction recalibrateClicked,
        UnityAction controlClicked, UnityAction repositionClicked)
    {
        if (ipInput != null) { ipInput.onValueChanged.RemoveListener(ipChanged); ipInput.onValueChanged.AddListener(ipChanged); }
        if (trackInput != null) { trackInput.onEndEdit.RemoveListener(trackChanged); trackInput.onEndEdit.AddListener(trackChanged); }
        if (webRtcButton != null) { webRtcButton.onClick.RemoveListener(webRtcClicked); webRtcButton.onClick.AddListener(webRtcClicked); }
        if (recalibrateButton != null) { recalibrateButton.onClick.RemoveListener(recalibrateClicked); recalibrateButton.onClick.AddListener(recalibrateClicked); }
        if (controlButton != null) { controlButton.onClick.RemoveListener(controlClicked); controlButton.onClick.AddListener(controlClicked); }
        if (repositionButton != null) { repositionButton.onClick.RemoveListener(repositionClicked); repositionButton.onClick.AddListener(repositionClicked); }
    }

    public void Detach(UnityAction<string> ipChanged, UnityAction<string> trackChanged,
        UnityAction webRtcClicked, UnityAction recalibrateClicked,
        UnityAction controlClicked, UnityAction repositionClicked)
    {
        if (ipInput != null) ipInput.onValueChanged.RemoveListener(ipChanged);
        if (trackInput != null) trackInput.onEndEdit.RemoveListener(trackChanged);
        if (webRtcButton != null) webRtcButton.onClick.RemoveListener(webRtcClicked);
        if (recalibrateButton != null) recalibrateButton.onClick.RemoveListener(recalibrateClicked);
        if (controlButton != null) controlButton.onClick.RemoveListener(controlClicked);
        if (repositionButton != null) repositionButton.onClick.RemoveListener(repositionClicked);
    }

    public void SetVersion(string version)
    {
        if (versionLabel != null) versionLabel.text = version;
    }

    public void SyncInputs(string host, int tracks)
    {
        if (ipInput != null && ipInput.text != host) ipInput.text = host;
        string count = tracks.ToString();
        if (trackInput != null && trackInput.text != count) trackInput.text = count;
    }

    public void SetMenuVisible(bool visible)
    {
        if (menu != null) menu.SetActive(visible);
    }

    public void SetAddUdpInteractable(bool interactable)
    {
        if (addUdpButton != null) addUdpButton.interactable = interactable;
    }

    public void SetWebRtcEnabled(bool enabled)
    {
        if (webRtcLabel != null) webRtcLabel.text = enabled ? "Disable WebRTC" : "Enable WebRTC";
    }

    public void SetPanels(int count)
    {
        if (singlePanel != null) singlePanel.SetActive(count == 1);
        SetPanelGroup(dualPanels, count == 2);
        SetPanelGroup(triPanels, count == 3);
    }

    public void SetDebugVisible(bool visible)
    {
        if (debugToggle != null && debugToggle.isOn != visible) debugToggle.isOn = visible;
    }

    public void SetState(TeleopSessionState state, bool streaming, bool needsRecalibration,
        string startText)
    {
        if (sessionState != null) sessionState.text = state.ToString();
        if (recalibrateButton != null)
            recalibrateButton.gameObject.SetActive(streaming && needsRecalibration);
        SetStartText(startText);
    }

    public void SetStartText(string text)
    {
        SetStartTextInternal(text);
    }

    public void SetStatus(string message, TeleopSessionStateMachine.StatusKind kind)
    {
        Color color = kind == TeleopSessionStateMachine.StatusKind.Error ? Color.red :
            kind == TeleopSessionStateMachine.StatusKind.Warning ? Color.yellow : Color.green;
        if (status != null)
        {
            status.text = message;
            status.color = color;
        }
        if (startButton != null)
            startButton.interactable = kind != TeleopSessionStateMachine.StatusKind.Error;
    }

    public void SetControlMode(TeleopControlModeManager.ControlMode mode)
    {
        if (controlLabel != null)
            controlLabel.text = mode == TeleopControlModeManager.ControlMode.Mirror
                ? "Control Mode\nMirror" : "Control Mode\nView";
        if (repositionButton != null)
            repositionButton.interactable = mode == TeleopControlModeManager.ControlMode.View;
    }

    private void SetStartTextInternal(string text)
    {
        if (startButton == null) return;
        TextMeshProUGUI label = startButton.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = text;
    }

    private static void SetPanelGroup(GameObject[] panels, bool active)
    {
        if (panels == null) return;
        foreach (GameObject panel in panels)
            if (panel != null) panel.SetActive(active);
    }
}
