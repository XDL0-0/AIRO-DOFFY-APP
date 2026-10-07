using System;

/// <summary>
/// Unity-free session state and send-safety gate. UI labels are projections of
/// this state; no command path is allowed to infer state from a button label.
/// </summary>
public sealed class TeleopSessionStateMachine
{
    public enum StatusKind
    {
        Normal,
        Warning,
        Error
    }

    public TeleopSessionState State { get; private set; } = TeleopSessionState.Idle;
    public bool IsStreaming { get; private set; }
    public bool NeedsRecalibration { get; private set; }
    public string StatusText { get; private set; } = "System Ready";
    public string LastError { get; private set; } = string.Empty;
    public StatusKind Status { get; private set; } = StatusKind.Normal;

    // Starting/video negotiation are still an active teleop session, matching
    // the classic sender behavior. Tracking loss and stopping always close it.
    public bool CanSend => IsStreaming && !NeedsRecalibration &&
                           State != TeleopSessionState.TrackingLost &&
                           State != TeleopSessionState.Stopping;

    public event Action Changed;

    public void BeginStart()
    {
        IsStreaming = true;
        NeedsRecalibration = false;
        Set(TeleopSessionState.Starting, "Starting Streaming", StatusKind.Normal, string.Empty);
    }

    public void BeginVideoConnecting()
    {
        if (!IsStreaming || NeedsRecalibration || State == TeleopSessionState.TrackingLost) return;
        Set(TeleopSessionState.VideoConnecting, "Teleop Active - Video Connecting", StatusKind.Warning, string.Empty);
    }

    public void MarkStreaming(string message = "Streaming Active")
    {
        if (!IsStreaming || NeedsRecalibration || State == TeleopSessionState.TrackingLost) return;
        Set(TeleopSessionState.Streaming, message, StatusKind.Normal, string.Empty);
    }

    public void MarkVideoUnavailable(string reason)
    {
        if (!IsStreaming || NeedsRecalibration || State == TeleopSessionState.TrackingLost) return;
        string suffix = string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim();
        Set(TeleopSessionState.Streaming, "Streaming Active - Video Unavailable" + suffix,
            StatusKind.Warning, string.Empty);
    }

    public void BeginStopping()
    {
        if (!IsStreaming && State == TeleopSessionState.Idle) return;
        IsStreaming = false;
        NeedsRecalibration = false;
        Set(TeleopSessionState.Stopping, "Stopping Streaming", StatusKind.Warning, string.Empty);
    }

    public void CompleteStop()
    {
        IsStreaming = false;
        NeedsRecalibration = false;
        Set(TeleopSessionState.Idle, "System Ready", StatusKind.Normal, string.Empty);
    }

    public void MarkDisconnected(string reason)
    {
        IsStreaming = false;
        NeedsRecalibration = false;
        string suffix = string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim();
        Set(TeleopSessionState.Idle, "Disconnected" + suffix, StatusKind.Warning, string.Empty);
    }

    public void MarkVideoIssue(string reason)
    {
        if (!IsStreaming || NeedsRecalibration || State == TeleopSessionState.TrackingLost) return;
        string suffix = string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim();
        Set(TeleopSessionState.Streaming, "Video issue" + suffix, StatusKind.Warning, string.Empty);
    }

    public void MarkTrackingLost(string reason, bool pauseControl)
    {
        if (!IsStreaming) return;
        // A tracking callback must require a deliberate recalibration even when
        // a legacy config disabled its old pause/recalibration flags. Automatic
        // recovery can otherwise resume robot commands after focus returns.
        // `pauseControl` remains in the signature for the classic call contract;
        // safety loss always closes the send gate in the refactored session.
        NeedsRecalibration = true;
        string suffix = string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim();
        Set(TeleopSessionState.TrackingLost, "Tracking paused" + suffix + ". Recalibrate.",
            StatusKind.Warning, string.Empty);
    }

    public void MarkTrackingAvailable(string reason)
    {
        if (!IsStreaming || State != TeleopSessionState.TrackingLost) return;
        string suffix = string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim();
        Set(State, "Tracking back" + suffix + ". Recalibrate to resume.",
            StatusKind.Warning, string.Empty);
    }

    public void MarkRecalibrationFailed(string reason = "Recalibration failed")
    {
        if (!IsStreaming)
        {
            NeedsRecalibration = true;
            Set(TeleopSessionState.Idle, reason, StatusKind.Error, reason);
            return;
        }

        NeedsRecalibration = true;
        Set(TeleopSessionState.TrackingLost, reason, StatusKind.Error, reason);
    }

    public void MarkRecalibrated(bool streaming)
    {
        NeedsRecalibration = false;
        LastError = string.Empty;
        if (streaming)
            Set(TeleopSessionState.Streaming, "Streaming Active", StatusKind.Normal, string.Empty);
        else
            Set(TeleopSessionState.Idle, "Teleop Frame Calibrated", StatusKind.Normal, string.Empty);
    }

    public void MarkConnectionReady()
    {
        if (IsStreaming) return;
        Set(TeleopSessionState.Idle, "Connection ready", StatusKind.Normal, string.Empty);
    }

    public void MarkConnectionChanged()
    {
        if (!IsStreaming) return;
        NeedsRecalibration = true;
        Set(TeleopSessionState.TrackingLost,
            "Connection changed. Recalibrate before sending.", StatusKind.Warning, string.Empty);
    }

    public void SetError(string message)
    {
        string safe = string.IsNullOrWhiteSpace(message) ? "Error" : message.Trim();
        LastError = safe;
        Set(State, safe, StatusKind.Error, safe);
    }

    public void ClearError()
    {
        if (LastError.Length == 0) return;
        LastError = string.Empty;
        StatusKind kind = State == TeleopSessionState.TrackingLost
            ? StatusKind.Warning
            : StatusKind.Normal;
        string text = State == TeleopSessionState.TrackingLost
            ? "Tracking paused. Recalibrate to resume."
            : StatusForState(State);
        Status = kind;
        StatusText = text;
        OnChanged();
    }

    private void Set(TeleopSessionState state, string statusText, StatusKind status, string error)
    {
        State = state;
        StatusText = string.IsNullOrWhiteSpace(statusText) ? state.ToString() : statusText;
        Status = status;
        LastError = error ?? string.Empty;
        OnChanged();
    }

    private static string StatusForState(TeleopSessionState state)
    {
        switch (state)
        {
            case TeleopSessionState.Starting: return "Starting Streaming";
            case TeleopSessionState.VideoConnecting: return "Teleop Active - Video Connecting";
            case TeleopSessionState.Streaming: return "Streaming Active";
            case TeleopSessionState.Stopping: return "Stopping Streaming";
            case TeleopSessionState.TrackingLost: return "Tracking paused. Recalibrate to resume.";
            default: return "System Ready";
        }
    }

    private void OnChanged()
    {
        Changed?.Invoke();
    }
}
