using System;
using System.Collections;
using Unity.WebRTC;
using UnityEngine;

/// <summary>
/// Pure coroutine orchestration for WebRTC SDP operations. Native objects remain owned by
/// WebRTCVideoReceiver; the supplied predicate invalidates work after a peer replacement.
/// </summary>
internal static class WebRTCSessionOperations
{
    public static IEnumerator CreateOffer(
        RTCPeerConnection peer,
        Func<bool> isCurrent,
        Action<string> offerReady,
        Action<string> reportError,
        Action<bool> completed)
    {
        if (!IsCurrent(isCurrent))
        {
            Complete(completed, false);
            yield break;
        }

        RTCSessionDescriptionAsyncOperation createOffer;
        try
        {
            createOffer = peer.CreateOffer();
        }
        catch (Exception ex)
        {
            Fail(reportError, completed, $"CreateOffer failed: {ex.Message}");
            yield break;
        }

        yield return createOffer;
        if (!IsCurrent(isCurrent))
        {
            Complete(completed, false);
            yield break;
        }
        if (createOffer == null || createOffer.IsError)
        {
            Fail(reportError, completed,
                $"CreateOffer failed: {ErrorMessage(createOffer)}");
            yield break;
        }

        RTCSessionDescription description = createOffer.Desc;
        RTCSetSessionDescriptionAsyncOperation setLocal;
        try
        {
            if (!IsCurrent(isCurrent))
            {
                Complete(completed, false);
                yield break;
            }
            setLocal = peer.SetLocalDescription(ref description);
        }
        catch (Exception ex)
        {
            Fail(reportError, completed, $"SetLocal failed: {ex.Message}");
            yield break;
        }

        yield return setLocal;
        if (!IsCurrent(isCurrent))
        {
            Complete(completed, false);
            yield break;
        }
        if (setLocal == null || setLocal.IsError)
        {
            Fail(reportError, completed,
                $"SetLocal failed: {ErrorMessage(setLocal)}");
            yield break;
        }

        if (!IsCurrent(isCurrent))
        {
            Complete(completed, false);
            yield break;
        }
        try { offerReady?.Invoke(description.sdp); }
        catch (Exception ex) { Debug.LogException(ex); }
        Complete(completed, true);
    }

    public static IEnumerator SetAnswer(
        RTCPeerConnection peer,
        string sdp,
        Func<bool> isCurrent,
        Action<string> reportError,
        Action<bool> completed)
    {
        if (!IsCurrent(isCurrent))
        {
            Complete(completed, false);
            yield break;
        }

        RTCSessionDescription description = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = sdp,
        };
        RTCSetSessionDescriptionAsyncOperation setRemote;
        try
        {
            setRemote = peer.SetRemoteDescription(ref description);
        }
        catch (Exception ex)
        {
            Fail(reportError, completed, $"SetRemote failed: {ex.Message}");
            yield break;
        }

        yield return setRemote;
        if (!IsCurrent(isCurrent))
        {
            Complete(completed, false);
            yield break;
        }
        if (setRemote == null || setRemote.IsError)
        {
            Fail(reportError, completed,
                $"SetRemote failed: {ErrorMessage(setRemote)}");
            yield break;
        }

        Complete(completed, true);
    }

    private static bool IsCurrent(Func<bool> predicate)
    {
        try { return predicate != null && predicate(); }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }
    }

    private static string ErrorMessage(AsyncOperationBase operation)
    {
        if (operation == null || string.IsNullOrEmpty(operation.Error.message))
            return "unknown error";
        return operation.Error.message;
    }

    private static void Fail(
        Action<string> reportError, Action<bool> completed, string message)
    {
        try { reportError?.Invoke(message); }
        catch (Exception ex) { Debug.LogException(ex); }
        Complete(completed, false);
    }

    private static void Complete(Action<bool> completed, bool result)
    {
        try { completed?.Invoke(result); }
        catch (Exception ex) { Debug.LogException(ex); }
    }
}
