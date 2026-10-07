using System;
using System.Collections.Generic;

/// <summary>
/// Focused, Unity-free acceptance checks for the session core. The project
/// source check excludes this folder; the same class is run by the validation
/// command with a small .NET harness.
/// </summary>
public static class TeleopCoreTests
{
    public static void RunAll()
    {
        ValidateConnectionInputs();
        LoadAndSaveLegacyPreferences();
        EnforceTrackingSafetyGate();
    }

    private static void ValidateConnectionInputs()
    {
        string error;
        Assert(!TeleopConnectionValidator.TryValidate("", 1, out error), "empty host rejected");
        Assert(!TeleopConnectionValidator.TryValidate("not-an-ip", 1, out error), "malformed host rejected");
        Assert(!TeleopConnectionValidator.TryValidate("::1", 1, out error), "IPv6 host rejected by IPv4 transports");
        Assert(!TeleopConnectionValidator.TryValidate("127.0.0.1", 0, out error), "zero tracks rejected");
        Assert(!TeleopConnectionValidator.TryValidate("127.0.0.1", 4, out error), "four tracks rejected");
        Assert(TeleopConnectionValidator.TryValidate(" 127.0.0.1 ", 3, out error), "valid host and tracks accepted");
    }

    private static void LoadAndSaveLegacyPreferences()
    {
        var prefs = new MemoryPreferences();
        prefs.SetString(TeleopSettingsStore.HostKey, "bad host");
        prefs.SetInt(TeleopSettingsStore.TrackingModeKey, 12);
        prefs.SetFloat(TeleopSettingsStore.SendRateKey, float.NaN);
        prefs.SetInt(TeleopSettingsStore.BinaryProtocolKey, 0);
        prefs.SetInt(TeleopSettingsStore.WebRtcTracksKey, 12);

        var defaults = TeleopRuntimeSettings.Defaults();
        TeleopRuntimeSettings loaded = new TeleopSettingsStore().Load(defaults, prefs);
        Assert(loaded.ServerHost == defaults.ServerHost, "invalid saved host falls back to asset host");
        Assert(loaded.TrackingMode == 1, "tracking mode is clamped");
        Assert(loaded.SendRateHz == defaults.SendRateHz, "invalid send rate falls back");
        Assert(!loaded.UseBinaryProtocol, "binary preference is restored");
        Assert(loaded.WebRtcTrackCount == 3, "track count is clamped");

        loaded.ServerHost = "192.168.1.20";
        loaded.TrackingMode = 0;
        loaded.WebRtcTrackCount = 2;
        new TeleopSettingsStore().Save(loaded, prefs);
        Assert(prefs.GetString(TeleopSettingsStore.HostKey, "") == "192.168.1.20", "host saved under legacy key");
        Assert(prefs.GetInt(TeleopSettingsStore.WebRtcTracksKey, 0) == 2, "track count saved under legacy key");
        Assert(prefs.SaveCount == 1, "preference store saved once");
    }

    private static void EnforceTrackingSafetyGate()
    {
        var session = new TeleopSessionStateMachine();
        Assert(!session.CanSend, "idle session cannot send");
        session.BeginStart();
        Assert(session.IsStreaming && session.CanSend, "active startup retains classic send behavior");
        session.BeginVideoConnecting();
        Assert(session.CanSend, "video negotiation does not stop teleop data");
        session.MarkTrackingLost("focus lost", true);
        Assert(session.NeedsRecalibration && !session.CanSend, "tracking loss closes send gate");
        session.MarkStreaming();
        session.MarkVideoUnavailable("late video result");
        session.MarkVideoIssue("late video event");
        Assert(session.State == TeleopSessionState.TrackingLost && session.NeedsRecalibration && !session.CanSend,
            "late video completion cannot clear tracking recovery gate");
        session.MarkTrackingAvailable("focus acquired");
        Assert(session.State == TeleopSessionState.TrackingLost && !session.CanSend,
            "tracking acquisition does not auto-recover control");
        session.MarkRecalibrated(true);
        Assert(session.State == TeleopSessionState.Streaming && session.CanSend,
            "explicit recalibration reopens control");

        var configuredWithoutPause = new TeleopSessionStateMachine();
        configuredWithoutPause.BeginStart();
        configuredWithoutPause.MarkTrackingLost("focus lost", false);
        Assert(configuredWithoutPause.NeedsRecalibration && !configuredWithoutPause.CanSend,
            "tracking loss remains a safety gate even when the legacy pause flag is false");

        session.BeginStopping();
        Assert(!session.CanSend && !session.IsStreaming, "stopping closes send gate");
        session.CompleteStop();
        Assert(session.State == TeleopSessionState.Idle, "stop returns to idle");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TeleopCoreTests: " + message);
    }

    private sealed class MemoryPreferences : ITeleopPreferenceStore
    {
        private readonly Dictionary<string, string> strings = new Dictionary<string, string>();
        private readonly Dictionary<string, int> ints = new Dictionary<string, int>();
        private readonly Dictionary<string, float> floats = new Dictionary<string, float>();
        public int SaveCount { get; private set; }

        public string GetString(string key, string fallback) => strings.TryGetValue(key, out string value) ? value : fallback;
        public int GetInt(string key, int fallback) => ints.TryGetValue(key, out int value) ? value : fallback;
        public float GetFloat(string key, float fallback) => floats.TryGetValue(key, out float value) ? value : fallback;
        public void SetString(string key, string value) => strings[key] = value;
        public void SetInt(string key, int value) => ints[key] = value;
        public void SetFloat(string key, float value) => floats[key] = value;
        public void Save() => SaveCount++;
    }
}
