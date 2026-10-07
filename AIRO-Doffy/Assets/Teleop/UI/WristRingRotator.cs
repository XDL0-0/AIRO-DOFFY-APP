using System;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Rotates a logical wrist-ring value while the configured right controller
/// holds the annular UI graphic. The ring frame itself is not rotated.
/// </summary>
public sealed class WristRingRotator : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
{
    private const int MaxQueuedHapticPulses = 3;
    private const float HapticGapSeconds = 0.012f;
    private const float MinHapticDurationSeconds = 0.005f;
    private const float MaxHapticDurationSeconds = 0.05f;

    [Header("Rotation")]
    [SerializeField, Min(0f)] private float angleSensitivity = 1f;

    [Header("Detents")]
    [SerializeField, Min(0.1f)] private float detentAngleDegrees = 10f;
    [SerializeField, Min(0f)] private float detentHysteresisDegrees = 0.75f;
    [SerializeField, Min(0f)] private float centerDeadzoneMeters = 0.01f;

    [Header("Right controller haptics")]
    [SerializeField, Range(0f, 1f)] private float hapticFrequency = 0.5f;
    [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.25f;
    [SerializeField, Range(0.005f, 0.05f)] private float hapticDurationSeconds = 0.015f;
    [SerializeField] private Graphic dragGraphic;

    private readonly WristDetentTracker _detents = new WristDetentTracker();
    private Transform _ringFrame;
    private RayInteractor _rightRay;
    private Func<bool> _canRotate;
    private Action<float> _angleChanged;
    private bool _configured;
    private bool _dragging;
    private bool _hapticActive;
    private int _pendingHapticPulses;
    private float _hapticStopTime;
    private float _hapticGapUntil;
    private float _appliedDetentAngle = float.NaN;
    private float _appliedHysteresis = float.NaN;

    public float CurrentAngle => _detents.AngleDegrees;

    /// <summary>
    /// Configures the frame used for ray-plane intersection, the right ray,
    /// and the current mode gate. The annular Graphic should send pointer
    /// events to this component (usually by sharing its GameObject).
    /// </summary>
    public void Configure(
        Transform ringFrame,
        RayInteractor rightRay,
        Func<bool> canRotate,
        Action<float> angleChanged)
    {
        CancelInteraction();
        _ringFrame = ringFrame;
        _rightRay = rightRay;
        _canRotate = canRotate;
        _angleChanged = angleChanged;
        _configured = _ringFrame != null && _canRotate != null;
        ApplyDetentSettings();
    }

    /// <summary>Updates the right ray after its input source becomes available.</summary>
    public void SetRightRay(RayInteractor rightRay)
    {
        _rightRay = rightRay;
        if (rightRay == null)
            CancelInteraction();
    }

    /// <summary>Applies tuning loaded by the wrist UI settings asset.</summary>
    public void SetTuning(float sensitivity, float detentAngle, float amplitude, float duration)
    {
        angleSensitivity = IsFinite(sensitivity) ? Mathf.Max(0f, sensitivity) : 1f;
        detentAngleDegrees = IsFinite(detentAngle) ? Mathf.Max(0.1f, detentAngle) : 10f;
        hapticAmplitude = IsFinite(amplitude) ? Mathf.Clamp01(amplitude) : 0.25f;
        hapticDurationSeconds = IsFinite(duration)
            ? Mathf.Clamp(duration, MinHapticDurationSeconds, MaxHapticDurationSeconds)
            : 0.015f;
        ApplyDetentSettings();
    }

    /// <summary>Sets the logical dial angle to zero and notifies the owner.</summary>
    public void ResetRotation()
    {
        CancelInteraction();
        _detents.Reset();
        _angleChanged?.Invoke(0f);
    }

    private void Awake()
    {
        if (dragGraphic == null)
            dragGraphic = GetComponent<Graphic>();
        ApplyDetentSettings();
    }

    private void OnEnable()
    {
        ApplyDetentSettings();
    }

    private void OnDisable()
    {
        CancelInteraction();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            CancelInteraction();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            CancelInteraction();
    }

    private void Update()
    {
        ApplyDetentSettings();
        if (!_configured)
            return;

        if (!IsAvailable() || !CanRotateNow())
        {
            CancelInteraction();
            return;
        }

        if (!_dragging)
            return;

        if (_rightRay == null || _rightRay.State != InteractorState.Select || !TryGetRingAngle(out float pointerAngle))
        {
            CancelInteraction();
            return;
        }

        float previousAngle = _detents.AngleDegrees;
        int detentCrossings = _detents.UpdateSample(pointerAngle, angleSensitivity);
        float currentAngle = _detents.AngleDegrees;
        if (currentAngle != previousAngle)
            _angleChanged?.Invoke(currentAngle);

        if (detentCrossings != 0)
            QueueHapticPulses(detentCrossings);

        UpdateHaptics();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (IsRightRayEvent(eventData) && CanRotateNow())
            eventData.useDragThreshold = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsRightRayEvent(eventData))
            return;

        if (!_configured || !IsAvailable() || !CanRotateNow())
        {
            CancelInteraction();
            return;
        }

        CancelHaptics();
        _dragging = true;
        if (TryGetRingAngle(out float pointerAngle))
            _detents.BeginSample(pointerAngle);
        else
            _detents.ClearSample();
    }

    public void OnDrag(PointerEventData eventData)
    {
        // The ray is sampled in Update against ringFrame's stable plane. The
        // EventSystem drag callback is intentionally only used for capture.
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (IsRightRayEvent(eventData))
            CancelInteraction();
    }

    private bool TryGetRingAngle(out float angleDegrees)
    {
        angleDegrees = 0f;
        if (_ringFrame == null || _rightRay == null)
            return false;

        Plane ringPlane = new Plane(_ringFrame.forward, _ringFrame.position);
        Ray ray = new Ray(_rightRay.Origin, _rightRay.Forward);
        if (!ringPlane.Raycast(ray, out float enter) || enter < 0f)
            return false;

        Vector3 hitPoint = ray.GetPoint(enter);
        Vector3 worldRadial = Vector3.ProjectOnPlane(hitPoint - _ringFrame.position, _ringFrame.forward);
        float centerDeadzone = Mathf.Max(0f, centerDeadzoneMeters);
        if (worldRadial.sqrMagnitude < centerDeadzone * centerDeadzone)
            return false;

        Vector3 localHit = _ringFrame.InverseTransformPoint(hitPoint);
        if (localHit.x * localHit.x + localHit.y * localHit.y < 0.000001f)
            return false;

        angleDegrees = Mathf.Atan2(localHit.y, localHit.x) * Mathf.Rad2Deg;
        return true;
    }

    private bool IsRightRayEvent(PointerEventData eventData)
    {
        return eventData != null && _rightRay != null && eventData.pointerId == _rightRay.Identifier;
    }

    private bool IsAvailable()
    {
        return isActiveAndEnabled && gameObject.activeInHierarchy &&
            _ringFrame != null && _ringFrame.gameObject.activeInHierarchy &&
            (dragGraphic == null || dragGraphic.isActiveAndEnabled);
    }

    private bool CanRotateNow()
    {
        return _canRotate != null && _canRotate();
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void ApplyDetentSettings()
    {
        if (_appliedDetentAngle == detentAngleDegrees && _appliedHysteresis == detentHysteresisDegrees)
            return;

        _detents.Configure(detentAngleDegrees, detentHysteresisDegrees);
        _appliedDetentAngle = detentAngleDegrees;
        _appliedHysteresis = detentHysteresisDegrees;
    }

    private void QueueHapticPulses(int detentCrossings)
    {
        int count = detentCrossings < 0 ? -detentCrossings : detentCrossings;
        int available = MaxQueuedHapticPulses - _pendingHapticPulses;
        if (count > available)
            count = available;
        if (count > 0)
            _pendingHapticPulses += count;
    }

    private void UpdateHaptics()
    {
        float now = Time.unscaledTime;
        if (_hapticActive)
        {
            if (now < _hapticStopTime)
                return;

            StopHapticMotor();
            _hapticGapUntil = now + HapticGapSeconds;
        }

        if (_pendingHapticPulses <= 0 || now < _hapticGapUntil)
            return;

        _pendingHapticPulses--;
        float frequency = Mathf.Clamp01(hapticFrequency);
        float amplitude = Mathf.Clamp01(hapticAmplitude);
        float duration = Mathf.Clamp(hapticDurationSeconds, MinHapticDurationSeconds, MaxHapticDurationSeconds);
        OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.RTouch);
        _hapticActive = true;
        _hapticStopTime = now + duration;
    }

    private void CancelInteraction()
    {
        _dragging = false;
        CancelHaptics();
    }

    private void CancelHaptics()
    {
        _pendingHapticPulses = 0;
        _hapticGapUntil = 0f;
        if (_hapticActive)
            StopHapticMotor();
    }

    private void StopHapticMotor()
    {
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        _hapticActive = false;
        _hapticStopTime = 0f;
    }
}
