using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AppPofo.Presentation.FaceRegistration
{
    [DisallowMultipleComponent]
    public sealed class FaceSelectionBoxView : MonoBehaviour,
        IPointerClickHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IScrollHandler
    {
        private static readonly Color NormalColor = new Color(0.13f, 0.78f, 0.83f, 0.10f);
        private static readonly Color SelectedColor = new Color(0.13f, 0.78f, 0.83f, 0.28f);
        private static readonly Color NormalOutlineColor = new Color(0.78f, 0.98f, 1f, 0.95f);
        private static readonly Color SelectedOutlineColor = new Color(0.13f, 0.78f, 0.83f, 1f);

        private Image boxImage;
        private Outline outline;
        private FaceImageAdjustView adjustView;
        private Action<int> onSelected;
        private int faceIndex;
        private bool wasDragged;

        public void Configure(
            int index,
            FaceImageAdjustView targetAdjustView,
            Action<int> selectionCallback)
        {
            faceIndex = index;
            adjustView = targetAdjustView;
            onSelected = selectionCallback;

            boxImage = GetComponent<Image>();
            outline = GetComponent<Outline>();

            if (outline == null)
            {
                outline = gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(3f, -3f);
                outline.useGraphicAlpha = false;
            }

            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (boxImage != null)
            {
                boxImage.color = selected ? SelectedColor : NormalColor;
            }

            if (outline != null)
            {
                outline.effectColor = selected
                    ? SelectedOutlineColor
                    : NormalOutlineColor;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!wasDragged)
            {
                onSelected?.Invoke(faceIndex);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            wasDragged = false;
            adjustView?.OnBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            wasDragged = true;
            adjustView?.OnDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            adjustView?.OnEndDrag(eventData);
        }

        public void OnScroll(PointerEventData eventData)
        {
            adjustView?.OnScroll(eventData);
        }
    }
}
