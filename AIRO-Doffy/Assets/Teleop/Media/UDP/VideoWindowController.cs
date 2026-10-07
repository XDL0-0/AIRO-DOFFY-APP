using System.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation and commands for one floating camera view.</summary>
public class VideoWindowController : MonoBehaviour
{
    public Button closeButton;
    public TMP_InputField portInputField;
    public Button resolutionButton;
    public TextMeshProUGUI resolutionButtonLabel;
    public TextMeshProUGUI textStatus;
    [SerializeField] private string IP;
    [SerializeField] private int port;
    private UdpSocketMultiHD receiver;
    private UdpWindowManager owner;
    private float nextStatus;
    public string IpAddress => IP;
    public int Port => port;
    public string Resolution { get; private set; } = "x1.0";

    private void Awake()
    {
        InitializeCameraInteraction();
        receiver = GetComponentInChildren<UdpSocketMultiHD>(true);
        owner = FindAnyObjectByType<UdpWindowManager>();
        if (closeButton != null) closeButton.onClick.AddListener(CloseWindow);
        if (resolutionButton != null) resolutionButton.onClick.AddListener(ChangeResolution);
        if (portInputField != null)
        {
            portInputField.onEndEdit.AddListener(UpdatePort);
            portInputField.onSelect.AddListener(OpenPortKeypad);
            portInputField.shouldHideSoftKeyboard = true;
        }
    }

    public void Initialize(string ip, int receivePort, VideoWindowController unused)
    {
        InitializeCameraInteraction();
        if (receiver == null) { Status("Camera renderer is missing"); return; }
        if (!IPAddress.TryParse(ip, out _) || receivePort < 1 || receivePort > 65535)
        { Status("Invalid camera endpoint"); return; }
        IP = ip;
        port = receivePort;
        receiver.Initialize(ip, port);
        if (portInputField != null) portInputField.SetTextWithoutNotify(port.ToString());
        SetResolution(Resolution);
        Status("Waiting for camera on " + port);
    }

    /// <summary>Wires camera presentation independently of UDP endpoint initialization.</summary>
    public void InitializeCameraInteraction()
    {
        RefreshControlReadability();
        var rect = transform as RectTransform;
        if (rect != null) Doffy.UI.CameraPanelInteraction.Ensure(rect);
    }

    /// <summary>Keep compact camera controls legible after applying the general UI palette.</summary>
    public void RefreshControlReadability()
    {
        if (closeButton != null)
        {
            var label = closeButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "X";
                FormatControlText(label, 42f);
                label.margin = new Vector4(4f, 2f, 4f, 2f);
            }
        }

        if (portInputField == null) return;
        // TMP_InputField owns the point size and can reapply it as the value changes.
        portInputField.pointSize = 32f;
        var inputRect = portInputField.transform as RectTransform;
        if (inputRect != null)
            inputRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 60f);
        var viewport = portInputField.textViewport;
        if (viewport != null)
        {
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(6f, 4f);
            viewport.offsetMax = new Vector2(-6f, -4f);
        }
        if (portInputField.textComponent != null)
            FormatControlText(portInputField.textComponent, 32f);
        if (portInputField.placeholder is TMP_Text placeholder)
        {
            placeholder.text = "Port";
            FormatControlText(placeholder, 32f);
            placeholder.color = Doffy.UI.WorkspaceTheme.Muted;
        }
    }

    private static void FormatControlText(TMP_Text label, float size)
    {
        label.color = Doffy.UI.WorkspaceTheme.Text;
        label.fontSize = size;
        label.enableAutoSizing = false;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Midline;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.margin = Vector4.zero;
        label.raycastTarget = false;
    }

    private void OpenPortKeypad(string unused)
    {
        GetComponent<Doffy.UI.CameraPanelInteraction>()?.OpenPortKeypad(portInputField);
    }

    private void UpdatePort(string value)
    {
        if (!int.TryParse(value, out int candidate) || candidate < 1 || candidate > 65535 ||
            (owner != null && !owner.IsPortAvailable(candidate, this)))
        {
            Status("Choose an unused port (1–65535)");
            if (portInputField != null) portInputField.SetTextWithoutNotify(port.ToString());
            return;
        }
        Initialize(IP, candidate, this);
    }

    public void UpdateIpAddress(string value) => Initialize(value, port, this);
    public void SetResolution(string value)
    {
        Resolution = value == "x1.5" || value == "x2.0" ? value : "x1.0";
        if (resolutionButtonLabel != null) resolutionButtonLabel.text = Resolution;
        receiver?.SetZoom(Resolution == "x1.5" ? 1.5f : Resolution == "x2.0" ? 2 : 1);
    }
    private void ChangeResolution() => SetResolution(Resolution == "x1.0" ? "x1.5" : Resolution == "x1.5" ? "x2.0" : "x1.0");
    private void CloseWindow()
    {
        if (owner != null) owner.CloseWindow(this);
        else Destroy(gameObject);
    }
    private void Update()
    {
        if (receiver == null || Time.unscaledTime < nextStatus) return;
        nextStatus = Time.unscaledTime + .25f;
        string error = receiver.LastError;
        Status(!string.IsNullOrEmpty(error) ? error : receiver.FramesPresented == 0 ? "Waiting for camera on " + port :
            Time.unscaledTime - receiver.LastFrameAt > 1.5f ? "Camera paused · " + port : "LIVE · " + port);
    }
    private void Status(string text) { if (textStatus != null) textStatus.text = text; }
    private void OnDestroy()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(CloseWindow);
        if (resolutionButton != null) resolutionButton.onClick.RemoveListener(ChangeResolution);
        if (portInputField != null)
        {
            portInputField.onEndEdit.RemoveListener(UpdatePort);
            portInputField.onSelect.RemoveListener(OpenPortKeypad);
        }
    }
}
