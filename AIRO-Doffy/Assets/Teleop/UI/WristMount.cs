using System;
using Oculus.Interaction;
using UnityEngine;

namespace Doffy.UI
{
    [DefaultExecutionOrder(-40)]
    public sealed class WristMount : MonoBehaviour
    {
        private AppManager app;
        private WristInteractionSource input;
        private WristUISettings settings;
        private GameObject presentation;
        private Action hidden;
        private readonly WristVisibilityGate visibility = new WristVisibilityGate();
        private bool poseInitialized, previousHands;
        private HandVisual controllerHandVisual;
        public bool Visible => presentation != null && presentation.activeSelf;

        public void Initialize(AppManager manager, WristInteractionSource source, WristUISettings config,
            GameObject visuals, Action onHidden)
        {
            app = manager;
            input = source;
            settings = config;
            presentation = visuals;
            hidden = onHidden;
            presentation.SetActive(false);
            if (app != null) app.Changed += OnAppChanged;
            BindControllerHandVisual();
        }

        private void OnAppChanged()
        {
            if (WristInteractionSource.IsSessionOpen(app))
            {
                visibility.Reset();
                SetVisible(false);
            }
        }

        private void Update()
        {
            if (presentation == null) return;
            bool tracked = input != null && input.TryGetLeftPose(out _);
            if (!tracked || app == null || WristInteractionSource.IsSessionOpen(app))
            {
                visibility.Reset();
                SetVisible(false);
                if (!tracked) poseInitialized = false;
            }
        }

        private void LateUpdate()
        {
            if (presentation == null || settings == null || input == null) return;
            BindControllerHandVisual();
            bool tracked = input.TryGetLeftPose(out Pose wrist);
            if (!tracked || input.Head == null)
            {
                visibility.Reset();
                SetVisible(false);
                poseInitialized = false;
                return;
            }
            bool hands = input.HandsActive;
            Vector3 offset = hands ? settings.handOffset : settings.controllerOffset;
            Quaternion rotation = wrist.rotation * Quaternion.Euler(hands ? settings.handEuler : settings.controllerEuler);
            Vector3 position = wrist.position + wrist.rotation * offset;
            if (!hands && input.TryGetControllerWristPose(out Pose controllerWrist))
            {
                position = controllerWrist.position;
                rotation = controllerWrist.rotation * Quaternion.Euler(settings.controllerEuler);
            }
            if (!poseInitialized || previousHands != hands)
            {
                transform.SetPositionAndRotation(position, rotation);
                visibility.Reset();
                SetVisible(false);
                poseInitialized = true;
            }
            else if (!hands)
            {
                // Controller-driven hand visuals use the current pose directly. A separate
                // smoothing step lets their wrist move through the cuff during fast motion.
                transform.SetPositionAndRotation(position, rotation);
            }
            else
            {
                float t = 1 - Mathf.Exp(-settings.followSharpness * Time.unscaledDeltaTime);
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, position, t),
                    Quaternion.Slerp(transform.rotation, rotation, t));
            }
            previousHands = hands;
            // Use raw wrist orientation for intent. Smoothing must not delay a rest/active hide.
            // A cylindrical cuff has usable controls all around it. Wrist roll rotates the
            // cuff physically and must never be interpreted as turning a flat panel away.
            float down = Vector3.Dot(wrist.rotation * Vector3.forward, Vector3.down);
            bool showPose = down <= Mathf.Sin(settings.showDownAngle * Mathf.Deg2Rad);
            bool hidePose = down > Mathf.Sin(settings.hideDownAngle * Mathf.Deg2Rad);
            bool blocked = app == null || WristInteractionSource.IsSessionOpen(app) || !input.CanPresentWrist;
            SetVisible(visibility.Step(blocked, showPose, hidePose, Time.unscaledDeltaTime,
                settings.showDelay, settings.hideDelay));
        }

        private void SetVisible(bool visible)
        {
            if (presentation.activeSelf == visible) return;
            if (!visible) hidden?.Invoke();
            presentation.SetActive(visible); // Also removes Meta interactables/colliders from selection.
        }

        public void SnapToWrist() => poseInitialized = false;

        private void BindControllerHandVisual()
        {
            var visual = input != null ? input.ControllerHandVisual : null;
            if (visual == controllerHandVisual) return;
            if (controllerHandVisual != null)
                controllerHandVisual.WhenHandVisualUpdated -= OnControllerHandVisualUpdated;
            controllerHandVisual = visual;
            if (controllerHandVisual != null)
                controllerHandVisual.WhenHandVisualUpdated += OnControllerHandVisualUpdated;
        }

        private void OnControllerHandVisualUpdated()
        {
            // Follow render-time skeleton updates too, including Meta's before-render pose.
            if (!isActiveAndEnabled || settings == null || input == null || input.Head == null ||
                app == null || WristInteractionSource.IsSessionOpen(app)) return;
            if (input.TryGetControllerWristPose(out Pose wrist))
                transform.SetPositionAndRotation(wrist.position,
                    wrist.rotation * Quaternion.Euler(settings.controllerEuler));
        }

        private void OnDisable()
        {
            if (controllerHandVisual != null)
                controllerHandVisual.WhenHandVisualUpdated -= OnControllerHandVisualUpdated;
            controllerHandVisual = null;
            visibility.Reset();
            if (presentation != null) SetVisible(false);
        }

        private void OnDestroy()
        {
            if (controllerHandVisual != null)
                controllerHandVisual.WhenHandVisualUpdated -= OnControllerHandVisualUpdated;
            if (app != null) app.Changed -= OnAppChanged;
        }
    }
}
