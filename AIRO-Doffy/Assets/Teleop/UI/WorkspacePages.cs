using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Doffy.UI
{
    public sealed partial class WorkspacePages
    {
        private readonly WorkspaceBindings b;
        private readonly WorkspaceShell shell;
        private RectTransform root;
        private RectTransform configContent;
        private bool alignmentTab;
        private Button connectionTabButton, alignmentTabButton, axesButton, confirmAlignmentButton;
        private string pendingHost;
        private TextMeshProUGUI detail, metric;
        private Button modeButton, wrmButton, calibrationButton, bodyDataButton;
        private readonly Button[] trackingButtons = new Button[2];
        private readonly Button[] trackButtons = new Button[3];

        public WorkspacePages(WorkspaceBindings bindings, WorkspaceShell workspace)
        {
            b = bindings;
            shell = workspace;
            pendingHost = b.App != null ? b.App.ServerIP : "";
        }

        public bool ApplyConnection() => b.App != null && b.App.ConfigureConnection(pendingHost, b.App.NumWebRTC);

        public void Build(int page, RectTransform parent)
        {
            root = parent;
            detail = metric = null;
            modeButton = wrmButton = calibrationButton = bodyDataButton = null;
            configContent = null;
            axesButton = confirmAlignmentButton = null;
            alignmentTab = false;
            switch (page)
            {
                case 0: TeleopConfig(); break;
                case 1: Cameras(); break;
                case 2: UpperLimb(); break;
                case 3: SystemSetting(); break;
                default: ExitApp(); break;
            }
        }

        private RectTransform Card(string title, float y, float height)
        {
            var card = WorkspaceWidgets.Panel(title, root, 0, y, 842, height);
            WorkspaceWidgets.Label(card, title, 22, 15, 798, 35, 24, null, true);
            return card;
        }

        private void TeleopConfig()
        {
            connectionTabButton = WorkspaceWidgets.Button(root, "Connection & input", 0, 0, 410,
                () => SelectConfigTab(false), false, 44);
            alignmentTabButton = WorkspaceWidgets.Button(root, "Alignment", 432, 0, 410,
                () => SelectConfigTab(true), false, 44);
            configContent = WorkspaceWidgets.Rect("Teleop config content", root, 0, 58, 842, 394);
            SelectConfigTab(false);
        }

        private void SelectConfigTab(bool alignment)
        {
            shell.CloseKeypad();
            foreach (Transform child in configContent)
            {
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
            alignmentTab = alignment;
            detail = metric = null;
            axesButton = confirmAlignmentButton = modeButton = null;
            WorkspaceWidgets.Selected(connectionTabButton, !alignment);
            WorkspaceWidgets.Selected(alignmentTabButton, alignment);
            RectTransform outerRoot = root;
            root = configContent;
            if (alignment) Alignment();
            else ConnectionAndInput();
            root = outerRoot;
        }

        private void ConnectionAndInput()
        {
            var connection = Card("Connect to your workstation", 0, 154);
            WorkspaceWidgets.Label(connection, "Same network required. Tap Edit IP to enter the workstation address.", 22, 47, 798, 30, 20, WorkspaceTheme.Muted);
            var field = WorkspaceWidgets.Input(connection, pendingHost, "PC IPv4 address", 22, 82, 394, value => pendingHost = value);
            field.name = "Workspace host input";
            field.readOnly = true;
            field.shouldHideSoftKeyboard = true;
            field.shouldActivateOnSelect = false;
            WorkspaceWidgets.Button(connection, "Edit IP", 428, 83, 188, () => shell.OpenKeypad(field));
            WorkspaceWidgets.Button(connection, "Apply", 628, 83, 190, () => ApplyConnection());
            var input = Card("Input & reference", 168, 110);
            trackingButtons[0] = WorkspaceWidgets.Button(input, "Controllers", 22, 49, 247, () => ChangeTrackingMode(0), false, 50);
            trackingButtons[1] = WorkspaceWidgets.Button(input, "Hand tracking", 287, 49, 247, () => ChangeTrackingMode(1), false, 50);
            modeButton = WorkspaceWidgets.Button(input, "Mirror mode", 552, 49, 266, () => { b.App?.HandleTrackingLost("Control mode changed"); b.App?.ToggleTeleopControlMode(); }, false, 50);
            detail = WorkspaceWidgets.Label(root, "Use Alignment to set the reference, then select Start Teleop.", 6, 298, 830, 80, 20, WorkspaceTheme.Muted);
        }

        private void ToggleBodyData()
        {
            BodyPoseTelemetrySender sender = b.BodyTelemetry;
            sender.SetSendingEnabled(!sender.SendingEnabled);
            RefreshBodyData();
        }

        private void RefreshBodyData()
        {
            if (bodyDataButton == null) return;
            bool sending = b.BodyTelemetry.SendingEnabled;
            WorkspaceWidgets.Text(bodyDataButton, sending ? "Body data: ON" : "Body data: OFF");
            WorkspaceWidgets.Selected(bodyDataButton, sending);
        }

        private void ChangeTrackingMode(int mode)
        {
            if (b.App == null || b.App.TrackingMode == mode) return;
            b.App.HandleTrackingLost("Input mode changed");
            b.App.SetTrackingMode(mode);
        }

        private void Cameras()
        {
            var transport = Card("Video transport", 0, 144);
            WorkspaceWidgets.Label(transport, "Floating views remain in your space while you work.", 22, 47, 798, 32, 21, WorkspaceTheme.Muted);
            modeButton = WorkspaceWidgets.Button(transport, "Use WebRTC", 22, 85, 246, () => b.App?.ToggleWebRTC(), false, 50);
            for (int i = 1; i <= 3; i++)
            {
                int count = i;
                trackButtons[i - 1] = WorkspaceWidgets.Button(transport, i + (i == 1 ? " view" : " views"), 286 + (i - 1) * 176, 85, 158,
                    () => b.App?.SetWebRTCTrackCount(count), false, 50);
            }
            var views = Card("Camera windows", 160, 142);
            WorkspaceWidgets.Button(views, "Add UDP view", 22, 65, 247, () => b.Windows?.CreateUdpWindow());
            WorkspaceWidgets.Button(views, "Close UDP views", 287, 65, 247, () => b.Windows?.CloseAllWindows());
            WorkspaceWidgets.Button(views, "Arrange views", 552, 65, 266, () => b.Windows?.CycleLayout());
            var zoom = Card("Zoom all views", 318, 126);
            string[] factors = { "x1.0", "x1.5", "x2.0" };
            for (int i = 0; i < factors.Length; i++)
            {
                string factor = factors[i];
                WorkspaceWidgets.Button(zoom, factor, 22 + i * 267, 61, 248, () => SetVideoZoom(factor));
            }
        }

        private void SetVideoZoom(string label)
        {
            if (b.Windows != null)
                foreach (var window in b.Windows.windowControllers) if (window != null) window.SetResolution(label);
            if (b.Video == null) return;
            for (int i = 0; i < 3; i++) b.Video.SetResolution(i, label);
            b.Video.SendResolutionControl();
        }

        public void Refresh(int page)
        {
            if (page == 3) { RefreshBodyData(); RefreshSystemSetting(); }
            if (b.App == null) return;
            if (page == 0 && alignmentTab) RefreshAlignment();
            if (page == 0 && !alignmentTab)
            {
                if (modeButton != null) WorkspaceWidgets.Text(modeButton, TeleopControlModeManager.CurrentMode + " mode");
                for (int i = 0; i < trackingButtons.Length; i++) WorkspaceWidgets.Selected(trackingButtons[i], b.App.TrackingMode == i);
                if (detail != null) detail.text = b.App.NeedsRecalibration
                    ? "Input paused. Restore tracking, then reset the reference in Alignment."
                    : string.IsNullOrEmpty(b.App.LastError) ? (b.App.TrackingMode == 1 ? "Select Start Teleop. Use the small panel to record, undo or quit teleop." : "Select Start Teleop. Hold the grip to control; release it to pause.") : b.App.LastError;
            }
            if (page == 1 && modeButton != null)
            {
                WorkspaceWidgets.Text(modeButton, b.App.IsWebRTCEnabled ? "Use UDP" : "Use WebRTC");
                for (int i = 0; i < trackButtons.Length; i++) WorkspaceWidgets.Selected(trackButtons[i], b.App.NumWebRTC == i + 1);
            }
            if (page == 2) RefreshUpperLimb();
            if (page == 3 && detail != null)
                detail.text = b.App.ShowDebugInfo
                    ? $"Host {b.App.ServerIP}   Input {b.App.SessionState}   Video {b.Video?.CurrentState}\n" +
                      $"TCP packets rejected: {b.Feedback?.RejectedPackets ?? 0}   Tracks: {b.Video?.ActiveTrackCount ?? 0}"
                    : "Body data can run without teleop or WRM.\nEnable diagnostics to view connection and tracking status.";
        }
    }
}
