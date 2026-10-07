using Doffy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Apply the workspace palette to serialized camera-window prefabs.</summary>
public static class RuntimeUITheme
{
    public static void ApplySceneTheme()
    {
        var canvas = GameObject.Find("Main Canvas");
        if (canvas != null) ApplyTo(canvas.transform);
    }

    public static void ApplyTo(Transform root)
    {
        if (root == null) return;
        foreach (var image in root.GetComponentsInChildren<Image>(true))
            image.color = image.GetComponent<Selectable>() != null ? WorkspaceTheme.Raised : WorkspaceTheme.Surface;
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            text.color = WorkspaceTheme.Text;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }
        foreach (var control in root.GetComponentsInChildren<Selectable>(true))
        {
            var colors = control.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(.7f, .82f, .78f);
            colors.disabledColor = new Color(.5f, .5f, .5f, .5f);
            colors.fadeDuration = .09f;
            control.colors = colors;
            control.navigation = new Navigation { mode = Navigation.Mode.None };
        }
        foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
        {
            input.customCaretColor = true;
            input.caretColor = WorkspaceTheme.Accent;
            input.selectionColor = new Color(.4f, .7f, .6f, .4f);
        }
        // Compact UDP controls need bounded single-line type; the generic ellipsis
        // policy must not replace the close glyph or clip a five-digit port number.
        foreach (var camera in root.GetComponentsInChildren<VideoWindowController>(true))
            camera.RefreshControlReadability();
        // RawImages and video materials keep their original white tint and UVs.
        // Never scale the prefab here: its collider and drag surface share that geometry.
    }
}
