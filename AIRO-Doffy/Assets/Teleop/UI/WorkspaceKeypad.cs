using System;
using TMPro;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Doffy.UI
{
    /// <summary>Large in-world IPv4 keypad on its own ray and poke surface.</summary>
    public sealed class WorkspaceKeypad : MonoBehaviour
    {
        private const float FlyoutWidth = 900f;
        private const float FlyoutHeight = 620f;
        private static WorkspaceKeypad current;

        private TMP_InputField target;
        private Action onClosed;
        private bool closing;
        private bool endEditRaised;
        private bool originalReadOnly;
        private bool originalHideSoftKeyboard;
        private bool originalActivateOnSelect;
        private bool cameraScoped;
        private Func<bool> inputAvailable;

        public static bool IsOpen => current != null && current.gameObject.activeInHierarchy;

        public static void Close()
        {
            WorkspaceKeypad closingKeypad = current;
            current = null;
            if (closingKeypad != null)
                closingKeypad.CloseAndDestroy();
        }

        /// <summary>
        /// Creates an independently pointable flyout. The caller may hide its page
        /// content before opening and restore it through <paramref name="onClosed"/>.
        /// </summary>
        public static void Show(RectTransform hostCanvas, TMP_InputField field, Action onClosed = null,
            WristInteractionSource interactionSource = null, Func<bool> availability = null)
        {
            if (hostCanvas == null || field == null)
            {
                onClosed?.Invoke();
                return;
            }
            Close();

            Canvas host = hostCanvas.GetComponentInParent<Canvas>()?.rootCanvas;
            PointableCanvas pointable = host != null ? host.GetComponent<PointableCanvas>() : null;
            if (pointable == null)
            {
                Debug.LogError("[WorkspaceKeypad] A root PointableCanvas is required for keypad input.");
                onClosed?.Invoke();
                return;
            }
            // Meta routes graphic hits against the event canvas's rootCanvas.
            // A nested Canvas with its own PointableCanvas rejects every key hit.
            // Keep the independent surface, but route it through the host canvas.
            var root = new GameObject("Workspace keypad flyout", typeof(RectTransform));
            var flyoutRect = (RectTransform)root.transform;
            flyoutRect.SetParent(hostCanvas, false);
            flyoutRect.anchorMin = flyoutRect.anchorMax = flyoutRect.pivot = new Vector2(.5f, .5f);
            flyoutRect.anchoredPosition = Vector2.zero;
            flyoutRect.localScale = Vector3.one;
            flyoutRect.sizeDelta = new Vector2(FlyoutWidth, FlyoutHeight);

            WristInteractionSource source = interactionSource != null
                ? interactionSource : hostCanvas.GetComponentInParent<WristInteractionSource>();
            if (source == null) source = WristInteractionSource.Ensure();

            var keypad = root.AddComponent<WorkspaceKeypad>();
            current = keypad;
            keypad.target = field;
            keypad.onClosed = onClosed;
            keypad.originalReadOnly = field.readOnly;
            keypad.originalHideSoftKeyboard = field.shouldHideSoftKeyboard;
            keypad.originalActivateOnSelect = field.shouldActivateOnSelect;
            keypad.cameraScoped = availability != null;
            keypad.inputAvailable = availability;
            // A keypad owns text entry while it is open. In particular, do not let
            // TMP activate a native keyboard after an onSelect caller opens us.
            field.readOnly = true;
            field.shouldHideSoftKeyboard = true;
            field.shouldActivateOnSelect = false;
            field.onEndEdit.AddListener(keypad.ObserveEndEdit);

            var panel = WorkspaceWidgets.Panel("IP address keypad", flyoutRect, 28, 132,
                845, 456, WorkspaceTheme.Raised);
            panel.localPosition += Vector3.back * 2f;
            panel.GetComponent<RoundedPanel>().raycastTarget = true;
            WristCanvasSurface.Create(panel, pointable, source, true, false, availability);

            WorkspaceWidgets.Label(panel, field.name == "Workspace host input" ? "PC address" : "Camera port",
                22, 16, 380, 40, 27, null, true);
            var value = WorkspaceWidgets.Label(panel, field.text, 22, 57, 790, 48, 29, WorkspaceTheme.Accent);
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "Back" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                Button button = WorkspaceWidgets.Button(panel, key, 22 + i % 3 * 173, 123 + i / 3 * 77,
                    157, () =>
                    {
                        if (!keypad.CanEditTarget()) return;
                        field.text = key == "Back"
                            ? (field.text.Length > 0 ? field.text.Substring(0, field.text.Length - 1) : "")
                            : field.text + key;
                        value.text = field.text;
                    }, false, 74);
                button.gameObject.AddComponent<WorkspaceKeyPressDepth>();
            }

            Button clear = WorkspaceWidgets.Button(panel, "Clear", 558, 123, 261, () =>
            {
                if (!keypad.CanEditTarget()) return;
                field.text = "";
                value.text = "";
            }, false, 74);
            clear.gameObject.AddComponent<WorkspaceKeyPressDepth>();

            Button done = WorkspaceWidgets.Button(panel, "Done", 558, 277, 261, () =>
            {
                if (!keypad.CanEditTarget()) return;
                keypad.CommitTargetOnce();
                Close();
            }, true, 141);
            done.gameObject.AddComponent<WorkspaceKeyPressDepth>();

            WorkspaceWidgets.Label(panel,
                field.name == "Workspace host input" ? "Use the address\nshown on your PC." : "Use the video port\nset on your PC.",
                558, 198, 261, 66, 22, WorkspaceTheme.Muted);
        }

        private void Update()
        {
            if (target == null) Close();
        }

        private void OnDisable()
        {
            // A camera transport/group switch can hide the field and flyout together.
            // Restore text-entry flags and release the modal so it cannot block other views.
            if (cameraScoped && !closing && current == this) Close();
        }

        private bool CanEditTarget() => !closing && target != null &&
            (inputAvailable?.Invoke() ?? true);

        private void ObserveEndEdit(string _)
        {
            endEditRaised = true;
        }

        private void CommitTargetOnce()
        {
            if (endEditRaised || target == null) return;
            // Mark first so callbacks that close/re-enter the keypad cannot dispatch
            // the same edit a second time.
            endEditRaised = true;
            target.onEndEdit.Invoke(target.text);
        }

        private void CloseAndDestroy()
        {
            if (closing) return;
            closing = true;
            FinishTargetEditing();
            gameObject.SetActive(false);
            Destroy(gameObject);
            Action callback = onClosed;
            onClosed = null;
            callback?.Invoke();
        }

        private void FinishTargetEditing()
        {
            TMP_InputField field = target;
            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            // The only valid focus while this modal is open belongs to the keypad or
            // its dedicated header-close control. Clear either so neither is left as
            // a selected, inactive object when the page surfaces are restored.
            if (eventSystem != null && selected != null)
                eventSystem.SetSelectedGameObject(null);

            if (field != null)
            {
                if (field.isFocused)
                    field.DeactivateInputField();
                CommitTargetOnce();
                field.onEndEdit.RemoveListener(ObserveEndEdit);
                field.readOnly = originalReadOnly;
                field.shouldHideSoftKeyboard = originalHideSoftKeyboard;
                field.shouldActivateOnSelect = originalActivateOnSelect;
            }
        }
    }

    /// <summary>Moves keypad buttons slightly into the canvas while pressed.</summary>
    public sealed class WorkspaceKeyPressDepth : MonoBehaviour, IPointerDownHandler,
        IPointerUpHandler, IPointerExitHandler
    {
        private const float PressDepth = 4f;
        private RectTransform rect;
        private Vector3 restPosition;
        private bool initialized;

        private void Awake()
        {
            rect = transform as RectTransform;
            if (rect != null)
            {
                restPosition = rect.localPosition;
                initialized = true;
            }
        }

        public void OnPointerDown(PointerEventData eventData) => SetPressed(true);
        public void OnPointerUp(PointerEventData eventData) => SetPressed(false);
        public void OnPointerExit(PointerEventData eventData) => SetPressed(false);

        private void OnDisable() => SetPressed(false);

        private void SetPressed(bool pressed)
        {
            if (!initialized || rect == null) return;
            rect.localPosition = pressed ? restPosition + Vector3.forward * PressDepth : restPosition;
        }
    }
}
