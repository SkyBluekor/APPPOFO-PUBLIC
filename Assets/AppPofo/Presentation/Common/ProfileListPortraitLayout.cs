using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ProfileListPortraitLayout : MonoBehaviour
    {
        [SerializeField] private ResponsiveUIContext context;
        [SerializeField] private RectTransform centeredContent;
        [SerializeField] private ScrollRect emptyScroll;
        [SerializeField] private RectTransform hero;
        [SerializeField] private RectTransform title;
        [SerializeField] private RectTransform description;
        [SerializeField] private RectTransform features;
        [SerializeField] private RectTransform primaryAdd;

        private void OnEnable()
        {
            if (context == null) return;
            context.Changed += ApplyLayout;
            context.Refresh();
            ApplyLayout();
        }

        private void OnDisable() { if (context != null) context.Changed -= ApplyLayout; }

        public void ApplyLayout()
        {
            if (!isActiveAndEnabled || context == null || centeredContent == null) return;
            centeredContent.sizeDelta = new Vector2(Mathf.Min(1080, context.AvailableSize.x), 0);
            var header = (RectTransform)centeredContent.Find("TopBar");
            var headerTitle = (RectTransform)header.Find("Title");
            // Leave room for back + logo on the left and admin + add on the right.
            headerTitle.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Min(260f, Mathf.Max(0f, header.rect.width - 248f - 276f)));
            float width = emptyScroll.viewport.rect.width;
            float height = Mathf.Max(939, emptyScroll.viewport.rect.height);
            emptyScroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            emptyScroll.vertical = height > emptyScroll.viewport.rect.height + .1f;
            if (!emptyScroll.vertical) emptyScroll.verticalNormalizedPosition = 1;
            // Collapse empty gaps first. Keep text, feature cards and buttons at their authored heights.
            float heroHeight = Mathf.Clamp(height - 639, 300, 500);
            float spare = Mathf.Max(0, height - 639 - heroHeight);
            float top = Mathf.Min(80, 24 + spare);
            spare = Mathf.Max(0, spare - (top - 24));
            float heroGap = Mathf.Min(190, spare);
            spare = Mathf.Max(0, spare - heroGap);
            float buttonGap = Mathf.Min(30, spare);
            float y = top;
            Place(hero, y, Mathf.Min(560 * heroHeight / 500, width), heroHeight); y += heroHeight + heroGap;
            Place(title, y, Mathf.Min(800, width), 150); y += 165;
            Place(description, y, Mathf.Min(850, width), 80); y += 110;
            Place(features, y, Mathf.Min(900, width), 140); y += 140 + buttonGap;
            Place(primaryAdd, y, Mathf.Min(850, width), 200);
            float cardWidth = Mathf.Min(290, Mathf.Max(0, (features.rect.width - 30) / 3));
            foreach (RectTransform card in features) card.sizeDelta = new Vector2(cardWidth, 120 * cardWidth / 290);
            var actions = (RectTransform)centeredContent.Find("BottomActions");
            ((RectTransform)actions.Find("CameraSearchButton")).SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal, Mathf.Min(850, actions.rect.width));
            LayoutRebuilder.MarkLayoutForRebuild(centeredContent);
        }

        private static void Place(RectTransform rect, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -top);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
