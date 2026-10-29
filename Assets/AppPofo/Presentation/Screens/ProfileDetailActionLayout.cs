using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ProfileDetailActionLayout : LayoutGroup
    {
        [SerializeField] private RectTransform profileRoot;
        [SerializeField] private RectTransform profileScrollView;

        private Vector2 lastSize = new Vector2(-1f, -1f);

        public bool IsPortraitLayout { get; private set; }

        protected override void OnEnable()
        {
            base.OnEnable();
            ApplyLayout();
        }

        private void Update()
        {
            RectTransform root = GetProfileRoot();
            if (root == null || root.rect.size == lastSize)
            {
                return;
            }

            ApplyLayout(root.rect.size);
        }

        public void ApplyLayout()
        {
            RectTransform root = GetProfileRoot();
            if (root != null)
            {
                ApplyLayout(root.rect.size);
            }
        }

        public void ApplyLayout(Vector2 availableSize)
        {
            if (profileScrollView == null)
            {
                return;
            }

            lastSize = availableSize;
            IsPortraitLayout = availableSize.y >= availableSize.x;

            if (IsPortraitLayout)
            {
                SetBottomStretch(rectTransform, 80f, 50f, 470f);
                SetFullStretch(profileScrollView, 40f, 495f, 40f, 190f);
            }
            else
            {
                SetBottomStretch(rectTransform, 48f, 30f, 120f);
                SetFullStretch(profileScrollView, 40f, 145f, 40f, 190f);
            }

            SetDirty();
        }

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            SetLayoutInputForAxis(270f, 920f, 1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            float preferred = IsPortraitLayout ? 420f : 90f;
            SetLayoutInputForAxis(preferred, preferred, 0f, 1);
        }

        public override void SetLayoutHorizontal()
        {
            int count = rectChildren.Count;
            if (count == 0)
            {
                return;
            }

            if (IsPortraitLayout)
            {
                for (int i = 0; i < count; i++)
                {
                    SetChildAlongAxis(rectChildren[i], 0, 0f, rectTransform.rect.width);
                }
                return;
            }

            const float spacing = 24f;
            float width = Mathf.Max(0f,
                (rectTransform.rect.width - (spacing * (count - 1))) / count);
            for (int i = 0; i < count; i++)
            {
                SetChildAlongAxis(rectChildren[i], 0, i * (width + spacing), width);
            }
        }

        public override void SetLayoutVertical()
        {
            int count = rectChildren.Count;
            if (count == 0)
            {
                return;
            }

            if (IsPortraitLayout)
            {
                const float height = 110f;
                const float spacing = 45f;
                for (int i = 0; i < count; i++)
                {
                    SetChildAlongAxis(rectChildren[i], 1, i * (height + spacing), height);
                }
                return;
            }

            for (int i = 0; i < count; i++)
            {
                SetChildAlongAxis(rectChildren[i], 1, 0f, rectTransform.rect.height);
            }
        }

        private RectTransform GetProfileRoot()
        {
            if (profileRoot == null)
            {
                profileRoot = transform.parent as RectTransform;
            }

            return profileRoot;
        }

        private static void SetBottomStretch(
            RectTransform target,
            float horizontalMargin,
            float bottom,
            float top)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = new Vector2(1f, 0f);
            target.pivot = new Vector2(0.5f, 0f);
            target.offsetMin = new Vector2(horizontalMargin, bottom);
            target.offsetMax = new Vector2(-horizontalMargin, top);
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
        }

        private static void SetFullStretch(
            RectTransform target,
            float left,
            float bottom,
            float right,
            float top)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.pivot = new Vector2(0.5f, 0.5f);
            target.offsetMin = new Vector2(left, bottom);
            target.offsetMax = new Vector2(-right, -top);
        }
    }
}
