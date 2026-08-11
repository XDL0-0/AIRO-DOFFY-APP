using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class RuntimeUITheme
{
    private static bool _sceneApplied;
    private static Sprite _roundedGlassSprite;

    private static readonly Color CanvasVeil = Hex("050914", 0.03f);
    private static readonly Color Glass = Hex("07111f", 0.36f);
    private static readonly Color GlassSoft = Hex("182235", 0.32f);
    private static readonly Color Text = Hex("f8fafc", 1f);
    private static readonly Color MutedText = Hex("cbd5e1", 1f);
    private static readonly Color DimText = Hex("94a3b8", 1f);
    private static readonly Color Field = Hex("020617", 0.46f);
    private static readonly Color Primary = Hex("0f766e", 0.62f);
    private static readonly Color Video = Hex("1d4ed8", 0.58f);
    private static readonly Color Utility = Hex("111827", 0.54f);
    private static readonly Color Warning = Hex("b45309", 0.6f);
    private static readonly Color Danger = Hex("b91c1c", 0.64f);
    private static readonly Color Disabled = Hex("475569", 0.42f);
    private static readonly Color StatusGood = Hex("6ee7b7", 1f);
    private static readonly Color VideoEmpty = Hex("020617", 0.7f);

    public static void ApplySceneTheme()
    {
        if (_sceneApplied) return;

        GameObject mainCanvas = GameObject.Find("Main Canvas");
        if (mainCanvas == null) return;

        _sceneApplied = true;
        ApplyTo(mainCanvas.transform);
        ApplyObservationLayout(mainCanvas.transform);
    }

    public static void ApplyTo(Transform root)
    {
        if (root == null) return;

        StylePanels(root);
        StyleVideoSurfaces(root);
        StyleInputs(root);
        StyleToggles(root);
        StyleButtons(root);
        StyleText(root);
    }

    private static void StylePanels(Transform root)
    {
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (HasParentComponent<Button>(image.transform) ||
                HasParentComponent<TMP_InputField>(image.transform) ||
                HasParentComponent<Toggle>(image.transform))
            {
                continue;
            }

            string name = image.gameObject.name.ToLowerInvariant();
            bool panelLike =
                name.Contains("panel") ||
                name.Contains("canvas") ||
                name.Contains("display") ||
                name.Contains("instruction") ||
                name.Contains("menu") ||
                name.Contains("background");

            if (!panelLike) continue;

            bool isBackdrop = IsBackdrop(image);
            image.color = isBackdrop
                ? CanvasVeil
                : (name.Contains("instruction") || name.Contains("display") ? GlassSoft : Glass);
            image.raycastTarget = !isBackdrop;
            if (!isBackdrop) ApplyRoundedShape(image);

            AddGlassEffects(image, isBackdrop ? Hex("ffffff", 0.05f) : Hex("ffffff", 0.16f),
                Hex("000000", isBackdrop ? 0.06f : 0.38f),
                isBackdrop ? new Vector2(0f, 0f) : new Vector2(0f, -8f));
        }
    }

    private static void StyleButtons(Transform root)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            Image image = button.targetGraphic as Image;
            if (image == null) image = button.GetComponent<Image>();
            if (image == null) continue;

            Color baseColor = GetButtonColor(button.gameObject.name);
            Color normal = button.interactable ? baseColor : Disabled;
            image.color = normal;
            image.raycastTarget = true;
            ApplyRoundedShape(image);

            AddGlassEffects(image, GetAccentLine(baseColor), Hex("000000", 0.42f), new Vector2(0f, -6f));

            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = Tint(baseColor, 1.18f, 0.96f);
            colors.pressedColor = Tint(baseColor, 0.78f, 0.92f);
            colors.selectedColor = Tint(baseColor, 1.08f, 0.96f);
            colors.disabledColor = Disabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.gameObject.SetActive(true);
                label.enabled = true;
                label.transform.SetAsLastSibling();

                RectTransform labelRect = label.GetComponent<RectTransform>();
                if (labelRect != null)
                {
                    labelRect.anchorMin = Vector2.zero;
                    labelRect.anchorMax = Vector2.one;
                    labelRect.offsetMin = Vector2.zero;
                    labelRect.offsetMax = Vector2.zero;
                    labelRect.localScale = Vector3.one;
                }

                label.color = Text;
                label.alignment = TextAlignmentOptions.Center;
                label.fontStyle = FontStyles.Bold;
                label.enableWordWrapping = true;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.enableAutoSizing = true;
                label.fontSizeMin = 11f;
                label.fontSizeMax = Mathf.Clamp(label.fontSize <= 0f ? 22f : label.fontSize, 18f, 28f);
                label.margin = new Vector4(10f, 5f, 10f, 5f);
                AddTextShadow(label);
            }
        }
    }

    private static void StyleInputs(Transform root)
    {
        foreach (TMP_InputField input in root.GetComponentsInChildren<TMP_InputField>(true))
        {
            Image image = input.GetComponent<Image>();
            if (image != null)
            {
                image.color = Field;
                ApplyRoundedShape(image);
                AddGlassEffects(image, Hex("67e8f9", 0.22f), Hex("000000", 0.35f), new Vector2(0f, -4f));
            }

            ColorBlock colors = input.colors;
            colors.normalColor = Field;
            colors.highlightedColor = Hex("102033", 0.92f);
            colors.pressedColor = Hex("020617", 0.92f);
            colors.selectedColor = Hex("102033", 0.92f);
            colors.disabledColor = Disabled;
            colors.colorMultiplier = 1f;
            input.colors = colors;

            input.caretColor = Text;
            input.selectionColor = Hex("38bdf8", 0.32f);

            if (input.textComponent != null)
            {
                input.textComponent.color = Text;
                input.textComponent.alignment = TextAlignmentOptions.Center;
                input.textComponent.enableAutoSizing = true;
                input.textComponent.fontSizeMin = 12f;
                input.textComponent.fontSizeMax = 24f;
                input.textComponent.margin = new Vector4(12f, 4f, 12f, 4f);
                AddTextShadow(input.textComponent);
            }

            TextMeshProUGUI placeholder = input.placeholder as TextMeshProUGUI;
            if (placeholder != null)
            {
                placeholder.color = DimText;
                placeholder.alignment = TextAlignmentOptions.Center;
                placeholder.enableAutoSizing = true;
                placeholder.fontSizeMin = 10f;
                placeholder.fontSizeMax = 20f;
            }
        }
    }

    private static void StyleToggles(Transform root)
    {
        foreach (Toggle toggle in root.GetComponentsInChildren<Toggle>(true))
        {
            Image image = toggle.targetGraphic as Image;
            if (image == null) image = toggle.GetComponentInChildren<Image>(true);
            if (image != null)
            {
                image.color = Field;
                ApplyRoundedShape(image);
                AddGlassEffects(image, Hex("ffffff", 0.12f), Hex("000000", 0.25f), new Vector2(0f, -3f));
            }

            Graphic check = toggle.graphic;
            if (check != null) check.color = Primary;

            TextMeshProUGUI label = toggle.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.color = MutedText;
                label.enableAutoSizing = true;
                label.fontSizeMin = 10f;
                label.fontSizeMax = 20f;
            }
        }
    }

    private static void StyleText(Transform root)
    {
        foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (HasParentComponent<Button>(text.transform) ||
                HasParentComponent<TMP_InputField>(text.transform))
            {
                continue;
            }

            string name = text.gameObject.name.ToLowerInvariant();
            if (name.Contains("status") ||
                name.Contains("state") ||
                name.Contains("streaming"))
            {
                text.color = StatusGood;
                text.fontStyle = FontStyles.Bold;
            }
            else if (name.Contains("front") ||
                     name.Contains("warning") ||
                     name.Contains("tactile"))
            {
                text.color = Hex("fde68a", 1f);
            }
            else
            {
                text.color = MutedText;
            }

            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = Mathf.Clamp(text.fontSize <= 0f ? 22f : text.fontSize, 16f, 30f);
            AddTextShadow(text);
        }
    }

    private static void StyleVideoSurfaces(Transform root)
    {
        foreach (RawImage rawImage in root.GetComponentsInChildren<RawImage>(true))
        {
            if (!rawImage.enabled) continue;

            if (!IsVideoSurface(rawImage))
            {
                if (rawImage.texture == null)
                    rawImage.color = new Color(rawImage.color.r, rawImage.color.g, rawImage.color.b, 0f);
                continue;
            }

            rawImage.color = rawImage.texture == null ? VideoEmpty : Color.white;
            AddGlassEffects(rawImage, Hex("38bdf8", 0.2f), Hex("000000", 0.45f), new Vector2(0f, -10f));
        }
    }

    private static void ApplyObservationLayout(Transform root)
    {
        RectTransform rootRect = root as RectTransform;
        if (rootRect == null) return;

        float width = rootRect.rect.width > 1f ? rootRect.rect.width : Mathf.Max(rootRect.sizeDelta.x, 1920f);
        float height = rootRect.rect.height > 1f ? rootRect.rect.height : Mathf.Max(rootRect.sizeDelta.y, 1080f);

        float leftX = -width * 0.5f + 132f;
        float topY = height * 0.5f - 82f;
        float bottomY = -height * 0.5f + 86f;
        float rightX = width * 0.5f - 208f;

        SetRect(root, "FoldingCanvas Button", new Vector2(leftX - 102f, topY + 8f), new Vector2(42f, 42f));
        SetRect(root, "Start_Streaming_button", new Vector2(leftX, topY), new Vector2(206f, 66f));
        SetRect(root, "Record_button", new Vector2(leftX, topY - 78f), new Vector2(206f, 54f));
        SetRect(root, "CONTROL MODE Button", new Vector2(leftX, topY - 142f), new Vector2(206f, 58f));
        SetRect(root, "Enable WebRTC CAMERA Button", new Vector2(leftX, topY - 210f), new Vector2(206f, 54f));
        SetRect(root, "num_WebRTC Input", new Vector2(leftX - 62f, topY - 274f), new Vector2(82f, 48f));
        SetRect(root, "ADD UDP CAMERA Button", new Vector2(leftX, topY - 340f), new Vector2(206f, 58f));
        SetRect(root, "Tactile UI Button", new Vector2(leftX, topY - 410f), new Vector2(206f, 52f));
        SetRect(root, "Instruction_display_button", new Vector2(leftX, topY - 472f), new Vector2(206f, 50f));
        SetRect(root, "Quit Button", new Vector2(leftX, bottomY), new Vector2(140f, 46f));

        float ipY = height * 0.5f - 46f;
        SetTextRectByContent(root, "Enter Target IP", new Vector2(-214f, ipY), new Vector2(190f, 34f), TextAlignmentOptions.MidlineRight);
        SetRect(root, "IP Input", new Vector2(46f, ipY), new Vector2(316f, 38f));
        SetRect(root, "Direction_Instruction", new Vector2(0f, height * 0.5f - 92f), new Vector2(116f, 32f));
        SetRect(root, "Streaming_state_text", new Vector2(0f, height * 0.5f - 138f), new Vector2(420f, 34f));
        SetRect(root, "Tactile IP", new Vector2(rightX, bottomY - 4f), new Vector2(220f, 36f));
        SetRect(root, "Teleop_Instruction", new Vector2(rightX, bottomY + 58f), new Vector2(300f, 54f));
        SetContainerRectByTextContent(root, "Fine Control Mode", new Vector2(rightX, bottomY + 126f), new Vector2(272f, 54f), TextAlignmentOptions.Center);
        SetContainerRectByTextContent(root, "User Manual", new Vector2(rightX - 210f, bottomY + 124f), new Vector2(120f, 46f), TextAlignmentOptions.Center);

        MoveVideoGroup(root, width);
    }

    private static void MoveVideoGroup(Transform root, float width)
    {
        Transform videoGroup = FindByName(root, "WebRTC_Panels");
        if (videoGroup == null) return;

        Vector3 position = videoGroup.localPosition;
        position.x = width * 0.31f;
        position.y = 18f;
        videoGroup.localPosition = position;

        if (videoGroup.localScale.x > 40f)
        {
            float scale = Mathf.Min(videoGroup.localScale.x, 96f);
            videoGroup.localScale = new Vector3(scale, scale, scale);
        }
    }

    private static void SetRect(Transform root, string objectName, Vector2 position, Vector2 size)
    {
        Transform target = FindByName(root, objectName);
        RectTransform rect = target as RectTransform;
        if (rect == null) return;

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void SetTextRectByContent(
        Transform root,
        string content,
        Vector2 position,
        Vector2 size,
        TextAlignmentOptions alignment)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (string.IsNullOrEmpty(text.text) ||
                !text.text.Contains(content))
            {
                continue;
            }

            RectTransform rect = text.GetComponent<RectTransform>();
            if (rect == null) continue;

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 22f;
            text.color = MutedText;
            AddTextShadow(text);
        }
    }

    private static void SetContainerRectByTextContent(
        Transform root,
        string content,
        Vector2 position,
        Vector2 size,
        TextAlignmentOptions alignment)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (string.IsNullOrEmpty(text.text) ||
                !text.text.Contains(content))
            {
                continue;
            }

            RectTransform rect = GetNearestControlRect(text.transform);
            if (rect == null)
                rect = text.GetComponent<RectTransform>();
            if (rect == null) continue;

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = 20f;
            AddTextShadow(text);
        }
    }

    private static RectTransform GetNearestControlRect(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.GetComponent<Button>() != null ||
                current.GetComponent<TMP_InputField>() != null ||
                current.GetComponent<Toggle>() != null)
            {
                return current as RectTransform;
            }

            current = current.parent;
        }

        return null;
    }

    private static Transform FindByName(Transform root, string objectName)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(candidate.name, objectName, System.StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    private static Color GetButtonColor(string objectName)
    {
        string name = objectName.ToLowerInvariant();
        if (name.Contains("quit") || name.Contains("close")) return Danger;
        if (name.Contains("record")) return Danger;
        if (name.Contains("start") || name.Contains("stream")) return Primary;
        if (name.Contains("webrtc") || name.Contains("camera") || name.Contains("udp") || name.Contains("video")) return Video;
        if (name.Contains("recalibrate") || name.Contains("tracking") || name.Contains("tactile") || name.Contains("fine") || name.Contains("focus")) return Warning;
        if (name.Contains("control") || name.Contains("mode") || name.Contains("instruction") || name.Contains("fold") || name.Contains("manual")) return Utility;
        return Utility;
    }

    private static Color GetAccentLine(Color baseColor)
    {
        return new Color(
            Mathf.Clamp01(baseColor.r + 0.22f),
            Mathf.Clamp01(baseColor.g + 0.22f),
            Mathf.Clamp01(baseColor.b + 0.22f),
            0.28f);
    }

    private static bool IsBackdrop(Image image)
    {
        RectTransform rect = image.rectTransform;
        if (rect == null) return false;

        bool stretchesFullCanvas =
            rect.anchorMin.x <= 0.01f &&
            rect.anchorMin.y <= 0.01f &&
            rect.anchorMax.x >= 0.99f &&
            rect.anchorMax.y >= 0.99f;

        bool veryLarge = rect.rect.width > 640f && rect.rect.height > 360f;
        string name = image.gameObject.name.ToLowerInvariant();
        bool backdropName = name == "panel" || name.Contains("background") || name.Contains("backdrop");
        return backdropName && (stretchesFullCanvas || veryLarge);
    }

    private static bool IsVideoSurface(RawImage rawImage)
    {
        if (rawImage.texture != null) return true;

        Transform current = rawImage.transform;
        while (current != null)
        {
            string name = current.name.ToLowerInvariant();
            if (name.Contains("webrtc") ||
                name.Contains("video") ||
                name.Contains("plane") ||
                name.Contains("screen"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void AddGlassEffects(Graphic graphic, Color outlineColor, Color shadowColor, Vector2 shadowDistance)
    {
        if (graphic == null) return;

        Outline outline = GetOrAddOutline(graphic.gameObject);
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        if (shadowDistance.sqrMagnitude > 0.01f)
        {
            Shadow shadow = GetOrAddPlainShadow(graphic.gameObject);
            shadow.effectColor = shadowColor;
            shadow.effectDistance = shadowDistance;
            shadow.useGraphicAlpha = true;
        }
    }

    private static void ApplyRoundedShape(Image image)
    {
        if (image == null) return;

        image.sprite = GetRoundedGlassSprite();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
    }

    private static Sprite GetRoundedGlassSprite()
    {
        if (_roundedGlassSprite != null) return _roundedGlassSprite;

        const int size = 64;
        const int radius = 14;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "RuntimeUITheme_RoundedGlass",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x, x - (size - radius - 1), 0f);
                float dy = Mathf.Max(radius - y, y - (size - radius - 1), 0f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = 1f - Mathf.Clamp01(distance - radius + 1f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        Vector4 border = new Vector4(radius, radius, radius, radius);
        _roundedGlassSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border);

        _roundedGlassSprite.name = "RuntimeUITheme_RoundedGlass";
        return _roundedGlassSprite;
    }

    private static void AddTextShadow(TMP_Text text)
    {
        if (text == null) return;

        Shadow shadow = GetOrAddPlainShadow(text.gameObject);
        shadow.effectColor = Hex("000000", 0.62f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        shadow.useGraphicAlpha = true;
    }

    private static Outline GetOrAddOutline(GameObject gameObject)
    {
        Outline outline = gameObject.GetComponent<Outline>();
        if (outline == null) outline = gameObject.AddComponent<Outline>();
        return outline;
    }

    private static Shadow GetOrAddPlainShadow(GameObject gameObject)
    {
        Shadow[] shadows = gameObject.GetComponents<Shadow>();
        foreach (Shadow shadow in shadows)
        {
            if (!(shadow is Outline))
                return shadow;
        }

        return gameObject.AddComponent<Shadow>();
    }

    private static bool HasParentComponent<T>(Transform transform) where T : Component
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.GetComponent<T>() != null) return true;
            current = current.parent;
        }

        return false;
    }

    private static Color Tint(Color color, float multiplier, float alpha)
    {
        return new Color(
            Mathf.Clamp01(color.r * multiplier),
            Mathf.Clamp01(color.g * multiplier),
            Mathf.Clamp01(color.b * multiplier),
            alpha);
    }

    private static Color Hex(string hex, float alpha)
    {
        if (!ColorUtility.TryParseHtmlString("#" + hex, out Color color))
            color = Color.white;
        color.a = alpha;
        return color;
    }
}
