using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Doffy.UI
{
    public static class WorkspaceWidgets
    {
        public static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static RectTransform Panel(string name, Transform parent, float x, float y,
            float width, float height, Color? color = null)
        {
            var rect = Rect(name, parent, x, y, width, height);
            var graphic = rect.gameObject.AddComponent<RoundedPanel>();
            graphic.color = color ?? WorkspaceTheme.Surface;
            graphic.raycastTarget = false;
            return rect;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float x, float y,
            float width, float height = 40, float size = 24, Color? color = null, bool bold = false)
        {
            var rect = Rect("Label", parent, x, y, width, height);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset ?? Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            label.text = text;
            label.fontSize = size;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.color = color ?? WorkspaceTheme.Text;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(Transform parent, string text, float x, float y,
            float width, Action click, bool primary = false, float height = 58)
        {
            var rect = Panel(text, parent, x, y, width, height,
                primary ? WorkspaceTheme.Accent : WorkspaceTheme.Raised);
            var graphic = rect.GetComponent<RoundedPanel>();
            graphic.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.13f, 1.13f, 1.13f);
            colors.pressedColor = new Color(.78f, .84f, .86f);
            colors.disabledColor = new Color(.55f, .55f, .55f, .5f);
            colors.fadeDuration = .09f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var label = Label(rect, text, 16, 0, width - 32, height, 23,
                primary ? WorkspaceTheme.Background : WorkspaceTheme.Text, true);
            label.alignment = TextAlignmentOptions.Midline;
            button.onClick.AddListener(() => click?.Invoke());
            return button;
        }

        public static TMP_InputField Input(Transform parent, string value, string placeholder,
            float x, float y, float width, Action<string> changed)
        {
            var rect = Panel("Host input", parent, x, y, width, 64, WorkspaceTheme.Background);
            var graphic = rect.GetComponent<RoundedPanel>();
            graphic.raycastTarget = true;
            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = graphic;
            var text = Label(rect, "", 18, 0, width - 36, 64, 25);
            var hint = Label(rect, placeholder, 18, 0, width - 36, 64, 25, WorkspaceTheme.Muted);
            input.textViewport = rect;
            input.textComponent = text;
            input.placeholder = hint;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 253;
            input.caretColor = WorkspaceTheme.Accent;
            input.customCaretColor = true;
            input.selectionColor = new Color(.4f, .7f, .6f, .4f);
            input.text = value;
            input.onValueChanged.AddListener(s => changed?.Invoke(s));
            return input;
        }

        public static void Selected(Button button, bool selected)
        {
            if (button == null) return;
            button.GetComponent<RoundedPanel>().color = selected ? WorkspaceTheme.Accent : WorkspaceTheme.Raised;
            button.GetComponentInChildren<TextMeshProUGUI>().color = selected ? WorkspaceTheme.Background : WorkspaceTheme.Text;
        }

        public static void Text(Button button, string value)
        {
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null && label.text != value) label.text = value;
        }
    }
}
