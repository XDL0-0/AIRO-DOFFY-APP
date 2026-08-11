using UnityEngine;

public class TeleopTrackingGuard : MonoBehaviour
{
    private AppManager _appManager;

    public void Initialize(AppManager appManager)
    {
        _appManager = appManager;
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

    private void OnApplicationPause(bool pause)
    {
        if (pause)
            ReportLost("Application paused");
        else
            ReportAvailable("Application resumed");
    }

    private void OnHmdUnmounted() => ReportLost("HMD unmounted");
    private void OnVrFocusLost() => ReportLost("VR focus lost");
    private void OnInputFocusLost() => ReportLost("Input focus lost");
    private void OnTrackingLost() => ReportLost("Tracking lost");

    private void OnHmdMounted() => ReportAvailable("HMD mounted");
    private void OnVrFocusAcquired() => ReportAvailable("VR focus acquired");
    private void OnInputFocusAcquired() => ReportAvailable("Input focus acquired");
    private void OnTrackingAcquired() => ReportAvailable("Tracking acquired");

    private void ReportLost(string reason)
    {
        _appManager?.HandleTrackingLost(reason);
    }

    private void ReportAvailable(string reason)
    {
        _appManager?.HandleTrackingAvailable(reason);
    }
}
