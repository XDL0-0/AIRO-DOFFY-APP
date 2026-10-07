using TMPro;
using UnityEngine;

/// <summary>
/// Recording command adapter for legacy UDP port 8003. The local state is an
/// explicit request state because the classic protocol has no acknowledgement;
/// it is never inferred from a label and a missing label/socket is harmless.
/// </summary>
public class RecordingController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI recordButtonText;
    [SerializeField] private UdpSocket udpSocket;

    public bool IsRecording { get; private set; }

    private void Start()
    {
        if (udpSocket == null)
            udpSocket = FindAnyObjectByType<UdpSocket>();
        UpdateLabel();
    }

    /// <summary>Toggle the requested recording state and send the old command.</summary>
    public void Recording()
    {
        IsRecording = !IsRecording;
        Send(IsRecording ? "Start" : "Stop");
        UpdateLabel();
    }

    /// <summary>Ask the recorder to undo its last accepted recording action.</summary>
    public void Undo()
    {
        // Undo discards the active Python-side buffer. Do not follow it with
        // Stop: the legacy recorder would export that buffer before undoing it.
        IsRecording = false;
        Send("Undo");
        UpdateLabel();
    }

    /// <summary>Idempotent stop used by AppManager when a teleop session ends.</summary>
    public void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        Send("Stop");
        UpdateLabel();
    }

    private void Send(string command)
    {
        if (udpSocket != null)
            udpSocket.SendData8003(command);
    }

    private void UpdateLabel()
    {
        if (recordButtonText != null)
            recordButtonText.text = IsRecording ? "Stop Record" : "Start Record";
    }
}
