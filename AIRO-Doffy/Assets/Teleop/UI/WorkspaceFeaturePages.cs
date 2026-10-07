using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Doffy.UI
{
    public sealed partial class WorkspacePages
    {
        private void Alignment()
        {
            var reference = Card("01  Reset the teleop reference", 0, 122);
            WorkspaceWidgets.Label(reference, "Face the robot's forward direction. Reset uses your headset heading\nand position, replacing any manual alignment.", 22, 43, 798, 43, 19, WorkspaceTheme.Muted);
            WorkspaceWidgets.Button(reference, "Reset reference", 22, 87, 370,
                () => b.App?.RecalibrateTeleopFrame(), true, 30);
            var baseFrame = Card("02  Place or adjust the robot axes", 130, 124);
            WorkspaceWidgets.Label(baseFrame, "Place base: point at the origin and press the right stick.\nSet the direction, then press the right stick again.", 22, 43, 798, 34, 17, WorkspaceTheme.Muted);
            WorkspaceWidgets.Button(baseFrame, "Place / replace base", 22, 79, 370,
                () => b.App?.BeginRobotBaseReposition(), false, 38);
            axesButton = WorkspaceWidgets.Button(baseFrame, "Adjust axes", 410, 79, 407,
                () =>
                {
                    if (b.Calibration != null) b.Calibration.SwitchAlign(!b.Calibration.IsCalibrating);
                    RefreshAlignment();
                }, false, 38);
            var hint = Card("03  Finish alignment", 262, 124);
            WorkspaceWidgets.Label(hint, "In axes edit: left trigger moves, right trigger rotates.\nFinish editing before confirming the reference.", 22, 43, 798, 43, 19, WorkspaceTheme.Muted);
            confirmAlignmentButton = WorkspaceWidgets.Button(hint, "Confirm alignment", 22, 87, 370,
                () => b.App?.ConfirmAlignment(), true, 30);
            WorkspaceWidgets.Button(hint, "Cancel base placement", 410, 87, 407,
                () => b.Placement?.EndPlacementEdit(), false, 30);
            RefreshAlignment();
        }

        private void RefreshAlignment()
        {
            bool axesEditing = b.Calibration != null && b.Calibration.IsCalibrating;
            WorkspaceWidgets.Text(axesButton, axesEditing ? "Finish axes edit" : "Adjust axes");
            if (axesButton != null) axesButton.interactable = b.Calibration != null;
            if (confirmAlignmentButton != null)
                confirmAlignmentButton.interactable = !axesEditing &&
                    (b.Placement == null || !b.Placement.IsPlacementEditing);
        }

        private void UpperLimb()
        {
            var card = Card("Upper-limb mapping", 0, 149);
            WorkspaceWidgets.Label(card, "Use your arm posture to control the robot's redundant configuration.", 22, 46, 798, 40, 22, WorkspaceTheme.Muted);
            wrmButton = WorkspaceWidgets.Button(card, "Enable WRM", 22, 85, 370, () => b.UpperLimb?.ToggleWrm());
            calibrationButton = WorkspaceWidgets.Button(card, "Calibrate arm", 410, 85, 407, () => b.UpperLimb?.OnCalibrationClicked());
            var calibration = Card("Two-pose calibration", 168, 161);
            detail = WorkspaceWidgets.Label(calibration,
                "1  Relax your arm down. Hold right A for two seconds.\n2  Raise your arm to shoulder height. Hold right A again.", 22, 52, 798, 99, 23, WorkspaceTheme.Muted);
            var feedback = Card("Live feedback", 348, 98);
            metric = WorkspaceWidgets.Label(feedback, "Waiting for body tracking", 22, 49, 798, 38, 23, WorkspaceTheme.Accent);
            RefreshUpperLimb();
        }

        private void RefreshUpperLimb()
        {
            var manager = b.UpperLimb;
            if (manager == null)
            {
                if (detail != null)
                {
                    detail.text = "Upper-body tracking is not available in this scene.";
                    detail.color = WorkspaceTheme.Muted;
                }
                if (wrmButton != null) wrmButton.interactable = false;
                if (calibrationButton != null) calibrationButton.interactable = false;
                return;
            }
            bool hands = b.App != null && b.App.TrackingMode == 1;
            wrmButton.interactable = !hands;
            calibrationButton.interactable = !hands && manager.WrmEnabled;
            WorkspaceWidgets.Text(wrmButton, manager.WrmEnabled ? "Disable WRM" : "Enable WRM");
            detail.color = hands ? WorkspaceTheme.Danger : WorkspaceTheme.Muted;
            if (hands) detail.text = "WRM is unavailable in Hand tracking mode.\nSwitch to Controllers in Teleop Config to enable WRM.";
            else switch (manager.State)
            {
                case UpperLimbAkmManager.CalibrationState.WaitingDown:
                    detail.text = "Relax your arm naturally down.\nHold right A for two seconds to capture the first pose."; break;
                case UpperLimbAkmManager.CalibrationState.WaitingHorizontal:
                    detail.text = "Raise your arm to shoulder height.\nHold right A for two seconds to capture the second pose."; break;
                case UpperLimbAkmManager.CalibrationState.CalibratingDown:
                case UpperLimbAkmManager.CalibrationState.CalibratingHorizontal:
                    detail.text = "Sampling your pose...\nKeep still and continue holding right A."; break;
                case UpperLimbAkmManager.CalibrationState.Calibrated:
                    detail.text = "Arm calibrated. Hold grip to control; release to pause.\nPress the right stick to recenter."; break;
                default:
                    detail.text = "Select Calibrate arm to begin.\nYou will capture relaxed and shoulder-height poses."; break;
            }
            metric.text = $"Arm position  {manager.ElbowAlpha:F2}     Confidence  {manager.Confidence:P0}     " +
                (manager.IsTrackingValid ? manager.IsClutched ? "CLUTCHED" : "TRACKING" : "NO TRACKING");
            metric.color = manager.IsTrackingValid ? WorkspaceTheme.Accent : WorkspaceTheme.Warning;
        }

        private Button forceButton, passthroughButton, robotAxesButton, tactileButton, diagnosticsButton;

        private void SystemSetting()
        {
            var visibility = Card("Environment & overlays", 0, 190);
            passthroughButton = WorkspaceWidgets.Button(visibility, "Passthrough", 22, 53, 385,
                () => { b.SetPassthrough(!PassthroughVisible()); RefreshSystemSetting(); });
            forceButton = WorkspaceWidgets.Button(visibility, "Force vectors", 427, 53, 391,
                () => { b.SetForceVisible(!ForceVisible()); RefreshSystemSetting(); });
            robotAxesButton = WorkspaceWidgets.Button(visibility, "Robot axes", 22, 119, 385,
                () => { if (b.World != null) b.World.SetAxesVisible(!b.World.AxesVisible); RefreshSystemSetting(); });
            tactileButton = WorkspaceWidgets.Button(visibility, "Tactile display", 427, 119, 391,
                () => { b.SetTactileVisible(!TactileVisible()); RefreshSystemSetting(); });
            var tracking = Card("Tracking & diagnostics", 204, 142);
            bodyDataButton = WorkspaceWidgets.Button(tracking, "Body data: OFF", 22, 53, 247, ToggleBodyData, false, 50);
            bodyDataButton.name = "Body data toggle";
            diagnosticsButton = WorkspaceWidgets.Button(tracking, "Diagnostics: OFF", 287, 53, 247,
                () => { if (b.App != null) b.App.SetDebugVisible(!b.App.ShowDebugInfo); RefreshSystemSetting(); }, false, 50);
            WorkspaceWidgets.Button(tracking, "Reset bracelet", 552, 53, 266, shell.Recenter, false, 50);
            WorkspaceWidgets.Label(tracking, "Body data works without teleop, WRM or calibration.", 22, 110, 798, 26, 18, WorkspaceTheme.Muted);
            detail = WorkspaceWidgets.Label(root, "", 6, 362, 830, 80, 18, WorkspaceTheme.Muted);
            RefreshBodyData();
            RefreshSystemSetting();
        }

        private bool ForceVisible()
        {
            foreach (var force in b.Forces) if (force != null && force.gameObject.activeSelf) return true;
            return false;
        }

        private bool PassthroughVisible()
        {
            foreach (var layer in b.Passthrough) if (layer != null && layer.enabled) return true;
            return false;
        }

        private bool TactileVisible() => b.Tactile != null && b.Tactile.container != null && b.Tactile.container.gameObject.activeSelf;

        private void RefreshSystemSetting()
        {
            SetToggleState(passthroughButton, "Passthrough", PassthroughVisible(), b.Passthrough.Length > 0);
            SetToggleState(forceButton, "Force vectors", ForceVisible(), b.Forces.Length > 0);
            SetToggleState(robotAxesButton, "Robot axes", b.World != null && b.World.AxesVisible, b.World != null);
            SetToggleState(tactileButton, "Tactile display", TactileVisible(), b.Tactile != null && b.Tactile.container != null);
            SetToggleState(diagnosticsButton, "Diagnostics", b.App != null && b.App.ShowDebugInfo, b.App != null);
        }

        private static void SetToggleState(Button button, string label, bool enabled, bool available)
        {
            if (button == null) return;
            WorkspaceWidgets.Text(button, label + (available ? enabled ? ": ON" : ": OFF" : ": unavailable"));
            WorkspaceWidgets.Selected(button, enabled && available);
            button.interactable = available;
        }

        private void ExitApp()
        {
            var confirmation = Card("Exit app?", 80, 250);
            WorkspaceWidgets.Label(confirmation, "Close DOFFY Bracelet and return to the headset home.\nSelect Cancel to keep the app open.", 22, 63, 798, 82, 24, WorkspaceTheme.Muted);
            WorkspaceWidgets.Button(confirmation, "Cancel", 22, 170, 370, shell.CloseDetails, false, 58);
            var exit = WorkspaceWidgets.Button(confirmation, "Exit APP", 410, 170, 407, Application.Quit, false, 58);
            exit.name = "Confirm exit app";
            exit.GetComponent<RoundedPanel>().color = WorkspaceTheme.Danger;
        }
    }
}
