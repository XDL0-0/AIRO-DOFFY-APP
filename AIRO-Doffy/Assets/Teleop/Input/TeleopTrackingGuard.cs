using System;
using UnityEngine;

[DefaultExecutionOrder(-100)]
/// <summary>Keep independent tracking/focus losses latched until each has recovered.</summary>
public class TeleopTrackingGuard : MonoBehaviour
{
    [Flags] private enum Loss
    {
        None = 0,
        Headset = 1,
        VrFocus = 2,
        InputFocus = 4,
        Tracking = 8,
        Pause = 16,
        HandSystemGesture = 32
    }

    private AppManager app;
    private Loss losses;
    private OVRHand[] hands = new OVRHand[0];
    public bool IsAvailable => losses == Loss.None;

    public void Initialize(AppManager manager)
    {
        app = manager;
        CacheHands();
    }

    private void OnEnable()
    {
        OVRManager.HMDUnmounted += OnHmdUnmounted;
        OVRManager.HMDMounted += OnHmdMounted;
        OVRManager.VrFocusLost += OnVrFocusLost;
        OVRManager.VrFocusAcquired += OnVrFocusAcquired;
        OVRManager.InputFocusLost += OnInputFocusLost;
        OVRManager.InputFocusAcquired += OnInputFocusAcquired;
        OVRManager.TrackingLost += OnTrackingLost;
        OVRManager.TrackingAcquired += OnTrackingAcquired;
        CacheHands();
        // A loss may already exist before this component subscribes (scene reload).
        losses &= Loss.Pause;
        if (!OVRManager.isHmdPresent || OVRManager.instance == null || !OVRManager.instance.isUserPresent)
            losses |= Loss.Headset;
        if (!OVRManager.hasVrFocus) losses |= Loss.VrFocus;
        if (!OVRManager.hasInputFocus) losses |= Loss.InputFocus;
        if (OVRManager.tracker == null || !OVRManager.tracker.isPositionTracked) losses |= Loss.Tracking;
        if (!IsAvailable) app?.HandleTrackingLost("Tracking unavailable");
    }

    private void OnDisable()
    {
        OVRManager.HMDUnmounted -= OnHmdUnmounted;
        OVRManager.HMDMounted -= OnHmdMounted;
        OVRManager.VrFocusLost -= OnVrFocusLost;
        OVRManager.VrFocusAcquired -= OnVrFocusAcquired;
        OVRManager.InputFocusLost -= OnInputFocusLost;
        OVRManager.InputFocusAcquired -= OnInputFocusAcquired;
        OVRManager.TrackingLost -= OnTrackingLost;
        OVRManager.TrackingAcquired -= OnTrackingAcquired;
    }

    private void Start() => CacheHands();

    private void OnApplicationPause(bool paused) => Set(Loss.Pause, paused, paused ? "Application paused" : "Application resumed");
    private void OnHmdUnmounted() => Set(Loss.Headset, true, "HMD unmounted");
    private void OnHmdMounted() => Set(Loss.Headset, false, "HMD mounted");
    private void OnVrFocusLost() => Set(Loss.VrFocus, true, "VR focus lost");
    private void OnVrFocusAcquired() => Set(Loss.VrFocus, false, "VR focus acquired");
    private void OnInputFocusLost() => Set(Loss.InputFocus, true, "Input focus lost");
    private void OnInputFocusAcquired() => Set(Loss.InputFocus, false, "Input focus acquired");
    private void OnTrackingLost() => Set(Loss.Tracking, true, "Tracking lost");
    private void OnTrackingAcquired() => Set(Loss.Tracking, false, "Tracking acquired");

    private void Update()
    {
        bool monitorHandGestures = app != null && app.IsStreaming && app.TrackingMode == 1;
        bool systemGesture = monitorHandGestures && HasSystemGestureInProgress();
        Set(Loss.HandSystemGesture, systemGesture, "System hand gesture in progress");
    }

    private void CacheHands()
    {
        if (hands.Length == 0)
            hands = UnityEngine.Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Include);
    }

    private bool HasSystemGestureInProgress()
    {
        foreach (OVRHand hand in hands)
            if (hand != null && hand.IsSystemGestureInProgress)
                return true;
        return false;
    }

    private void Set(Loss kind, bool lost, string reason)
    {
        bool wasLost = (losses & kind) != 0;
        if (wasLost == lost) return;
        if (lost) losses |= kind;
        else losses &= ~kind;
        if (!IsAvailable) app?.HandleTrackingLost(reason);
        else app?.HandleTrackingAvailable(reason);
    }
}
