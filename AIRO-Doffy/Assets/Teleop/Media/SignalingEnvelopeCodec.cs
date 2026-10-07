using System;
using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>JSON envelope codec shared by the transport client and its tests.</summary>
internal static class SignalingEnvelopeCodec
{
    public static byte[] Encode(string type, string sessionId, string payloadJson)
    {
        JObject envelope = new JObject
        {
            ["type"] = type ?? "",
            ["session_id"] = sessionId ?? "",
            ["payload"] = ParsePayload(payloadJson),
        };
        return Encoding.UTF8.GetBytes(
            envelope.ToString(Newtonsoft.Json.Formatting.None));
    }

    public static VideoSignalingClient.Envelope Decode(string raw)
    {
        JObject root = JObject.Parse(raw);
        return new VideoSignalingClient.Envelope
        {
            Type = root.Value<string>("type") ?? "",
            SessionId = root.Value<string>("session_id") ?? "",
            PayloadJson = root["payload"]?.ToString() ?? "{}",
        };
    }

    private static JToken ParsePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JObject();
        try { return JToken.Parse(json); } catch { return new JObject(); }
    }
}
