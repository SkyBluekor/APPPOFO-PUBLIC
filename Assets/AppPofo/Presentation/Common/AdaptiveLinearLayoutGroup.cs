using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    // One layout owner; axis changes reuse Unity's min/preferred/flexible solver.
    public sealed class AdaptiveLinearLayoutGroup : HorizontalOrVerticalLayoutGroup
    {
        [SerializeField] private bool vertical = true;
        public bool Vertical { get => vertical; set => SetProperty(ref vertical, value); }
        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            CalcAlongAxis(0, vertical);
        }
        public override void CalculateLayoutInputVertical() { CalcAlongAxis(1, vertical); }
        public override void SetLayoutHorizontal() { SetChildrenAlongAxis(0, vertical); }
        public override void SetLayoutVertical() { SetChildrenAlongAxis(1, vertical); }
    }
}
