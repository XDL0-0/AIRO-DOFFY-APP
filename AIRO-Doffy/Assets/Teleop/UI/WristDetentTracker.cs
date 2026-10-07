using System;

/// <summary>
/// Tracks angular motion and emits detent crossings without depending on Unity.
/// Input samples may use the usual [-180, 180] range; the tracker unwraps them
/// and keeps the accumulated angle continuous across that boundary.
/// </summary>
public sealed class WristDetentTracker
{
    private const int MaxCrossingsPerUpdate = 64;

    private float _detentAngleDegrees;
    private float _hysteresisDegrees;
    private float _angleDegrees;
    private float _lastSampleDegrees;
    private bool _hasSample;
    private int _detentIndex;

    public float AngleDegrees => _angleDegrees;
    public int DetentIndex => _detentIndex;

    public WristDetentTracker(float detentAngleDegrees = 10f, float hysteresisDegrees = 0.75f)
    {
        Configure(detentAngleDegrees, hysteresisDegrees);
        Reset();
    }

    /// <summary>
    /// Changes the detent spacing while preserving the current angle. The
    /// current index is re-seeded without producing a crossing.
    /// </summary>
    public void Configure(float detentAngleDegrees, float hysteresisDegrees)
    {
        _detentAngleDegrees = IsFinite(detentAngleDegrees) && detentAngleDegrees > 0f
            ? detentAngleDegrees
            : 10f;

        float requestedHysteresis = IsFinite(hysteresisDegrees) && hysteresisDegrees > 0f
            ? hysteresisDegrees
            : 0f;
        _hysteresisDegrees = Math.Min(requestedHysteresis, _detentAngleDegrees * 0.49f);
        _detentIndex = StableIndexForAngle(_angleDegrees);
    }

    /// <summary>
    /// Resets the accumulated angle and seeds the detent index without a tick.
    /// </summary>
    public void Reset(float angleDegrees = 0f)
    {
        _angleDegrees = IsFinite(angleDegrees) ? angleDegrees : 0f;
        _detentIndex = StableIndexForAngle(_angleDegrees);
        _hasSample = false;
        _lastSampleDegrees = 0f;
    }

    /// <summary>
    /// Starts tracking a wrapped angle sample. Call this on pointer down so
    /// the controller's initial position does not change the dial angle.
    /// </summary>
    public void BeginSample(float sampleDegrees)
    {
        _lastSampleDegrees = IsFinite(sampleDegrees) ? sampleDegrees : 0f;
        _hasSample = true;
    }

    /// <summary>Clears the wrapped input baseline without changing the angle.</summary>
    public void ClearSample()
    {
        _hasSample = false;
        _lastSampleDegrees = 0f;
    }

    /// <summary>
    /// Consumes a wrapped angular sample and returns the signed number of
    /// detents crossed. A first sample only seeds the baseline.
    /// </summary>
    public int UpdateSample(float sampleDegrees, float sensitivity = 1f)
    {
        if (!IsFinite(sampleDegrees))
        {
            _hasSample = false;
            return 0;
        }

        if (!_hasSample)
        {
            BeginSample(sampleDegrees);
            return 0;
        }

        float delta = UnwrapDelta(_lastSampleDegrees, sampleDegrees);
        _lastSampleDegrees = sampleDegrees;
        if (!IsFinite(sensitivity))
            sensitivity = 1f;

        return AdvanceBy(delta * sensitivity);
    }

    /// <summary>
    /// Advances the logical angle directly. This is useful when an input
    /// source already supplies unwrapped angular deltas.
    /// </summary>
    public int AdvanceBy(float deltaDegrees)
    {
        if (!IsFinite(deltaDegrees))
            return 0;

        float updatedAngle = _angleDegrees + deltaDegrees;
        if (!IsFinite(updatedAngle))
            return 0;

        _angleDegrees = updatedAngle;
        int crossings = 0;
        int processed = 0;

        while (processed < MaxCrossingsPerUpdate && _angleDegrees >= NextUpThreshold())
        {
            _detentIndex++;
            crossings++;
            processed++;
        }

        while (processed < MaxCrossingsPerUpdate && _angleDegrees <= NextDownThreshold())
        {
            _detentIndex--;
            crossings--;
            processed++;
        }

        // Avoid carrying unprocessed crossings into stationary future frames.
        // Large angle jumps report a bounded count, then resume from the
        // stable detent for the current angle without frame-driven catch-up.
        if (processed == MaxCrossingsPerUpdate &&
            (_angleDegrees >= NextUpThreshold() || _angleDegrees <= NextDownThreshold()))
        {
            _detentIndex = StableIndexForAngle(_angleDegrees);
        }

        return crossings;
    }

    /// <summary>
    /// Returns the shortest signed difference between two wrapped angles.
    /// For example, 179 to -179 degrees is +2 degrees.
    /// </summary>
    public static float UnwrapDelta(float previousDegrees, float currentDegrees)
    {
        if (!IsFinite(previousDegrees) || !IsFinite(currentDegrees))
            return 0f;

        float delta = (currentDegrees - previousDegrees) % 360f;
        if (delta > 180f)
            delta -= 360f;
        else if (delta < -180f)
            delta += 360f;
        return delta;
    }

    private float NextUpThreshold()
    {
        float boundary = _detentIndex < 0
            ? _detentIndex * _detentAngleDegrees
            : (_detentIndex + 1) * _detentAngleDegrees;
        return boundary + _hysteresisDegrees;
    }

    private float NextDownThreshold()
    {
        float boundary = _detentIndex > 0
            ? _detentIndex * _detentAngleDegrees
            : (_detentIndex - 1) * _detentAngleDegrees;
        return boundary - _hysteresisDegrees;
    }

    private int StableIndexForAngle(float angleDegrees)
    {
        if (angleDegrees >= 0f)
        {
            float index = (angleDegrees - _hysteresisDegrees) / _detentAngleDegrees;
            return index <= 0f ? 0 : (int)Math.Floor(index);
        }

        float negativeIndex = (angleDegrees + _hysteresisDegrees) / _detentAngleDegrees;
        return negativeIndex >= 0f ? 0 : (int)Math.Ceiling(negativeIndex);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
