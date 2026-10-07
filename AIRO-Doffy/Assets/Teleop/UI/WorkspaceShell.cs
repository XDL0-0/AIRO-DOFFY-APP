using System.Collections;
using TMPro;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace Doffy.UI
{
    /// <summary>Wrist navigation; existing feature pages still own all application callbacks.</summary>
    [DefaultExecutionOrder(-20)]
    public sealed class WorkspaceShell : MonoBehaviour
    {
        // Kept for existing preview/tools integrations. Detail pages retain their logical dimensions.
        public const float Width = 900, Height = 620;
        [SerializeField] private WristUISettings wristSettings;
        [SerializeField] private Canvas legacyCanvas;
        [SerializeField] private Transform legacyVideoPanels;
        private WorkspaceBindings bindings;
        private WorkspacePages pages;
        private WristInteractionSource input;
        private WristMount mount;
        private BraceletRotator rotator;
        private Transform braceletRotor;
        private TeleopRecordPanel recordingPanel;
        private RectTransform pageCanvas, content;
        private WristDetailAnchor detailAnchor;
        private WristCanvasSurface pageSurface, headerCloseSurface;
        private TextMeshProUGUI status, inputStatus, feedbackStatus, title;
        private Button stream;
        private readonly Button[] navigation = new Button[5];
        private readonly RectTransform[] ringControls = new RectTransform[6];
        private static readonly string[] PageNames = { "Teleop Config", "Camera", "WRM Setting", "System Setting", "Exit APP" };
        private int selectedPage;
        private float nextRefresh;
        private bool layoutModeInitialized, layoutHands;
        private GameObject presentation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindAnyObjectByType<AppManager>() == null ||
                Object.FindAnyObjectByType<WorkspaceShell>() != null) return;
            new GameObject("DOFFY Wrist Workspace").AddComponent<WorkspaceShell>();
        }

        private IEnumerator Start()
        {
            yield return null; // Existing feature and Meta rig bootstraps get their Start first.
            bindings = new WorkspaceBindings();
            input = WristInteractionSource.Ensure();
            if (wristSettings == null) wristSettings = Resources.Load<WristUISettings>("WristUISettings");
            if (wristSettings == null) wristSettings = ScriptableObject.CreateInstance<WristUISettings>();
            RetireLegacyControls();
            Build();
            mount = gameObject.AddComponent<WristMount>();
            mount.Initialize(bindings.App, input, wristSettings, presentation, OnHidden);
            // The record control has an independent lifetime from the hidden wrist presentation.
            recordingPanel = new GameObject("Teleop recording panel").AddComponent<TeleopRecordPanel>();
            recordingPanel.Initialize(bindings.App, bindings.Recorder, input);
        }

        private void RetireLegacyControls()
        {
            // Scene references preserve service lifetime and floating video descendants.
            if (legacyCanvas == null) return;
            foreach (var graphic in legacyCanvas.GetComponentsInChildren<Graphic>(true))
            {
                if (legacyVideoPanels != null && graphic.transform.IsChildOf(legacyVideoPanels)) continue;
                graphic.enabled = false;
                graphic.raycastTarget = false;
            }
            foreach (var control in legacyCanvas.GetComponentsInChildren<Selectable>(true))
                if (legacyVideoPanels == null || !control.transform.IsChildOf(legacyVideoPanels)) control.enabled = false;
            foreach (var ray in legacyCanvas.GetComponentsInChildren<RayInteractable>(true))
                if (legacyVideoPanels == null || !ray.transform.IsChildOf(legacyVideoPanels)) ray.enabled = false;
            foreach (var poke in legacyCanvas.GetComponentsInChildren<PokeInteractable>(true))
                if (legacyVideoPanels == null || !poke.transform.IsChildOf(legacyVideoPanels)) poke.enabled = false;
            var fold = legacyCanvas.GetComponent<CollapsibleCanvas>();
            if (fold != null) fold.enabled = false;
        }

        private RectTransform CreateCanvas(string name, Vector2 size, float scale)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PointableCanvas));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(presentation.transform, false);
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * scale;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 30;
            root.GetComponent<PointableCanvas>().InjectCanvas(canvas);
            return rect;
        }

        private void Build()
        {
            presentation = new GameObject("Wrist presentation");
            presentation.transform.SetParent(transform, false);
            braceletRotor = new GameObject("Bracelet rotor").transform;
            braceletRotor.SetParent(presentation.transform, false);
            BraceletBody.Create(braceletRotor, wristSettings.radius - .0015f,
                wristSettings.buttonHeight * .5f + .003f,
                -wristSettings.buttonHeight * .5f - wristSettings.dialWidth - .005f);
            rotator = presentation.AddComponent<BraceletRotator>();
            rotator.SetTuning(wristSettings.rotationSensitivity, wristSettings.detentAngle,
                wristSettings.hapticAmplitude, wristSettings.hapticDuration);
            rotator.SetPokeTuning(wristSettings.pokeRotationSensitivity);
            float bandZ = -wristSettings.buttonHeight * .5f - wristSettings.dialWidth * .5f - .003f;
            rotator.Configure(transform, input, wristSettings.radius, bandZ, wristSettings.dialWidth,
                () => mount != null && mount.Visible && input.CanPresentWrist, LayoutRing);
            BuildDragBand(bandZ);

            for (int i = 0; i < navigation.Length; i++)
            {
                int index = i;
                navigation[i] = RingButton(PageNames[i], i + 1, () => SelectPage(index));
            }
            stream = RingButton("Start\nTeleop", 0, ToggleSession, true);
            LayoutRing(0);

            pageCanvas = CreateCanvas("Wrist details", new Vector2(Width, Height), wristSettings.pageScale);
            pageCanvas.pivot = new Vector2(0, 1);
            detailAnchor = pageCanvas.gameObject.AddComponent<WristDetailAnchor>();
            detailAnchor.Configure(input, wristSettings);
            var panel = WorkspaceWidgets.Panel("Details", pageCanvas, 0, 0, Width, Height, WorkspaceTheme.Background);
            title = WorkspaceWidgets.Label(panel, "Teleop Config", 28, 16, 540, 42, 30, null, true);
            var closeButton = WorkspaceWidgets.Button(panel, "Close", 690, 20, 182, CloseDetails, false, 58);
            headerCloseSurface = WristCanvasSurface.Create(closeButton.GetComponent<RectTransform>(),
                pageCanvas.GetComponent<PointableCanvas>(), input);
            headerCloseSurface.enabled = false;
            inputStatus = WorkspaceWidgets.Label(panel, "", 28, 61, 620, 28, 19, WorkspaceTheme.Muted);
            feedbackStatus = WorkspaceWidgets.Label(panel, "", 28, 93, 540, 26, 18, WorkspaceTheme.Muted);
            status = WorkspaceWidgets.Label(panel, "", 575, 93, 270, 26, 15, WorkspaceTheme.Accent);
            content = WorkspaceWidgets.Rect("Page", panel, 28, 132, 842, 452);
            pageSurface = WristCanvasSurface.Create(pageCanvas, pageCanvas.GetComponent<PointableCanvas>(), input);
            pages = new WorkspacePages(bindings, this);
            BuildPage(0);
            pageCanvas.gameObject.SetActive(false);
            ApplyInputLayout();
        }

        private void BuildDragBand(float bandZ)
        {
            const int segments = 24;
            float width = 2 * wristSettings.radius * Mathf.Tan(Mathf.PI / segments) * 1000;
            for (int i = 0; i < segments; i++)
            {
                var canvas = CreateCanvas("Bracelet drag segment " + i,
                    new Vector2(width, wristSettings.dialWidth * 1000), .001f);
                canvas.SetParent(braceletRotor, false);
                PlaceOnCuff(canvas, i * 360f / segments, bandZ);
                var strip = WorkspaceWidgets.Panel("Drag bracelet", canvas, 0, 0,
                    width, wristSettings.dialWidth * 1000, WorkspaceTheme.Raised);
                Centre(strip, Vector2.zero);
                strip.GetComponent<RoundedPanel>().raycastTarget = true;
                strip.gameObject.AddComponent<BraceletDragHandle>().Initialize(rotator);
                var mark = WorkspaceWidgets.Panel("Grip mark", strip, 0, 0, 1.5f,
                    wristSettings.dialWidth * 650, i % 3 == 0 ? WorkspaceTheme.Accent : WorkspaceTheme.Muted);
                Centre(mark, Vector2.zero);
                WristCanvasSurface.Create(strip, canvas.GetComponent<PointableCanvas>(), input);
            }
        }

        private Button RingButton(string text, int index, System.Action click, bool primary = false)
        {
            var canvas = CreateCanvas("Bracelet control " + index,
                new Vector2(wristSettings.buttonWidth * 1000, wristSettings.buttonHeight * 1000), .001f);
            canvas.SetParent(braceletRotor, false);
            PlaceOnCuff(canvas, 90 - index * 360f / ringControls.Length, 0);
            var button = WorkspaceWidgets.Button(canvas, text, 0, 0,
                wristSettings.buttonWidth * 1000, () => { if (rotator == null || !rotator.IsDragging) click?.Invoke(); }, primary, wristSettings.buttonHeight * 1000);
            var rect = button.GetComponent<RectTransform>();
            Centre(rect, Vector2.zero);
            ringControls[index] = canvas;
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.rectTransform.anchoredPosition = new Vector2(2, 0);
            label.rectTransform.sizeDelta = new Vector2(wristSettings.buttonWidth * 1000 - 4, wristSettings.buttonHeight * 1000);
            label.fontSize = 9;
            label.enableAutoSizing = true;
            label.fontSizeMin = 7;
            label.fontSizeMax = 10;
            WristCanvasSurface.Create(rect, canvas.GetComponent<PointableCanvas>(), input);
            return button;
        }

        private void PlaceOnCuff(RectTransform rect, float angle, float z)
        {
            float radians = angle * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0);
            rect.localPosition = radial * wristSettings.radius + Vector3.forward * z;
            // A UI canvas faces -Z; align that front with the cuff's outward radial normal.
            rect.localRotation = Quaternion.LookRotation(-radial, Vector3.forward);
        }

        private static void Centre(RectTransform rect, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
        }

        private void LayoutRing(float angle)
        {
            // Only the visual transform wraps: the rotary value has no end stops.
            braceletRotor.localRotation = Quaternion.AngleAxis(angle % 360f, Vector3.forward);
        }

        private void BuildPage(int page)
        {
            selectedPage = page;
            title.text = PageNames[page];
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            for (int i = 0; i < navigation.Length; i++) WorkspaceWidgets.Selected(navigation[i], i == page);
            pages.Build(page, content);
        }

        private void SelectPage(int page)
        {
            bool close = pageCanvas.gameObject.activeSelf && selectedPage == page;
            WorkspaceKeypad.Close();
            if (close) { CloseDetails(); return; }
            BuildPage(page);
            pageCanvas.gameObject.SetActive(true);
        }

        public void CloseDetails()
        {
            WorkspaceKeypad.Close();
            pageCanvas.gameObject.SetActive(false);
        }

        private void OnHidden()
        {
            WorkspaceKeypad.Close();
            if (pageCanvas != null) pageCanvas.gameObject.SetActive(false);
        }

        private void Update()
        {
            bool tilesReady = rotator == null || !rotator.IsDragging;
            foreach (var button in navigation)
                if (button != null) button.interactable = tilesReady;
            if (stream != null) stream.interactable = tilesReady;
            if (bindings == null || pages == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .15f;
            var app = bindings.App;
            if (app == null) return;
            status.text = app.StatusText;
            status.color = app.NeedsRecalibration ? WorkspaceTheme.Warning : WorkspaceTheme.Accent;
            inputStatus.text = input.HandsActive ? "UI: RIGHT INDEX POKE" : "UI: RIGHT CONTROLLER RAY";
            WorkspaceWidgets.Text(stream, app.IsStreaming ? "Stop\nTeleop" : "Start\nTeleop");
            bool live = bindings.Feedback != null && bindings.Feedback.IsFresh;
            feedbackStatus.text = live ? "ROBOT FEEDBACK · LIVE" : bindings.Feedback != null && bindings.Feedback.HasReceivedData ? "ROBOT FEEDBACK · STALE" : "FEEDBACK · WAITING";
            feedbackStatus.color = live ? WorkspaceTheme.Accent : WorkspaceTheme.Muted;
            stream.GetComponent<RoundedPanel>().color = app.IsStreaming ? WorkspaceTheme.Warning : WorkspaceTheme.Accent;
            pages.Refresh(selectedPage);
        }

        // Input source (-80), wrist mount (-40), layout (-20), then rotary sampling (0).
        private void LateUpdate() => ApplyInputLayout();

        private void ApplyInputLayout()
        {
            if (input == null || wristSettings == null || braceletRotor == null || pageCanvas == null) return;
            bool hands = input.HandsActive;
            if (layoutModeInitialized && layoutHands == hands) return;
            layoutModeInitialized = true;
            layoutHands = hands;
            float scale = hands ? wristSettings.handBraceletScale : 1f;
            braceletRotor.localScale = Vector3.one * scale;
            float bandZ = -wristSettings.buttonHeight * .5f - wristSettings.dialWidth * .5f - .003f;
            rotator.SetGeometry(wristSettings.radius * scale, bandZ * scale, wristSettings.dialWidth * scale);
            pageCanvas.localScale = Vector3.one * (hands ? wristSettings.handPageScale : wristSettings.pageScale);
            detailAnchor?.RefreshPose();
        }

        private void OnDestroy()
        {
            if (recordingPanel != null) Destroy(recordingPanel.gameObject);
        }

        public void Recenter()
        {
            rotator?.ResetRotation();
            mount?.SnapToWrist();
        }

        private void ToggleSession()
        {
            WorkspaceKeypad.Close();
            if (bindings.App == null) return;
            if (bindings.App.IsStreaming || pages.ApplyConnection()) bindings.App.ToggleStreaming();
        }

        public void OpenKeypad(TMP_InputField field)
        {
            WorkspaceKeypad.Close();
            content.gameObject.SetActive(false);
            pageSurface.enabled = false;
            headerCloseSurface.enabled = true;
            WorkspaceKeypad.Show(pageCanvas, field, () =>
            {
                if (content != null) content.gameObject.SetActive(true);
                if (pageSurface != null) pageSurface.enabled = true;
                if (headerCloseSurface != null) headerCloseSurface.enabled = false;
            });
        }

        public void CloseKeypad() => WorkspaceKeypad.Close();
    }
}
