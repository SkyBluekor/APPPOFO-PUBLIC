using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    /// <summary>
    /// InputField의 클릭은 유지하면서 명확한 세로 드래그만 부모 ScrollRect로 전달합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScrollDragForwarder
        : MonoBehaviour,
          IInitializePotentialDragHandler,
          IBeginDragHandler,
          IDragHandler,
          IEndDragHandler
    {
        [SerializeField] private ScrollRect targetScrollRect;
        [SerializeField, Min(0f)] private float verticalDragThreshold = 12f;
        [SerializeField, Min(1f)] private float verticalDominanceRatio = 1.15f;

        private bool isPointerDragging;
        private bool isForwardingDrag;

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            ResetDragState();

            if(!TryResolveScrollRect())
                return;

            // ScrollRect가 속도와 드래그 기준 위치를 정상적으로 초기화하게 합니다.
            targetScrollRect.OnInitializePotentialDrag(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            isPointerDragging = true;
            TryBeginForwarding(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if(!isPointerDragging || !TryResolveScrollRect())
                return;

            if(!isForwardingDrag && !TryBeginForwarding(eventData))
                return;

            targetScrollRect.OnDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if(isForwardingDrag && targetScrollRect != null)
                targetScrollRect.OnEndDrag(eventData);

            ResetDragState();
        }

        private bool TryBeginForwarding(PointerEventData eventData)
        {
            if(isForwardingDrag)
                return true;

            if(!TryResolveScrollRect() || !IsIntentionalVerticalDrag(eventData))
                return false;

            targetScrollRect.OnBeginDrag(eventData);
            isForwardingDrag = true;

            return true;
        }

        private bool IsIntentionalVerticalDrag(PointerEventData eventData)
        {
            Vector2 dragDistance =
                eventData.position - eventData.pressPosition;

            float verticalDistance = Mathf.Abs(dragDistance.y);
            float horizontalDistance = Mathf.Abs(dragDistance.x);

            return verticalDistance >= verticalDragThreshold &&
                   verticalDistance >=
                   horizontalDistance * verticalDominanceRatio;
        }

        private bool TryResolveScrollRect()
        {
            if(targetScrollRect == null)
                targetScrollRect = GetComponentInParent<ScrollRect>();

            return targetScrollRect != null;
        }

        private void OnDisable()
        {
            ResetDragState();
        }

        private void ResetDragState()
        {
            isPointerDragging = false;
            isForwardingDrag = false;
        }
    }
}
