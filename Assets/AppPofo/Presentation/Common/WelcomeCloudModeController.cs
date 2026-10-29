using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    // Attached by WelcomePortraitLayout only in Play mode.
    // The existing serialized StartButton navigation stays intact.
    [DisallowMultipleComponent]
    public sealed class WelcomeCloudModeController : MonoBehaviour
    {
        private Button startButton;
        private Button localButton;
        private Button serverButton;
        private Text localLabel;
        private Text serverLabel;
        private Text caption;
        private Image selection;
        private Image statusDot;
        private Sprite surfaceSprite;
        private Sprite selectionSprite;
        private Texture2D surfaceTexture;
        private Texture2D selectionTexture;
        private CloudConnectionStatus displayedStatus;
        private bool wantsCloud;
        private bool submitted;

        public bool WantsCloud => wantsCloud;

        private void Awake()
        {
            var actions = transform.Find("CenteredContent/BottomActions");
            if (actions == null)
            {
                Debug.LogError("[AppPofo] Welcome actions missing: cloud selector not attached.");
                return;
            }
            var start = actions.Find("StartButton");
            startButton = start != null ? start.GetComponent<Button>() : null;
            if (startButton == null)
            {
                Debug.LogError("[AppPofo] Welcome StartButton missing.");
                return;
            }
            var startText = start.GetComponentInChildren<Text>(true);
            BuildSelector(actions.parent, startText != null ? startText.font : null);
            startButton.onClick.AddListener(ApplyOnStart);
        }

        private void OnEnable()
        {
            submitted = false;
            RefreshAppearance();
        }

        private void Update()
        {
            if (displayedStatus != CloudModeSession.Status) RefreshAppearance();
        }

        private void BuildSelector(Transform parent, Font font)
        {
            var root = new GameObject("ServerConnectionToggle",
                typeof(RectTransform), typeof(Image), typeof(Shadow));
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(0, -24f);
            rect.sizeDelta = new Vector2(332f, 116f);
            surfaceSprite = MakeSurface(false, out surfaceTexture);
            selectionSprite = MakeSurface(true, out selectionTexture);
            var background = root.GetComponent<Image>();
            background.sprite = surfaceSprite;
            background.type = Image.Type.Sliced;
            background.raycastTarget = false;
            var shadow = root.GetComponent<Shadow>();
            shadow.effectColor = new Color(.42f, .30f, .74f, .14f);
            shadow.effectDistance = new Vector2(0, -4f);

            var highlight = new GameObject("SelectedMode", typeof(RectTransform), typeof(Image));
            highlight.layer = root.layer;
            highlight.transform.SetParent(root.transform, false);
            selection = highlight.GetComponent<Image>();
            selection.sprite = selectionSprite;
            selection.type = Image.Type.Sliced;
            selection.raycastTarget = false;

            localButton = CreateModeButton(root.transform, "LocalButton", "내 기기", font,
                0f, .5f, out localLabel);
            serverButton = CreateModeButton(root.transform, "ServerButton", "서버", font,
                .5f, 1f, out serverLabel);
            localButton.onClick.AddListener(SelectLocal);
            serverButton.onClick.AddListener(SelectServer);

            var status = new GameObject("ConnectionStatus", typeof(RectTransform));
            status.layer = root.layer;
            status.transform.SetParent(root.transform, false);
            var statusRect = (RectTransform)status.transform;
            statusRect.anchorMin = new Vector2(.5f, 0);
            statusRect.anchorMax = new Vector2(.5f, 0);
            statusRect.anchoredPosition = new Vector2(0, 26f);
            statusRect.sizeDelta = new Vector2(240f, 30f);
            var dot = new GameObject("StatusDot", typeof(RectTransform), typeof(Image));
            dot.layer = root.layer;
            dot.transform.SetParent(status.transform, false);
            var dotRect = (RectTransform)dot.transform;
            dotRect.anchorMin = dotRect.anchorMax = new Vector2(0, .5f);
            dotRect.anchoredPosition = new Vector2(18f, 0);
            dotRect.sizeDelta = new Vector2(12f, 12f);
            statusDot = dot.GetComponent<Image>();
            statusDot.sprite = surfaceSprite;
            statusDot.raycastTarget = false;
            caption = CreateText(status.transform, "ModeValue", "기기에 저장",
                font, TextAnchor.MiddleCenter, 23, new Color(.35f, .31f, .50f));
            Stretch((RectTransform)caption.transform, new Vector2(34f, 0), Vector2.zero);
            RefreshAppearance();
        }

        private Button CreateModeButton(Transform parent, string name, string value,
            Font font, float left, float right, out Text label)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(left, 1f);
            rect.anchorMax = new Vector2(right, 1f);
            rect.offsetMin = new Vector2(8f, -70f);
            rect.offsetMax = new Vector2(-8f, -8f);
            var target = root.GetComponent<Image>();
            target.sprite = surfaceSprite;
            target.type = Image.Type.Sliced;
            var button = root.GetComponent<Button>();
            button.targetGraphic = target;
            var colors = button.colors;
            colors.highlightedColor = new Color(.84f, .80f, 1f, .20f);
            colors.pressedColor = new Color(.68f, .60f, .94f, .32f);
            colors.selectedColor = Color.clear;
            colors.normalColor = Color.clear;
            button.colors = colors;
            // Keep the graphic white so ColorTint can show pointer/keyboard feedback.
            target.color = Color.white;
            target.CrossFadeColor(Color.clear, 0f, true, true);
            label = CreateText(root.transform, "Label", value, font,
                TextAnchor.MiddleCenter, 27, Color.white);
            Stretch((RectTransform)label.transform, Vector2.zero, Vector2.zero);
            return button;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        private static Text CreateText(Transform parent, string name, string value,
            Font font, TextAnchor alignment, int size, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            gameObject.layer = parent.gameObject.layer;
            gameObject.transform.SetParent(parent, false);
            var text = gameObject.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.color = color;
            text.alignment = alignment;
            text.fontSize = size;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        public void ToggleCloud()
        {
            SelectCloud(!wantsCloud);
        }

        private void SelectLocal() => SelectCloud(false);
        private void SelectServer() => SelectCloud(true);

        private void SelectCloud(bool enabled)
        {
            if (submitted) return;
            wantsCloud = enabled;
            RefreshAppearance();
        }

        private void RefreshAppearance()
        {
            if (selection == null) return;
            displayedStatus = CloudModeSession.Status;
            var rect = selection.rectTransform;
            rect.anchorMin = new Vector2(wantsCloud ? .5f : 0, 1f);
            rect.anchorMax = new Vector2(wantsCloud ? 1f : .5f, 1f);
            rect.offsetMin = new Vector2(8f, -70f);
            rect.offsetMax = new Vector2(-8f, -8f);
            var inactive = new Color(.39f, .33f, .58f);
            localLabel.color = wantsCloud ? inactive : Color.white;
            serverLabel.color = wantsCloud ? Color.white : inactive;
            caption.text = "기기에 저장";
            statusDot.color = new Color(.60f, .48f, .88f);
            if (!wantsCloud) return;
            // Selecting a mode does not establish a connection. Only report a
            // health result for the server session actually started by this UI.
            caption.text = (CloudServerConfiguration.IsStaging ? "검증 서버 · " : "") + "시작 시 연결";
            statusDot.color = new Color(.63f, .56f, .78f);
            if (!submitted || CloudModeSession.Mode != ProfileDataMode.Cloud) return;
            switch (displayedStatus)
            {
                case CloudConnectionStatus.Connecting:
                    caption.text = "연결 중";
                    statusDot.color = new Color(.88f, .63f, .26f);
                    break;
                case CloudConnectionStatus.ServerReachable:
                    caption.text = (CloudServerConfiguration.IsStaging ? "검증 서버 · " : "") + "연결됨";
                    statusDot.color = new Color(.16f, .66f, .64f);
                    break;
                case CloudConnectionStatus.Offline:
                    caption.text = "연결 안 됨";
                    statusDot.color = new Color(.82f, .34f, .48f);
                    break;
            }
        }

        public void ApplyOnStart()
        {
            if (submitted) return;
            submitted = true;
            CloudConnectionRuntime.BeginFromWelcome(wantsCloud);
            RefreshAppearance();
        }

        private static Sprite MakeSurface(bool selected, out Texture2D texture)
        {
            const int size = 64;
            const float radius = 30f;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = selected ? "WelcomeModeGradient" : "WelcomeModeGlass",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x + .5f - size / 2f) - (size / 2f - radius);
                float dy = Mathf.Abs(y + .5f - size / 2f) - (size / 2f - radius);
                float distance = Mathf.Min(Mathf.Max(dx, dy), 0f) +
                    new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude - radius;
                Color fill = selected
                    ? Color.Lerp(new Color(.55f, .31f, 1f), new Color(.35f, .53f, .99f),
                        (x + y) / (2f * (size - 1)))
                    : Color.Lerp(new Color(.93f, .92f, 1f, .96f), new Color(1f, 1f, 1f, .96f),
                        y / (float)(size - 1));
                Color border = selected ? new Color(.75f, .68f, 1f) : new Color(.79f, .74f, .94f);
                Color pixel = Color.Lerp(border, fill, Mathf.Clamp01(-distance - .5f));
                pixel.a *= Mathf.Clamp01(.5f - distance);
                pixels[y * size + x] = pixel;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                100, 0, SpriteMeshType.FullRect, new Vector4(31, 31, 31, 31));
        }

        private void OnDestroy()
        {
            if (startButton != null) startButton.onClick.RemoveListener(ApplyOnStart);
            if (localButton != null) localButton.onClick.RemoveListener(SelectLocal);
            if (serverButton != null) serverButton.onClick.RemoveListener(SelectServer);
            if (surfaceSprite != null) Destroy(surfaceSprite);
            if (selectionSprite != null) Destroy(selectionSprite);
            if (surfaceTexture != null) Destroy(surfaceTexture);
            if (selectionTexture != null) Destroy(selectionTexture);
        }
    }
}
