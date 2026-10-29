using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    /// <summary>
    /// 폼 Content의 가로 여백과 항목 간격을 화면 방향에 맞게 조정합니다.
    /// 실제 항목 배치는 VerticalLayoutGroup이 담당합니다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VerticalLayoutGroup))]
    public sealed class ResponsiveFormLayout : MonoBehaviour
    {
        [Header("Portrait")]
        [SerializeField, Min(0)] private int portraitHorizontalPadding = 40;
        [SerializeField, Min(0f)] private float portraitSpacing = 20f;

        [Header("Landscape")]
        [SerializeField, Min(0)] private int landscapeHorizontalPadding = 170;
        [SerializeField, Min(0f)] private float landscapeSpacing = 15f;

        private VerticalLayoutGroup layoutGroup;
        private RectTransform rectTransform;
        private bool hasAppliedOrientation;
        private bool wasLandscape;

        private void Awake()
        {
            CacheComponents();
            ApplyLayout(force: true);
        }

        private void OnEnable()
        {
            CacheComponents();
            ApplyLayout(force: true);
        }

        private void OnRectTransformDimensionsChange()
        {
            ApplyLayout(force: false);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheComponents();
            ApplyLayout(force: true);
        }
#endif

        private void CacheComponents()
        {
            if(layoutGroup == null)
                layoutGroup = GetComponent<VerticalLayoutGroup>();

            if(rectTransform == null)
                rectTransform = transform as RectTransform;
        }

        private void ApplyLayout(bool force)
        {
            if(layoutGroup == null || rectTransform == null)
                return;

            RectTransform orientationRect =
                rectTransform.parent as RectTransform;

            bool isLandscape = orientationRect != null
                ? orientationRect.rect.width > orientationRect.rect.height
                : Screen.width > Screen.height;

            int horizontalPadding = isLandscape
                ? landscapeHorizontalPadding
                : portraitHorizontalPadding;

            float spacing = isLandscape
                ? landscapeSpacing
                : portraitSpacing;

            if(!force &&
               hasAppliedOrientation &&
               wasLandscape == isLandscape &&
               layoutGroup.padding.left == horizontalPadding &&
               layoutGroup.padding.right == horizontalPadding &&
               Mathf.Approximately(layoutGroup.spacing,spacing))
            {
                return;
            }

            RectOffset currentPadding = layoutGroup.padding;

            layoutGroup.padding = new RectOffset(
                horizontalPadding,
                horizontalPadding,
                currentPadding.top,
                currentPadding.bottom);

            layoutGroup.spacing = spacing;

            hasAppliedOrientation = true;
            wasLandscape = isLandscape;

            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
        }
    }
}
