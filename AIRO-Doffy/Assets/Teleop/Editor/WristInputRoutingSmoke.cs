using System;
using System.Collections;
using System.Reflection;
using Doffy.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Doffy.Editor
{
    /// <summary>Exercises SDK canvas routing and real graphic hits without XR input or robot commands.</summary>
    internal static class WristInputRoutingSmoke
    {
        private static int nextPointer = 900000;

        public static IEnumerator Run(WorkspaceShell shell, WristInteractionSource source)
        {
            IEnumerator steps = Exercise(shell, source);
            while (true)
            {
                object wait = null;
                bool more;
                try
                {
                    more = steps.MoveNext();
                    if (more) wait = steps.Current;
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                    yield break;
                }
                if (!more) break;
                yield return wait;
            }
            Debug.Log("DOFFY bracelet Play smoke passed: geometry, sizes, session states, wrist yaw/book pitch, SDK ray/poke graphic routing, session panel and IP keypad; no robot commands.");
            EditorApplication.Exit(0);
        }

        private static IEnumerator Exercise(WorkspaceShell shell, WristInteractionSource source)
        {
            Require(!AppManager.Instance.IsStreaming, "Routing smoke starts with an idle robot session");
            // Freeze hardware-driven presentation only in this Editor fixture. Production
            // session callbacks remain gated off; button spies observe actual Unity clicks.
            shell.GetComponent<WristMount>().enabled = false;
            source.enabled = false;
            shell.enabled = false;
            Transform presentation = shell.transform.Find("Wrist presentation");
            presentation.gameObject.SetActive(true);
            foreach (BraceletRotator rotator in presentation.GetComponentsInChildren<BraceletRotator>(true))
                rotator.enabled = false;
            WristDetailAnchor anchor = shell.GetComponentInChildren<WristDetailAnchor>(true);
            anchor.enabled = false;
            Button sessionPage = FindButton(shell, "Teleop Config");
            sessionPage.onClick.Invoke();
            anchor.transform.SetPositionAndRotation(new Vector3(-.2f, .2f, 1f), Quaternion.Euler(45f, 0f, 0f));
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            PointableCanvasModule module = UnityEngine.Object.FindAnyObjectByType<PointableCanvasModule>();
            Require(module != null, "Live Meta canvas module exists");
            IEnumerator cameraSteps = WristWorkspaceRegressionSmoke.ExerciseCameras(shell, source,
                (surface, rect, poke) => RouteGraphic(module, surface, rect, poke));
            while (cameraSteps.MoveNext()) yield return cameraSteps.Current;
            WristCanvasSurface pageSurface = anchor.GetComponent<WristCanvasSurface>();
            IEnumerator bodySteps = BodyTelemetrySessionSmoke.Exercise(shell,
                (button, poke) => Click(module, pageSurface, button, poke));
            while (bodySteps.MoveNext()) yield return bodySteps.Current;
            FindButton(shell, "Teleop Config").onClick.Invoke();
            yield return null;
            Canvas.ForceUpdateCanvases();
            TMP_InputField field = null;
            foreach (TMP_InputField candidate in shell.GetComponentsInChildren<TMP_InputField>())
                if (candidate.name == "Workspace host input") field = candidate;
            Require(field != null, "Active IP input exists");
            string original = field.text;
            Button opener = FindButton(field.transform.parent, "Edit IP");

            // Open through the same surface -> PointableCanvas -> module -> GraphicRaycaster
            // -> Unity Button path used by Meta input. Never invoke the keypad callbacks directly.
            Click(module, pageSurface, opener, false);
            Require(WorkspaceKeypad.IsOpen, "Controller-routed Edit IP opens the keypad");
            yield return null;
            yield return null;
            WorkspaceKeypad keypad = shell.GetComponentInChildren<WorkspaceKeypad>();
            Require(keypad != null && keypad.GetComponent<Canvas>() == null,
                "Keypad shares the host root canvas instead of creating an unroutable nested canvas");
            WristCanvasSurface keySurface = keypad.GetComponentInChildren<WristCanvasSurface>();
            ValidateFilters(keySurface);
            int commits = 0;
            UnityEngine.Events.UnityAction<string> commit = _ => commits++;
            field.onEndEdit.AddListener(commit);
            Click(module, keySurface, FindButton(keypad, "Clear"), false);
            Click(module, keySurface, FindButton(keypad, "1"), false);
            Click(module, keySurface, FindButton(keypad, "Back"), true);
            Require(field.text.Length == 0, "Ray digit and poke Back reach the actual text field");
            int digitIndex = 0;
            foreach (char digit in "192.168.1.42")
                Click(module, keySurface, FindButton(keypad, digit.ToString()), digitIndex++ % 2 == 1);
            Require(field.text == "192.168.1.42", "SDK ray/poke events enter a complete IPv4 address");
            Click(module, keySurface, FindButton(keypad, "Done"), true);
            Require(!WorkspaceKeypad.IsOpen && commits == 1, "Poke Done commits exactly once");
            field.onEndEdit.RemoveListener(commit);
            yield return null;
            Click(module, pageSurface, opener, true);
            yield return null;
            Button close = FindButton(anchor, "Close");
            Click(module, close.GetComponent<WristCanvasSurface>(), close, false);
            Require(!WorkspaceKeypad.IsOpen && !anchor.gameObject.activeSelf,
                "Controller-routed header Close closes the keypad and detail panel");
            field.text = original;

            TeleopRecordPanel session = UnityEngine.Object.FindAnyObjectByType<TeleopRecordPanel>();
            session.enabled = false;
            Canvas sessionCanvas = session.GetComponentInChildren<Canvas>(true);
            sessionCanvas.gameObject.SetActive(true);
            session.transform.SetPositionAndRotation(new Vector3(0f, 0f, 1f), Quaternion.identity);
            yield return null;
            yield return null;
            WristCanvasSurface sessionSurface = sessionCanvas.GetComponent<WristCanvasSurface>();
            ValidateFilters(sessionSurface);
            foreach (Button button in sessionCanvas.GetComponentsInChildren<Button>())
            {
                int clicks = 0;
                UnityEngine.Events.UnityAction clicked = () => clicks++;
                button.onClick.AddListener(clicked);
                button.interactable = true;
                Click(module, sessionSurface, button, false);
                Click(module, sessionSurface, button, true);
                Require(clicks == 2, "Session button receives both controller and poke clicks: " + button.name);
                button.onClick.RemoveListener(clicked);
            }
            sessionCanvas.gameObject.SetActive(false);
            sessionCanvas.gameObject.SetActive(true);
            ValidateFilters(sessionSurface);
            Require(!AppManager.Instance.IsStreaming, "Routing tests never start teleoperation, record or undo");
            Debug.Log("DOFFY Meta input routing passed: all three session actions, full IP entry, Done and header Close via graphic hits.");
        }

        private static void ValidateFilters(WristCanvasSurface surface)
        {
            Require(surface != null, "Interaction surface exists");
            ValidateFilters(surface.Ray, surface);
            ValidateFilters(surface.Ray.GetComponent<PokeInteractable>(), surface);
        }

        private static void ValidateFilters(Component interactable, WristCanvasSurface owner)
        {
            Type type = interactable.GetType();
            FieldInfo field = null;
            while (type != null && field == null)
            {
                field = type.GetField("InteractorFilters", BindingFlags.Instance | BindingFlags.NonPublic);
                type = type.BaseType;
            }
            Require(field != null, "SDK runtime filter list is inspectable");
            IList filters = (IList)field.GetValue(interactable);
            Require(filters != null && filters.Count == 1 && ReferenceEquals(filters[0], owner),
                "SDK filter survives inactive creation, Awake, and re-enable: " + interactable.GetType().Name);
        }

        private static void Click(PointableCanvasModule module, WristCanvasSurface surface, Button button, bool poke)
        {
            Require(surface != null && button != null && button.gameObject.activeInHierarchy,
                "Click targets an active UI button");
            RouteGraphic(module, surface, (RectTransform)button.transform, poke);
        }

        private static void RouteGraphic(PointableCanvasModule module, WristCanvasSurface surface, RectTransform rect, bool poke)
        {
            Require(surface != null && rect != null && rect.gameObject.activeInHierarchy,
                "Pointer route targets an active graphic");
            Canvas.ForceUpdateCanvases();
            Vector3 center = rect.TransformPoint(rect.rect.center);
            PokeInteractable pokeSurface = surface.Ray.GetComponent<PokeInteractable>();
            PointableCanvas pointable = (poke ? pokeSurface.PointableElement : surface.Ray.PointableElement) as PointableCanvas;
            Graphic graphic = rect.GetComponent<Graphic>();
            Require(pointable != null && graphic != null && graphic.canvas != null &&
                    pointable.Canvas == graphic.canvas.rootCanvas,
                "Pointer canvas matches the root canvas used by SDK hit filtering");
            SurfaceHit hit;
            if (poke)
            {
                Require(pokeSurface.SurfacePatch.ClosestSurfacePoint(center - rect.forward * .02f, out hit) &&
                        Vector3.Distance(hit.Point, center) < .001f,
                    "Poke patch covers visible graphic center: " + rect.name);
            }
            else
            {
                BoxCollider collider = surface.Ray.GetComponent<BoxCollider>();
                bool enabled = collider.enabled;
                collider.enabled = true;
                Physics.SyncTransforms();
                bool intersects = surface.Ray.Surface.Raycast(new Ray(center - rect.forward * .2f, rect.forward), out hit, .3f);
                string geometry = $"hit={intersects}, point={hit.Point:F5}, button={center:F5}, bounds={collider.bounds}, scale={collider.transform.lossyScale:F6}";
                collider.enabled = enabled;
                Require(intersects && Vector3.Distance(hit.Point, center) < .001f,
                    "Controller surface covers visible graphic center: " + rect.name + "; " + geometry);
            }

            int id = nextPointer++;
            Pose pose = new Pose(hit.Point, Quaternion.identity);
            Action<PointerEvent> send = poke ? new Action<PointerEvent>(pokeSurface.PublishPointerEvent) : surface.Ray.PublishPointerEvent;
            foreach (PointerEventType type in new[] { PointerEventType.Hover, PointerEventType.Select, PointerEventType.Unselect, PointerEventType.Unhover })
            {
                send(new PointerEvent(id, type, pose));
                module.Process();
            }
        }

        private static Button FindButton(Component root, string name)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>())
                if (button.name == name) return button;
            throw new InvalidOperationException("Active button not found: " + name);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Meta routing validation: " + message);
        }
    }
}
