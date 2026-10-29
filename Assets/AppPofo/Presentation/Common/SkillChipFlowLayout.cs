using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    // Content-sized chips wrap to a new row within the available form width.
    public sealed class SkillChipFlowLayout : LayoutGroup
    {
        private const float ChipHeight = 72;
        private const float Gap = 12;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            SetLayoutInputForAxis(0, 0, 1, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            float height = Arrange(false);
            SetLayoutInputForAxis(height, height, 0, 1);
        }

        public override void SetLayoutHorizontal() => Arrange(true);
        public override void SetLayoutVertical() => Arrange(true);

        private float Arrange(bool apply)
        {
            float available = Mathf.Max(1, rectTransform.rect.width - padding.horizontal);
            float x = 0;
            float y = padding.top;
            foreach (RectTransform child in rectChildren)
            {
                float width = Mathf.Clamp(LayoutUtility.GetPreferredWidth(child), 1, available);
                if (x > 0 && x + width > available)
                {
                    x = 0;
                    y += ChipHeight + Gap;
                }
                if (apply)
                {
                    SetChildAlongAxis(child, 0, padding.left + x, width);
                    SetChildAlongAxis(child, 1, y, ChipHeight);
                }
                x += width + Gap;
            }
            return rectChildren.Count == 0 ? padding.vertical : y + ChipHeight + padding.bottom;
        }
    }
}
