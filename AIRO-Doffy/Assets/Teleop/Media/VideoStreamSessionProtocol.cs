using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// Owns the signaling envelope flow for one video session. It is transport-facing only:
/// session validity, failure routing and Unity-thread scheduling are supplied by the manager.
/// </summary>
internal sealed class VideoStreamSessionProtocol
{
    private readonly VideoSignalingClient _signaling;
    private readonly WebRTCVideoReceiver _receiver;
    private readonly Func<bool> _isCurrent;
    private readonly Func<string> _sessionId;
    private readonly Action<string> _onFailure;
    private readonly Action<string> _onLog;

    public VideoStreamSessionProtocol(
        VideoSignalingClient signaling,
        WebRTCVideoReceiver receiver,
        Func<bool> isCurrent,
        Func<string> sessionId,
        Action<string> onFailure,
        Action<string> onLog)
    {
        _signaling = signaling;
        _receiver = receiver;
        _isCurrent = isCurrent;
        _sessionId = sessionId;
        _onFailure = onFailure;
        _onLog = onLog;
    }

    public async Task SendOfferAsync(string sdp)
    {
        try
        {
            if (!Current()) return;
            await _signaling.SendAsync("offer", _sessionId(),
                new JObject { ["sdp"] = sdp ?? "" }
                    .ToString(Newtonsoft.Json.Formatting.None));
            if (Current()) _onLog?.Invoke("Offer sent to host");
        }
        catch (Exception ex) { Fail($"Offer send failed: {ex.Message}"); }
    }

    public async Task SendIceAsync(string candidate, string sdpMid, int? index)
    {
        try
        {
            if (!Current()) return;
            JObject payload = new JObject
            {
                ["candidate"] = candidate ?? "",
                ["sdpMid"] = sdpMid,
                ["sdpMLineIndex"] = index.HasValue ? index.Value : JValue.CreateNull(),
            };
            await _signaling.SendAsync("ice_candidate", _sessionId(),
                payload.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { Fail($"ICE send failed: {ex.Message}"); }
    }

    public async Task HandleEnvelopeAsync(VideoSignalingClient.Envelope env)
    {
        try
        {
            if (!Current() || env == null || env.SessionId != _sessionId()) return;
            switch (env.Type)
            {
                case "answer":
                {
                    JObject root = JObject.Parse(env.PayloadJson ?? "{}");
                    string sdp = root.Value<string>("sdp");
                    if (string.IsNullOrWhiteSpace(sdp) || !Current()) return;
                    bool ok = await _receiver.SetRemoteAnswerAsync(sdp);
                    if (!Current()) return;
                    if (!ok) Fail("SetRemoteAnswer failed");
                    else _onLog?.Invoke("Answer applied");
                    break;
                }
                case "ice_candidate":
                {
                    JObject ice = JObject.Parse(env.PayloadJson ?? "{}");
                    if (!Current()) return;
                    _receiver.AddRemoteIceCandidate(
                        ice.Value<string>("candidate"),
                        ice.Value<string>("sdpMid"),
                        ice["sdpMLineIndex"]?.Type == JTokenType.Integer
                            ? ice.Value<int>("sdpMLineIndex") : null);
                    break;
                }
                case "hello_ack":
                    _onLog?.Invoke("hello_ack received");
                    break;
                case "error":
                    JObject error = JObject.Parse(env.PayloadJson ?? "{}");
                    Fail(error.Value<string>("message") ?? "Server error");
                    break;
            }
        }
        catch (Exception ex) { Fail($"Signaling envelope failed: {ex.Message}"); }
    }

    private bool Current()
    {
        try { return _isCurrent != null && _isCurrent(); }
        catch (Exception ex)
        {
            try { _onFailure?.Invoke($"Session state check failed: {ex.Message}"); } catch { }
            return false;
        }
    }

    private void Fail(string reason)
    {
        try { if (Current()) _onFailure?.Invoke(reason); }
        catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
    }
}
