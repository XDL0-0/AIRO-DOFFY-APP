using System;

namespace Doffy.Protocol
{
    /// <summary>Fixed down-pose height reference, dead zones, EMA and smoothstep.</summary>
    public sealed class UpperLimbHeightMapping
    {
        private float filtered;
        public float Value { get; private set; }
        public void Reset() { filtered = 0; Value = 0; }

        public float Step(bool calibrated, bool tracked, float hangHeight, float currentHeight,
            float deltaTime, float deadZone, float fullZone, float timeConstant)
        {
            if (!calibrated) { Reset(); return Value; }
            // Hold the last alpha during tracking loss; confidence travels separately.
            if (!tracked) return Value;
            if (hangHeight <= .0001f) { Reset(); return Value; }
            float raw = Clamp01((hangHeight - currentHeight) / hangHeight);
            float target = raw < deadZone ? 0 : raw > 1 - fullZone ? 1 : raw;
            float blend = Clamp01(1 - (float)Math.Exp(-deltaTime / Math.Max(timeConstant, .0001f)));
            filtered += (target - filtered) * blend;
            Value = filtered * filtered * (3 - 2 * filtered);
            return Value;
        }

        private static float Clamp01(float value) => value < 0 ? 0 : value > 1 ? 1 : value;
    }
}
