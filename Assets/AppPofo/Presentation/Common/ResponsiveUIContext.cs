using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AppPofo.Presentation.Common
{
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class ResponsiveUIContext : UIBehaviour
    {
        public Vector2 AvailableSize { get; private set; }
        public float AspectRatio => AvailableSize.y > 0 ? AvailableSize.x / AvailableSize.y : 0;
        public Rect SafeArea { get; private set; }
        public event Action Changed;
        protected override void OnEnable() { base.OnEnable(); Refresh(); }
        protected override void OnRectTransformDimensionsChange() { Refresh(); }
        public void Refresh()
        {
            Vector2 size = ((RectTransform)transform).rect.size;
            SafeAreaRoot safe = GetComponent<SafeAreaRoot>();
            Rect area = safe != null ? safe.AppliedSafeArea : default;
            if (size == AvailableSize && area == SafeArea) return;
            AvailableSize = size;
            SafeArea = area;
            Changed?.Invoke();
        }
    }
}
