using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AppPofo.Presentation.Common
{
    // Attach beneath a full-canvas RectTransform. Backgrounds stay outside this root.
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaRoot : UIBehaviour
    {
        public Rect AppliedSafeArea { get; private set; }
        private Rect lastViewport;
        private bool applying;
        private Coroutine insetWatcher;
        protected override void OnEnable()
        {
            base.OnEnable();
            Refresh();
            if (UnityEngine.Application.isPlaying) insetWatcher = StartCoroutine(WatchInsets());
        }
        protected override void OnDisable()
        {
            if (insetWatcher != null) StopCoroutine(insetWatcher);
            insetWatcher = null;
            base.OnDisable();
        }
        protected override void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled && !applying) Refresh();
        }
        private void OnApplicationFocus(bool focused) { if (focused) Refresh(); }
        // Unity has no safe-area-only change event. One shared low-frequency
        // fallback catches inset changes without per-frame polling.
        private IEnumerator WatchInsets()
        {
            var delay = new WaitForSecondsRealtime(0.5f);
            while (true) { yield return delay; Refresh(); }
        }
        public void Refresh()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null) ApplyViewport(canvas.rootCanvas.pixelRect, Screen.safeArea);
        }
        public void ApplyViewport(Rect viewport, Rect safeArea)
        {
            if (applying || viewport.width <= 0 || viewport.height <= 0) return;
            Rect clipped = Rect.MinMaxRect(
                Mathf.Max(viewport.xMin, safeArea.xMin), Mathf.Max(viewport.yMin, safeArea.yMin),
                Mathf.Min(viewport.xMax, safeArea.xMax), Mathf.Min(viewport.yMax, safeArea.yMax));
            if (clipped.width <= 0 || clipped.height <= 0) return;
            var rect = (RectTransform)transform;
            Vector2 min = (clipped.min - viewport.min) / viewport.size;
            Vector2 max = (clipped.max - viewport.min) / viewport.size;
            if (lastViewport == viewport && AppliedSafeArea == clipped &&
                rect.anchorMin == min && rect.anchorMax == max) return;
            applying = true;
            lastViewport = viewport;
            AppliedSafeArea = clipped;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            applying = false;
            GetComponent<ResponsiveUIContext>()?.Refresh();
        }
    }
}
