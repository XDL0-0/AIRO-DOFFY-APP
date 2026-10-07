using System;

/// <summary>Small preference abstraction used to test legacy PlayerPrefs migration.</summary>
public interface ITeleopPreferenceStore
{
    string GetString(string key, string fallback);
    int GetInt(string key, int fallback);
    float GetFloat(string key, float fallback);
    void SetString(string key, string value);
    void SetInt(string key, int value);
    void SetFloat(string key, float value);
    void Save();
}

/// <summary>
/// Loads and saves the keys used by the classic app. Ports remain asset-owned;
/// the old app never persisted them and changing that would invalidate scenes.
/// </summary>
public sealed class TeleopSettingsStore
{
    public const string HostKey = "cfg_ip";
    public const string TrackingModeKey = "cfg_trackingMode";
    public const string SendRateKey = "cfg_sendRateHz";
    public const string BinaryProtocolKey = "cfg_useBinary";
    public const string WebRtcTracksKey = "cfg_numWebRTC";

    public TeleopRuntimeSettings Load(TeleopRuntimeSettings defaults, ITeleopPreferenceStore preferences)
    {
        if (defaults == null) throw new ArgumentNullException(nameof(defaults));
        if (preferences == null) throw new ArgumentNullException(nameof(preferences));

        TeleopRuntimeSettings result = defaults.Clone();
        string savedHost = preferences.GetString(HostKey, result.ServerHost);
        if (TeleopConnectionValidator.TryValidate(savedHost, result.WebRtcTrackCount, out _))
            result.ServerHost = savedHost.Trim();

        result.TrackingMode = TeleopRuntimeSettings.ClampTrackingMode(
            preferences.GetInt(TrackingModeKey, result.TrackingMode));

        float savedRate = preferences.GetFloat(SendRateKey, result.SendRateHz);
        result.SendRateHz = IsValidRate(savedRate) ? savedRate : result.SendRateHz;
        result.UseBinaryProtocol = preferences.GetInt(BinaryProtocolKey,
            result.UseBinaryProtocol ? 1 : 0) == 1;
        result.WebRtcTrackCount = TeleopRuntimeSettings.ClampTrackCount(
            preferences.GetInt(WebRtcTracksKey, result.WebRtcTrackCount));
        result.Normalize();
        return result;
    }

    public void Save(TeleopRuntimeSettings settings, ITeleopPreferenceStore preferences)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (preferences == null) throw new ArgumentNullException(nameof(preferences));

        TeleopRuntimeSettings snapshot = settings.Clone();
        snapshot.Normalize();
        preferences.SetString(HostKey, snapshot.ServerHost);
        preferences.SetInt(TrackingModeKey, snapshot.TrackingMode);
        preferences.SetFloat(SendRateKey, snapshot.SendRateHz);
        preferences.SetInt(BinaryProtocolKey, snapshot.UseBinaryProtocol ? 1 : 0);
        preferences.SetInt(WebRtcTracksKey, snapshot.WebRtcTrackCount);
        preferences.Save();
    }

    private static bool IsValidRate(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }
}
