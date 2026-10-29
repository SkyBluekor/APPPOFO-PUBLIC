using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    /// <summary>
    /// Compact search controls matching the existing AppPofo pastel palette.
    /// Fixed-size nine-sliced rounded primitives are used instead of stretching
    /// illustration sprites (which previously distorted into giant neon ribbons).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ProfileSearchPanelView : MonoBehaviour
    {
        [SerializeField] private Font regularFont;
        [SerializeField] private Font mediumFont;
        [SerializeField] private Font boldFont;
        [SerializeField] private Sprite inputFieldSprite;

        private readonly List<Sprite> ownedSprites = new List<Sprite>();
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        private InputField nameInput;
        private InputField skillInput;
        private Image nameBackground;
        private Image skillBackground;
        private Text pageLabel;
        private Button previousButton;
        private Button nextButton;
        private Button resetButton;
        private Sprite fieldSprite;
        private Outline nameFocusOutline;
        private Outline skillFocusOutline;
        private Sprite resetSprite;
        private Sprite navigationSprite;
        private bool built;
        private bool wasNameFocused;
        private float lastLayoutWidth = -1f;
        private bool wasSkillFocused;
        private bool cloudMode;

        public string NameQuery => nameInput != null ? nameInput.text : string.Empty;
        public string SkillsQuery => skillInput != null ? skillInput.text : string.Empty;

        private void Awake() => BuildOnce();

        public void Bind(Action<string> changed, Action reset, Action previous, Action next)
        {
            BuildOnce();
            nameInput.onValueChanged.RemoveAllListeners();
            skillInput.onValueChanged.RemoveAllListeners();
            resetButton.onClick.RemoveAllListeners();
            previousButton.onClick.RemoveAllListeners();
            nextButton.onClick.RemoveAllListeners();

            nameInput.onValueChanged.AddListener(value => changed?.Invoke(value));
            skillInput.onValueChanged.AddListener(value => changed?.Invoke(value));
            resetButton.onClick.AddListener(() => reset?.Invoke());
            previousButton.onClick.AddListener(() => previous?.Invoke());
            nextButton.onClick.AddListener(() => next?.Invoke());
        }

        public void ClearFiltersWithoutNotifying()
        {
            if (nameInput == null || skillInput == null) return;
            nameInput.SetTextWithoutNotify(string.Empty);
            skillInput.SetTextWithoutNotify(string.Empty);
        }

        // Reuse the authored search artwork as a compact cloud pagination bar.
        // Server text search is not yet implemented; never pretend local SQL filters apply.
        public void SetCloudMode(bool enabled)
        {
            BuildOnce();
            cloudMode = enabled;
            if (nameInput != null) nameInput.interactable = !enabled;
            if (skillInput != null) skillInput.interactable = !enabled;
            transform.Find("NameSearchRow").gameObject.SetActive(!enabled);
            transform.Find("SkillSearchRow").gameObject.SetActive(!enabled);
            var height = enabled ? 108f : 260f;
            ((RectTransform)transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            var element = GetComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = height;
            var caption = resetButton.transform.Find("Caption").GetComponent<Text>();
            caption.text = enabled ? "새로고침" : "초기화";
            lastLayoutWidth = -1f;
            RefreshGeometry();
        }

        public void SetStatus(string label, bool canPrevious, bool canNext)
        {
            if (pageLabel != null) pageLabel.text = label;
            if (previousButton != null) previousButton.interactable = canPrevious;
            if (nextButton != null) nextButton.interactable = canNext;
        }

        private void LateUpdate()
        {
            // Content's VerticalLayoutGroup resolves this card's actual width
            // after Awake. Derive every child from that width, not prefab guesses.
            RefreshGeometry();
        }

        public void RefreshGeometry()
        {
            if (!built) return;
            float actualWidth = ((RectTransform)transform).rect.width;
            if (actualWidth < 180f || Mathf.Abs(actualWidth - lastLayoutWidth) < .5f)
                return;
            lastLayoutWidth = actualWidth;
            LayoutChildren(actualWidth);
        }

        private void Update()
        {
            bool nameFocused = nameInput != null && nameInput.isFocused;
            bool skillFocused = skillInput != null && skillInput.isFocused;
            if (nameFocused != wasNameFocused)
            {
                wasNameFocused = nameFocused;
                if (nameFocusOutline != null) nameFocusOutline.enabled = nameFocused;
            }
            if (skillFocused != wasSkillFocused)
            {
                wasSkillFocused = skillFocused;
                if (skillFocusOutline != null) skillFocusOutline.enabled = skillFocused;
            }
        }

        private void BuildOnce()
        {
            if (built) return;
            built = true;

            // The panel is intentionally visibly filled. The background image is
            // an actual rounded nine-slice, never a transparent rectangular overlay.
            Sprite panelSprite = RoundedSprite(
                new Color(.988f, .984f, 1f, 1f),
                new Color(.933f, .940f, .992f, 1f),
                new Color(.80f, .79f, .95f, 1f), 30f);
            fieldSprite = SliceExistingInputSprite(inputFieldSprite);
            resetSprite = RoundedSprite(
                new Color(.996f, .991f, 1f, 1f),
                new Color(.953f, .938f, .992f, 1f),
                new Color(.76f, .69f, .94f, 1f), 20f);
            navigationSprite = RoundedSprite(
                new Color(.954f, .942f, 1f, 1f),
                new Color(.864f, .859f, .980f, 1f),
                new Color(.71f, .64f, .94f, 1f), 20f);

            var root = (RectTransform)transform;
            // The containing VerticalLayoutGroup owns our width and horizontal
            // placement. Never overwrite its driven anchors/sizeDelta.x.
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 260f);

            Image background = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            SetupImage(background, panelSprite);
            background.raycastTarget = false;
            if (GetComponent<RectMask2D>() == null) gameObject.AddComponent<RectMask2D>();

            var shadow = GetComponent<Shadow>() ?? gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.37f, .32f, .68f, .11f);
            shadow.effectDistance = new Vector2(0f, -2f);
            shadow.useGraphicAlpha = true;

            var element = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = 260;
            element.flexibleWidth = 1f;

            // Content owns the list layout. Explicit anchors below prevent
            // nested layout groups from squeezing text and input controls.
            var oldLayout = GetComponent<VerticalLayoutGroup>();
            if (oldLayout != null) oldLayout.enabled = false;

            nameInput = BuildInputRow("NameSearchRow", "이름", "이름으로 검색", out nameBackground);
            skillInput = BuildInputRow("SkillSearchRow", "기술", "Python, Unity  ·  모두 포함",
                out skillBackground);

            RectTransform navigation = MakeRow("PageNavigation", 174f, 62f);
            resetButton = MakeButton(navigation, "초기화", 124, resetSprite, false);
            pageLabel = MakeText(navigation, "SearchStatus", "총 0명 · 1/1 페이지", 22,
                mediumFont, new Color(.33f, .32f, .49f, 1f));
            SetRect(pageLabel.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(142f, 0f), new Vector2(-156f, 0f));
            pageLabel.alignment = TextAnchor.MiddleCenter;
            pageLabel.resizeTextForBestFit = true;
            pageLabel.resizeTextMinSize = 15;
            pageLabel.resizeTextMaxSize = 22;
            previousButton = MakeButton(navigation, "〈", 62, navigationSprite, true);
            nextButton = MakeButton(navigation, "〉", 62, navigationSprite, true);
            SetStatus("총 0명 · 1/1 페이지", false, false);
            RefreshGeometry();
        }

        private void LayoutChildren(float cardWidth)
        {
            const float cardInset = 28f;
            const float labelWidth = 82f;
            const float labelGap = 20f;
            float rowWidth = Mathf.Max(0f, cardWidth - 2f * cardInset);
            float inputLeft = labelWidth + labelGap;

            PositionChild((RectTransform)transform.Find("NameSearchRow"),
                cardInset, 22f, rowWidth, 62f);
            PositionChild((RectTransform)transform.Find("SkillSearchRow"),
                cardInset, 98f, rowWidth, 62f);
            foreach (string rowName in new[] { "NameSearchRow", "SkillSearchRow" })
            {
                var row = (RectTransform)transform.Find(rowName);
                PositionChild((RectTransform)row.Find("Label"),
                    0f, 0f, labelWidth, 62f);
                var inputRect = (RectTransform)row.Find("SearchInput");
                PositionChild(inputRect, inputLeft, 0f,
                    Mathf.Max(0f, rowWidth - inputLeft), 62f);
                PositionChild((RectTransform)inputRect.Find("Text"),
                    22f, 0f, Mathf.Max(0f, inputRect.rect.width - 44f), 62f);
                PositionChild((RectTransform)inputRect.Find("Placeholder"),
                    22f, 0f, Mathf.Max(0f, inputRect.rect.width - 44f), 62f);
            }

            var navigation = (RectTransform)transform.Find("PageNavigation");
            PositionChild(navigation, cardInset, cloudMode ? 22f : 174f, rowWidth, 62f);
            PositionChild((RectTransform)navigation.Find("ResetButton"), 0f, 0f, 124f, 62f);
            float rightStart = rowWidth - 140f;
            PositionChild((RectTransform)navigation.Find("PreviousButton"),
                rightStart, 0f, 62f, 62f);
            PositionChild((RectTransform)navigation.Find("NextButton"),
                rowWidth - 62f, 0f, 62f, 62f);
            PositionChild((RectTransform)navigation.Find("SearchStatus"),
                142f, 0f, Mathf.Max(0f, rowWidth - 298f), 62f);
            foreach (string name in new[] { "ResetButton", "PreviousButton", "NextButton" })
            {
                var button = (RectTransform)navigation.Find(name);
                PositionChild((RectTransform)button.Find("Caption"),
                    5f, 0f, button.rect.width - 10f, 62f);
            }
        }

        private static void PositionChild(RectTransform rect,
            float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
        }

        private InputField BuildInputRow(string name, string label, string hint, out Image box)
        {
            RectTransform row = MakeRow(name, name == "NameSearchRow" ? 22f : 98f, 62f);
            Text title = MakeText(row, "Label", label, 26, boldFont,
                new Color(.17f, .17f, .34f, 1f));
            SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(82f, 0f));
            title.alignment = TextAnchor.MiddleCenter;
            title.gameObject.AddComponent<LayoutElement>().preferredWidth = 82f;

            var host = NewUIObject("SearchInput", row, typeof(Image), typeof(InputField),
                typeof(LayoutElement), typeof(RectMask2D));
            var rect = (RectTransform)host.transform;
            SetRect(rect, Vector2.zero, Vector2.one, new Vector2(101f, 0f), Vector2.zero);
            var inputLayout = host.GetComponent<LayoutElement>();
            inputLayout.preferredHeight = inputLayout.minHeight = 62f;
            box = host.GetComponent<Image>();
            SetupImage(box, fieldSprite);
            box.color = new Color(1f, 1f, 1f, .80f);
            box.raycastTarget = true;

            var outline = host.AddComponent<Outline>();
            outline.effectColor = new Color(.60f, .48f, .95f, .70f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.enabled = false;
            if (name == "NameSearchRow") nameFocusOutline = outline;
            else skillFocusOutline = outline;

            var input = host.GetComponent<InputField>();
            input.targetGraphic = box;
            input.characterLimit = 150;
            input.lineType = InputField.LineType.SingleLine;
            Text value = MakeText(host.transform, "Text", "", 23, regularFont,
                new Color(.20f, .21f, .37f, 1f));
            StretchText(value.rectTransform, 22f, -22f);
            value.alignment = TextAnchor.MiddleLeft;
            value.horizontalOverflow = HorizontalWrapMode.Wrap;
            value.verticalOverflow = VerticalWrapMode.Truncate;
            value.supportRichText = false;

            Text placeholder = MakeText(host.transform, "Placeholder", hint, 22, regularFont,
                new Color(.52f, .50f, .66f, 1f));
            StretchText(placeholder.rectTransform, 22f, -22f);
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.horizontalOverflow = HorizontalWrapMode.Wrap;
            placeholder.verticalOverflow = VerticalWrapMode.Truncate;
            input.textComponent = value;
            input.placeholder = placeholder;
            title.transform.SetAsLastSibling(); // Labels must not be hidden by input artwork.
            return input;
        }

        private RectTransform MakeRow(string name, float top, float height)
        {
            var host = NewUIObject(name, transform);
            var rect = (RectTransform)host.transform;
            SetRect(rect, new Vector2(0f, 1f), Vector2.one,
                new Vector2(28f, -top - height), new Vector2(-28f, -top));
            return rect;
        }

        private Button MakeButton(Transform parent, string label, float width,
            Sprite sprite, bool arrow)
        {
            string name = label == "〈" ? "PreviousButton" :
                label == "〉" ? "NextButton" : "ResetButton";
            var host = NewUIObject(name, parent, typeof(Image), typeof(Button),
                typeof(LayoutElement));
            var rect = (RectTransform)host.transform;
            if (name == "ResetButton")
                SetRect(rect, Vector2.zero, new Vector2(0f, 1f),
                    Vector2.zero, new Vector2(width, 0f));
            else if (name == "PreviousButton")
                SetRect(rect, new Vector2(1f, 0f), Vector2.one,
                    new Vector2(-140f, 0f), new Vector2(-78f, 0f));
            else
                SetRect(rect, new Vector2(1f, 0f), Vector2.one,
                    new Vector2(-62f, 0f), Vector2.zero);
            var element = host.GetComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.minHeight = element.preferredHeight = 62f;
            Image background = host.GetComponent<Image>();
            SetupImage(background, sprite);
            Button button = host.GetComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.98f, .98f, 1f, 1f);
            colors.pressedColor = new Color(.86f, .82f, .97f, 1f);
            colors.disabledColor = new Color(.97f, .97f, 1f, .58f);
            button.colors = colors;
            Text caption = MakeText(host.transform, "Caption", label,
                arrow ? 29 : 22, arrow ? mediumFont : boldFont,
                new Color(.32f, .23f, .57f, 1f));
            StretchText(caption.rectTransform, 5f, -5f);
            caption.alignment = TextAnchor.MiddleCenter;
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 16;
            caption.resizeTextMaxSize = arrow ? 29 : 22;
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorsMin,
            Vector2 anchorsMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorsMin;
            rect.anchorMax = anchorsMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SetupImage(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }

        private Text MakeText(Transform parent, string name, string value,
            int size, Font font, Color color)
        {
            var host = NewUIObject(name, parent, typeof(Text));
            var label = host.GetComponent<Text>();
            label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size;
            label.color = color;
            label.text = value;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }

        private GameObject NewUIObject(string name, Transform parent, params Type[] components)
        {
            var types = new Type[components.Length + 2];
            types[0] = typeof(RectTransform);
            types[1] = typeof(CanvasRenderer);
            Array.Copy(components, 0, types, 2, components.Length);
            var host = new GameObject(name, types);
            host.layer = gameObject.layer;
            host.transform.SetParent(parent, false);
            return host;
        }

        private static void StretchText(RectTransform rect, float left, float right)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, 0);
            rect.offsetMax = new Vector2(right, 0);
        }

        private Sprite SliceExistingInputSprite(Sprite source)
        {
            if (source == null)
            {
                Debug.LogError("[ProfileSearch] input_profile_add_field sprite is missing.", this);
                return RoundedSprite(Color.white, Color.white,
                    new Color(.76f, .73f, .92f, 1f), 17f);
            }
            if (source.border != Vector4.zero) return source;

            // This is the same input-specific asset used by the profile form.
            // Stretch the center only so the original end caps stay rounded.
            Rect rect = source.rect;
            float leftRight = Mathf.Min(rect.height * .46f, rect.width * .23f);
            float topBottom = rect.height * .40f;
            float scaledPpu = Mathf.Max(source.pixelsPerUnit, rect.height * 2.4f);
            var sprite = Sprite.Create(source.texture, rect, new Vector2(.5f, .5f),
                scaledPpu, 0, SpriteMeshType.FullRect,
                new Vector4(leftRight, topBottom, leftRight, topBottom));
            sprite.name = source.name + "_SearchSliced";
            ownedSprites.Add(sprite);
            return sprite;
        }

        private Sprite RoundedSprite(Color top, Color bottom, Color border, float radius)
        {
            const int size = 72;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "ProfileSearchRoundedUI";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float t = y / (float)(size - 1);
                Color fill = Color.Lerp(bottom, top, t);
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + .5f - size / 2f) - (size / 2f - radius);
                    float dy = Mathf.Abs(y + .5f - size / 2f) - (size / 2f - radius);
                    float positiveX = Mathf.Max(dx, 0f), positiveY = Mathf.Max(dy, 0f);
                    float signedDistance = Mathf.Min(Mathf.Max(dx, dy), 0f) +
                        Mathf.Sqrt(positiveX * positiveX + positiveY * positiveY) - radius;
                    // dx/dy describe the inset corner centers. Subtract the radius
                    // to reach the outer contour; otherwise the visible background
                    // shrinks inside its RectTransform and leaves controls outside it.
                    float coverage = Mathf.Clamp01(.5f - signedDistance);
                    float inner = Mathf.Clamp01(.5f - (signedDistance + 1.5f));
                    Color pixel = Color.Lerp(border, fill, inner);
                    pixel.a *= coverage;
                    pixels[y * size + x] = pixel;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(Mathf.Min(radius + 2f, 34f), Mathf.Min(radius + 2f, 34f),
                    Mathf.Min(radius + 2f, 34f), Mathf.Min(radius + 2f, 34f)));
            sprite.name = "ProfileSearchNineSlice";
            ownedTextures.Add(texture);
            ownedSprites.Add(sprite);
            return sprite;
        }

        private void OnDestroy()
        {
            foreach (Sprite sprite in ownedSprites)
                if (sprite != null) Destroy(sprite);
            foreach (Texture2D texture in ownedTextures)
                if (texture != null) Destroy(texture);
            ownedSprites.Clear();
            ownedTextures.Clear();
        }
    }
}
