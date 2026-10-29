using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Components;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRegistration;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace AppPofo.Presentation.Screens
{
    /// <summary>
    /// 명함 UI의 아바타 입력과 프로필 갱신 유스케이스를 연결합니다.
    /// </summary>
    public sealed class BusinessCardPanelController : MonoBehaviour
    {
        [SerializeField] private AppPofoBootstrap bootstrap;
        [SerializeField] private BusinessCardView businessCardView;
        [SerializeField] private GameObject avatarSourcePanel;

        [SerializeField] private Image previewImage;
        [SerializeField] private RawImage cameraPreview;
        [SerializeField] private AvatarImageSourceResolver avatarImageSourceResolver;

        [SerializeField] private Text cameraButtonLabel;
        [SerializeField] private Text businessCameraButtonLabel;
        [SerializeField] private GameObject avatarSourceTitle;
        [SerializeField] private GameObject galleryButton;
        [SerializeField] private GameObject cameraButton;
        [SerializeField] private GameObject galleryButtonLabel;

        [Header("Theme Preview")]
        [SerializeField] private Image businessCardBackground;
        [SerializeField] private Image avatarFrameImage;
        [SerializeField] private Sprite auroraBackgroundSprite;
        [SerializeField] private Sprite blushBackgroundSprite;
        [SerializeField] private Sprite neutralBackgroundSprite;
        [SerializeField] private Sprite auroraAvatarFrameSprite;
        [SerializeField] private Sprite blushAvatarFrameSprite;
        [SerializeField] private Sprite neutralAvatarFrameSprite;
        [SerializeField] private Outline auroraThemeOutline;
        [SerializeField] private Outline blushThemeOutline;
        [SerializeField] private Outline neutralThemeOutline;

        private IAvatarImageSource avatarImageSource;
        private Texture2D previewTexture;
        private Sprite previewSprite;
        private long currentProfileId;
        private CloudProfileDetail currentCloudProfile;
        private bool cloudThemeSaving;
        private int avatarContextVersion;
        private bool inlineCameraMode;
        private bool cameraOperationPending;
        private Transform cameraPreviewHome;
        private CameraPreviewOrientationEffect cameraOrientationEffect;

        public event Action<long> AvatarSaved;
        public event Action<string> CloudAvatarSaved;
        public event Action<bool> SummaryVisibilityChanged;
        private ProfileSummaryPanel recognizedSummary;
        public bool IsSummaryOpen => recognizedSummary != null && recognizedSummary.IsOpen;

        public void CloseRecognizedSummary() { if (recognizedSummary != null) recognizedSummary.Close(); }
        public void ShowRecognizedLocalProfile(long id)
        {
            var profile = bootstrap.GetProfileUseCase.Execute(id);
            if (profile == null || !profile.IsActive) return;
            var data = ProfileSummaryData.FromLocal(profile, bootstrap.LoadAvatarUseCase.Execute(profile.AvatarPath));
            EnsureSummary()?.ShowLocal(data);
        }
        public void ShowRecognizedCloudProfile(string id) { EnsureSummary()?.ShowCloud(id); }
        private ProfileSummaryPanel EnsureSummary()
        {
            if (recognizedSummary == null)
                recognizedSummary = ProfileSummaryPanel.Create(transform, businessCardView, visible => SummaryVisibilityChanged?.Invoke(visible), StyleSummaryIdentity);
            return recognizedSummary;
        }
        public void StyleSummaryIdentity(BusinessCardView card)
        {
            var source = businessCardView.GetComponentsInChildren<Image>(true);
            var copies = card.GetComponentsInChildren<Image>(true);
            int background = Array.IndexOf(source, businessCardBackground), frame = Array.IndexOf(source, avatarFrameImage);
            if (background >= 0 && background < copies.Length) copies[background].sprite = auroraBackgroundSprite;
            if (frame >= 0 && frame < copies.Length) copies[frame].sprite = auroraAvatarFrameSprite;
        }

        public void Awake()
        {
            avatarImageSource = avatarImageSourceResolver.Resolve();

            if(avatarImageSource == null)
            {
                Debug.LogError("[BusinessCardPanelController] AvatarImageSource를 가져오지 못했습니다.");
            }
        }

        public void ApplyAuroraTheme()
        {
            if (currentCloudProfile != null && cloudThemeSaving) return;
            ApplyTheme(
                auroraBackgroundSprite,
                auroraAvatarFrameSprite,
                auroraThemeOutline);

            SaveBusinessCardTheme(BusinessCardTheme.Aurora);
        }

        public void ApplyBlushTheme()
        {
            if (currentCloudProfile != null && cloudThemeSaving) return;
            ApplyTheme(
                blushBackgroundSprite,
                blushAvatarFrameSprite,
                blushThemeOutline);

            SaveBusinessCardTheme(BusinessCardTheme.Blush);
        }

        public void ApplyNeutralTheme()
        {
            if (currentCloudProfile != null && cloudThemeSaving) return;
            ApplyTheme(
                neutralBackgroundSprite,
                neutralAvatarFrameSprite,
                neutralThemeOutline);

            SaveBusinessCardTheme(BusinessCardTheme.Neutral);
        }

        private void ApplyTheme(
            Sprite backgroundSprite,
            Sprite avatarFrameSprite,
            Outline selectedOutline)
        {
            businessCardBackground.sprite = backgroundSprite;
            avatarFrameImage.sprite = avatarFrameSprite;

            auroraThemeOutline.enabled = selectedOutline == auroraThemeOutline;
            blushThemeOutline.enabled = selectedOutline == blushThemeOutline;
            neutralThemeOutline.enabled = selectedOutline == neutralThemeOutline;
        }

        private void ApplyBusinessCardThemeVisual(BusinessCardTheme theme)
        {
            switch(theme)
            {
                case BusinessCardTheme.Blush:
                    ApplyTheme(
                        blushBackgroundSprite,
                        blushAvatarFrameSprite,
                        blushThemeOutline);
                    break;

                case BusinessCardTheme.Neutral:
                    ApplyTheme(
                        neutralBackgroundSprite,
                        neutralAvatarFrameSprite,
                        neutralThemeOutline);
                    break;

                case BusinessCardTheme.Aurora:
                default:
                    ApplyTheme(
                        auroraBackgroundSprite,
                        auroraAvatarFrameSprite,
                        auroraThemeOutline);
                    break;
            }
        }

        private void SaveBusinessCardTheme(BusinessCardTheme theme)
        {
            if (currentCloudProfile != null)
            {
                if (!cloudThemeSaving) StartCoroutine(SaveCloudTheme(theme));
                return;
            }
            if(currentProfileId <= 0)
            {
                Debug.LogWarning(
                    "[BusinessCardTheme] 현재 선택된 프로필이 없어 저장을 건너뜁니다.");
                return;
            }

            try
            {
                bootstrap.UpdateBusinessCardThemeUseCase.Execute(
                    currentProfileId,
                    theme,
                    DateTimeOffset.UtcNow);
            }
            catch(Exception ex)
            {
                Debug.LogError(
                    $"[BusinessCardTheme] 테마 저장에 실패했습니다.\n{ex.Message}");
            }
        }

        public void Show(long profileId)
        {
            if(profileId <= 0)
            {
                Debug.LogError("명함을 표시할 ProfileId가 올바르지 않습니다");
                return;
            }

            Profile profile = bootstrap.GetProfileUseCase.Execute(profileId);

            if(profile == null)
            {
                Debug.LogError($"명함을 표시할 프로필을 찾을수 없습니다. profileID: {profileId}");
                return;
            }
            // 이 패널 인스턴스는 모든 프로필이 공유하므로, 이전 프로필에서 만든
            // 촬영/선택 미리보기를 새 프로필 화면으로 가져가지 않습니다.
            avatarContextVersion++;
            ResetAvatarSourceUi(closeSourcePanel: true);
            currentProfileId = profileId;
            currentCloudProfile = null;
            ApplyBusinessCardThemeVisual(profile.BusinessCardTheme);

            byte[] avatarBytes = bootstrap.LoadAvatarUseCase.Execute(profile.AvatarPath);

            businessCardView.Bind(profile,avatarBytes);
            
            gameObject.SetActive(true);
        }

        public void ShowCloud(CloudProfileDetail profile)
        {
            if (profile == null || !Guid.TryParse(profile.id, out _) || !CloudProfileApi.CanRequest) return;
            avatarContextVersion++;
            ResetAvatarSourceUi(closeSourcePanel: true);
            currentProfileId = 0; currentCloudProfile = profile; cloudThemeSaving = false;
            ApplyBusinessCardThemeVisual(CloudTheme(profile.business_card_theme));
            businessCardView.BindCloud(profile);
            gameObject.SetActive(true);
        }
        public void CloseCloudProfile(string id)
        {
            if (currentCloudProfile?.id == id) Close();
        }
        private static BusinessCardTheme CloudTheme(string theme) =>
            theme == "BLUSH" ? BusinessCardTheme.Blush : theme == "NEUTRAL" ? BusinessCardTheme.Neutral : BusinessCardTheme.Aurora;
        private System.Collections.IEnumerator SaveCloudTheme(BusinessCardTheme theme)
        {
            var profile = currentCloudProfile; int version = avatarContextVersion;
            if (profile == null || !CloudProfileApi.CanRequest) yield break;
            cloudThemeSaving = true;
            var payload = CloudProfileWritePayload.FromDetail(profile);
            payload.business_card_theme = theme.ToString().ToUpperInvariant();
            string error = null;
            yield return CloudProfileWriteApi.Replace(profile.id, payload, (_, e) => error = e);
            if (version != avatarContextVersion || currentCloudProfile != profile) yield break;
            cloudThemeSaving = false;
            if (error != null) { ApplyBusinessCardThemeVisual(CloudTheme(profile.business_card_theme)); ShowAvatarError(error); }
            else profile.business_card_theme = payload.business_card_theme;
        }

        // Reuse only the authored card, without opening the editor or avatar controls.
        public BusinessCardView CreateRecognitionCard(long profileId, Transform parent, Action onSelected = null)
        {
            if (profileId <= 0 || parent == null || businessCardView == null)
                return null;

            Profile profile = bootstrap.GetProfileUseCase.Execute(profileId);
            if (profile == null || !profile.IsActive)
                return null;

            BusinessCardView card = Instantiate(businessCardView, parent, false);
            card.name = "RecognitionBusinessCard";
            Image[] sourceImages = businessCardView.GetComponentsInChildren<Image>(true);
            Image[] cardImages = card.GetComponentsInChildren<Image>(true);
            int backgroundIndex = Array.IndexOf(sourceImages, businessCardBackground);
            int frameIndex = Array.IndexOf(sourceImages, avatarFrameImage);
            Sprite background = auroraBackgroundSprite;
            Sprite frame = auroraAvatarFrameSprite;
            if (profile.BusinessCardTheme == BusinessCardTheme.Blush)
            {
                background = blushBackgroundSprite;
                frame = blushAvatarFrameSprite;
            }
            else if (profile.BusinessCardTheme == BusinessCardTheme.Neutral)
            {
                background = neutralBackgroundSprite;
                frame = neutralAvatarFrameSprite;
            }
            if (backgroundIndex >= 0) cardImages[backgroundIndex].sprite = background;
            if (frameIndex >= 0) cardImages[frameIndex].sprite = frame;

            foreach (Button button in card.GetComponentsInChildren<Button>(true))
                button.enabled = false;
            foreach (Graphic graphic in card.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;

            card.Bind(profile, bootstrap.LoadAvatarUseCase.Execute(profile.AvatarPath));
            RecognitionClick(card, onSelected);
            card.gameObject.SetActive(true);
            return card;
        }

        // S5-09-B: public server card only. Cloud UUIDs NEVER become local long IDs.
        public BusinessCardView CreateCloudRecognitionCard(
            string cloudId, string displayName, string headline, Transform parent,
            string avatarBase64 = null, string theme = null, Action onSelected = null)
        {
            if (!Guid.TryParse(cloudId, out _) ||
                string.IsNullOrWhiteSpace(displayName) ||
                parent == null || businessCardView == null) return null;
            BusinessCardView card = Instantiate(businessCardView, parent, false);
            card.name = "CloudRecognitionBusinessCard";
            Image[] sourceImages = businessCardView.GetComponentsInChildren<Image>(true);
            Image[] cardImages = card.GetComponentsInChildren<Image>(true);
            int backgroundIndex = Array.IndexOf(sourceImages, businessCardBackground);
            int frameIndex = Array.IndexOf(sourceImages, avatarFrameImage);
            if (backgroundIndex >= 0 && backgroundIndex < cardImages.Length)
                cardImages[backgroundIndex].sprite = auroraBackgroundSprite;
            if (frameIndex >= 0 && frameIndex < cardImages.Length)
                cardImages[frameIndex].sprite = auroraAvatarFrameSprite;
            foreach (Button button in card.GetComponentsInChildren<Button>(true))
                button.enabled = false;
            foreach (Graphic graphic in card.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            if (backgroundIndex >= 0 && backgroundIndex < cardImages.Length)
                cardImages[backgroundIndex].sprite = theme == "BLUSH" ? blushBackgroundSprite : theme == "NEUTRAL" ? neutralBackgroundSprite : auroraBackgroundSprite;
            if (frameIndex >= 0 && frameIndex < cardImages.Length)
                cardImages[frameIndex].sprite = theme == "BLUSH" ? blushAvatarFrameSprite : theme == "NEUTRAL" ? neutralAvatarFrameSprite : auroraAvatarFrameSprite;
            card.BindCloudPublic(displayName, headline, avatarBase64);
            RecognitionClick(card, onSelected);
            card.gameObject.SetActive(true);
            return card;
        }

        private static void RecognitionClick(BusinessCardView card, Action selected)
        {
            if (selected == null) return;
            var image = card.GetComponent<Image>();
            if (image == null) { image = card.gameObject.AddComponent<Image>(); image.color = Color.clear; }
            image.raycastTarget = true;
            var button = card.GetComponent<Button>() ?? card.gameObject.AddComponent<Button>();
            button.enabled = true; button.interactable = true; button.targetGraphic = image;
            button.navigation = new Navigation {mode = Navigation.Mode.None};
            button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(() => selected());
        }

        public void Close()
        {
            avatarContextVersion++;
            ResetAvatarSourceUi(closeSourcePanel: true);
            currentProfileId = 0;
            currentCloudProfile = null;
            gameObject.SetActive(false);
        }
        public void OpenAvatarSourcePanel()
        {
            OpenAvatarSourcePanel(showSourceSelection: true);
        }

        private void OpenAvatarSourcePanel(bool showSourceSelection)
        {
            // 소스 선택 화면은 저장 결과가 아닌 일시적인 작업 화면입니다.
            // 다시 열 때마다 직전 촬영 이미지를 비운 상태로 시작합니다.
            ResetAvatarSourceUi(closeSourcePanel: false);

            if(showSourceSelection)
                ShowSourceSelectionState();
            else
                ShowGalleryReviewState();

            avatarSourcePanel.SetActive(true);
        }

        public void CloseAvatarSourcePanel()
        {
            avatarContextVersion++;
            ResetAvatarSourceUi(closeSourcePanel: true);
        }

        public void PickAvatarImage()
        {
            PickAvatarImage(showAvatarSourcePanelOnSuccess: false);
        }

        public void StartGalleryFromBusinessCard()
        {
            avatarContextVersion++;
            ResetAvatarSourceUi(closeSourcePanel: true);
            PickAvatarImage(showAvatarSourcePanelOnSuccess: false);
        }

        public void StartCameraFromBusinessCard()
        {
            if(cameraOperationPending) return;
            if(!inlineCameraMode)
            {
                avatarContextVersion++;
                ResetAvatarSourceUi(closeSourcePanel: true);
                inlineCameraMode = true;
                cameraPreviewHome = cameraPreview.transform.parent;
                cameraPreview.transform.SetParent(businessCardView.AvatarDisplayRoot,false);
                RectTransform rect = cameraPreview.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f,.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                rect.localScale = Vector3.one;
            }
            OnCameraButtonClicked();
        }

        private void PickAvatarImage(bool showAvatarSourcePanelOnSuccess)
        {
            if(avatarImageSource == null)
            {
                Debug.LogError("[BusinessCardPanelController] AvatarImageSource가 없습니다.");
                return;
            }

            long targetProfileId = currentProfileId;
            int targetContextVersion = avatarContextVersion;

            avatarImageSource.PickImage(
                imageBytes =>
                {
                    if(showAvatarSourcePanelOnSuccess &&
                       IsCurrentAvatarContext(targetProfileId,targetContextVersion))
                    {
                        OpenAvatarSourcePanel(showSourceSelection: false);
                    }

                    CompleteAvatarSelection(
                        targetProfileId,
                        targetContextVersion,
                        imageBytes);
                },
                message => ShowAvatarErrorForContext(
                    targetProfileId,
                    targetContextVersion,
                    message)
            );
        }

        public void OnCameraButtonClicked()
        {
            if(cameraOperationPending) return;
            if(avatarImageSource == null)
            {
                ShowAvatarError("AvatarImageSource가 없습니다.");

                return;
            }

            bool isCameraPreviewVisible =
                cameraPreview != null &&
                cameraPreview.gameObject.activeSelf;

            long targetProfileId = currentProfileId;
            int targetContextVersion = avatarContextVersion;


            if(!avatarImageSource.IsCameraRunning ||
               !isCameraPreviewVisible)
            {
                cameraOperationPending = true;
                avatarImageSource.StartCamera(
                    cameraTexture => ShowCameraPreviewForContext(
                        targetProfileId,
                        targetContextVersion,
                        cameraTexture),
                    message => ShowAvatarErrorForContext(
                        targetProfileId,
                        targetContextVersion,
                        message));
                return;
            }

            cameraOperationPending = true;
            Action<byte[]> onCaptured = imageBytes => CompleteAvatarSelection(
                targetProfileId,targetContextVersion,imageBytes);
            Action<string> onCaptureFailed = message => ShowAvatarErrorForContext(
                targetProfileId,targetContextVersion,message);
            if(avatarImageSource is IOrientedCameraSource orientedSource)
                orientedSource.CaptureUprightImage(onCaptured,onCaptureFailed);
            else avatarImageSource.CaptureImage(onCaptured,onCaptureFailed);
        }

        private void ShowCameraPreviewForContext(
            long targetProfileId,
            int targetContextVersion,
            Texture cameraTexture)
        {
            if(!IsCurrentAvatarContext(targetProfileId,targetContextVersion))
                return;

            cameraOperationPending = false;
            ShowCameraPreview(cameraTexture);
        }

        private void ShowCameraPreview(Texture cameraTexture)
        {
            if(cameraTexture == null)
            {
                ShowAvatarError("카메라 미리보기 texture가 없습니다.");
                return;
            }

            if(cameraTexture.width <= 0 || cameraTexture.height <= 0)
            {
                ShowAvatarError("카메라 미리보기 크기가 올바르지 않습니다.");
                return;
            }

            RectInt cropRect = CalculateCenteredSquareCrop(
                cameraTexture.width,
                cameraTexture.height);

            cameraPreview.texture = cameraTexture;
            cameraPreview.uvRect = new Rect(
                (float)cropRect.x / cameraTexture.width,
                (float)cropRect.y / cameraTexture.height,
                (float)cropRect.width / cameraTexture.width,
                (float)cropRect.height / cameraTexture.height);

            previewImage.gameObject.SetActive(false);
            cameraPreview.gameObject.SetActive(true);
            UpdateCameraOrientation();
            if(inlineCameraMode)
            {
                if(businessCameraButtonLabel != null) businessCameraButtonLabel.text = "완료";
            }
            else ShowCameraState();

            if(cameraButtonLabel != null)
            {
                cameraButtonLabel.text = "사진 찍기";
            }
        }

        private void CompleteAvatarSelection(
            long targetProfileId,
            int targetContextVersion,
            byte[] imageBytes)
        {
            // Android 선택창이나 카메라 코루틴의 결과가 늦게 도착해도
            // 그 사이에 연 다른 프로필에 저장되지 않도록 막습니다.
            if(!IsCurrentAvatarContext(targetProfileId,targetContextVersion))
            {
                return;
            }

            cameraOperationPending = false;
            ShowAvatarPreview(targetProfileId,imageBytes);
        }

        private void HideCameraPreview()
        {
            if(cameraOrientationEffect != null) cameraOrientationEffect.ClearOrientation();
            // Unity 6 Windows Editor에서는 Stop/Play 후 updateCount가 1에서 멈출 수 있습니다.
            // 패널을 닫을 때는 UI만 숨기고, 스트림 해제는 OnDestroy까지 미룹니다.
            if(cameraPreview != null)
            {
                cameraPreview.texture = null;
                cameraPreview.gameObject.SetActive(false);
                if(cameraPreviewHome != null)
                {
                    cameraPreview.transform.SetParent(cameraPreviewHome,false);
                    cameraPreviewHome = null;
                }
            }

            inlineCameraMode = false;
            if(businessCameraButtonLabel != null) businessCameraButtonLabel.text = "촬영";

            if(cameraButtonLabel != null)
            {
                cameraButtonLabel.text = "촬영";
            }
        }

        private void ShowAvatarPreview(
            long targetProfileId,
            byte[] imageBytes)
        {
            if(imageBytes == null || imageBytes.Length == 0)
            {
                ShowAvatarError(
                    "이미지 데이터가 비어있습니다."
                );
                return;
            }

            if(!TryCreateSquareAvatar(
                    imageBytes,
                    out Texture2D squareTexture,
                    out byte[] squarePngBytes))
            {
                ShowAvatarError(
                    "선택한 이미지를 불러오지 못했습니다."
                );
                return;
            }

            ClearPreviewImage();

            HideCameraPreview();
            ShowGalleryReviewState();

            previewTexture = squareTexture;

            previewSprite = Sprite.Create(
                previewTexture,
                new Rect(0,0,previewTexture.width,previewTexture.height),
                new Vector2(0.5f,0.5f)
            );

            previewImage.sprite = previewSprite;
            previewImage.color = Color.white;
            previewImage.enabled = true;

            previewImage.preserveAspect = true;

            previewImage.gameObject.SetActive(true);
            cameraPreview.gameObject.SetActive(false);

            SaveAvatar(targetProfileId,squarePngBytes);
        }

        private bool TryCreateSquareAvatar(
            byte[] imageBytes,
            out Texture2D squareTexture,
            out byte[] squarePngBytes)
        {
            squareTexture = null;
            squarePngBytes = null;

            Texture2D sourceTexture = new Texture2D(2,2);

            try
            {
                if(!sourceTexture.LoadImage(imageBytes))
                    return false;

                RectInt cropRect = CalculateCenteredSquareCrop(
                    sourceTexture.width,
                    sourceTexture.height);

                squareTexture = new Texture2D(
                    cropRect.width,
                    cropRect.height,
                    TextureFormat.RGBA32,
                    false);

                squareTexture.SetPixels(
                    sourceTexture.GetPixels(
                        cropRect.x,
                        cropRect.y,
                        cropRect.width,
                        cropRect.height));

                squareTexture.Apply();

                squarePngBytes = squareTexture.EncodeToPNG();

                if(squarePngBytes == null || squarePngBytes.Length == 0)
                {
                    Destroy(squareTexture);
                    squareTexture = null;
                    return false;
                }

                return true;
            }
            catch(Exception ex)
            {
                if(squareTexture != null)
                {
                    Destroy(squareTexture);
                    squareTexture = null;
                }

                squarePngBytes = null;

                Debug.LogError(
                    $"[Avatar] 정사각형 이미지 생성에 실패했습니다.\n{ex.Message}");

                return false;
            }
            finally
            {
                Destroy(sourceTexture);
            }
        }

        private static RectInt CalculateCenteredSquareCrop(
            int width,
            int height)
        {
            int cropSize = Mathf.Min(width,height);

            return new RectInt(
                (width - cropSize) / 2,
                (height - cropSize) / 2,
                cropSize,
                cropSize);
        }

        private void ShowAvatarErrorForContext(
            long targetProfileId,
            int targetContextVersion,
            string message)
        {
            if(IsCurrentAvatarContext(targetProfileId,targetContextVersion))
            {
                cameraOperationPending = false;
                if(inlineCameraMode) HideCameraPreview();
                ShowAvatarError(message);
            }
        }

        private bool IsCurrentAvatarContext(
            long targetProfileId,
            int targetContextVersion)
        {
            return (targetProfileId > 0 || (currentCloudProfile != null && CloudProfileApi.CanRequest)) &&
                   targetProfileId == currentProfileId &&
                   targetContextVersion == avatarContextVersion;
        }
        private void ShowAvatarError(string message)
        {
            Debug.LogError(
                $"[Avatar] {message}");
        }


        private void ClearPreviewImage()
        {
            if(previewImage != null)
            {
                previewImage.sprite = null;
                previewImage.color = new Color(1f,1f,1f,0f);
                previewImage.enabled = false;
                previewImage.gameObject.SetActive(false);
            }

            if (previewSprite != null)
            {
                Destroy(previewSprite);
                previewSprite = null;
            }

            if (previewTexture != null)
            {
                Destroy(previewTexture);
                previewTexture = null;
            }
        }

        private void ResetAvatarSourceUi(bool closeSourcePanel)
        {
            cameraOperationPending = false;
            ClearPreviewImage();
            HideCameraPreview();

            if(closeSourcePanel && avatarSourcePanel != null)
            {
                ShowSourceSelectionState();
                avatarSourcePanel.SetActive(false);
            }
        }

        private void ShowSourceSelectionState()
        {
            SetSourceSelectionUiVisible(true);
        }

        private void ShowGalleryReviewState()
        {
            SetSourceSelectionUiVisible(false);
        }

        private void ShowCameraState()
        {
            SetSourceSelectionUiVisible(false);
            cameraButton.SetActive(true);
            cameraButtonLabel.gameObject.SetActive(true);
        }

        private void SetSourceSelectionUiVisible(bool isVisible)
        {
            avatarSourceTitle.SetActive(isVisible);
            galleryButton.SetActive(isVisible);
            cameraButton.SetActive(isVisible);
            galleryButtonLabel.SetActive(isVisible);
            cameraButtonLabel.gameObject.SetActive(isVisible);
        }


        private void OnDestroy()
        {
            if(avatarImageSource != null &&
               avatarImageSource.IsCameraRunning)
            {
                avatarImageSource.StopCamera();
            }

            ClearPreviewImage();
        }

        private void OnDisable()
        {
            avatarContextVersion++;
            ResetAvatarSourceUi(closeSourcePanel: true);
        }

        private void LateUpdate()
        {
            if(cameraPreview != null && cameraPreview.gameObject.activeSelf)
                UpdateCameraOrientation();
        }

        private void UpdateCameraOrientation()
        {
            if(!(avatarImageSource is IOrientedCameraSource source) ||
               !source.TryGetPreviewFrame(out Texture texture,out var orientation)) return;

            cameraPreview.texture = texture;
            // RawImage UVs are in upright space; the shared effect maps them back to the sensor.
            RectInt crop = CalculateCenteredSquareCrop(orientation.EffectiveWidth,orientation.EffectiveHeight);
            cameraPreview.uvRect = new Rect(
                (float)crop.x / orientation.EffectiveWidth,
                (float)crop.y / orientation.EffectiveHeight,
                (float)crop.width / orientation.EffectiveWidth,
                (float)crop.height / orientation.EffectiveHeight);
            if(cameraOrientationEffect == null)
                cameraOrientationEffect = cameraPreview.GetComponent<CameraPreviewOrientationEffect>() ??
                    cameraPreview.gameObject.AddComponent<CameraPreviewOrientationEffect>();
            // Its fit dimensions describe the displayed crop, not the uncropped sensor frame.
            // Rotation and mirror metadata remain identical to FaceRegistration's shared path.
            cameraOrientationEffect.SetOrientation(new CameraFrameOrientation(
                orientation.RotationAngle,orientation.VerticallyMirrored,crop.width,crop.height));
        }

        private void SaveAvatar(
            long targetProfileId,
            byte[] imagebytes)
        {
            if (currentCloudProfile != null)
            {
                StartCoroutine(SaveCloudAvatar(imagebytes));
                return;
            }
            if(targetProfileId <= 0 || targetProfileId != currentProfileId)
            {
                ShowAvatarError("현재 선택된 프로필이 없습니다.");
                return;
            }

            try
            {
                Profile updatedProfile = bootstrap.UpdateAvatarUseCase.Execute(targetProfileId,imagebytes,DateTimeOffset.UtcNow);

                byte[] savedAvatarBytes = bootstrap.LoadAvatarUseCase.Execute(updatedProfile.AvatarPath);

                businessCardView.Bind(updatedProfile,savedAvatarBytes);

                AvatarSaved?.Invoke(targetProfileId);

                Debug.Log(
                    $"[Avatar] 저장 및 명합 갱신 완료. " +
                    $"ProfileId: {targetProfileId}, ");

            }
            catch (Exception ex)
            {
                ShowAvatarError(
                    $"아바타 저장에 실패했습니다.\n{ex.Message}");
            }
        }
        private System.Collections.IEnumerator SaveCloudAvatar(byte[] image)
        {
            var profile = currentCloudProfile; int version = avatarContextVersion;
            byte[] thumbnail = CloudProfileAvatar.Thumbnail(image);
            if (thumbnail == null) { ShowAvatarError("프로필 사진을 준비하지 못했습니다."); yield break; }
            string error = null;
            yield return CloudProfileAvatar.Save(profile.id, thumbnail, false, e => error = e);
            if (version != avatarContextVersion || currentCloudProfile != profile) yield break;
            if (error != null) { ShowAvatarError(error); yield break; }
            profile.avatar_base64 = Convert.ToBase64String(thumbnail);
            businessCardView.BindCloud(profile);
            CloudAvatarSaved?.Invoke(profile.id);
        }
    }

}
