using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    // Layout only: authored card art and all image-source/crop components stay untouched.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BusinessCardPortraitLayout : MonoBehaviour
    {
        [SerializeField] private ResponsiveUIContext context;
        [SerializeField] private RectTransform editorPanel, editorHeader, previewSlot, card, photoActions, themeSection;
        [SerializeField] private ScrollRect editorBody, themeScroll;
        [SerializeField] private RectTransform dialog, dialogHeader, dialogFooter;
        [SerializeField] private ScrollRect dialogBody;

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
            if (!isActiveAndEnabled || context == null || editorPanel == null) return;
            Vector2 available = context.AvailableSize;
            float width = Mathf.Min(960, Mathf.Max(0, available.x - 48));
            float height = Mathf.Min(1200, Mathf.Max(0, available.y - 48));
            // Preserve the authored frame ratio when safe insets narrow the panel.
            height = Mathf.Min(height, width * 1200f / 960f);
            editorPanel.sizeDelta = new Vector2(width, height);
            float innerWidth = Mathf.Max(0, width - 60);
            float headerTop = Mathf.Lerp(28, 61, Mathf.Clamp01((height - 800) / 400));
            Place(editorHeader, headerTop, innerWidth, 110);
            float bodyTop = headerTop + 110 + Mathf.Lerp(24, 69, Mathf.Clamp01((height - 800) / 400));
            float bottomGap = Mathf.Lerp(24, 125, Mathf.Clamp01((height - 800) / 400));
            Place(editorBody.viewport, bodyTop, innerWidth, Mathf.Max(1, height - bodyTop - bottomGap));
            float cardScale = Mathf.Min(1, innerWidth / 900);
            // Scale only the fixed-aspect card artwork, never the editor controls or dialog.
            card.localScale = Vector3.one * cardScale;
            Place(card, 0, 900, 360);
            Place(previewSlot, 0, innerWidth, 390 * cardScale);
            Place(photoActions, previewSlot.rect.height + 77, Mathf.Min(420, innerWidth), 100);
            Place(themeSection, previewSlot.rect.height + 195, innerWidth, 250);
            SetScroll(editorBody, previewSlot.rect.height + 445);
            ((RectTransform)themeScroll.transform.parent).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, innerWidth);
            themeScroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, innerWidth);
            themeScroll.horizontal = innerWidth < 900 - .1f;
            themeScroll.viewport.GetComponent<RectMask2D>().enabled = themeScroll.horizontal;
            if (!themeScroll.horizontal) themeScroll.horizontalNormalizedPosition = 0;

            float dialogWidth = Mathf.Min(700, Mathf.Max(0, available.x - 48));
            float dialogHeight = Mathf.Min(760, Mathf.Max(0, available.y - 48));
            dialog.sizeDelta = new Vector2(dialogWidth, dialogHeight);
            Place(dialogHeader, 0, dialogWidth, 130);
            dialogFooter.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, dialogWidth);
            Place(dialogBody.viewport, 100, dialogWidth, Mathf.Max(1, dialogHeight - 335));
            SetScroll(dialogBody, 391);
            LayoutRebuilder.MarkLayoutForRebuild(editorPanel);
            LayoutRebuilder.MarkLayoutForRebuild(dialog);
        }

        private static void SetScroll(ScrollRect scroll, float minimumHeight)
        {
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(minimumHeight, scroll.viewport.rect.height));
            scroll.vertical = minimumHeight > scroll.viewport.rect.height + .1f;
            scroll.viewport.GetComponent<RectMask2D>().enabled = scroll.vertical;
            if (!scroll.vertical) scroll.verticalNormalizedPosition = 1;
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
