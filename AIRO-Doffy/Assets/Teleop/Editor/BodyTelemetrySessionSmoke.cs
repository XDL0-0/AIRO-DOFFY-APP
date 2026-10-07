using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Doffy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Doffy.Editor
{
    /// <summary>Real Session button clicks and UDP loopback; no robot session or SDK tracking changes.</summary>
    internal static class BodyTelemetrySessionSmoke
    {
        public static IEnumerator Exercise(WorkspaceShell shell, Action<Button, bool> click)
        {
            FindButton(shell, "System Setting").onClick.Invoke();
            yield return null;
            Canvas.ForceUpdateCanvases();
            AppManager app = AppManager.Instance;
            BodyPoseTelemetrySender sender = BodyPoseTelemetrySender.Instance;
            UpperLimbAkmManager manager = UnityEngine.Object.FindAnyObjectByType<UpperLimbAkmManager>();
            bool wrmEnabled = manager != null && manager.WrmEnabled;
            bool fullBodyRequested = manager != null && manager.requestFullBodyTracking;
            UpperLimbAkmManager.CalibrationState calibration = manager != null ? manager.State : default;
            string originalHost = app.ServerIP;
            int originalPort = sender.destinationPort;
            float originalRate = sender.sendRateHz;

            Require(!app.IsStreaming && !sender.SendingEnabled && Socket(sender) == null,
                "Launch defaults to BODY OFF with no diagnostic socket or robot session");
            Require(!fullBodyRequested, "Existing upper-body default remains in place");
            using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                try
                {
                    Require(app.ConfigureConnection("127.0.0.1", app.NumWebRTC), "Loopback host is accepted without starting a robot session");
                    sender.destinationPort = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;
                    sender.sendRateHz = 25f;
                    Button toggle = FindButton(shell, "Body data toggle");
                    CheckButton(toggle, false);
                    yield return new WaitForSecondsRealtime(.2f);
                    Require(receiver.Available == 0 && Socket(sender) == null,
                        "OFF emits no BODY datagrams and does not open a socket");

                    click(toggle, false);
                    CheckButton(toggle, true);
                    Require(sender.SendingEnabled && !app.IsStreaming, "Controller click enables BODY independently of robot Start");
                    yield return new WaitForSecondsRealtime(.2f);
                    Require(Drain(receiver) > 0 && Socket(sender) != null, "ON emits BODY packets to current AppManager host");

                    FindButton(shell, "Close").onClick.Invoke();
                    FindButton(shell, "System Setting").onClick.Invoke();
                    yield return null;
                    toggle = FindButton(shell, "Body data toggle");
                    CheckButton(toggle, true);
                    Require(sender.SendingEnabled, "Closing and rebuilding System Setting retains ON state");
                    click(toggle, true);
                    CheckButton(toggle, false);
                    Require(!sender.SendingEnabled && Socket(sender) == null, "Poke click stops BODY and closes its socket immediately");
                    Drain(receiver);
                    yield return new WaitForSecondsRealtime(.2f);
                    Require(receiver.Available == 0, "OFF sends no BODY heartbeat or other packets");

                    click(toggle, false);
                    yield return new WaitForSecondsRealtime(.2f);
                    Require(sender.SendingEnabled && Drain(receiver) > 0, "BODY can be enabled again after closing the socket");
                    sender.enabled = false;
                    Require(!sender.SendingEnabled && Socket(sender) == null, "Disabling the component clears ON and closes its socket immediately");
                    Drain(receiver);
                    sender.enabled = true;
                    yield return new WaitForSecondsRealtime(.2f);
                    Require(receiver.Available == 0 && !sender.SendingEnabled,
                        "Re-enabling the component requires a fresh transmission opt-in");

                    sender.SetSendingEnabled(true);
                    yield return new WaitForSecondsRealtime(.2f);
                    Require(Drain(receiver) > 0, "Explicit opt-in restores sending after component disable");
                    UnityEngine.Object.Destroy(sender.gameObject);
                    yield return null;
                    Drain(receiver);
                    sender = BodyPoseTelemetrySender.Instance;
                    Require(!sender.SendingEnabled && Socket(sender) == null,
                        "Lazy replacement after destruction starts OFF with no stale socket");
                    FindButton(shell, "Close").onClick.Invoke();
                    FindButton(shell, "System Setting").onClick.Invoke();
                    yield return null;
                    CheckButton(FindButton(shell, "Body data toggle"), false);
                    Require(!app.IsStreaming && (manager == null ||
                        (manager.WrmEnabled == wrmEnabled && manager.State == calibration &&
                         manager.requestFullBodyTracking == fullBodyRequested)),
                        "Session body toggle never starts robot control, WRM, calibration or full-body tracking");
                }
                finally
                {
                    if (sender != null)
                    {
                        sender.SetSendingEnabled(false);
                        sender.destinationPort = originalPort;
                        sender.sendRateHz = originalRate;
                    }
                    app.ConfigureConnection(originalHost, app.NumWebRTC);
                }
            }
            Debug.Log("DOFFY BODY Session smoke passed: default OFF, real ray/poke button clicks, UDP loopback ON/OFF/re-enable, immediate socket close, page reopen, component lifetime and lazy replacement; robot/WRM/calibration remain idle.");
        }

        private static int Drain(UdpClient receiver)
        {
            int count = 0;
            while (receiver.Available > 0)
            {
                IPEndPoint source = null;
                string packet = Encoding.UTF8.GetString(receiver.Receive(ref source));
                Require(packet.Contains("\"type\":\"BODY\"") && packet.Contains("\"joint_set\":\"upper_body\""),
                    "Loopback receives the existing BODY v1 upper-body payload");
                count++;
            }
            return count;
        }

        private static UdpClient Socket(BodyPoseTelemetrySender sender) =>
            (UdpClient)typeof(BodyPoseTelemetrySender).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sender);

        private static void CheckButton(Button button, bool sending)
        {
            Require(button.GetComponentInChildren<TextMeshProUGUI>().text == (sending ? "Body data: ON" : "Body data: OFF") &&
                    button.GetComponent<RoundedPanel>().color == (sending ? WorkspaceTheme.Accent : WorkspaceTheme.Raised),
                "Session label and selected color match transmission state");
        }

        private static Button FindButton(Component root, string name)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.name == name && button.gameObject.activeInHierarchy) return button;
            throw new InvalidOperationException("BODY Session active button not found: " + name);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("BODY Session validation: " + message);
        }
    }
}
