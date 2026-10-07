using TMPro;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace Doffy.UI
{
    /// <summary>
    /// Compact recording and session control shown for the teleoperation session lifetime.
    /// Attach this component to a root object outside the wrist presentation hierarchy.
    /// </summary>
    public sealed class TeleopRecordPanel : MonoBehaviour
    {
        private const float CanvasScale = 0.0003f;
        private const float FacingSharpness = 10f;
        private static readonly Vector2 CanvasSize = new Vector2(600f, 520f);

        private AppManager app;
        private RecordingController recorder;
        private WristInteractionSource input;
        private GameObject canvasObject;
        private Canvas canvas;
        private RectTransform canvasRect;
        private TextMeshProUGUI title;
        private Button recordButton;
        private Button quitButton;
        private Button undoButton;
        private float confirmUndoUntil;
        private RoundedPanel recordButtonBackground;
        private TextMeshProUGUI recordButtonLabel;
        private bool poseInitialized;
        private Vector3 lastHorizontalForward = Vector3.forward;
        private TeleopPanelDragHandle dragHandle;
        private bool initialized;

        /// <summary>Bind existing services and build the standalone panel once.</summary>
        public void Initialize(AppManager manager, RecordingController recordingController,
            WristInteractionSource interactionSource)
        {
            if (manager == null)
                throw new System.ArgumentNullException(nameof(manager));

            app = manager;
            recorder = recordingController;
            input = interactionSource == null ? WristInteractionSource.Ensure() : interactionSource;
            if (initialized)
            {
                dragHandle?.Initialize(transform, input);
                WristCanvasSurface.Create(canvasRect, canvasObject.GetComponent<PointableCanvas>(),
                    input, true, true);
                return;
            }

            BuildPanel();
            initialized = true;
        }

        private void BuildPanel()
        {
            canvasObject = new GameObject("Teleop record panel canvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PointableCanvas));
            canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.SetParent(transform, false);
            canvasRect.anchorMin = canvasRect.anchorMax = canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.sizeDelta = CanvasSize;
            canvasRect.localScale = Vector3.one * CanvasScale;

            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 50;
            canvasObject.GetComponent<PointableCanvas>().InjectCanvas(canvas);
            canvasObject.SetActive(false);

            WorkspaceWidgets.Panel("Session panel background", canvasRect, 0f, 0f,
                CanvasSize.x, CanvasSize.y, WorkspaceTheme.Background);
            title = WorkspaceWidgets.Label(canvasRect, "TELEOP", 24f, 14f,
                CanvasSize.x - 48f, 64f, 27f, WorkspaceTheme.Accent, true);
            title.alignment = TextAlignmentOptions.MidlineLeft;

            const float sideMargin = 20f;
            const float gap = 20f;
            const float buttonY = 92f;
            const float buttonHeight = 180f;
            float buttonWidth = (CanvasSize.x - sideMargin * 2f - gap) * 0.5f;
            recordButton = WorkspaceWidgets.Button(canvasRect, "Start recording", sideMargin, buttonY,
                buttonWidth, ToggleRecording, true, buttonHeight);
            recordButtonBackground = recordButton.GetComponent<RoundedPanel>();
            recordButtonLabel = recordButton.GetComponentInChildren<TextMeshProUGUI>();
            quitButton = WorkspaceWidgets.Button(canvasRect, "Quit teleop",
                sideMargin + buttonWidth + gap, buttonY, buttonWidth, QuitSession, false, buttonHeight);
            undoButton = WorkspaceWidgets.Button(canvasRect, "Undo episode", sideMargin, 288f,
                CanvasSize.x - sideMargin * 2f, UndoEpisode, false, 136f);
            var handle = WorkspaceWidgets.Panel("Session panel drag bar", canvasRect,
                80f, 450f, CanvasSize.x - 160f, 50f, WorkspaceTheme.Raised);
            handle.GetComponent<RoundedPanel>().raycastTarget = true;
            dragHandle = handle.gameObject.AddComponent<TeleopPanelDragHandle>();
            dragHandle.Initialize(transform, input);
            WristCanvasSurface.Create(canvasRect, canvasObject.GetComponent<PointableCanvas>(),
                input, true, true);
        }

        private void Update()
        {
            if (!initialized || canvasObject == null)
                return;

            bool sessionOpen = WristInteractionSource.IsSessionOpen(app);
            if (!sessionOpen) poseInitialized = false;
            bool hasHeadPose = input != null && input.Head != null;
            SetVisible(sessionOpen && hasHeadPose);
            RefreshRecordingState();
        }

        private void LateUpdate()
        {
            if (!initialized || canvasObject == null || !canvasObject.activeSelf ||
                input == null || input.Head == null)
                return;

            Transform head = input.Head;
            if (!poseInitialized)
            {
                Vector3 horizontalForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                if (horizontalForward.sqrMagnitude < 0.0001f)
                    horizontalForward = lastHorizontalForward;
                horizontalForward.Normalize();
                lastHorizontalForward = horizontalForward;
                transform.SetPositionAndRotation(
                    head.position + horizontalForward * 0.43f + Vector3.down * 0.22f,
                    Quaternion.LookRotation(horizontalForward, Vector3.up));
                poseInitialized = true;
            }
            else if (dragHandle == null || !dragHandle.IsDragging)
            {
                // Canvas front is local -Z. Keep the chosen world position and only turn
                // horizontally toward the head; dragging owns the pose while held.
                Vector3 awayFromHead = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);
                if (awayFromHead.sqrMagnitude > 0.0001f)
                {
                    float blend = 1f - Mathf.Exp(-FacingSharpness * Time.unscaledDeltaTime);
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(awayFromHead, Vector3.up), blend);
                }
            }

            if (canvas != null && canvas.worldCamera == null)
                canvas.worldCamera = Camera.main;
        }

        private void SetVisible(bool visible)
        {
            if (!visible) confirmUndoUntil = 0f;
            if (canvasObject != null && canvasObject.activeSelf != visible)
                canvasObject.SetActive(visible);
        }

        private void RefreshRecordingState()
        {
            if (title == null || recordButton == null || quitButton == null)
                return;

            bool recording = recorder != null && recorder.IsRecording;
            bool sessionOpen = WristInteractionSource.IsSessionOpen(app);
            bool inputReady = sessionOpen && input != null && input.CanInteractForRecording;
            bool streamingReady = app != null && app.IsStreaming &&
                                  app.SessionState == TeleopSessionState.Streaming;

            string stateTitle = GetSessionTitle(recording);
            Color stateColor = recording ? WorkspaceTheme.Danger : WorkspaceTheme.Accent;
            if (title.text != stateTitle) title.text = stateTitle;
            if (title.color != stateColor) title.color = stateColor;
            WorkspaceWidgets.Text(recordButton, recording ? "Stop recording" : "Start recording");
            if (recordButtonBackground != null)
                recordButtonBackground.color = recording ? WorkspaceTheme.Danger : WorkspaceTheme.Accent;
            if (recordButtonLabel != null)
                recordButtonLabel.color = WorkspaceTheme.Background;
            // Once recording has started, keep the stop action available while tracking is paused.
            recordButton.interactable = recorder != null && inputReady && (recording || streamingReady);
            quitButton.interactable = inputReady;
            if (undoButton != null)
            {
                bool confirm = confirmUndoUntil > Time.unscaledTime;
                undoButton.interactable = recorder != null && inputReady;
                WorkspaceWidgets.Text(undoButton, confirm ? "Confirm undo" : "Undo episode");
                undoButton.GetComponent<RoundedPanel>().color = confirm ? WorkspaceTheme.Warning : WorkspaceTheme.Raised;
            }
        }

        private string GetSessionTitle(bool recording)
        {
            if (recording)
                return "TELEOP  ·  RECORDING";
            if (app == null)
                return "TELEOP";

            switch (app.SessionState)
            {
                case TeleopSessionState.Starting: return "TELEOP  ·  STARTING";
                case TeleopSessionState.VideoConnecting: return "TELEOP  ·  CONNECTING VIDEO";
                case TeleopSessionState.TrackingLost: return "TELEOP  ·  TRACKING PAUSED";
                default: return "TELEOP  ·  ACTIVE";
            }
        }

        private void ToggleRecording()
        {
            if (recorder == null || !WristInteractionSource.IsSessionOpen(app) || input == null ||
                !input.CanInteractForRecording)
                return;

            if (recorder.IsRecording)
                recorder.StopRecording();
            else if (app.IsStreaming && app.SessionState == TeleopSessionState.Streaming)
                recorder.Recording();
            RefreshRecordingState();
        }

        private void UndoEpisode()
        {
            if (recorder == null || !WristInteractionSource.IsSessionOpen(app) || input == null ||
                !input.CanInteractForRecording) return;
            if (confirmUndoUntil > Time.unscaledTime)
            {
                confirmUndoUntil = 0f;
                recorder.Undo();
            }
            else confirmUndoUntil = Time.unscaledTime + 4f;
            RefreshRecordingState();
        }

        private void QuitSession()
        {
            if (app == null || !WristInteractionSource.IsSessionOpen(app) || input == null ||
                !input.CanInteractForRecording)
                return;

            // StopStreaming is the established idempotent session shutdown path. ToggleStreaming
            // could start a new session from an already-idle/error state.
            app.StopStreaming();
            SetVisible(false);
        }

        private void OnDisable()
        {
            poseInitialized = false;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (canvasObject != null)
                Destroy(canvasObject);
        }
    }
}
