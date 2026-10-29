using System;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.FaceRecognition
{
    [DisallowMultipleComponent]
    public sealed class FaceRecognitionOverlayView : MonoBehaviour
    {
        [SerializeField] private Button button = null;
        private Action<long> onSelected;
        private bool listenerRegistered;
        private bool enableDebugLogs;
        public long ProfileId { get; private set; }
        public bool HasRequiredReferences => button != null;

        // Bound once when created; recognition ticks only update its RectTransform.
        public void Bind(long profileId, Action<long> onSelected, bool enableDebugLogs = false)
        {
            ProfileId = profileId;
            this.onSelected = onSelected;
            this.enableDebugLogs = enableDebugLogs;
            if (button != null)
            {
                button.interactable = onSelected != null;
                if (button.targetGraphic != null)
                    button.targetGraphic.raycastTarget = onSelected != null;
            }
            if (!listenerRegistered && button != null)
            {
                button.onClick.AddListener(SelectProfile);
                listenerRegistered = true;
            }
        }

        private void SelectProfile()
        {
            if (enableDebugLogs)
                Debug.Log($"[FaceRecognition] Overlay clicked ProfileId={ProfileId}", this);
            onSelected?.Invoke(ProfileId);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(SelectProfile);
            onSelected = null;
        }
    }
}
