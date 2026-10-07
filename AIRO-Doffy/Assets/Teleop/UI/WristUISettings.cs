using UnityEngine;

namespace Doffy.UI
{
    [CreateAssetMenu(menuName = "DOFFY/Wrist UI settings")]
    public sealed class WristUISettings : ScriptableObject
    {
        [Header("Bracelet layout (metres)")]
        [Range(.045f, .10f)] public float radius = .055f;
        [Range(.03f, .06f)] public float buttonWidth = .036f;
        [Range(.032f, .065f)] public float buttonHeight = .038f;
        [Range(.012f, .035f)] public float dialWidth = .020f;
        [Range(.00035f, .001f)] public float pageScale = .00052f;
        [Range(.6f, 1f)] public float handBraceletScale = .8f;
        [Range(.00035f, .001f)] public float handPageScale = .00042f;
        public Vector2 pageOffset = new Vector2(.075f, .10f);

        [Header("Wrist attachment")]
        [Tooltip("Fallback wrist estimate when the controller-driven hand skeleton is unavailable.")]
        public Vector3 controllerOffset = new Vector3(0, 0, -.105f);
        public Vector3 controllerEuler = Vector3.zero;
        public Vector3 handOffset = new Vector3(0, 0, -.035f);
        public Vector3 handEuler = Vector3.zero;
        [Min(1)] public float followSharpness = 24;

        [Header("Rotary detents")]
        [Range(.1f, 3)] public float rotationSensitivity = 1;
        [Range(.5f, 3)] public float pokeRotationSensitivity = 1.3f;
        [Range(2, 45)] public float detentAngle = 10;
        [Range(0, 1)] public float hapticAmplitude = .25f;
        [Range(.005f, .05f)] public float hapticDuration = .015f;

        [Header("Viewing pose hysteresis")]
        [Tooltip("Reserved for flat-panel compatibility; bracelet visibility is independent of wrist roll.")]
        [Range(15, 85)] public float showFacingAngle = 65;
        [Range(20, 100)] public float hideFacingAngle = 82;
        [Tooltip("Hide when the wrist's forward/finger direction points this far below horizontal.")]
        [Range(20, 85)] public float hideDownAngle = 55;
        [Range(0, 70)] public float showDownAngle = 35;
        [Min(0)] public float showDelay = .18f;
        [Min(0)] public float hideDelay = .25f;

        private void OnValidate()
        {
            hideFacingAngle = Mathf.Max(showFacingAngle + 2, hideFacingAngle);
            hideDownAngle = Mathf.Max(showDownAngle + 2, hideDownAngle);
        }
    }
}
