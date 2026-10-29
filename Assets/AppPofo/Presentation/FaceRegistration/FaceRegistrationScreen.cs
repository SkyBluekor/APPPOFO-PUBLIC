using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRecognition;
using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.Screens;

namespace AppPofo.Presentation.FaceRegistration
{
    [DisallowMultipleComponent]
    public sealed class FaceRegistrationScreen : MonoBehaviour
    {
        public const int RequiredSampleCount = 5;

        // MainPanel currently provides a live preview, guidance and navigation.
        // The old five-photo review UI is optional as a complete group; its
        // sample slots are not the three decorative pose steps in MainPanel.

        [Header("Navigation")]
        [SerializeField] private GameObject returnScreenRoot;

        [Header("Shared camera screen")]
        [SerializeField] private FaceRegistrationCamera registrationCamera;
        [SerializeField] private FaceRecognitionCamera recognitionCamera;
        [SerializeField] private BusinessCardPanelController businessCardPanel;
        [SerializeField] private GameObject registrationIntroduction;
        [SerializeField] private GameObject registrationGuideSection;
        [SerializeField] private GameObject registrationFooter = null;
        [SerializeField] private Text cameraScreenTitle;
        [SerializeField] private Text cameraScreenDescription;
        [SerializeField] private Text recognitionInstruction = null;
        private GameObject recognitionReturnScreen;
        private bool recognitionMode;
        private Button cameraSwitchButton;
        private CameraSwitchButtonArtwork cameraSwitchArtwork;
        private RectTransform cameraSwitchSafeArea;
        private RectTransform registrationPreviewViewport;
        private GameObject cameraSwitchConfirmation;
        private bool cameraSwitching;
        private CameraFacing preferredFaceCamera = CameraFacing.Front;
        private Coroutine cameraSwitchWait;
        private GameObject registrationReturnScreen;
        private Action<long, bool> registrationReturned;
        private Action<string, bool> cloudRegistrationReturned;
        public string CurrentCloudProfileId { get; private set; }
        public bool IsCloudEnrollment => !string.IsNullOrEmpty(CurrentCloudProfileId);
        public bool HasEnrollmentTarget => CurrentProfileId > 0 || IsCloudEnrollment;
        public bool IsRecognitionMode => recognitionMode;

        [Header("Header")]
        [SerializeField] private Text progressText;

        [Header("Preview")]
        [SerializeField] private RawImage adjustableImage;
        [SerializeField] private RawImage cameraPreview;
        private Material rearPreviewMaterial;
        private Material originalPreviewMaterial;
        private bool missingRearPreviewShaderLogged;
        [SerializeField] private RectTransform faceOverlayLayer;
        [SerializeField] private GameObject faceGuide;
        [SerializeField] private Text emptyText;
        [SerializeField] private FaceImageAdjustView imageAdjustView;

        [Header("Status")]
        [SerializeField] private GameObject statusPanel;
        [SerializeField] private Text statusText;
        private Sprite instructionBackground;

        [Header("Progress")]
        [SerializeField] private FaceSampleSlotView[] sampleSlots;

        [Header("Live registration progress (Front / Left / Right)")]
        [SerializeField] private Image[] registrationStepCircles;
        [SerializeField] private Text[] registrationStepNumbers;
        [SerializeField] private Sprite currentStepSprite;
        [SerializeField] private Sprite pendingStepSprite;
        [SerializeField] private Color completedStepColor = new Color(0.13725491f, 0.9254902f, 1f, 1f);

        // Read the camera's committed step directly; the optional five-photo review
        // state and sample count do not describe the live registration pipeline.
        public void RefreshRegistrationProgressUi(FaceRegistrationCamera camera)
        {
            if (recognitionMode || HasPhotoReviewUI || camera == null) return;

            FaceRegistrationStep step = camera.CurrentStep;
            bool complete = step == FaceRegistrationStep.Complete;
            string instruction = step switch
            {
                FaceRegistrationStep.Left => "고개를 왼쪽으로 돌려 주세요",
                FaceRegistrationStep.Right => "고개를 오른쪽으로 돌려 주세요",
                FaceRegistrationStep.Complete => IsCloudEnrollment &&
                    CloudFaceLocalTestFactory.IsLocalCaptureEnabled
                        ? "로컬 테스트 서버 등록 완료 (운영 서버 미반영)"
                        : "얼굴 등록이 완료되었습니다.",
                _ => "정면을 봐주세요"
            };
            if (camera.IsFinalizingRegistration)
                instruction = "얼굴 등록을 마무리하고 있습니다.";
            else if (!complete && camera.RegistrationFeedbackStep == step &&
                !string.IsNullOrEmpty(camera.RegistrationFeedback))
                instruction = camera.RegistrationFeedback;
            if (statusText != null)
                FitRegistrationInstruction(instruction,
                    complete || camera.IsFinalizingRegistration ? string.Empty :
                    $"({camera.ValidCandidateCount}/{camera.RequiredCandidatesPerStep})");

            if (registrationStepCircles == null) return;
            for (int i = 0; i < registrationStepCircles.Length; i++)
            {
                bool completed = complete || i < (int)step;
                bool current = !complete && i == (int)step;
                Image circle = registrationStepCircles[i];
                if (circle != null)
                {
                    circle.sprite = completed || current ? currentStepSprite : pendingStepSprite;
                    circle.color = completed ? completedStepColor : Color.white;
                }
                if (registrationStepNumbers != null && i < registrationStepNumbers.Length &&
                    registrationStepNumbers[i] != null)
                {
                    registrationStepNumbers[i].color = current
                        ? new Color(0.94f, 0.98f, 1f, 1f)
                        : new Color(0.16f, 0.25f, 0.35f, 1f);
                }
            }
        }

        [Header("Actions")]
        [SerializeField] private GameObject sourceButtons;
        [SerializeField] private Button galleryButton;
        [SerializeField] private Button cameraButton;
        private void FitRegistrationInstruction(string instruction, string progress)
        {
            if (statusPanel == null || !(statusPanel.transform is RectTransform chip) ||
                !(chip.parent is RectTransform parent)) return;
            const float textLeft = 84;
            const float textRight = 24;
            float available = Mathf.Max(1, parent.rect.width - 48);
            statusText.fontSize = 24;
            statusText.resizeTextForBestFit = false;
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusText.verticalOverflow = VerticalWrapMode.Truncate;
            string fullText = string.IsNullOrEmpty(progress) ? instruction : instruction + " " + progress;
            statusText.text = fullText;
            float naturalWidth = Mathf.Ceil(statusText.preferredWidth) + textLeft + textRight;
            float width = Mathf.Min(available, naturalWidth);
            bool twoLines = naturalWidth > available && !string.IsNullOrEmpty(progress);
            if (twoLines) statusText.text = instruction + "\n" + progress;
            statusText.rectTransform.anchorMin = Vector2.zero;
            statusText.rectTransform.anchorMax = Vector2.one;
            statusText.rectTransform.offsetMin = new Vector2(textLeft, 12);
            statusText.rectTransform.offsetMax = new Vector2(-textRight, -12);
            chip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            float height = Mathf.Max(twoLines ? 96 : 64, Mathf.Ceil(statusText.preferredHeight) + 24);
            chip.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            LayoutElement element = chip.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.preferredWidth = width;
                element.flexibleWidth = 0;
                element.minHeight = element.preferredHeight = height;
            }
            RectTransform icon = chip.Find("FaceScanIcon") as RectTransform;
            if (icon != null)
            {
                icon.anchorMin = icon.anchorMax = new Vector2(0, .5f);
                icon.anchoredPosition = new Vector2(44, 0);
                icon.sizeDelta = new Vector2(40, 40);
            }
            Image background = chip.GetComponent<Image>();
            if (background == null || background.sprite == null) return;
            if (instructionBackground == null)
            {
                Sprite source = background.sprite;
                float sx = source.rect.width / 2121f;
                float sy = source.rect.height / 675f;
                // Reuse the visible body of the existing instruction artwork;
                // its imported sprite also contains transparent margins.
                Rect visible = new Rect(source.rect.x + 84 * sx, source.rect.y + 116 * sy,
                    2005 * sx, 465 * sy);
                instructionBackground = Sprite.Create(source.texture, visible, new Vector2(.5f, .5f),
                    source.pixelsPerUnit, 0, SpriteMeshType.FullRect,
                    new Vector4(230 * sx, 230 * sy, 230 * sx, 230 * sy));
                instructionBackground.hideFlags = HideFlags.DontSave;
            }
            background.sprite = instructionBackground;
            background.type = Image.Type.Sliced;
            background.preserveAspect = false;
            background.pixelsPerUnitMultiplier = instructionBackground.rect.height / height;
        }

        [SerializeField] private Text cameraButtonLabel;
        [SerializeField] private GameObject reviewActions;
        [SerializeField] private Button autoFitButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button usePhotoButton;
        [SerializeField] private Button completeButton;

        [Header("Platform input")]
        [SerializeField] private FaceRegistrationImageSourceAdapter imageSource;

        private readonly List<Texture2D> sampleTextures =
            new List<Texture2D>(RequiredSampleCount);
        private readonly List<FaceSelectionBoxView> faceBoxes =
            new List<FaceSelectionBoxView>();

        private Texture2D candidateTexture;
        private FaceRegistrationViewState currentState;
        private string statusMessage = string.Empty;
        private int selectedFaceIndex = -1;
        private int sessionVersion;
        private bool candidateApproved;

        public event Action<Texture2D> AnalysisRequested;

        public long CurrentProfileId { get; private set; }
        public int SampleCount => sampleTextures.Count;
        public int SelectedFaceIndex => selectedFaceIndex;
        public Texture2D CandidateTexture => candidateTexture;
        public FaceRegistrationViewState CurrentState => currentState;

        public bool HasPhotoReviewUI =>
            progressText != null || adjustableImage != null ||
            faceOverlayLayer != null || emptyText != null || imageAdjustView != null ||
            sourceButtons != null || galleryButton != null || cameraButton != null ||
            cameraButtonLabel != null || reviewActions != null || autoFitButton != null ||
            retryButton != null || usePhotoButton != null || completeButton != null ||
            (sampleSlots != null && sampleSlots.Length > 0);

        private void ValidateReferences()
        {
            RequireReference(returnScreenRoot, nameof(returnScreenRoot));
            RequireReference(cameraPreview, nameof(cameraPreview));
            RequireReference(faceGuide, nameof(faceGuide));
            RequireReference(statusPanel, nameof(statusPanel));
            RequireReference(statusText, nameof(statusText));
            RequireReference(imageSource, nameof(imageSource));

            if (!HasPhotoReviewUI) return;

            RequireReference(progressText, nameof(progressText));
            RequireReference(adjustableImage, nameof(adjustableImage));
            RequireReference(faceOverlayLayer, nameof(faceOverlayLayer));
            RequireReference(emptyText, nameof(emptyText));
            RequireReference(imageAdjustView, nameof(imageAdjustView));
            RequireReference(sourceButtons, nameof(sourceButtons));
            RequireReference(galleryButton, nameof(galleryButton));
            RequireReference(cameraButton, nameof(cameraButton));
            RequireReference(reviewActions, nameof(reviewActions));
            RequireReference(autoFitButton, nameof(autoFitButton));
            RequireReference(retryButton, nameof(retryButton));
            RequireReference(usePhotoButton, nameof(usePhotoButton));
            RequireReference(completeButton, nameof(completeButton));
            if (sampleSlots == null || sampleSlots.Length != RequiredSampleCount)
                throw new InvalidOperationException("FaceRegistrationScreen.sampleSlots requires five photo slots.");
            foreach (FaceSampleSlotView slot in sampleSlots)
                RequireReference(slot, nameof(sampleSlots));
        }

        private static void RequireReference(UnityEngine.Object reference, string fieldName)
        {
            if (reference == null)
                throw new InvalidOperationException($"FaceRegistrationScreen.{fieldName} is not connected.");
        }

        private void RequirePhotoReviewUI()
        {
            ValidateReferences();
            if (!HasPhotoReviewUI)
                throw new InvalidOperationException("The current MainPanel does not provide the optional photo review UI.");
        }

        private void Update()
        {
            UpdateRearPreviewExposure();
            // On the first permission grant WebCamTexture.devices may become available
            // only after Show; expose the switch once the actual camera is ready.
            if (cameraSwitchButton == null && imageSource != null && imageSource.IsCameraReady)
                EnsureCameraSwitchUi();
            if (!HasPhotoReviewUI || currentState != FaceRegistrationViewState.Analyzing ||
                cameraPreview == null ||
                cameraPreview.texture == null ||
                imageSource == null ||
                !imageSource.IsCameraReady ||
                cameraButton.interactable)
            {
                return;
            }

            SetStatus("카메라 준비 완료. 촬영 버튼을 눌러주세요.");
            ApplyState(FaceRegistrationViewState.Analyzing);
        }

        public void Show(long profileId)
        {
            if (profileId <= 0)
            {
                Debug.LogError("얼굴 등록을 열려면 유효한 ProfileId가 필요합니다.");
                return;
            }
            OpenEnrollment(profileId, null);
        }

        public void Show(long profileId, GameObject previousScreen, Action<long, bool> onReturned)
        {
            if (previousScreen == null) throw new ArgumentNullException(nameof(previousScreen));
            Show(profileId);
            registrationReturnScreen = previousScreen;
            registrationReturned = onReturned;
            previousScreen.SetActive(false);
        }

        // Distinct target entry point: server UUIDs must never be converted to
        // local numeric IDs or cause any SQLite face records to be created.
        // Keep the five-argument reflection contract used by the local Editor tool.
        public void ShowCloud(string cloudProfileId, GameObject previousScreen,
            Action<string, bool> onReturned, bool publicRecognitionConsent = false,
            bool subjectPresent = false) => ShowCloud(cloudProfileId, previousScreen,
                onReturned, publicRecognitionConsent, subjectPresent, false);

        public void ShowCloud(string cloudProfileId, GameObject previousScreen,
            Action<string, bool> onReturned, bool publicRecognitionConsent,
            bool subjectPresent, bool storageConsent, CloudFaceStatusData approvedSnapshot = null)
        {
            if (!Guid.TryParse(cloudProfileId, out _))
                throw new ArgumentException("서버 프로필 UUID가 올바르지 않습니다.", nameof(cloudProfileId));
            if (previousScreen == null) throw new ArgumentNullException(nameof(previousScreen));
            if (HasPhotoReviewUI)
                throw new InvalidOperationException("서버 얼굴 등록은 실시간 카메라 화면에서만 지원합니다.");
            OpenEnrollment(0, cloudProfileId, publicRecognitionConsent, subjectPresent, storageConsent, approvedSnapshot);
            registrationReturnScreen = previousScreen;
            cloudRegistrationReturned = onReturned;
            previousScreen.SetActive(false);
        }

        private void OpenEnrollment(long localProfileId, string cloudProfileId,
            bool publicRecognitionConsent = false, bool subjectPresent = false, bool storageConsent = false,
            CloudFaceStatusData approvedSnapshot = null)
        {
            ValidateReferences();
            registrationReturnScreen = null;
            registrationReturned = null;
            cloudRegistrationReturned = null;
            ResetSession();
            if (recognitionCamera != null) recognitionCamera.enabled = false;
            // Keep the existing camera ownership/facing behaviour in both modes.
            imageSource?.TrySelectFacing(preferredFaceCamera);
            recognitionMode = false;
            var stopScan = (transform.Find("SafeAreaRoot/StopCloudFaceScan") ?? transform.Find("StopCloudFaceScan"));
            if (stopScan != null) stopScan.gameObject.SetActive(false);
            recognitionReturnScreen = null;
            ApplyCameraModeUi();
            CurrentProfileId = localProfileId;
            CurrentCloudProfileId = cloudProfileId;
            if (registrationCamera != null)
            {
                if (cloudProfileId == null)
                    registrationCamera.ConfigureLocalEnrollment(localProfileId);
                else
                    registrationCamera.ConfigureEnrollmentWriter(
                        AppPofo.Presentation.Common.CloudFaceRuntime.Available && storageConsent && subjectPresent && approvedSnapshot != null
                            ? new CloudFaceEnrollmentWriter(cloudProfileId,
                                AppPofo.Presentation.Common.CloudFaceRuntime.Config.enrollment_policy,
                                publicRecognitionConsent, subjectPresent, new CloudFaceHttpsTransport(approvedSnapshot, cloudProfileId))
                            : (AppPofo.Presentation.Common.CloudServerConfiguration.IsStaging ||
                                (AppPofo.Presentation.Common.CloudServerConfiguration.IsProduction &&
                                 AppPofo.Presentation.Common.CloudServerConfiguration.RemoteFacesEnabled))
                                ? new PendingCloudFaceEnrollmentWriter(cloudProfileId)
                                : CloudFaceLocalTestFactory.CreateOrPending(cloudProfileId,
                                publicRecognitionConsent, subjectPresent));
            }

            if (returnScreenRoot != null) returnScreenRoot.SetActive(false);
            bool wasActive = gameObject.activeInHierarchy;
            bool registrationWasEnabled = registrationCamera != null && registrationCamera.enabled;
            if (registrationCamera != null) registrationCamera.enabled = true;
            gameObject.SetActive(true);
            EnsureCameraSwitchUi();
            if (wasActive && !HasPhotoReviewUI && registrationWasEnabled)
                GetComponentInChildren<FaceRegistrationCamera>(true)?.RestartSession();
            ApplyState(FaceRegistrationViewState.Empty);
        }

        public void ShowRecognition(GameObject previousScreen)
        {
            RequireReference(registrationCamera, nameof(registrationCamera));
            RequireReference(recognitionCamera, nameof(recognitionCamera));
            RequireReference(businessCardPanel, nameof(businessCardPanel));
            RequireReference(cameraPreview, nameof(cameraPreview));
            RequireReference(statusText, nameof(statusText));
            RequireReference(previousScreen, nameof(previousScreen));
            RequireReference(recognitionInstruction, nameof(recognitionInstruction));

            // Release the previous owner before enabling the next owner, even
            // when switching modes while this screen is already active.
            registrationCamera.enabled = false;
            recognitionCamera.enabled = false;
            imageSource?.TrySelectFacing(preferredFaceCamera);
            recognitionMode = true;
            if (cameraSwitchConfirmation != null) cameraSwitchConfirmation.SetActive(false);
            recognitionReturnScreen = previousScreen;
            CurrentProfileId = 0;
            CurrentCloudProfileId = null;
            cloudRegistrationReturned = null;
            registrationCamera.ConfigureEnrollmentWriter(null);
            ApplyCameraModeUi();
            var stopScan = (transform.Find("SafeAreaRoot/StopCloudFaceScan") ?? transform.Find("StopCloudFaceScan"));
            if (stopScan != null) stopScan.gameObject.SetActive(false);
            businessCardPanel.Close();
            previousScreen.SetActive(false);
            gameObject.SetActive(true);
            EnsureCameraSwitchUi();
            recognitionCamera.enabled = true;
            if (AppPofo.Presentation.Common.CloudFaceRuntime.Available)
            {
                var consent = GetComponent<CloudFaceConsentPanel>() ?? gameObject.AddComponent<CloudFaceConsentPanel>();
                consent.AddScanStopControl();
                (transform.Find("SafeAreaRoot/StopCloudFaceScan") ?? transform.Find("StopCloudFaceScan")).gameObject.SetActive(true);
            }
        }


        // The same camera-switch control is shared by registration and fullscreen
        // recognition. It is always rendered above the live preview (SafeAreaRoot).
        private void EnsureCameraSwitchUi()
        {
            RectTransform safeArea = transform.Find("SafeAreaRoot") as RectTransform;
            if (safeArea == null || imageSource == null || !imageSource.SupportsRearCamera)
            {
                if (cameraSwitchButton != null) cameraSwitchButton.gameObject.SetActive(false);
                return;
            }
            cameraSwitchSafeArea = safeArea;
            if (registrationPreviewViewport == null)
                registrationPreviewViewport = transform.Find(
                    "SafeAreaRoot/MainPanel/Body/CameraSection/CameraContainer/PreviewViewport")
                    as RectTransform;

            if (cameraSwitchButton == null)
            {
                var host = new GameObject("CameraSwitchButton",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                host.layer = safeArea.gameObject.layer;
                host.transform.SetParent(safeArea, false);
                cameraSwitchButton = host.GetComponent<Button>();
                cameraSwitchArtwork = host.AddComponent<CameraSwitchButtonArtwork>();
                cameraSwitchButton.targetGraphic = host.GetComponent<Image>();
                cameraSwitchButton.onClick.AddListener(RequestCameraSwitch);
            }

            cameraSwitchButton.gameObject.SetActive(true);
            cameraSwitchArtwork?.SetMode(recognitionMode);
            cameraSwitchButton.transform.SetAsLastSibling();
            UpdateCameraSwitchLabel();
            RefreshCameraSwitchPlacement();
        }

        // Keep the original method name for the existing camera lifecycle calls.
        // Camera-facing state is represented by arrows, never a two-line label.
        private void UpdateCameraSwitchLabel()
        {
            if (cameraSwitching || imageSource == null) return;
            cameraSwitchArtwork?.SetFacing(imageSource.CurrentFacing == CameraFacing.Rear);
        }

        private void LateUpdate()
        {
            if (cameraSwitchButton != null && cameraSwitchButton.gameObject.activeInHierarchy)
                RefreshCameraSwitchPlacement();
        }

        /// <summary>
        /// Position from measured UI bounds, not a guessed resolution-dependent offset.
        /// Registration: outside the preview near its lower-right edge.
        /// Recognition: small overlay near the SafeArea bottom-right corner.
        /// </summary>
        public void RefreshCameraSwitchPlacement()
        {
            if (cameraSwitchButton == null || !cameraSwitchButton.gameObject.activeSelf)
                return;
            RectTransform safe = cameraSwitchSafeArea;
            RectTransform target = cameraSwitchButton.transform as RectTransform;
            if (safe == null || target == null || safe.rect.width <= 0f ||
                safe.rect.height <= 0f) return;

            const float margin = 20f;
            float size = recognitionMode ? 80f : 96f;
            Vector2 center;
            if (recognitionMode)
            {
                // Recognition intentionally overlays the full-screen camera image.
                float half = size * .5f;
                center = new Vector2(safe.rect.xMax - half - margin,
                    safe.rect.yMin + half + margin);
            }
            else
            {
                // The rendered RawImage may extend past its PreviewViewport due to
                // AspectRatioFitter. Position against the SAME image bounds that
                // the PlayMode visibility regression measures.
                RectTransform preview = cameraPreview != null &&
                    cameraPreview.gameObject.activeInHierarchy
                    ? cameraPreview.rectTransform : registrationPreviewViewport;
                if (preview == null || !preview.gameObject.activeInHierarchy) return;
                Rect imageBounds = RectInLocalSpace(safe, preview);
                const float gap = 12f;
                // Retain the authored 96-unit hit target whenever possible.
                // Small viewports can use the 80/72-unit fallback, still keeping
                // the complete button outside the live image.
                if (!TryPositionOutsidePreview(safe.rect, imageBounds, 96f,
                        margin, gap, out center))
                {
                    if (TryPositionOutsidePreview(safe.rect, imageBounds, 80f,
                            margin, gap, out center)) size = 80f;
                    else if (TryPositionOutsidePreview(safe.rect, imageBounds, 72f,
                            margin, gap, out center)) size = 72f;
                    else
                    {
                        // If no non-overlapping position physically fits, keep a
                        // reachable control and emit geometry for a real layout fix.
                        float half = size * .5f;
                        center = new Vector2(safe.rect.xMax - half - margin,
                            safe.rect.yMax - half - margin);
                    }
                }
            }

            Vector2 anchored = center - safe.rect.min;
            if (target.anchorMin != Vector2.zero || target.anchorMax != Vector2.zero)
            {
                target.anchorMin = target.anchorMax = Vector2.zero;
                target.pivot = new Vector2(.5f, .5f);
            }
            Vector2 desiredSize = new Vector2(size, size);
            if ((target.sizeDelta - desiredSize).sqrMagnitude > .01f)
                target.sizeDelta = desiredSize;
            if ((target.anchoredPosition - anchored).sqrMagnitude > .25f)
                target.anchoredPosition = anchored;
            cameraSwitchArtwork?.SetMode(recognitionMode);
        }

        private static bool TryPositionOutsidePreview(Rect safe, Rect preview,
            float size, float margin, float gap, out Vector2 center)
        {
            float half = size * .5f;
            float left = safe.xMin + margin + half;
            float right = safe.xMax - margin - half;
            float bottom = safe.yMin + margin + half;
            float top = safe.yMax - margin - half;
            center = default;
            if (right < left || top < bottom) return false;

            // Prefer below / right / above / left, and test the final button
            // rectangle, not only its center against the image rectangle.
            Vector2 candidate = new Vector2(
                Mathf.Clamp(preview.xMax - half, left, right),
                preview.yMin - half - gap);
            if (FitsOutsidePreview(candidate, half, safe, preview, margin))
            {
                center = candidate;
                return true;
            }
            candidate = new Vector2(
                preview.xMax + half + gap,
                Mathf.Clamp(preview.yMin + half, bottom, top));
            if (FitsOutsidePreview(candidate, half, safe, preview, margin))
            {
                center = candidate;
                return true;
            }
            candidate = new Vector2(
                Mathf.Clamp(preview.xMax - half, left, right),
                preview.yMax + half + gap);
            if (FitsOutsidePreview(candidate, half, safe, preview, margin))
            {
                center = candidate;
                return true;
            }
            candidate = new Vector2(
                preview.xMin - half - gap,
                Mathf.Clamp(preview.yMin + half, bottom, top));
            if (FitsOutsidePreview(candidate, half, safe, preview, margin))
            {
                center = candidate;
                return true;
            }

            // A corner can be free even when none of the preferred strips has
            // enough space. Do not choose a corner without checking overlap.
            candidate = new Vector2(right, top);
            if (FitsOutsidePreview(candidate, half, safe, preview, margin))
            {
                center = candidate;
                return true;
            }
            candidate = new Vector2(right, bottom);
            if (FitsOutsidePreview(candidate, half, safe, preview, margin))
            {
                center = candidate;
                return true;
            }
            candidate = new Vector2(left, top);
            if (FitsOutsidePreview(candidate, half, safe, preview, margin))
            {
                center = candidate;
                return true;
            }
            candidate = new Vector2(left, bottom);
            if (FitsOutsidePreview(candidate, half, safe, preview, margin))
            {
                center = candidate;
                return true;
            }
            return false;
        }

        private static bool FitsOutsidePreview(Vector2 center, float half,
            Rect safe, Rect preview, float margin)
        {
            Rect button = Rect.MinMaxRect(center.x - half, center.y - half,
                center.x + half, center.y + half);
            return button.xMin >= safe.xMin + margin &&
                   button.xMax <= safe.xMax - margin &&
                   button.yMin >= safe.yMin + margin &&
                   button.yMax <= safe.yMax - margin &&
                   !button.Overlaps(preview);
        }

        private static Rect RectInLocalSpace(RectTransform relativeTo, RectTransform target)
        {
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 local = relativeTo.InverseTransformPoint(corners[i]);
                min = Vector2.Min(min, new Vector2(local.x, local.y));
                max = Vector2.Max(max, new Vector2(local.x, local.y));
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Text CreateCameraOverlayText(Transform parent, string value, int size,
            TextAnchor anchor)
        {
            var label = new GameObject("Label",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(parent, false);
            var text = label.GetComponent<Text>();
            text.font = statusText != null ? statusText.font : null;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.text = value;
            return text;
        }

        private void RequestCameraSwitch()
        {
            if (cameraSwitching || imageSource == null || !imageSource.SupportsRearCamera)
                return;
            if (recognitionMode)
            {
                ExecuteCameraSwitch();
                return;
            }
            ShowCameraSwitchConfirmation();
        }

        private void ShowCameraSwitchConfirmation()
        {
            if (cameraSwitchConfirmation == null)
            {
                RectTransform safe = transform.Find("SafeAreaRoot") as RectTransform;
                if (safe == null) return;
                var curtain = new GameObject("CameraSwitchConfirmation",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                curtain.transform.SetParent(safe, false);
                var fill = (RectTransform)curtain.transform;
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = Vector2.one;
                fill.offsetMin = fill.offsetMax = Vector2.zero;
                curtain.GetComponent<Image>().color = new Color(0, 0, 0, .70f);
                var box = new GameObject("ConfirmationCard",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                box.transform.SetParent(curtain.transform, false);
                var card = (RectTransform)box.transform;
                card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
                card.sizeDelta = new Vector2(
                    Mathf.Min(620f, Mathf.Max(280f, safe.rect.width - 28f)), 310f);
                box.GetComponent<Image>().color = new Color(.11f, .17f, .25f, 1);
                Text msg = CreateCameraOverlayText(box.transform,
                    "카메라를 바꾸면 현재 얼굴 등록이\n처음부터 다시 시작됩니다.",
                    27, TextAnchor.MiddleCenter);
                msg.rectTransform.anchorMin = new Vector2(.05f, .40f);
                msg.rectTransform.anchorMax = new Vector2(.95f, .95f);
                msg.rectTransform.offsetMin = msg.rectTransform.offsetMax = Vector2.zero;
                CreateCameraDialogButton(card, "취소", -150, () =>
                    cameraSwitchConfirmation.SetActive(false));
                CreateCameraDialogButton(card, "전환", 150, () =>
                {
                    cameraSwitchConfirmation.SetActive(false);
                    ExecuteCameraSwitch();
                });
                cameraSwitchConfirmation = curtain;
            }
            cameraSwitchConfirmation.SetActive(true);
            cameraSwitchConfirmation.transform.SetAsLastSibling();
        }

        private void CreateCameraDialogButton(RectTransform card, string title, float x,
            UnityEngine.Events.UnityAction action)
        {
            var obj = new GameObject(title + "Button",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            obj.transform.SetParent(card, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x < 0 ? -card.rect.width * .245f :
                card.rect.width * .245f, 73);
            rect.sizeDelta = new Vector2(Mathf.Min(240f, card.rect.width * .45f), 90);
            Image image = obj.GetComponent<Image>();
            image.color = new Color(.19f, .35f, .52f, 1f);
            Button button = obj.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            Text label = CreateCameraOverlayText(rect, title, 30, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.raycastTarget = false;
        }

        private void ExecuteCameraSwitch()
        {
            if (cameraSwitching || imageSource == null) return;
            CameraFacing previous = imageSource.CurrentFacing;
            CameraFacing next = previous == CameraFacing.Front
                ? CameraFacing.Rear : CameraFacing.Front;
            cameraSwitching = true;
            if (cameraSwitchButton != null) cameraSwitchButton.interactable = false;
            cameraSwitchArtwork?.SetBusy();
            if (recognitionMode)
                recognitionCamera.enabled = false;
            else
                registrationCamera.enabled = false;

            if (imageSource.TrySelectFacing(next))
                preferredFaceCamera = next;
            else
            {
                // The old stream has been released, so it can be opened again.
                imageSource.TrySelectFacing(previous);
                Debug.LogWarning("[S4-13] 카메라 전환 실패: 대상 장치를 사용할 수 없습니다.", this);
            }
            // WebCamTexture.Destroy is deferred to the end of the render frame.
            // Do not open the other physical device until the old one is released.
            cameraSwitchWait = StartCoroutine(RestartCameraAfterRelease(previous));
        }

        private IEnumerator RestartCameraAfterRelease(CameraFacing previous)
        {
            yield return null;
            if (!isActiveAndEnabled) yield break;
            if (recognitionMode)
                recognitionCamera.enabled = true;
            else
                registrationCamera.enabled = true;
            yield return WaitForCameraSwitch(previous);
        }

        private IEnumerator WaitForCameraSwitch(CameraFacing previous)
        {
            float until = Time.unscaledTime + 11f;
            while (isActiveAndEnabled && imageSource != null && !imageSource.IsCameraReady &&
                   Time.unscaledTime < until)
                yield return null;
            if (!isActiveAndEnabled || imageSource == null) yield break;
            if (!imageSource.IsCameraReady)
            {
                Debug.LogWarning("[S4-13] 카메라 전환 지연, 이전 렌즈로 한 번 복구합니다.", this);
                if (recognitionMode)
                    recognitionCamera.enabled = false;
                else
                    registrationCamera.enabled = false;
                if (imageSource.TrySelectFacing(previous))
                {
                    preferredFaceCamera = previous;
                    yield return null; // Let the failed lens release its native handle.
                    if (!isActiveAndEnabled) yield break;
                    if (recognitionMode)
                        recognitionCamera.enabled = true;
                    else
                        registrationCamera.enabled = true;
                    until = Time.unscaledTime + 11f;
                    while (isActiveAndEnabled && !imageSource.IsCameraReady &&
                           Time.unscaledTime < until)
                        yield return null;
                }
            }
            bool recovered = imageSource != null && imageSource.IsCameraReady;
            if (!recovered)
                Debug.LogError("[S4-13] 카메라를 연결하지 못했습니다. 화면을 다시 열어주세요.", this);
            cameraSwitching = false;
            cameraSwitchWait = null;
            if (cameraSwitchButton != null) cameraSwitchButton.interactable = true;
            if (recovered) UpdateCameraSwitchLabel();
            else cameraSwitchArtwork?.SetFailed();
        }

        private void ApplyCameraModeUi()
        {
            if (HasPhotoReviewUI) return;
            if (registrationIntroduction != null) registrationIntroduction.SetActive(!recognitionMode);
            if (registrationGuideSection != null) registrationGuideSection.SetActive(!recognitionMode);
            if (registrationFooter != null) registrationFooter.SetActive(!recognitionMode);
            if (faceGuide != null) faceGuide.SetActive(!recognitionMode);
            if (statusPanel != null) statusPanel.SetActive(!recognitionMode);
            if (recognitionInstruction != null) recognitionInstruction.gameObject.SetActive(recognitionMode);
            if (recognitionMode && recognitionInstruction != null && cameraScreenTitle != null)
                recognitionInstruction.color = cameraScreenTitle.color;
            if (cameraScreenTitle != null)
                cameraScreenTitle.text = recognitionMode ? "카메라로 명함 보기" : "얼굴 등록";
            if (cameraScreenDescription != null)
                cameraScreenDescription.text = recognitionMode
                    ? "카메라에 얼굴을 비춰 명함을 확인하세요.\n인식된 얼굴 아래에 명함이 자동으로 표시됩니다."
                    : "안내에 따라 얼굴 방향을 천천히 움직여주세요.";
            if (cameraPreview != null) cameraPreview.gameObject.SetActive(true);
            if (recognitionMode && cameraPreview != null) cameraPreview.color = Color.white;
            if (recognitionMode && statusText != null)
                statusText.text = "카메라에 얼굴을 비춰 명함을 확인하세요.";
            GetComponent<FaceRegistrationResponsiveLayout>()?.ApplyLayout();
            if (cameraSwitchButton != null)
                cameraSwitchButton.gameObject.SetActive(imageSource != null &&
                    imageSource.SupportsRearCamera);
        }

        public void Hide()
        {
            GameObject destination = recognitionMode ? recognitionReturnScreen : registrationReturnScreen != null
                ? registrationReturnScreen : returnScreenRoot;
            long returnedProfileId = CurrentProfileId;
            string returnedCloudId = CurrentCloudProfileId;
            bool completed = registrationCamera != null && registrationCamera.CurrentStep == FaceRegistrationStep.Complete;
            Action<long, bool> onReturned = recognitionMode ? null : registrationReturned;
            Action<string, bool> onCloudReturned = recognitionMode ? null : cloudRegistrationReturned;
            registrationReturnScreen = null;
            registrationReturned = null;
            cloudRegistrationReturned = null;
            if (recognitionCamera != null) recognitionCamera.enabled = false;
            if (recognitionMode && businessCardPanel != null) businessCardPanel.Close();
            ResetSession();
            // Revert the shared transport for the existing avatar photo workflow.
            imageSource?.TrySelectFacing(CameraFacing.Front);
            CurrentProfileId = 0;
            CurrentCloudProfileId = null;
            registrationCamera?.ConfigureEnrollmentWriter(null);
            if (cameraSwitchConfirmation != null) cameraSwitchConfirmation.SetActive(false);
            if (cameraSwitchWait != null) StopCoroutine(cameraSwitchWait);
            cameraSwitchWait = null;
            cameraSwitching = false;
            gameObject.SetActive(false);
            recognitionMode = false;
            recognitionReturnScreen = null;
            ApplyCameraModeUi();
            if (registrationCamera != null) registrationCamera.enabled = true;

            if (destination != null)
            {
                destination.SetActive(true);
            }
            onReturned?.Invoke(returnedProfileId, completed);
            if (!string.IsNullOrEmpty(returnedCloudId))
                onCloudReturned?.Invoke(returnedCloudId, completed);
        }

        public void SelectFromGallery()
        {
            RequirePhotoReviewUI();
            if (CurrentProfileId <= 0 || currentState == FaceRegistrationViewState.Analyzing)
            {
                return;
            }

            BeginNewCandidate();
            int requestVersion = sessionVersion;
            SetStatus("얼굴을 확인하고 있습니다...");
            ApplyState(FaceRegistrationViewState.Analyzing);

            imageSource.PickImage(
                bytes => ReceiveImageBytes(bytes, requestVersion),
                message => HandleInputFailure(message, requestVersion));
        }

        public void CaptureFromCamera()
        {
            RequirePhotoReviewUI();
            if (CurrentProfileId <= 0)
            {
                return;
            }

            bool canCaptureCurrentPreview =
                currentState == FaceRegistrationViewState.Analyzing &&
                cameraPreview != null &&
                cameraPreview.texture != null &&
                imageSource != null &&
                imageSource.IsCameraReady;

            if (canCaptureCurrentPreview)
            {
                int captureVersion = sessionVersion;
                SetStatus("촬영한 사진을 준비하고 있습니다...");
                cameraButton.interactable = false;
                imageSource.CaptureImage(
                    bytes => ReceiveImageBytes(bytes, captureVersion),
                    message => HandleInputFailure(message, captureVersion));
                return;
            }

            if (currentState != FaceRegistrationViewState.Empty)
            {
                return;
            }

            BeginNewCandidate();
            int requestVersion = sessionVersion;
            SetStatus("카메라를 준비하고 있습니다...");
            ApplyState(FaceRegistrationViewState.Analyzing);

            imageSource.StartCamera(
                texture => OnCameraReady(texture, requestVersion),
                message => HandleInputFailure(message, requestVersion));
        }

        public void AutoFit()
        {
            RequirePhotoReviewUI();
            imageAdjustView.ResetView();
        }

        public void Retry()
        {
            RequirePhotoReviewUI();
            BeginNewCandidate();
            SetStatus(string.Empty);
            ApplyState(FaceRegistrationViewState.Empty);
        }

        public void UsePhoto()
        {
            RequirePhotoReviewUI();
            if (currentState != FaceRegistrationViewState.Reviewing ||
                candidateTexture == null ||
                !candidateApproved ||
                sampleTextures.Count >= RequiredSampleCount)
            {
                return;
            }

            int slotIndex = sampleTextures.Count;
            sampleTextures.Add(candidateTexture);
            sampleSlots[slotIndex].SetSample(candidateTexture);
            candidateTexture = null;

            adjustableImage.texture = null;
            ClearFaceOverlays();
            imageAdjustView.ResetView();
            SetStatus(string.Empty);
            UpdateProgress();

            ApplyState(sampleTextures.Count == RequiredSampleCount
                ? FaceRegistrationViewState.ReadyToComplete
                : FaceRegistrationViewState.Empty);
        }

        public void Complete()
        {
            RequirePhotoReviewUI();
            if (sampleTextures.Count != RequiredSampleCount)
            {
                return;
            }

            SetStatus("5장의 얼굴 사진 준비가 완료되었습니다.");
            ApplyState(FaceRegistrationViewState.ReadyToComplete);
            Debug.Log($"[FaceRegistration] ProfileId={CurrentProfileId}, 5개 샘플 준비 완료. 임베딩 저장은 수행하지 않습니다.");
        }

        public void ApplyState(FaceRegistrationViewState state)
        {
            ValidateReferences();
            // CameraController owns the live preview in the current MainPanel.
            // Do not hide it or replace the authored guidance with review text.
            if (!HasPhotoReviewUI) return;

            currentState = state;

            bool isEmpty = state == FaceRegistrationViewState.Empty;
            bool isAnalyzing = state == FaceRegistrationViewState.Analyzing;
            bool isReviewing = state == FaceRegistrationViewState.Reviewing;
            bool isReady = state == FaceRegistrationViewState.ReadyToComplete;
            bool hasStillPreview = candidateTexture != null;
            bool hasCameraPreview = cameraPreview != null && cameraPreview.texture != null;
            bool canCapture = isAnalyzing &&
                hasCameraPreview &&
                imageSource != null &&
                imageSource.IsCameraReady;

            sourceButtons.SetActive(isEmpty || isAnalyzing);
            reviewActions.SetActive(isReviewing);
            galleryButton.interactable = isEmpty;
            cameraButton.interactable = isEmpty || canCapture;
            if (cameraButtonLabel != null)
            {
                cameraButtonLabel.text = canCapture ? "촬영" : "카메라";
            }
            autoFitButton.interactable = isReviewing && hasStillPreview;
            retryButton.interactable = isReviewing;
            usePhotoButton.interactable = isReviewing && hasStillPreview && candidateApproved;
            completeButton.interactable = isReady && sampleTextures.Count == RequiredSampleCount;

            adjustableImage.gameObject.SetActive(hasStillPreview);
            cameraPreview.gameObject.SetActive(isAnalyzing && hasCameraPreview);
            faceOverlayLayer.gameObject.SetActive(isReviewing && hasStillPreview);
            faceGuide.SetActive(isReviewing || (isAnalyzing && hasCameraPreview));

            bool showEmptyMessage = isEmpty || isReady;
            emptyText.gameObject.SetActive(showEmptyMessage);
            emptyText.text = isReady
                ? "사진 준비 완료"
                : sampleTextures.Count > 0
                    ? "다음 얼굴 사진을 선택해주세요"
                    : "얼굴 미등록";

            bool shouldShowStatus =
                isAnalyzing || isReviewing || isReady ||
                !string.IsNullOrWhiteSpace(statusMessage);

            statusPanel.SetActive(shouldShowStatus);
            statusText.text = ResolveStatusText(state);
            imageAdjustView.SetInteractionEnabled(isReviewing && hasStillPreview);
            UpdateProgress();
        }

        public void UpdateProgress()
        {
            if (!HasPhotoReviewUI) return;
            ValidateReferences();
            int count = Mathf.Clamp(sampleTextures.Count, 0, RequiredSampleCount);
            progressText.text = $"{count} / {RequiredSampleCount}";
            completeButton.interactable =
                currentState == FaceRegistrationViewState.ReadyToComplete &&
                count == RequiredSampleCount;
        }

        public void ResetSession()
        {
            sessionVersion++;
            // Showing an already active MainPanel must not stop the preview:
            // CameraController will not receive another OnEnable in that case.
            if (HasPhotoReviewUI)
                imageSource?.StopCamera();
            else
                GetComponentInChildren<FaceRegistrationCamera>(true)?.StopSession();

            if (HasPhotoReviewUI && cameraPreview != null)
            {
                cameraPreview.texture = null;
            }

            DestroyTexture(candidateTexture);
            candidateTexture = null;

            foreach (Texture2D texture in sampleTextures)
            {
                DestroyTexture(texture);
            }

            sampleTextures.Clear();

            if (sampleSlots != null)
            {
                for (int i = 0; i < sampleSlots.Length; i++)
                {
                    if (sampleSlots[i] == null)
                    {
                        continue;
                    }

                    sampleSlots[i].SetIndex(i + 1);
                    sampleSlots[i].Clear();
                }
            }

            ClearFaceOverlays();
            selectedFaceIndex = -1;
            candidateApproved = false;
            statusMessage = string.Empty;

            if (adjustableImage != null)
            {
                adjustableImage.texture = null;
            }

            imageAdjustView?.ResetView();
            currentState = FaceRegistrationViewState.Empty;

            if (HasPhotoReviewUI)
            {
                UpdateProgress();
                ApplyState(FaceRegistrationViewState.Empty);
            }
        }

        /// <summary>
        /// FaceDetector reports normalized, bottom-left-origin image rectangles here.
        /// Rect values must be in the 0..1 range.
        /// </summary>
        public void PresentDetectionResult(IReadOnlyList<Rect> normalizedFaceRects)
        {
            if (candidateTexture == null)
            {
                return;
            }

            ClearFaceOverlays();

            if (normalizedFaceRects == null || normalizedFaceRects.Count == 0)
            {
                candidateApproved = false;
                SetStatus("얼굴을 찾을 수 없습니다.");
                ApplyState(FaceRegistrationViewState.Reviewing);
                return;
            }

            int nearestIndex = FindNearestFaceToCenter(normalizedFaceRects);
            for (int i = 0; i < normalizedFaceRects.Count; i++)
            {
                CreateFaceBox(i, ClampNormalizedRect(normalizedFaceRects[i]));
            }

            candidateApproved = true;
            SelectFace(nearestIndex);
            SetStatus("선택한 얼굴이 본인인지 확인해주세요.");
            ApplyState(FaceRegistrationViewState.Reviewing);
        }

        public void ShowAnalysisFailure(string message)
        {
            if (candidateTexture == null)
            {
                return;
            }

            candidateApproved = false;
            SetStatus(string.IsNullOrWhiteSpace(message)
                ? "사진을 확인할 수 없습니다."
                : message);
            ApplyState(FaceRegistrationViewState.Reviewing);
        }

        private void OnCameraReady(Texture previewTexture, int requestVersion)
        {
            if (!IsCurrentRequest(requestVersion))
            {
                imageSource.StopCamera();
                return;
            }

            cameraPreview.texture = previewTexture;
            SetStatus(imageSource.IsCameraReady
                ? "카메라 준비 완료. 촬영 버튼을 눌러주세요."
                : "카메라 영상을 확인하고 있습니다...");
            ApplyState(FaceRegistrationViewState.Analyzing);
        }

        private void ReceiveImageBytes(byte[] imageBytes, int requestVersion)
        {
            if (!IsCurrentRequest(requestVersion))
            {
                return;
            }

            imageSource.StopCamera();
            cameraPreview.texture = null;

            if (imageBytes == null || imageBytes.Length == 0)
            {
                HandleInputFailure("선택한 사진이 비어 있습니다.", requestVersion);
                return;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = $"FaceRegistration_Profile_{CurrentProfileId}_Candidate",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (!texture.LoadImage(imageBytes, false))
            {
                DestroyTexture(texture);
                HandleInputFailure("사진을 불러올 수 없습니다.", requestVersion);
                return;
            }

            candidateTexture = texture;
            adjustableImage.texture = texture;
            imageAdjustView.SetContentAspect(texture.width, texture.height);
            SetStatus("얼굴을 확인하고 있습니다...");
            ApplyState(FaceRegistrationViewState.Analyzing);

            Action<Texture2D> analysisRequested = AnalysisRequested;
            if (analysisRequested != null)
            {
                candidateApproved = false;
                analysisRequested.Invoke(texture);
                return;
            }

            // FaceDetector가 아직 연결되지 않은 단계에서는 검출 결과나 얼굴
            // 박스를 만들지 않고, 입력 이미지를 조정 가능한 후보로만 표시합니다.
            candidateApproved = true;
            SetStatus("사진을 불러왔습니다. 얼굴 분석 준비 완료.");
            ApplyState(FaceRegistrationViewState.Reviewing);
        }

        private void HandleInputFailure(string message, int requestVersion)
        {
            if (!IsCurrentRequest(requestVersion))
            {
                return;
            }

            imageSource.StopCamera();
            cameraPreview.texture = null;
            SetStatus(string.IsNullOrWhiteSpace(message)
                ? "사진을 가져오지 못했습니다."
                : message);
            ApplyState(FaceRegistrationViewState.Empty);
        }

        private void BeginNewCandidate()
        {
            sessionVersion++;
            imageSource.StopCamera();
            cameraPreview.texture = null;
            DestroyTexture(candidateTexture);
            candidateTexture = null;
            adjustableImage.texture = null;
            candidateApproved = false;
            selectedFaceIndex = -1;
            ClearFaceOverlays();
            imageAdjustView.ResetView();
        }

        private bool IsCurrentRequest(int requestVersion)
        {
            return requestVersion == sessionVersion && CurrentProfileId > 0;
        }

        private void SetStatus(string message)
        {
            statusMessage = message ?? string.Empty;
        }

        private string ResolveStatusText(FaceRegistrationViewState state)
        {
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                return statusMessage;
            }

            return state switch
            {
                FaceRegistrationViewState.Analyzing => "얼굴을 확인하고 있습니다...",
                FaceRegistrationViewState.Reviewing => "선택한 얼굴이 본인인지 확인해주세요.",
                FaceRegistrationViewState.ReadyToComplete => "5장의 얼굴 사진이 준비되었습니다.",
                _ => string.Empty
            };
        }

        private void CreateFaceBox(int index, Rect normalizedRect)
        {
            var faceBoxObject = new GameObject(
                $"FaceBox_{index + 1}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(FaceSelectionBoxView));

            faceBoxObject.layer = faceOverlayLayer.gameObject.layer;
            RectTransform rect = faceBoxObject.GetComponent<RectTransform>();
            rect.SetParent(faceOverlayLayer, false);
            rect.anchorMin = new Vector2(normalizedRect.xMin, normalizedRect.yMin);
            rect.anchorMax = new Vector2(normalizedRect.xMax, normalizedRect.yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = faceBoxObject.GetComponent<Image>();
            image.raycastTarget = true;

            FaceSelectionBoxView faceBox =
                faceBoxObject.GetComponent<FaceSelectionBoxView>();
            faceBox.Configure(index, imageAdjustView, SelectFace);
            faceBoxes.Add(faceBox);
        }

        private void SelectFace(int faceIndex)
        {
            if (faceIndex < 0 || faceIndex >= faceBoxes.Count)
            {
                return;
            }

            selectedFaceIndex = faceIndex;
            for (int i = 0; i < faceBoxes.Count; i++)
            {
                faceBoxes[i].SetSelected(i == selectedFaceIndex);
            }
        }

        private void ClearFaceOverlays()
        {
            faceBoxes.Clear();

            if (faceOverlayLayer == null)
            {
                return;
            }

            for (int i = faceOverlayLayer.childCount - 1; i >= 0; i--)
            {
                Transform child = faceOverlayLayer.GetChild(i);
                child.SetParent(null, false);
                DestroyOwnedObject(child.gameObject);
            }
        }

        private static int FindNearestFaceToCenter(IReadOnlyList<Rect> faceRects)
        {
            int nearestIndex = 0;
            float nearestDistance = float.MaxValue;
            Vector2 imageCenter = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < faceRects.Count; i++)
            {
                float distance = (faceRects[i].center - imageCenter).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        private static Rect ClampNormalizedRect(Rect rect)
        {
            float xMin = Mathf.Clamp01(rect.xMin);
            float yMin = Mathf.Clamp01(rect.yMin);
            float xMax = Mathf.Clamp01(rect.xMax);
            float yMax = Mathf.Clamp01(rect.yMax);
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
            {
                DestroyOwnedObject(texture);
            }
        }

        private static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        // A single UI material on the shared camera preview is cheaper than
        // manufacturing/uploading a second corrected texture every render frame.
        // Registration and recognition share this RawImage, never the material state.
        private void UpdateRearPreviewExposure()
        {
            if (cameraPreview == null) return;
            bool enable = imageSource != null &&
                imageSource.CurrentFacing == CameraFacing.Rear &&
                RearCameraExposureProcessor.ExperimentalCorrectionEnabled;
            if (!enable)
            {
                RestoreRearPreviewMaterial();
                return;
            }

            if (rearPreviewMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("RearCameraExposure");
                if (shader == null)
                {
                    if (!missingRearPreviewShaderLogged)
                    {
                        missingRearPreviewShaderLogged = true;
                        Debug.LogError("[S4-15] Rear exposure preview shader is missing.", this);
                    }
                    return;
                }
                originalPreviewMaterial = cameraPreview.material;
                rearPreviewMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.DontSave
                };
                cameraPreview.material = rearPreviewMaterial;
            }
            float gain = recognitionMode
                ? (recognitionCamera != null ? recognitionCamera.PreviewExposureGain : 1f)
                : (registrationCamera != null ? registrationCamera.PreviewExposureGain : 1f);
            rearPreviewMaterial.SetFloat("_ExposureGain", Mathf.Clamp(gain, .80f, 1.10f));
        }

        private void RestoreRearPreviewMaterial()
        {
            if (rearPreviewMaterial == null) return;
            if (cameraPreview != null && cameraPreview.material == rearPreviewMaterial)
                cameraPreview.material = originalPreviewMaterial;
            if (UnityEngine.Application.isPlaying) Destroy(rearPreviewMaterial);
            else DestroyImmediate(rearPreviewMaterial);
            rearPreviewMaterial = null;
            originalPreviewMaterial = null;
        }

        private void OnDisable()
        {
            RestoreRearPreviewMaterial();
            imageSource?.StopCamera();
        }

        private void OnDestroy()
        {
            RestoreRearPreviewMaterial();
            imageSource?.StopCamera();
            if (instructionBackground != null)
            {
                if (UnityEngine.Application.isPlaying) Destroy(instructionBackground);
                else DestroyImmediate(instructionBackground);
            }
        }
    }
}
