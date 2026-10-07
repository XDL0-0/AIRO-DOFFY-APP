using System.Net;

/// <summary>Validation shared by the new UI and the legacy connection entrypoint.</summary>
public static class TeleopConnectionValidator
{
    public static bool TryValidate(string host, int trackCount, out string error)
    {
        string trimmed = host == null ? string.Empty : host.Trim();
        if (trimmed.Length == 0)
        {
            error = "Error: target IP is required";
            return false;
        }

        // The classic UDP receiver, keypad and sender endpoints are IPv4-only;
        // reject IPv6 until a dual-stack transport is implemented everywhere.
        if (!IPAddress.TryParse(trimmed, out IPAddress address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            error = "Error: invalid IP address";
            return false;
        }

        if (trackCount < TeleopRuntimeSettings.MinWebRtcTracks ||
            trackCount > TeleopRuntimeSettings.MaxWebRtcTracks)
        {
            error = "Error: WebRTC views must be 1, 2, or 3";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
