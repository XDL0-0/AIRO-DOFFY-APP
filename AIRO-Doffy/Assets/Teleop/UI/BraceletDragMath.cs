using System;

/// <summary>
/// Unity-independent geometry and contact math for bracelet dragging. The
/// methods use metres and degrees and have no frame, scene, or input state.
/// </summary>
internal static class BraceletDragMath
{
    private const double DegreesPerRadian = 180.0 / Math.PI;
    private const double MinimumProjectedDirectionSquared = 1e-8;
    private const double MinimumBearingRadiusMeters = 0.0005;
    private const double RootEpsilon = 1e-9;
    private const double CandidateTieEpsilonDegrees = 0.01;

    public static bool TryGetPointAngle(
        double x,
        double y,
        out float angleDegrees)
    {
        double radiusSquared = x * x + y * y;
        if (!IsFinite(x) || !IsFinite(y) ||
            radiusSquared < MinimumBearingRadiusMeters * MinimumBearingRadiusMeters)
        {
            angleDegrees = 0f;
            return false;
        }

        angleDegrees = (float)(Math.Atan2(y, x) * DegreesPerRadian);
        return IsFinite(angleDegrees);
    }

    /// <summary>
    /// Finds forward intersections of a ray and the wrist-frame cylinder. If
    /// both antipodal roots are forward, the root whose angle continues the
    /// previous sample is preferred; the first call uses the nearest root.
    /// A ray that misses by at most tangentMissToleranceMeters uses its closest
    /// forward radial point, keeping near-grazing rays stable.
    /// </summary>
    public static bool TryGetCylinderAngle(
        double originX,
        double originY,
        double directionX,
        double directionY,
        double radiusMeters,
        bool hasPreviousAngle,
        double previousAngleDegrees,
        double previousTravelDirectionDegrees,
        double tangentMissToleranceMeters,
        out float angleDegrees)
    {
        angleDegrees = 0f;
        if (!IsFinite(originX) || !IsFinite(originY) ||
            !IsFinite(directionX) || !IsFinite(directionY) ||
            !IsFinite(radiusMeters) || radiusMeters <= 0.0)
        {
            return false;
        }

        double a = directionX * directionX + directionY * directionY;
        if (a < MinimumProjectedDirectionSquared)
            return TryGetPointAngle(originX, originY, out angleDegrees);

        double dot = originX * directionX + originY * directionY;
        double b = 2.0 * dot;
        double c = originX * originX + originY * originY - radiusMeters * radiusMeters;
        double discriminant = b * b - 4.0 * a * c;
        double scale = Math.Abs(b * b) + Math.Abs(4.0 * a * c) + 1.0;

        if (discriminant < 0.0)
        {
            if (discriminant >= -1e-12 * scale)
            {
                discriminant = 0.0;
            }
            else
            {
                double closestT = Math.Max(0.0, -dot / a);
                double closestX = originX + directionX * closestT;
                double closestY = originY + directionY * closestT;
                double closestRadius = Math.Sqrt(closestX * closestX + closestY * closestY);
                if (!IsFinite(tangentMissToleranceMeters) ||
                    closestRadius > radiusMeters + Math.Max(0.0, tangentMissToleranceMeters))
                {
                    return false;
                }

                return TryGetPointAngle(closestX, closestY, out angleDegrees);
            }
        }

        double root = Math.Sqrt(discriminant);
        double q = -0.5 * (b + (b >= 0.0 ? root : -root));
        double firstT;
        double secondT;
        if (Math.Abs(q) < RootEpsilon)
        {
            firstT = secondT = -b / (2.0 * a);
        }
        else
        {
            firstT = q / a;
            secondT = c / q;
        }

        float firstAngle = 0f;
        float secondAngle = 0f;
        bool hasFirst = TryGetRootAngle(
            originX, originY, directionX, directionY, firstT, out firstAngle);
        bool hasSecond = Math.Abs(secondT - firstT) > RootEpsilon && TryGetRootAngle(
            originX, originY, directionX, directionY, secondT, out secondAngle);

        if (!hasFirst && !hasSecond)
            return false;

        if (!hasFirst)
        {
            angleDegrees = secondAngle;
            return true;
        }

        if (!hasSecond)
        {
            angleDegrees = firstAngle;
            return true;
        }

        if (!hasPreviousAngle)
        {
            angleDegrees = firstT <= secondT ? firstAngle : secondAngle;
            return true;
        }

        double firstDelta = UnwrapDeltaDegrees(previousAngleDegrees, firstAngle);
        double secondDelta = UnwrapDeltaDegrees(previousAngleDegrees, secondAngle);
        double firstDistance = Math.Abs(firstDelta);
        double secondDistance = Math.Abs(secondDelta);
        if (firstDistance + CandidateTieEpsilonDegrees < secondDistance)
        {
            angleDegrees = firstAngle;
            return true;
        }

        if (secondDistance + CandidateTieEpsilonDegrees < firstDistance)
        {
            angleDegrees = secondAngle;
            return true;
        }

        if (Math.Abs(previousTravelDirectionDegrees) > CandidateTieEpsilonDegrees)
        {
            angleDegrees = previousTravelDirectionDegrees > 0.0
                ? (firstDelta >= secondDelta ? firstAngle : secondAngle)
                : (firstDelta <= secondDelta ? firstAngle : secondAngle);
            return true;
        }

        angleDegrees = firstT <= secondT ? firstAngle : secondAngle;
        return true;
    }

    public static double UnwrapDeltaDegrees(double previousDegrees, double currentDegrees)
    {
        if (!IsFinite(previousDegrees) || !IsFinite(currentDegrees))
            return 0.0;

        double delta = (currentDegrees - previousDegrees) % 360.0;
        if (delta > 180.0)
            delta -= 360.0;
        else if (delta < -180.0)
            delta += 360.0;
        return delta;
    }

    public static bool IsWithinPokeAcquireBand(
        double radialGapMeters,
        double axialOffsetMeters,
        double halfBandWidthMeters,
        double pokeRadiusMeters,
        double radialExtraPaddingMeters,
        double axialExtraPaddingMeters)
    {
        return IsFinite(radialGapMeters) && IsFinite(axialOffsetMeters) &&
               IsFinite(halfBandWidthMeters) && IsFinite(pokeRadiusMeters) &&
               IsFinite(radialExtraPaddingMeters) && IsFinite(axialExtraPaddingMeters) &&
               radialGapMeters <= Math.Max(0.0, pokeRadiusMeters + radialExtraPaddingMeters) &&
               Math.Abs(axialOffsetMeters) <= Math.Max(0.0, halfBandWidthMeters + axialExtraPaddingMeters);
    }

    public static bool ShouldAcquirePoke(
        double radialGapMeters,
        double previousRadialGapMeters,
        bool hasPreviousGap,
        bool sdkSelected,
        double pokeRadiusMeters,
        double contactExtraMeters,
        double approachExtraMeters,
        double minimumApproachMeters)
    {
        if (sdkSelected)
            return true;

        if (!IsFinite(radialGapMeters) || !IsFinite(pokeRadiusMeters))
            return false;

        if (radialGapMeters <= Math.Max(0.0, pokeRadiusMeters + contactExtraMeters))
            return true;

        return hasPreviousGap && IsFinite(previousRadialGapMeters) &&
               radialGapMeters <= Math.Max(0.0, pokeRadiusMeters + approachExtraMeters) &&
               previousRadialGapMeters - radialGapMeters >= Math.Max(0.0, minimumApproachMeters);
    }

    public static bool IsWithinPokeReleaseBand(
        double radialDistanceMeters,
        double cylinderRadiusMeters,
        double axialOffsetMeters,
        double halfBandWidthMeters,
        double pokeRadiusMeters,
        double releaseExtraMeters)
    {
        return IsFinite(radialDistanceMeters) && IsFinite(cylinderRadiusMeters) &&
               IsFinite(axialOffsetMeters) && IsFinite(halfBandWidthMeters) &&
               IsFinite(pokeRadiusMeters) && IsFinite(releaseExtraMeters) &&
               Math.Abs(radialDistanceMeters - cylinderRadiusMeters) <=
                   Math.Max(0.0, pokeRadiusMeters + releaseExtraMeters) &&
               Math.Abs(axialOffsetMeters) <=
                   Math.Max(0.0, halfBandWidthMeters + pokeRadiusMeters + releaseExtraMeters);
    }

    /// <summary>
    /// Keeps a post-tracking-loss latch closed until the fingertip leaves the
    /// release band. Returns true on the first observed lift so callers can
    /// rebase approach history without reacquiring in that same sample.
    /// </summary>
    public static bool ObservePokeLift(ref bool requireLift, bool withinReleaseBand)
    {
        if (!requireLift || withinReleaseBand)
            return false;

        requireLift = false;
        return true;
    }

    private static bool TryGetRootAngle(
        double originX,
        double originY,
        double directionX,
        double directionY,
        double t,
        out float angleDegrees)
    {
        angleDegrees = 0f;
        if (!IsFinite(t) || t < -RootEpsilon)
            return false;

        double forwardT = Math.Max(0.0, t);
        return TryGetPointAngle(
            originX + directionX * forwardT,
            originY + directionY * forwardT,
            out angleDegrees);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
