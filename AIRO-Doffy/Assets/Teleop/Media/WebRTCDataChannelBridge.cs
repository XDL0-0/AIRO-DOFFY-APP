using System;
using System.Text;
using Unity.WebRTC;

/// <summary>Owns DataChannel delegates and the send/close boundary for one peer generation.</summary>
internal static class WebRTCDataChannelBridge
{
    public static bool Configure(
        RTCDataChannel channel,
        Func<bool> isCurrent,
        Func<bool> isCurrentChannel,
        Action<string> onMessage,
        Action onOpen,
        Action onClose,
        Action<string> onError)
    {
        channel.OnMessage = bytes =>
        {
            if (!Current(isCurrent, isCurrentChannel)) return;
            try { onMessage?.Invoke(Encoding.UTF8.GetString(bytes ?? new byte[0])); }
            catch (Exception ex) { Error(isCurrent, isCurrentChannel, onError, ex.Message); }
        };
        channel.OnOpen = () =>
        {
            if (!Current(isCurrent, isCurrentChannel)) return;
            try { onOpen?.Invoke(); } catch (Exception ex) { Error(isCurrent, isCurrentChannel, onError, ex.Message); }
        };
        channel.OnClose = () =>
        {
            if (!Current(isCurrent, isCurrentChannel)) return;
            try { onClose?.Invoke(); } catch (Exception ex) { Error(isCurrent, isCurrentChannel, onError, ex.Message); }
        };
        channel.OnError = error =>
            Error(isCurrent, isCurrentChannel, onError, $"DataChannel error: {error.message}");
        try { return channel.ReadyState == RTCDataChannelState.Open; }
        catch { return false; }
    }

    public static bool TrySend(RTCDataChannel channel, string message, Action<string> onError)
    {
        if (channel == null || message == null) return false;
        try
        {
            if (channel.ReadyState != RTCDataChannelState.Open) return false;
            channel.Send(Encoding.UTF8.GetBytes(message));
            return true;
        }
        catch (Exception ex)
        {
            try { onError?.Invoke($"DataChannel send failed: {ex.Message}"); } catch { }
            return false;
        }
    }

    public static void Dispose(RTCDataChannel channel)
    {
        if (channel == null) return;
        try { channel.OnMessage = null; } catch { }
        try { channel.OnOpen = null; } catch { }
        try { channel.OnClose = null; } catch { }
        try { channel.OnError = null; } catch { }
        try { channel.Close(); } catch { }
        try { channel.Dispose(); } catch { }
    }

    private static bool Current(Func<bool> isCurrent, Func<bool> isCurrentChannel)
    {
        try { return (isCurrent == null || isCurrent()) && (isCurrentChannel == null || isCurrentChannel()); }
        catch { return false; }
    }

    private static void Error(
        Func<bool> isCurrent, Func<bool> isCurrentChannel, Action<string> onError, string message)
    {
        if (!Current(isCurrent, isCurrentChannel)) return;
        try { onError?.Invoke(message); } catch { }
    }
}
