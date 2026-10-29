using AppPofo.Presentation.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.FaceRegistration
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class FaceRegistrationResponsiveLayout : MonoBehaviour
    {
        [SerializeField] private ResponsiveUIContext context;
        [SerializeField] private RectTransform body;
        [SerializeField] private AdaptiveLinearLayoutGroup bodyLayout;
        [SerializeField] private LayoutElement cameraSection;
        [SerializeField] private LayoutElement guideSection;
        [SerializeField] private RectTransform mainPanel;
        [SerializeField] private VerticalLayoutGroup panelLayout;
        [SerializeField] private VerticalLayoutGroup informationLayout;
        [SerializeField] private RectTransform introduction;
        [SerializeField] private LayoutElement header;
        [SerializeField] private RectTransform previewViewport = null;
        [SerializeField] private RectTransform displayFrame = null;
        [SerializeField] private RectTransform backButton = null;
        private Transform viewportParent, backButtonParent;
        private int viewportSiblingIndex, backButtonSiblingIndex;
        private Vector2 backAnchorMin, backAnchorMax, backPosition, backSize;
        private bool fullscreenRecognition;
        private CanvasGroup backButtonOpacity;
        private float registrationBackButtonAlpha;
        private bool previewLayoutCaptured;
        private Vector2 viewportOffsetMin, viewportOffsetMax, frameOffsetMin, frameOffsetMax;
        private AspectRatioFitter displayFrameFitter;
        private bool registrationFitterEnabled;
        private Vector2 viewportAnchorMin, viewportAnchorMax;
        private Image viewportClipImage;
        private Mask viewportClipMask;
        private bool registrationClipImageEnabled, registrationClipMaskEnabled;

        private void OnEnable()
        {
            if (context == null) return;
            context.Changed += ApplyLayout;
            context.Refresh();
            ApplyLayout();
        }
        private void OnDisable() { if (context != null) context.Changed -= ApplyLayout; }
        public void ApplyLayout()
        {
            bool recognition = UnityEngine.Application.isPlaying &&
                GetComponent<FaceRegistrationScreen>()?.IsRecognitionMode == true;
            // Existing open scenes may not yet contain the newly serialized reference.
            if (backButton == null && header != null)
                backButton = header.transform.Find("BackButton") as RectTransform;
            CapturePreviewLayout();
            ApplyRecognitionPresentation(recognition);
            if (recognition && fullscreenRecognition)
            {
                if (displayFrameFitter != null) displayFrameFitter.enabled = false;
                previewViewport.anchorMin = displayFrame.anchorMin = Vector2.zero;
                previewViewport.anchorMax = displayFrame.anchorMax = Vector2.one;
                previewViewport.offsetMin = previewViewport.offsetMax = Vector2.zero;
                displayFrame.offsetMin = displayFrame.offsetMax = Vector2.zero;
                if (viewportClipImage != null) viewportClipImage.enabled = false;
                if (viewportClipMask != null) viewportClipMask.enabled = false;
                GetComponent<FaceRegistrationScreen>()?.RefreshCameraSwitchPlacement();
                return;
            }
            if (!isActiveAndEnabled || context == null || body == null || mainPanel == null) return;
            Vector2 size = context.AvailableSize;
            // Limit tablet width while retaining the authored portrait composition.
            mainPanel.sizeDelta = new Vector2(
                Mathf.Min(1020, Mathf.Max(0, size.x - 60)),
                Mathf.Min(1700, Mathf.Max(0, size.y - 160)));
            mainPanel.GetComponent<Image>().preserveAspect = true;
            panelLayout.padding = new RectOffset(40, 40, 60, 70);
            panelLayout.spacing = 40;
            header.minHeight = header.preferredHeight = 120;
            header.preferredWidth = 876;
            header.flexibleWidth = 0;
            bodyLayout.Vertical = true;
            bodyLayout.spacing = 15;
            bodyLayout.childForceExpandHeight = false;
            informationLayout.spacing = 45;
            if (introduction.parent != body) introduction.SetParent(body, false);
            introduction.SetAsFirstSibling();
            cameraSection.minHeight = 440;
            cameraSection.preferredHeight = 760;
            cameraSection.flexibleHeight = 0;
            cameraSection.minWidth = 0;
            cameraSection.preferredWidth = 940;
            cameraSection.flexibleWidth = 0;
            guideSection.minWidth = 0;
            guideSection.preferredWidth = 940;
            guideSection.minHeight = guideSection.preferredHeight = 465;
            guideSection.flexibleHeight = 0;
            guideSection.flexibleWidth = 0;
            if (previewViewport != null && displayFrame != null)
            {
                if (displayFrameFitter != null)
                    displayFrameFitter.enabled = !recognition && registrationFitterEnabled;
                previewViewport.anchorMin = recognition ? Vector2.zero : viewportAnchorMin;
                previewViewport.anchorMax = recognition ? Vector2.one : viewportAnchorMax;
                previewViewport.offsetMin = recognition ? Vector2.zero : viewportOffsetMin;
                previewViewport.offsetMax = recognition ? Vector2.zero : viewportOffsetMax;
                if (viewportClipImage != null)
                    viewportClipImage.enabled = !recognition && registrationClipImageEnabled;
                if (viewportClipMask != null)
                    viewportClipMask.enabled = !recognition && registrationClipMaskEnabled;
                displayFrame.offsetMin = recognition ? Vector2.zero : frameOffsetMin;
                displayFrame.offsetMax = recognition ? Vector2.zero : frameOffsetMax;
            }
            LayoutRebuilder.MarkLayoutForRebuild(mainPanel);
            GetComponent<FaceRegistrationScreen>()?.RefreshCameraSwitchPlacement();
        }

        private void CapturePreviewLayout()
        {
            if (previewLayoutCaptured || previewViewport == null || displayFrame == null) return;
            viewportOffsetMin = previewViewport.offsetMin;
            viewportOffsetMax = previewViewport.offsetMax;
            viewportAnchorMin = previewViewport.anchorMin;
            viewportAnchorMax = previewViewport.anchorMax;
            viewportClipImage = previewViewport.GetComponent<Image>();
            viewportClipMask = previewViewport.GetComponent<Mask>();
            registrationClipImageEnabled = viewportClipImage != null && viewportClipImage.enabled;
            registrationClipMaskEnabled = viewportClipMask != null && viewportClipMask.enabled;
            frameOffsetMin = displayFrame.offsetMin;
            frameOffsetMax = displayFrame.offsetMax;
            displayFrameFitter = displayFrame.GetComponent<AspectRatioFitter>();
            registrationFitterEnabled = displayFrameFitter != null && displayFrameFitter.enabled;
            previewLayoutCaptured = true;
        }

        private void ApplyRecognitionPresentation(bool recognition)
        {
            if (previewViewport == null || displayFrame == null || mainPanel == null || backButton == null ||
                recognition == fullscreenRecognition) return;
            if (recognition)
            {
                viewportParent = previewViewport.parent;
                viewportSiblingIndex = previewViewport.GetSiblingIndex();
                backButtonParent = backButton.parent;
                backButtonSiblingIndex = backButton.GetSiblingIndex();
                backAnchorMin = backButton.anchorMin;
                backAnchorMax = backButton.anchorMax;
                backPosition = backButton.anchoredPosition;
                backSize = backButton.sizeDelta;
                backButtonOpacity = backButton.GetComponent<CanvasGroup>();
                if (backButtonOpacity == null)
                    backButtonOpacity = backButton.gameObject.AddComponent<CanvasGroup>();
                registrationBackButtonAlpha = backButtonOpacity.alpha;
                backButtonOpacity.alpha = .55f;
                // The preview fills the Canvas; navigation remains inside its safe area.
                previewViewport.SetParent(transform, false);
                // Unity draws later siblings on top: keep the camera above Background.
                previewViewport.SetAsLastSibling();
                mainPanel.parent.SetAsLastSibling();
                backButton.SetParent(mainPanel.parent, false);
                backButton.SetAsLastSibling();
                backButton.anchorMin = backButton.anchorMax = new Vector2(0, 1);
                backButton.sizeDelta = new Vector2(96, 96);
                backButton.anchoredPosition = new Vector2(72, -72);
                mainPanel.gameObject.SetActive(false);
            }
            else
            {
                previewViewport.SetParent(viewportParent, false);
                previewViewport.SetSiblingIndex(viewportSiblingIndex);
                backButton.SetParent(backButtonParent, false);
                backButton.SetSiblingIndex(backButtonSiblingIndex);
                backButton.anchorMin = backAnchorMin;
                backButton.anchorMax = backAnchorMax;
                backButton.anchoredPosition = backPosition;
                backButton.sizeDelta = backSize;
                if (backButtonOpacity != null) backButtonOpacity.alpha = registrationBackButtonAlpha;
                mainPanel.gameObject.SetActive(true);
            }
            fullscreenRecognition = recognition;
        }
    }
}
