using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class WelcomePortraitLayout : MonoBehaviour
    {
        [SerializeField] private ResponsiveUIContext context;
        [SerializeField] private RectTransform centeredContent;
        [SerializeField] private ScrollRect bodyScroll;
        [SerializeField] private RectTransform bottomActions;
        [SerializeField] private RectTransform hero;
        [SerializeField] private RectTransform features;

        private void OnEnable()
        {
            // S5-04A: add the optional cloud toggle at runtime without editing
            // Bootstrap.unity or altering the original Start/Skip navigation.
            if (UnityEngine.Application.isPlaying &&
                GetComponent<WelcomeCloudModeController>() == null)
                gameObject.AddComponent<WelcomeCloudModeController>();
            if (context == null) return;
            context.Changed += ApplyLayout;
            context.Refresh();
            ApplyLayout();
        }

        private void OnDisable() { if (context != null) context.Changed -= ApplyLayout; }

        public void ApplyLayout()
        {
            if (!isActiveAndEnabled || context == null || centeredContent == null) return;
            Vector2 available = context.AvailableSize;
            float padding = Mathf.Clamp((available.x - 936) / 2, 24, 72);
            float width = Mathf.Min(936, Mathf.Max(0, available.x - padding * 2));
            centeredContent.sizeDelta = new Vector2(width, 0);
            // Spend surplus height on the original whitespace before enabling scroll.
            float spare = Mathf.Max(0, available.y - 1230 - 400);
            float top = Mathf.Clamp(spare * 90 / 290, 24, 90);
            float bottom = Mathf.Clamp(spare * 105 / 290, 24, 105);
            float gap = Mathf.Clamp(spare * 95 / 290, 24, 95);
            bottomActions.anchoredPosition = new Vector2(0, bottom);
            var viewport = (RectTransform)bodyScroll.transform;
            viewport.offsetMin = new Vector2(0, bottom + 400 + gap);
            viewport.offsetMax = new Vector2(0, -top);
            bodyScroll.vertical = viewport.rect.height < 1230;
            if (!bodyScroll.vertical) bodyScroll.verticalNormalizedPosition = 1;

            foreach (RectTransform child in bodyScroll.content)
            {
                if (child.name == "AppNameText") SetWidth(child, Mathf.Min(500, width));
                if (child.name == "TaglineText") SetWidth(child, Mathf.Min(720, width));
                if (child.name == "DescriptionText") SetWidth(child, Mathf.Min(760, width));
            }
            float heroWidth = Mathf.Min(651.2146f, width - 28.7854f);
            hero.sizeDelta = new Vector2(heroWidth, heroWidth * 631.2864f / 651.2146f);
            float cardWidth = Mathf.Min(300, Mathf.Max(0, (width - 20) / 3));
            foreach (RectTransform card in features) card.sizeDelta = new Vector2(cardWidth, 126 * cardWidth / 300);
            SetWidth((RectTransform)bottomActions.Find("StartButton"), Mathf.Min(820, width));
            var cloudSelector = centeredContent.Find("ServerConnectionToggle") as RectTransform;
            if (cloudSelector != null) SetWidth(cloudSelector, Mathf.Min(332, width * .5f - 76));
            LayoutRebuilder.MarkLayoutForRebuild(centeredContent);
        }

        private static void SetWidth(RectTransform rect, float width)
        {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
    }
}
