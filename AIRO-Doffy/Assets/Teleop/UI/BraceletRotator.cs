using System;
using Doffy.UI;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tracks rotation of a wrist bracelet from a controller ray or a continuous
/// right-index-finger poke. The visual bracelet may rotate independently of
/// the stable wrist frame supplied to Configure.
/// </summary>
public sealed class BraceletRotator : MonoBehaviour
{
    private const int MaxQueuedHapticPulses = 3;
    private const float HapticGapSeconds = 0.012f;
    private const float MinHapticDurationSeconds = 0.005f;
    private const float MaxHapticDurationSeconds = 0.05f;
    private const float DetentHysteresisDegrees = 0.75f;
    private const float RayTangentMissToleranceMeters = 0.035f;
    private const float PokeAcquireRadialPaddingMeters = 0.020f;
    private const float PokeAcquireAxialPaddingMeters = 0.002f;
    private const float PokeContactPaddingMeters = 0.003f;
    private const float PokeApproachPaddingMeters = 0.020f;
    private const float PokeReleasePaddingMeters = 0.025f;
    private const float MinimumPokeApproachMeters = 0.0005f;
    private const float PokeTrackingGraceSeconds = 0.10f;
    private const float ControllerTriggerThreshold = 0.5f;

    private enum CaptureKind
    {
        None,
        ControllerRay,
        RightPoke
    }

    [Header("Rotation")]
    [SerializeField, Min(0f)] private float angleSensitivity = 1f;

    [Header("Detents")]
    [SerializeField, Min(0.1f)] private float detentAngleDegrees = 10f;

    [Header("Right controller haptics")]
    [SerializeField, Range(0f, 1f)] private float hapticFrequency = 0.5f;
    [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.25f;
    [SerializeField, Range(0.005f, 0.05f)] private float hapticDurationSeconds = 0.015f;

    private readonly WristDetentTracker _detents = new WristDetentTracker();
    private Transform _frame;
    private WristInteractionSource _input;
    private float _radius;
    private float _bandCenterZ;
    private float _bandWidth;
    private Func<bool> _available;
    private Action<float> _angleChanged;
    private CaptureKind _capture;
    private int _capturedPointerId = int.MinValue;
    private BraceletDragHandle _capturingHandle;
    private bool _configured;
    private bool _hapticActive;
    private int _pendingHapticPulses;
    private float _hapticStopTime;
    private float _hapticGapUntil;
    private float _appliedDetentAngle = float.NaN;
    private float _pokeAngleSensitivity = 1.3f;
    private float _lastRayAngle;
    private float _lastRayTravelDirection;
    private bool _hasRayAngleReference;
    private float _lastPokeRadialGap;
    private bool _hasPreviousPokeGap;
    private bool _pokeTrackingLossPending;
    private bool _rebasePokeSample;
    private bool _requirePokeLiftBeforeReacquire;
    private float _pokeTrackingLostAt;

    /// <summary>The unbounded cumulative angle in degrees.</summary>
    public float CurrentAngle => _detents.AngleDegrees;

    /// <summary>The stable wrist frame. Its local Z axis follows the fingers.</summary>
    public Transform Frame => _frame;

    /// <summary>The bracelet's radial distance from Frame, in world metres.</summary>
    public float Radius => _radius;

    /// <summary>The drag band's center along Frame local Z, in world metres.</summary>
    public float BandCenterZ => _bandCenterZ;

    /// <summary>The drag band's full width along Frame local Z, in world metres.</summary>
    public float BandWidth => _bandWidth;

    public bool IsConfigured => _configured;

    public bool IsDragging => _capture != CaptureKind.None;

    /// <summary>
    /// Configures the stable wrist frame and the cylinder occupied by the
    /// bracelet's drag band. The frame basis is normalized so distances remain
    /// in metres even if the frame has a scaled parent.
    /// </summary>
    public void Configure(
        Transform frame,
        WristInteractionSource input,
        float radius,
        float bandCenterZ,
        float bandWidth,
        Func<bool> available,
        Action<float> angleChanged)
    {
        CancelInteraction();
        _frame = frame;
        _input = input;
        SetGeometry(radius, bandCenterZ, bandWidth);
        _available = available;
        _angleChanged = angleChanged;
        _configured = _frame != null && _input != null && _available != null;
        ApplyDetentSettings();
    }

    /// <summary>
    /// Updates the cuff cylinder when the wrist UI mode changes. Geometry
    /// changes never reset the accumulated bracelet angle.
    /// </summary>
    public void SetGeometry(float radius, float bandCenterZ, float bandWidth)
    {
        _radius = IsFinite(radius) ? Mathf.Max(0.001f, radius) : 0.13f;
        _bandCenterZ = IsFinite(bandCenterZ) ? bandCenterZ : 0f;
        _bandWidth = IsFinite(bandWidth) ? Mathf.Max(0.001f, bandWidth) : 0.04f;

        // A geometry change can alter the ray roots. Seed the next sample so
        // the existing logical angle is preserved without a geometry jump.
        _hasRayAngleReference = false;
        _lastRayTravelDirection = 0f;
        _detents.ClearSample();
        _rebasePokeSample = _capture == CaptureKind.RightPoke;
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

    /// <summary>Sets hand-only sensitivity without changing controller tuning.</summary>
    public void SetPokeTuning(float sensitivity)
    {
        _pokeAngleSensitivity = IsFinite(sensitivity) ? Mathf.Max(0f, sensitivity) : 1.3f;
    }

    /// <summary>Sets the logical bracelet angle to zero and notifies its owner.</summary>
    public void ResetRotation()
    {
        CancelInteraction();
        _detents.Reset();
        _angleChanged?.Invoke(0f);
    }

    private void Awake()
    {
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

    private void LateUpdate()
    {
        ApplyDetentSettings();
        if (!_configured)
            return;

        if (_capture == CaptureKind.RightPoke)
        {
            UpdatePokeCapture();
            return;
        }

        if (!CanInteractNow())
        {
            CancelInteraction();
            return;
        }

        if (_capture == CaptureKind.None && _input.HandsActive)
        {
            TryBeginPokeCapture(false);
            return;
        }

        switch (_capture)
        {
            case CaptureKind.ControllerRay:
                UpdateControllerCapture();
                break;
        }
    }

    internal bool AcceptsPointer(PointerEventData eventData)
    {
        if (!_configured || eventData == null || _input == null)
            return false;

        RayInteractor ray = _input.RightRay;
        if (!_input.HandsActive && ray != null && eventData.pointerId == ray.Identifier)
            return true;

        PokeInteractor poke = _input.RightPoke;
        return _input.HandsActive && poke != null && eventData.pointerId == poke.Identifier;
    }

    internal void BeginPointerInteraction(BraceletDragHandle handle, PointerEventData eventData)
    {
        if (_capture != CaptureKind.None || !AcceptsPointer(eventData) || !CanInteractNow())
            return;

        if (_input.HandsActive)
        {
            PokeInteractor poke = _input.RightPoke;
            TryBeginPokeCapture(poke != null && poke.State == InteractorState.Select);
            return;
        }

        RayInteractor ray = _input.RightRay;
        if (ray == null || !ray.isActiveAndEnabled || !ray.gameObject.activeInHierarchy ||
            ray.State != InteractorState.Select)
            return;

        _capture = CaptureKind.ControllerRay;
        _capturedPointerId = ray.Identifier;
        _capturingHandle = handle;
        _hasRayAngleReference = false;
        _lastRayTravelDirection = 0f;
        if (TryGetRayAngle(ray, out float rayAngle))
            _detents.BeginSample(rayAngle);
        else
            _detents.ClearSample();
    }

    internal void PointerReleased(BraceletDragHandle handle, PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerId != _capturedPointerId)
            return;

        if (_capture == CaptureKind.ControllerRay)
        {
            // A selected segment can move out from under the ray as the
            // bracelet turns. The controller's trigger is the drag capture;
            // the SDK may send pointer-up for that segment before it is let go.
            if (!IsControllerTriggerHeld())
                CancelInteraction();
            return;
        }

        if (_capture == CaptureKind.RightPoke)
        {
            // Finger geometry owns poke release. Segment pointer-up can occur
            // while the fingertip is still in contact with the cuff.
            return;
        }
    }

    internal void HandleDisabled(BraceletDragHandle handle)
    {
        if (_capture == CaptureKind.ControllerRay && _capturingHandle == handle)
            CancelInteraction();
    }

    private void UpdateControllerCapture()
    {
        RayInteractor ray = _input.RightRay;
        if (_input.HandsActive || ray == null || !ray.isActiveAndEnabled ||
            !ray.gameObject.activeInHierarchy || ray.State == InteractorState.Disabled ||
            ray.Identifier != _capturedPointerId ||
            !IsControllerTriggerHeld())
        {
            CancelInteraction();
            return;
        }

        if (TryGetRayAngle(ray, out float pointerAngle))
            ConsumeAngleSample(pointerAngle, true, angleSensitivity);

        UpdateHaptics();
    }

    private void UpdatePokeCapture()
    {
        if (!CanInteractPokeEnvironment() || _input.ControllerModeActive)
        {
            CancelInteraction();
            return;
        }

        PokeInteractor poke = _input.RightPoke;
        bool tracked = _input.HandsActive && _input.CanInteract && poke != null &&
                       poke.Identifier == _capturedPointerId && IsPokeInteractorLive(poke);
        if (!tracked)
        {
            if (!_pokeTrackingLossPending)
            {
                _pokeTrackingLossPending = true;
                _pokeTrackingLostAt = Time.unscaledTime;
            }

            if (Time.unscaledTime - _pokeTrackingLostAt > PokeTrackingGraceSeconds)
            {
                CancelInteraction();
                _requirePokeLiftBeforeReacquire = true;
            }
            return;
        }

        if (!TryGetPokeGeometry(poke, out float radialDistance, out float axialOffset,
                out float radialGap, out float pointerAngle, out bool hasAngle))
        {
            CancelInteraction();
            return;
        }

        if (!IsPokeWithinReleaseBand(radialDistance, axialOffset, poke.Radius))
        {
            // Leaving the expanded contact band is the explicit lift-to-release.
            CancelInteraction();
            return;
        }

        if (!hasAngle)
        {
            CancelInteraction();
            return;
        }

        if (_pokeTrackingLossPending || _rebasePokeSample)
        {
            _pokeTrackingLossPending = false;
            _rebasePokeSample = false;
            _lastPokeRadialGap = radialGap;
            _hasPreviousPokeGap = true;
            _detents.BeginSample(pointerAngle);
            return;
        }

        ConsumeAngleSample(pointerAngle, false, _pokeAngleSensitivity);
        _lastPokeRadialGap = radialGap;
        _hasPreviousPokeGap = true;
    }

    private bool TryBeginPokeCapture(bool sdkSelected)
    {
        if (_capture != CaptureKind.None || !CanInteractNow() || !_input.HandsActive)
            return false;

        PokeInteractor poke = _input.RightPoke;
        if (!IsPokeInteractorLive(poke) ||
            !TryGetPokeGeometry(poke, out float radialDistance, out float axialOffset,
                out float radialGap, out float pointerAngle, out bool hasAngle))
        {
            _hasPreviousPokeGap = false;
            return false;
        }

        if (!hasAngle)
        {
            _lastPokeRadialGap = radialGap;
            _hasPreviousPokeGap = true;
            return false;
        }

        if (_requirePokeLiftBeforeReacquire)
        {
            bool outsideReleaseBand = !IsPokeWithinReleaseBand(radialDistance, axialOffset, poke.Radius);
            if (BraceletDragMath.ObservePokeLift(
                    ref _requirePokeLiftBeforeReacquire,
                    !outsideReleaseBand))
            {
                _lastPokeRadialGap = radialGap;
                _hasPreviousPokeGap = true;
            }
            return false;
        }

        bool withinAcquireBand = BraceletDragMath.IsWithinPokeAcquireBand(
            radialGap,
            axialOffset,
            _bandWidth * 0.5f,
            poke.Radius,
            PokeAcquireRadialPaddingMeters,
            PokeAcquireAxialPaddingMeters);
        bool shouldAcquire = BraceletDragMath.ShouldAcquirePoke(
            radialGap,
            _lastPokeRadialGap,
            _hasPreviousPokeGap,
            sdkSelected || poke.State == InteractorState.Select,
            poke.Radius,
            PokeContactPaddingMeters,
            PokeApproachPaddingMeters,
            MinimumPokeApproachMeters);

        _lastPokeRadialGap = radialGap;
        _hasPreviousPokeGap = true;
        if (!withinAcquireBand || !shouldAcquire)
            return false;

        _capture = CaptureKind.RightPoke;
        _capturedPointerId = poke.Identifier;
        _capturingHandle = null;
        _pokeTrackingLossPending = false;
        _rebasePokeSample = false;
        _detents.BeginSample(pointerAngle);
        return true;
    }

    private void ConsumeAngleSample(float pointerAngle, bool allowHaptics, float sensitivity)
    {
        float previousAngle = _detents.AngleDegrees;
        int detentCrossings = _detents.UpdateSample(pointerAngle, sensitivity);
        float currentAngle = _detents.AngleDegrees;
        if (currentAngle != previousAngle)
            _angleChanged?.Invoke(currentAngle);

        if (allowHaptics && detentCrossings != 0)
            QueueHapticPulses(detentCrossings);
    }

    private bool TryGetRayAngle(RayInteractor rayInteractor, out float angleDegrees)
    {
        angleDegrees = 0f;
        if (_frame == null || rayInteractor == null)
            return false;

        Vector3 right = _frame.right.normalized;
        Vector3 up = _frame.up.normalized;
        Vector3 forward = _frame.forward.normalized;
        Vector3 cylinderCenter = _frame.position + forward * _bandCenterZ;
        Vector3 relativeOrigin = rayInteractor.Origin - cylinderCenter;
        Vector3 direction = rayInteractor.Forward.normalized;
        float originX = Vector3.Dot(relativeOrigin, right);
        float originY = Vector3.Dot(relativeOrigin, up);
        float directionX = Vector3.Dot(direction, right);
        float directionY = Vector3.Dot(direction, up);
        if (!BraceletDragMath.TryGetCylinderAngle(
                originX,
                originY,
                directionX,
                directionY,
                _radius,
                _hasRayAngleReference,
                _lastRayAngle,
                _lastRayTravelDirection,
                RayTangentMissToleranceMeters,
                out angleDegrees))
        {
            return false;
        }

        if (_hasRayAngleReference)
        {
            float delta = (float)BraceletDragMath.UnwrapDeltaDegrees(_lastRayAngle, angleDegrees);
            if (Mathf.Abs(delta) > 0.01f)
                _lastRayTravelDirection = delta;
        }

        _lastRayAngle = angleDegrees;
        _hasRayAngleReference = true;
        return true;
    }

    private bool TryGetPokeGeometry(
        PokeInteractor poke,
        out float radialDistance,
        out float axialOffset,
        out float radialGap,
        out float angleDegrees,
        out bool hasAngle)
    {
        radialDistance = 0f;
        axialOffset = 0f;
        radialGap = 0f;
        angleDegrees = 0f;
        hasAngle = false;
        if (_frame == null || poke == null)
            return false;

        Vector3 right = _frame.right.normalized;
        Vector3 up = _frame.up.normalized;
        Vector3 forward = _frame.forward.normalized;
        Vector3 relativePosition = poke.Origin - (_frame.position + forward * _bandCenterZ);
        float radialX = Vector3.Dot(relativePosition, right);
        float radialY = Vector3.Dot(relativePosition, up);
        axialOffset = Vector3.Dot(relativePosition, forward);
        radialDistance = Mathf.Sqrt(radialX * radialX + radialY * radialY);
        radialGap = Mathf.Abs(radialDistance - _radius);
        bool finite = IsFinite(radialX) && IsFinite(radialY) && IsFinite(axialOffset) &&
                      IsFinite(radialDistance) && IsFinite(radialGap);
        if (!finite)
            return false;

        hasAngle = BraceletDragMath.TryGetPointAngle(radialX, radialY, out angleDegrees);
        return true;
    }

    private bool IsPokeWithinReleaseBand(float radialDistance, float axialOffset, float pokeRadius)
    {
        return BraceletDragMath.IsWithinPokeReleaseBand(
            radialDistance,
            _radius,
            axialOffset,
            _bandWidth * 0.5f,
            pokeRadius,
            PokeReleasePaddingMeters);
    }

    private bool CanInteractNow()
    {
        return CanInteractPokeEnvironment() && _input.CanInteract;
    }

    private bool CanInteractPokeEnvironment()
    {
        return isActiveAndEnabled && gameObject.activeInHierarchy && _configured &&
               _frame != null && _frame.gameObject.activeInHierarchy && _input != null &&
               _available != null && _available();
    }

    private bool IsPokeInteractorLive(PokeInteractor poke)
    {
        return poke != null && poke.isActiveAndEnabled && poke.gameObject.activeInHierarchy &&
               poke.State != InteractorState.Disabled;
    }

    private static bool IsControllerTriggerHeld()
    {
        return OVRInput.Get(OVRInput.RawButton.RIndexTrigger) ||
               OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) >= ControllerTriggerThreshold;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void ApplyDetentSettings()
    {
        if (_appliedDetentAngle == detentAngleDegrees)
            return;

        _detents.Configure(detentAngleDegrees, DetentHysteresisDegrees);
        _appliedDetentAngle = detentAngleDegrees;
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
        OVRInput.SetControllerVibration(
            Mathf.Clamp01(hapticFrequency),
            Mathf.Clamp01(hapticAmplitude),
            OVRInput.Controller.RTouch);
        _hapticActive = true;
        _hapticStopTime = now + Mathf.Clamp(hapticDurationSeconds,
            MinHapticDurationSeconds, MaxHapticDurationSeconds);
    }

    private void CancelInteraction()
    {
        _capture = CaptureKind.None;
        _capturedPointerId = int.MinValue;
        _capturingHandle = null;
        _hasRayAngleReference = false;
        _lastRayTravelDirection = 0f;
        _hasPreviousPokeGap = false;
        _pokeTrackingLossPending = false;
        _rebasePokeSample = false;
        _pokeTrackingLostAt = 0f;
        _detents.ClearSample();
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
