using System;
using System.Collections.Generic;
using AppPofo.Domain.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Components
{
    [Serializable]
    public sealed class SkillIconResolver
    {
        [Serializable]
        private sealed class SkillIconEntry
        {
            [SerializeField] private string skillName;
            [SerializeField] private Sprite icon;

            public string SkillName => skillName;
            public Sprite Icon => icon;
        }

        [SerializeField] private Sprite defaultIcon;
        [SerializeField] private SkillIconEntry[] knownSkills =
            Array.Empty<SkillIconEntry>();

        public Sprite Resolve(string skillName)
        {
            string lookupKey = NormalizeSkillName(skillName);

            if (string.IsNullOrEmpty(lookupKey))
            {
                return defaultIcon;
            }

            if (knownSkills == null)
            {
                return defaultIcon;
            }

            foreach (SkillIconEntry entry in knownSkills)
            {
                if (entry == null)
                {
                    continue;
                }

                if (!string.Equals(
                        NormalizeSkillName(entry.SkillName),
                        lookupKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return entry.Icon != null
                    ? entry.Icon
                    : defaultIcon;
            }

            return defaultIcon;
        }

        public IEnumerable<string> KnownSkillNames
        {
            get
            {
                if (knownSkills == null)
                {
                    yield break;
                }

                foreach (SkillIconEntry entry in knownSkills)
                {
                    if (entry != null &&
                        !string.IsNullOrWhiteSpace(entry.SkillName))
                    {
                        yield return entry.SkillName;
                    }
                }
            }
        }

        public bool AreEquivalent(string firstSkillName, string secondSkillName)
        {
            return string.Equals(
                NormalizeSkillName(firstSkillName),
                NormalizeSkillName(secondSkillName),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSkillName(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
            {
                return null;
            }

            string trimmedName = skillName.Trim();
            string compactKey = trimmedName
                .Replace(" ", string.Empty)
                .ToUpperInvariant();

            switch (compactKey)
            {
                case "C#":
                case "CSHARP":
                    return "C#";
                case "C++":
                case "CPP":
                    return "C++";
                case "JAVASCRIPT":
                case "JS":
                    return "JavaScript";
                case "TYPESCRIPT":
                case "TS":
                    return "TypeScript";
                case "HTML5":
                case "HTML":
                    return "HTML5";
                case "CSS3":
                case "CSS":
                    return "CSS3";
                case "MSSQL":
                case "SQLSERVER":
                case "MICROSOFTSQLSERVER":
                    return "MSSQL";
                case "FASTAPI":
                    return "FastAPI";
                case "OPENCV":
                    return "OpenCV";
                case "AWS":
                case "AMAZONWEBSERVICES":
                    return "AWS";
                default:
                    return trimmedName;
            }
        }
    }

    [RequireComponent(typeof(Button))]
    public class SkillChipView : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Image iconImage;
        [SerializeField] private Outline selectionOutline;
        [SerializeField] private Text selectionCheckText;
        [SerializeField] private SkillIconResolver iconResolver =
            new SkillIconResolver();

        private Button button;
        private ProfileItem item;
        private Action<ProfileItem> onClick;
        private string recommendedSkillName;
        private Action<string> onRecommendedClick;
        private Color defaultBackgroundColor;
        private bool hasDefaultBackgroundColor;
        private static readonly Dictionary<Sprite, Sprite> contentSizedBackgrounds = new Dictionary<Sprite, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearContentSizedBackgrounds()
        {
            foreach (Sprite sprite in contentSizedBackgrounds.Values)
                if (sprite != null) UnityEngine.Object.Destroy(sprite);
            contentSizedBackgrounds.Clear();
        }

        public IEnumerable<string> KnownSkillNames =>
            iconResolver?.KnownSkillNames ?? Array.Empty<string>();

        private void Awake()
        {
            button = GetComponent<Button>();
            CaptureDefaultBackgroundColor();
        }

        public void Bind(
            ProfileItem profileItem,
            Action<ProfileItem> clickAction)
        {
            if (profileItem == null)
            {
                Debug.LogError("[SkillChipView] ProfileItem이 null입니다.");
                return;
            }

            if (titleText == null)
            {
                Debug.LogError("[SkillChipView] TitleText가 연결되지 않았습니다.");
                return;
            }

            // 혹시 Awake 이전 상태에서도 안전하게 한 번 더 확인
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (button == null)
            {
                Debug.LogError("[SkillChipView] Button 컴포넌트가 없습니다.");
                return;
            }

            item = profileItem;
            onClick = clickAction;
            recommendedSkillName = null;
            onRecommendedClick = null;

            titleText.text = profileItem.Title;
            BindIcon(profileItem.Title);

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
            button.interactable = true;
            SetRecommendedSelectedVisual(false);
        }

        public void BindDisplay(string title, Action clickAction)
        {
            button = GetComponent<Button>();
            item = null; onClick = null; onRecommendedClick = null;
            titleText.supportRichText = false;
            titleText.text = title ?? "";
            BindIcon(title);
            button.onClick.RemoveAllListeners();
            if (clickAction != null) button.onClick.AddListener(() => clickAction());
            button.interactable = clickAction != null;
            SetRecommendedSelectedVisual(false);
        }

        public void BindRecommended(
            string skillName,
            Action<string> clickAction,
            bool selected)
        {
            if (titleText == null)
            {
                Debug.LogError("[SkillChipView] TitleText가 연결되지 않았습니다.");
                return;
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (button == null)
            {
                Debug.LogError("[SkillChipView] Button 컴포넌트가 없습니다.");
                return;
            }

            item = null;
            onClick = null;
            recommendedSkillName = skillName;
            onRecommendedClick = clickAction;

            titleText.text = skillName;
            BindIcon(skillName);

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleRecommendedClick);
            button.interactable = true;
            SetRecommendedSelectedVisual(selected);
        }

        public bool IsSameSkillName(string firstSkillName, string secondSkillName)
        {
            return iconResolver != null &&
                iconResolver.AreEquivalent(firstSkillName, secondSkillName);
        }

        public void ConfigureContentSizedLayout()
        {
            if (titleText == null) return;
            titleText.resizeTextForBestFit = false;
            titleText.fontSize = 28;
            FitVisibleBackground();
            var layout = GetComponent<HorizontalLayoutGroup>();
            if (layout == null) return;
            layout.padding = new RectOffset(16, 16, 0, 0);
            layout.spacing = 8;
            float preferred = layout.padding.horizontal;
            int visibleCount = 0;
            foreach (RectTransform child in transform)
            {
                if (!child.gameObject.activeSelf) continue;
                if (visibleCount++ > 0) preferred += layout.spacing;
                preferred += child == titleText.rectTransform
                    ? Mathf.Ceil(titleText.preferredWidth) : LayoutUtility.GetPreferredWidth(child);
            }
            var size = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            size.minWidth = 0;
            size.preferredWidth = preferred;
            size.flexibleWidth = 0;
            size.minHeight = size.preferredHeight = 72;
        }

        private void FitVisibleBackground()
        {
            Image background = GetComponent<Image>();
            if (background == null || background.sprite == null) return;
            Sprite source = background.sprite;
            // The existing 2172x724 artwork's visible body is x=307..1865,
            // y=162..561 from the top. Keep the original texture/artwork;
            // use its visible region instead of stretching its transparent margins.
            if (!source.name.EndsWith("_ContentSized"))
            {
                if (!contentSizedBackgrounds.TryGetValue(source, out Sprite cropped) || cropped == null)
                {
                    Rect rect = source.rect;
                    float scaleX = rect.width / 2172f;
                    float scaleY = rect.height / 724f;
                    Rect visible = new Rect(rect.x + 307 * scaleX, rect.y + 163 * scaleY,
                        1558 * scaleX, 399 * scaleY);
                    cropped = Sprite.Create(source.texture, visible, new Vector2(.5f, .5f),
                        source.pixelsPerUnit, 0, SpriteMeshType.FullRect,
                        new Vector4(120 * scaleX, 120 * scaleY, 120 * scaleX, 120 * scaleY));
                    cropped.name = source.name + "_ContentSized";
                    cropped.hideFlags = HideFlags.DontSave;
                    contentSizedBackgrounds[source] = cropped;
                }
                background.sprite = cropped;
            }
            background.type = Image.Type.Sliced;
            background.preserveAspect = false;
            // Keep the authored rounded ends when the chip's width changes.
            background.pixelsPerUnitMultiplier = background.sprite.rect.height / 72f;
        }

        private void BindIcon(string skillName)
        {
            if (iconImage == null)
            {
                return;
            }

            Sprite icon = iconResolver?.Resolve(skillName);

            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        private void HandleClick()
        {
            onClick?.Invoke(item);
        }

        private void HandleRecommendedClick()
        {
            onRecommendedClick?.Invoke(recommendedSkillName);
        }

        private void CaptureDefaultBackgroundColor()
        {
            if (button?.targetGraphic == null || hasDefaultBackgroundColor)
            {
                return;
            }

            defaultBackgroundColor = button.targetGraphic.color;
            hasDefaultBackgroundColor = true;
        }

        private void SetRecommendedSelectedVisual(bool selected)
        {
            CaptureDefaultBackgroundColor();

            if (button?.targetGraphic != null)
            {
                button.targetGraphic.color = selected
                    ? new Color(0.42f, 0.86f, 0.98f, 1f)
                    : defaultBackgroundColor;
            }

            if (selectionOutline != null)
            {
                selectionOutline.enabled = selected;
            }

            if (selectionCheckText != null)
            {
                selectionCheckText.gameObject.SetActive(selected);
            }
        }
    }
}
