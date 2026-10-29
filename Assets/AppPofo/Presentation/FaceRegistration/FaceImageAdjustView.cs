using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;

namespace AppPofo.Presentation.FaceRegistration
{
    [DisallowMultipleComponent]
    public sealed class FaceImageAdjustView : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IScrollHandler
    {
        [Header("References")]
        [SerializeField] private RectTransform previewArea;
        [SerializeField] private RectTransform adjustableImage;
        [SerializeField] private RectTransform faceOverlayLayer;

        [Header("Zoom")]
        [SerializeField, Min(0.1f)] private float minScale = 1f;
        [SerializeField, Min(0.1f)] private float maxScale = 4f;
        [SerializeField, Range(0.01f, 0.5f)] private float mouseWheelZoomSpeed = 0.12f;

        private Canvas rootCanvas;
        private bool interactionEnabled = true;
        private bool isDragging;
        private bool isPinching;
        private bool enabledEnhancedTouch;
        private float previousPinchDistance;
        private float contentAspectRatio;
        private float currentScale = 1f;
        private Vector2 currentPosition;

        public Vector2 CurrentPosition => currentPosition;
        public float CurrentScale => currentScale;

        private void Awake()
        {
            rootCanvas = GetComponentInParent<Canvas>();
            NormalizeScaleRange();
            ApplyTransform();
        }

        private void OnEnable()
        {
            if (!EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Enable();
                enabledEnhancedTouch = true;
            }
        }

        private void OnDisable()
        {
            isDragging = false;
            isPinching = false;

            if (enabledEnhancedTouch && EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Disable();
                enabledEnhancedTouch = false;
            }
        }

        private void Update()
        {
            if (!interactionEnabled)
            {
                isPinching = false;
                return;
            }

            if (!EnhancedTouchSupport.enabled)
            {
                isPinching = false;
                return;
            }

            var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
            if (touches.Count < 2)
            {
                isPinching = false;
                return;
            }

            Vector2 firstPosition = touches[0].screenPosition;
            Vector2 secondPosition = touches[1].screenPosition;
            Camera eventCamera = GetEventCamera();

            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    previewArea,
                    firstPosition,
                    eventCamera) ||
                !RectTransformUtility.RectangleContainsScreenPoint(
                    previewArea,
                    secondPosition,
                    eventCamera))
            {
                isPinching = false;
                return;
            }

            float pinchDistance = Vector2.Distance(firstPosition, secondPosition);
            if (!isPinching || previousPinchDistance <= Mathf.Epsilon)
            {
                isPinching = true;
                isDragging = false;
                previousPinchDistance = pinchDistance;
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    previewArea,
                    (firstPosition + secondPosition) * 0.5f,
                    eventCamera,
                    out Vector2 localCenter))
            {
                float ratio = pinchDistance / previousPinchDistance;
                SetScaleAroundPoint(currentScale * ratio, localCenter);
            }

            previousPinchDistance = pinchDistance;
        }

        public void SetInteractionEnabled(bool value)
        {
            interactionEnabled = value;

            if (!value)
            {
                isDragging = false;
                isPinching = false;
            }
        }

        public void SetContentAspect(float width, float height)
        {
            contentAspectRatio = width > 0f && height > 0f
                ? width / height
                : 0f;

            RefreshContentSize();
            ResetView();
        }

        public void ResetView()
        {
            NormalizeScaleRange();
            currentPosition = Vector2.zero;
            currentScale = Mathf.Clamp(1f, minScale, maxScale);
            ApplyTransform();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!interactionEnabled ||
                eventData.button != PointerEventData.InputButton.Left ||
                IsMultiTouchGesture())
            {
                return;
            }

            isDragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!interactionEnabled || !isDragging || IsMultiTouchGesture())
            {
                return;
            }

            float scaleFactor = rootCanvas != null
                ? Mathf.Max(0.01f, rootCanvas.scaleFactor)
                : 1f;

            currentPosition += eventData.delta / scaleFactor;
            ClampPosition();
            ApplyTransform();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            isDragging = false;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!interactionEnabled || Mathf.Approximately(eventData.scrollDelta.y, 0f))
            {
                return;
            }

            Vector2 localPoint = Vector2.zero;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                previewArea,
                eventData.position,
                eventData.pressEventCamera ?? GetEventCamera(),
                out localPoint);

            float scaleMultiplier = 1f +
                (eventData.scrollDelta.y * mouseWheelZoomSpeed);

            SetScaleAroundPoint(currentScale * scaleMultiplier, localPoint);
            eventData.Use();
        }

        private void SetScaleAroundPoint(float requestedScale, Vector2 localFocus)
        {
            NormalizeScaleRange();
            float nextScale = Mathf.Clamp(requestedScale, minScale, maxScale);

            if (Mathf.Approximately(nextScale, currentScale))
            {
                return;
            }

            float ratio = nextScale / currentScale;
            currentPosition = localFocus + ((currentPosition - localFocus) * ratio);
            currentScale = nextScale;
            ClampPosition();
            ApplyTransform();
        }

        private void RefreshContentSize()
        {
            if (previewArea == null ||
                adjustableImage == null ||
                contentAspectRatio <= 0f)
            {
                return;
            }

            Rect areaRect = previewArea.rect;
            if (areaRect.width <= 0f || areaRect.height <= 0f)
            {
                return;
            }

            float areaAspect = areaRect.width / areaRect.height;
            Vector2 contentSize;

            if (contentAspectRatio >= areaAspect)
            {
                contentSize = new Vector2(
                    areaRect.height * contentAspectRatio,
                    areaRect.height);
            }
            else
            {
                contentSize = new Vector2(
                    areaRect.width,
                    areaRect.width / contentAspectRatio);
            }

            adjustableImage.sizeDelta = contentSize;
            if (faceOverlayLayer != null)
            {
                faceOverlayLayer.sizeDelta = contentSize;
            }

            ClampPosition();
            ApplyTransform();
        }

        private void ClampPosition()
        {
            if (previewArea == null || adjustableImage == null)
            {
                return;
            }

            Vector2 scaledContentSize = new Vector2(
                adjustableImage.rect.width * currentScale,
                adjustableImage.rect.height * currentScale);

            Vector2 areaSize = previewArea.rect.size;
            float maxX = Mathf.Max(0f, (scaledContentSize.x - areaSize.x) * 0.5f);
            float maxY = Mathf.Max(0f, (scaledContentSize.y - areaSize.y) * 0.5f);

            currentPosition = new Vector2(
                Mathf.Clamp(currentPosition.x, -maxX, maxX),
                Mathf.Clamp(currentPosition.y, -maxY, maxY));
        }

        private void ApplyTransform()
        {
            ApplyTransform(adjustableImage);
            ApplyTransform(faceOverlayLayer);
        }

        private void ApplyTransform(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            target.anchoredPosition = currentPosition;
            target.localScale = Vector3.one * currentScale;
        }

        private void NormalizeScaleRange()
        {
            minScale = Mathf.Max(0.1f, minScale);
            maxScale = Mathf.Max(minScale, maxScale);
            currentScale = Mathf.Clamp(currentScale, minScale, maxScale);
        }

        private bool IsMultiTouchGesture()
        {
            return isPinching ||
                (EnhancedTouchSupport.enabled &&
                    UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count >= 2);
        }

        private Camera GetEventCamera()
        {
            if (rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return rootCanvas.worldCamera;
        }

        private void OnRectTransformDimensionsChange()
        {
            RefreshContentSize();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            NormalizeScaleRange();
        }
#endif
    }
}
