using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Components;
using AppPofo.Presentation.FaceRegistration;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    public sealed class CreateProfileScreen : MonoBehaviour
    {
        [SerializeField] private AppPofoBootstrap bootstrap;
        [SerializeField] private GameObject profileListScreenRoot;
        [SerializeField] private ProfileListScreen profileListScreen;
        [SerializeField] private InputField displayNameInput;
        [SerializeField] private InputField headlineInput;
        [SerializeField] private InputField bioInput;
        [SerializeField] private InputField emailInput;
        [SerializeField] private InputField websiteInput;
        [Header("First profile onboarding")]
        [SerializeField] private ProfileDetailScreen profileDetailScreen;
        [SerializeField] private FaceRegistrationScreen faceRegistrationScreen;
        [SerializeField] private BusinessCardPanelController businessCardPanel;
        [SerializeField] private SkillChipView skillChipPrefab;
        [SerializeField] private RectTransform formContent;
        [SerializeField] private ScrollRect formScroll;
        [SerializeField] private Text titleText;
        [SerializeField] private Text labelTemplate;
        [SerializeField] private Button nextButton;

        public enum RegistrationStep { BasicInfo, Skills, FaceChoice, Complete }
        public RegistrationStep CurrentStep { get; private set; }
        public long CurrentProfileId { get; private set; }
        private bool saving;
        private GameObject[] basicControls;
        private RectTransform skillsRoot, faceRoot, completeRoot, recommendedGrid, selectedGrid, cardSlot;
        private InputField skillSearch;
        private Text skillCount, messageText;
        private BusinessCardView completionCard;
        private readonly List<string> cloudSkills = new List<string>();
        private bool cloudRegistration;
        public string CloudProfileId { get; private set; }
        private Toggle cloudStoreConsent;
        private Toggle cloudPublicConsent;
        private Toggle cloudSubjectPresentConsent;
        private Text faceBenefitLabel;
        private bool cloudEditing;
        private Action<string> cloudEditClosed;
        private CloudProfileWritePayload cloudOriginal;
        private CloudProfileWritePayload pendingCreate;
        private string cloudCreateRequestId;
        private CloudFaceStatusData cloudCaptureSnapshot;

        public void BeginCloudEdit(CloudProfileDetail detail, Action<string> onClosed = null)
        {
            if (detail == null || !Guid.TryParse(detail.id, out _) || !CloudProfileApi.CanRequest) return;
            Begin();
            cloudEditing = true;
            cloudEditClosed = onClosed;
            cloudOriginal = CloudProfileWritePayload.FromDetail(detail);
            CloudProfileId = detail.id;
            displayNameInput.text = detail.display_name;
            headlineInput.text = detail.headline; bioInput.text = detail.bio;
            emailInput.text = detail.email; websiteInput.text = detail.website;
            foreach (var item in detail.items) if (item.item_type == "SKILL") cloudSkills.Add(item.title);
            RenderStep();
        }

        public void Begin()
        {
            ClearCompletionCard();
            cloudEditClosed = null;
            cloudEditing = false; cloudOriginal = null; pendingCreate = null; cloudCaptureSnapshot = null;
            cloudCreateRequestId = Guid.NewGuid().ToString("D");
            // Server onboarding is an in-memory draft, never a temporary SQLite profile.
            cloudRegistration = CloudModeSession.Mode == ProfileDataMode.Cloud;
            if (cloudRegistration && !CloudProfileApi.CanRequest)
            {
                Debug.LogWarning("[Create Profile] Server authentication required.");
                return;
            }
            saving = false;
            cloudSkills.Clear();
            CloudProfileId = null;
            if (cloudStoreConsent != null) cloudStoreConsent.isOn = false;
            if (cloudPublicConsent != null) cloudPublicConsent.isOn = false;
            if (cloudSubjectPresentConsent != null) cloudSubjectPresentConsent.isOn = false;
            CurrentProfileId = 0;
            CurrentStep = RegistrationStep.BasicInfo;
            ClearInputs();
            if (nextButton != null) nextButton.interactable = true;
            gameObject.SetActive(true);
            if (UnityEngine.Application.isPlaying) BuildWizard();
            RenderStep();
        }

        // Reuse the existing 3/3 onboarding face step for an already saved
        // cloud profile. No POST and no temporary local SQLite profile.
        public void BeginCloudFaceRegistration(string cloudProfileId, CloudProfileDetail detail = null)
        {
            if (!Guid.TryParse(cloudProfileId, out _))
                throw new ArgumentException("서버 프로필 UUID가 올바르지 않습니다.", nameof(cloudProfileId));
            if (CloudModeSession.Mode != ProfileDataMode.Cloud || !CloudProfileApi.CanRequest)
            {
                Debug.LogWarning("[Create Profile] Server face enrollment requires authentication.");
                return;
            }
            ClearCompletionCard();
            cloudRegistration = true;
            cloudEditing = false;
            cloudEditClosed = null;
            CloudProfileId = cloudProfileId;
            cloudCaptureSnapshot = null;
            saving = false;
            cloudSkills.Clear();
            CurrentProfileId = 0;
            CurrentStep = RegistrationStep.FaceChoice;
            ClearInputs();
            if (detail != null && detail.id == cloudProfileId)
            {
                displayNameInput.text = detail.display_name;
                headlineInput.text = detail.headline;
            }
            if (cloudStoreConsent != null) cloudStoreConsent.isOn = false;
            if (cloudPublicConsent != null) cloudPublicConsent.isOn = false;
            if (cloudSubjectPresentConsent != null) cloudSubjectPresentConsent.isOn = false;
            if (nextButton != null) nextButton.interactable = true;
            gameObject.SetActive(true);
            if (UnityEngine.Application.isPlaying) BuildWizard();
            RenderStep();
        }

        private void OnEnable()
        {
            if (!UnityEngine.Application.isPlaying) return;
            BuildWizard();
            RenderStep();
        }

        private void OnDisable()
        {
            // Unity stops coroutines on deactivation; avoid leaving the button locked.
            saving = false;
            if (nextButton != null) nextButton.interactable = true;
        }

        public void Save()
        {
            if (saving) return;
            if (cloudRegistration)
            {
                SaveCloudStep();
                return;
            }
            saving = true;
            try
            {
                if (CurrentStep == RegistrationStep.BasicInfo)
                {
                    Profile profile = CurrentProfileId == 0
                        ? bootstrap.CreateProfileUseCase.Execute(displayNameInput.text, headlineInput.text,
                            string.Empty, emailInput.text, websiteInput.text, DateTimeOffset.UtcNow)
                        : bootstrap.UpdateProfileUseCase.Execute(CurrentProfileId, displayNameInput.text,
                            headlineInput.text, bootstrap.GetProfileUseCase.Execute(CurrentProfileId).Bio,
                            emailInput.text, websiteInput.text, DateTimeOffset.UtcNow);
                    CurrentProfileId = profile.Id;
                    CurrentStep = RegistrationStep.Skills;
                    RenderStep();
                }
                else if (CurrentStep == RegistrationStep.Skills)
                {
                    CurrentStep = RegistrationStep.FaceChoice;
                    RenderStep();
                }
                else if (CurrentStep == RegistrationStep.FaceChoice)
                    OpenFaceRegistration();
                else
                    OpenProfile();
            }
            catch (Exception ex)
            {
                ShowMessage(ex is ArgumentException argument && argument.ParamName == "displayName"
                    ? (string.IsNullOrWhiteSpace(displayNameInput.text) ? "이름을 입력해 주세요." : "이름은 100자 이내로 입력해 주세요.")
                    : ex.Message);
                Debug.LogWarning($"[Create Profile] 저장 실패: {ex.Message}", this);
            }
            finally
            {
                saving = false;
                if (nextButton != null) nextButton.interactable = true;
            }
        }

        private void SaveCloudStep()
        {
            if (CurrentStep == RegistrationStep.Complete) { OpenProfile(); return; }
            if (CloudModeSession.Mode != ProfileDataMode.Cloud ||
                !CloudProfileApi.CanRequest)
            {
                ShowMessage("서버 인증 연결이 해제되었습니다. 목록에서 다시 확인해 주세요.");
                return;
            }
            if (CurrentStep == RegistrationStep.FaceChoice)
            {
                if (!CloudFaceRuntime.Available && !CloudFaceLocalTestFactory.IsLocalCaptureEnabled)
                {
                    ShowMessage("서버 얼굴 기능이 활성화되지 않았습니다. 얼굴 등록을 건너뛰어 프로필을 완료할 수 있습니다.");
                    return;
                }
                if (string.IsNullOrEmpty(CloudProfileId))
                {
                    ShowMessage("저장된 서버 프로필을 확인할 수 없습니다. 목록에서 확인해 주세요.");
                    return;
                }
                if (cloudStoreConsent == null || !cloudStoreConsent.isOn)
                {
                    ShowMessage("서버에 얼굴 특징 정보를 보관하려면 저장 동의가 필요합니다. 타 기기 인식 공개는 선택 사항입니다.");
                    return;
                }
                if ((CloudFaceRuntime.Available || CloudFaceLocalTestFactory.IsLocalCaptureEnabled) &&
                    (cloudSubjectPresentConsent == null || !cloudSubjectPresentConsent.isOn))
                {
                    ShowMessage("현장 촬영 대상자가 직접 고지를 확인하고 동의했는지 확인해 주세요.");
                    return;
                }
                if (CloudFaceRuntime.Available)
                {
                    if (cloudCaptureSnapshot == null) StartCoroutine(OpenCloudFaceWithApprovalSnapshot());
                    else faceRegistrationScreen.ShowCloud(CloudProfileId, gameObject, OnCloudFaceRegistrationReturned,
                        cloudPublicConsent.isOn, cloudSubjectPresentConsent.isOn, cloudStoreConsent.isOn, cloudCaptureSnapshot);
                    return;
                }
                faceRegistrationScreen.ShowCloud(CloudProfileId, gameObject,
                    OnCloudFaceRegistrationReturned,
                    cloudPublicConsent != null && cloudPublicConsent.isOn,
                    cloudSubjectPresentConsent != null && cloudSubjectPresentConsent.isOn,
                    storageConsent: cloudStoreConsent.isOn);
                return;
            }
            if (!cloudEditing && !string.IsNullOrEmpty(CloudProfileId))
            {
                ShowMessage("서버 프로필은 이미 생성됐습니다. 목록에서 수정해 주세요.");
                return;
            }
            var payload = CloudProfileWritePayload.FromRegistration(
                displayNameInput.text, headlineInput.text, bioInput.text,
                emailInput.text, websiteInput.text, cloudSkills);
            if (cloudEditing)
            {
                payload.items = cloudOriginal.items.Where(item => item.item_type != "SKILL").Concat(payload.items).ToArray();
                payload.business_card_theme = cloudOriginal.business_card_theme;
                payload.is_active = cloudOriginal.is_active;
            }
            if (!CloudProfileWritePayload.TryToJson(payload, out _, out string error))
            {
                ShowMessage(error);
                return;
            }

            if (CurrentStep == RegistrationStep.BasicInfo)
            {
                CurrentStep = RegistrationStep.Skills;
                RenderStep();
                return;
            }
            if (CurrentStep == RegistrationStep.Skills)
                StartCoroutine(CreateCloudProfile(payload));
        }

        private IEnumerator OpenCloudFaceWithApprovalSnapshot()
        {
            saving = true;
            nextButton.interactable = false;
            string id = CloudProfileId;
            CloudFaceStatusData snapshot = null;
            yield return new CloudFaceHttpsTransport().GetStatus(id, null, (_, data) => snapshot = data);
            saving = false;
            if (!isActiveAndEnabled || CloudProfileId != id || !CloudFaceRuntime.Available) yield break;
            nextButton.interactable = true;
            if (snapshot == null || snapshot.revision < 0 || snapshot.purge_epoch < 0 || snapshot.consent_generation < 0 ||
                !cloudStoreConsent.isOn || !cloudSubjectPresentConsent.isOn)
            {
                ShowMessage("현재 서버 얼굴 등록 상태와 동의를 확인하지 못했습니다. 다시 확인해 주세요.");
                yield break;
            }
            cloudCaptureSnapshot = snapshot;
            if (!snapshot.registered)
            {
                faceRegistrationScreen.ShowCloud(id, gameObject, OnCloudFaceRegistrationReturned,
                    cloudPublicConsent.isOn, cloudSubjectPresentConsent.isOn, cloudStoreConsent.isOn, snapshot);
                yield break;
            }
            ShowMessage("현재 얼굴 등록: " + (snapshot.registered ? "완료" : "없음") +
                " · 공개 인식: " + (snapshot.discoverable ? "허용" : "중지") +
                "\n선택한 동의를 확인한 뒤 촬영 시작을 눌러 주세요.");
            SetButtonLabel(nextButton, "동의하고 촬영 시작");
        }

        private IEnumerator CreateCloudProfile(CloudProfileWritePayload payload)
        {
            saving = true;
            nextButton.interactable = false;
            ShowMessage("서버에 프로필을 등록하고 있습니다...");
            string createdId = null;
            string failure = null;
            Action<string, string> completed = (id, error) => { createdId = id; failure = error; };
            if (cloudEditing) yield return CloudProfileWriteApi.Replace(CloudProfileId, payload, completed);
            else
            {
                if (pendingCreate == null) pendingCreate = JsonUtility.FromJson<CloudProfileWritePayload>(JsonUtility.ToJson(payload));
                yield return CloudProfileWriteApi.Create(pendingCreate, cloudCreateRequestId, completed);
            }
            saving = false;
            if (nextButton != null) nextButton.interactable = true;
            if (!isActiveAndEnabled || !cloudRegistration) yield break;
            if (!string.IsNullOrEmpty(createdId))
            {
                // Keep the server UUID and offer the optional third face step;
                // never create a temporary local SQLite profile.
                if (cloudEditing) { CloseToList(); yield break; }
                CloudProfileId = createdId;
                CurrentStep = RegistrationStep.FaceChoice;
                RenderStep();
                yield break;
            }
            // A timeout can occur after the server committed the POST.
            // Keep the draft intact, and ask for a list check before retrying.
            ShowMessage((failure ?? "서버 저장 결과를 확인할 수 없습니다.") +
                " 재시도하면 최초 입력과 같은 요청으로 저장 결과를 확인합니다.");
        }

        public void Back()
        {
            if (saving) return;
            if (cloudRegistration)
            {
                if (CurrentStep == RegistrationStep.BasicInfo ||
                    !string.IsNullOrEmpty(CloudProfileId)) CloseToList();
                else
                {
                    CurrentStep = RegistrationStep.BasicInfo;
                    RenderStep();
                }
                return;
            }
            if (CurrentStep == RegistrationStep.BasicInfo || CurrentStep == RegistrationStep.Complete)
            {
                CloseToList();
                return;
            }
            CurrentStep--;
            if (CurrentStep == RegistrationStep.BasicInfo) RestoreInputs();
            RenderStep();
        }

        public void SkipFaceRegistration()
        {
            if (CurrentStep != RegistrationStep.FaceChoice) return;
            if (cloudRegistration)
            {
                if (!string.IsNullOrEmpty(CloudProfileId))
                {
                    CurrentStep = RegistrationStep.Complete;
                    RenderStep();
                }
                return;
            }
            if (CurrentProfileId <= 0) return;
            CurrentStep = RegistrationStep.Complete;
            RenderStep();
        }

        private void OpenFaceRegistration()
        {
            // Only navigation is supplied here; capture, quality gates and DB writes remain unchanged.
            faceRegistrationScreen.Show(CurrentProfileId, gameObject, OnFaceRegistrationReturned);
        }

        private void OnFaceRegistrationReturned(long profileId, bool completed)
        {
            if (profileId != CurrentProfileId) return;
            CurrentStep = completed ? RegistrationStep.Complete : RegistrationStep.FaceChoice;
            RenderStep();
        }

        private void OnCloudFaceRegistrationReturned(string cloudId, bool completed)
        {
            cloudCaptureSnapshot = null;
            if (!cloudRegistration || cloudId != CloudProfileId) return;
            if (completed)
            {
                CurrentStep = RegistrationStep.Complete;
                RenderStep();
            }
            else
            {
                CurrentStep = RegistrationStep.FaceChoice;
                RenderStep();
                ShowMessage("얼굴 등록은 완료되지 않았습니다. 프로필 정보는 서버에 보관되어 있습니다.");
            }
        }

        public void ToggleSkill(string title)
        {
            if (CurrentStep != RegistrationStep.Skills || saving) return;
            if (cloudRegistration)
            {
                string skill = title?.Trim();
                if (string.IsNullOrEmpty(skill) || skill.Length > 150)
                {
                    ShowMessage("기술 이름은 1~150자로 입력해 주세요.");
                    return;
                }
                int existingIndex = cloudSkills.FindIndex(item =>
                    skillChipPrefab.IsSameSkillName(item, skill));
                if (existingIndex >= 0) cloudSkills.RemoveAt(existingIndex);
                else
                {
                    if (cloudSkills.Count >= 45)
                    {
                        ShowMessage("기술은 최대 45개까지 선택할 수 있습니다.");
                        return;
                    }
                    cloudSkills.Add(skill);
                }
                RefreshSkills();
                ShowMessage(string.Empty);
                return;
            }
            if (CurrentProfileId <= 0) return;
            try
            {
                Profile profile = bootstrap.GetProfileUseCase.Execute(CurrentProfileId);
                ProfileItem existing = profile.Items.FirstOrDefault(item => item.ItemType == ProfileItemType.Skill &&
                    skillChipPrefab.IsSameSkillName(item.Title, title));
                if (existing == null)
                    bootstrap.AddSkillUseCase.Execute(CurrentProfileId, title, DateTimeOffset.UtcNow);
                else
                    bootstrap.DeleteProfileItemUseCase.Execute(CurrentProfileId, existing.Id, DateTimeOffset.UtcNow);
                RefreshSkills();
                ShowMessage(string.Empty);
            }
            catch (Exception ex) { ShowMessage(ex.Message); }
        }

        private void AddDirectSkill()
        {
            string title = skillSearch.text.Trim();
            if (cloudRegistration)
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    ShowMessage("추가할 기술을 입력해 주세요.");
                    return;
                }
                if (cloudSkills.Any(item => skillChipPrefab.IsSameSkillName(item, title)))
                {
                    ShowMessage("이미 선택한 기술입니다.");
                    return;
                }
                ToggleSkill(title);
                return;
            }
            Profile profile = bootstrap.GetProfileUseCase.Execute(CurrentProfileId);
            if (profile.Items.Any(item => item.ItemType == ProfileItemType.Skill &&
                skillChipPrefab.IsSameSkillName(item.Title, title)))
            {
                ShowMessage("이미 선택한 기술입니다.");
                return;
            }
            ToggleSkill(title);
        }

        private void RenderStep()
        {
            if (titleText == null || basicControls == null) return;
            if (nextButton != null) nextButton.interactable = !saving;
            bool basic = CurrentStep == RegistrationStep.BasicInfo;
            foreach (GameObject control in basicControls)
                control.SetActive(basic && (cloudRegistration ||
                    (control != bioInput.gameObject && control.name != "BioLabel")));
            skillsRoot.gameObject.SetActive(CurrentStep == RegistrationStep.Skills);
            faceRoot.gameObject.SetActive(CurrentStep == RegistrationStep.FaceChoice);
            if (faceBenefitLabel != null)
            {
                LayoutElement guideLayout = faceBenefitLabel.GetComponent<LayoutElement>();
                if (guideLayout != null)
                    guideLayout.minHeight = guideLayout.preferredHeight = cloudRegistration ? 300 : 240;
                faceBenefitLabel.text = cloudRegistration
                    ? (CloudFaceRuntime.Available
                        ? "얼굴을 등록하면 카메라에서 내 명함을 찾을 수 있습니다.\n\n특징값은 서버에 암호화 보관하며 사진이 없으면 정면 사진을 작은 명함 사진으로 저장합니다. 원본 영상은 전송하지 않습니다. 공개 인식에 동의하면 이름·직무·사진과 소개·주요 기술·대표 프로젝트·링크를 보여줍니다. 이메일은 공개하지 않습니다. 프로필에서 공개 인식을 중지하거나 얼굴정보를 삭제할 수 있습니다. 등록은 나중에 해도 됩니다."
                        : CloudFaceLocalTestFactory.IsLocalCaptureEnabled
                        ? "로컬 테스트 전용: 정면·좌측·우측 얼굴 특징값 3개를 127.0.0.1의 테스트 Worker와 암호화된 로컬 D1에 보관합니다. 원본 사진·영상은 서버로 보내지 않습니다.\n\n실제 촬영 대상자의 동의가 있어야 합니다. 테스트 종료 후 로컬 얼굴정보를 삭제해 주세요. 다른 기기 인식은 아직 지원하지 않습니다."
                        : "서버 얼굴 기능이 아직 활성화되지 않았습니다. 얼굴 등록을 건너뛰어 텍스트 프로필을 완료할 수 있습니다.")
                    : "얼굴을 등록하면 카메라에서 사용자를 인식해 명함을 자동으로 표시할 수 있습니다.\n\n얼굴 등록은 선택 사항입니다. 나중에 프로필에서 등록할 수 있습니다.";
            }
            if (cloudStoreConsent != null) cloudStoreConsent.gameObject.SetActive(cloudRegistration && CurrentStep == RegistrationStep.FaceChoice);
            if (cloudPublicConsent != null) cloudPublicConsent.gameObject.SetActive(cloudRegistration && CurrentStep == RegistrationStep.FaceChoice);
            if (cloudSubjectPresentConsent != null)
                cloudSubjectPresentConsent.gameObject.SetActive(cloudRegistration &&
                    CurrentStep == RegistrationStep.FaceChoice && (CloudFaceRuntime.Available || CloudFaceLocalTestFactory.IsLocalCaptureEnabled));
            completeRoot.gameObject.SetActive(CurrentStep == RegistrationStep.Complete);
            foreach (string name in new[] { "EnrichmentLabel", "프로젝트 추가", "링크 추가", "자기소개 작성" })
            {
                var extra = completeRoot.Find(name);
                if (extra != null) extra.gameObject.SetActive(!cloudRegistration);
            }
            if (cloudRegistration)
            {
                if (CurrentStep == RegistrationStep.FaceChoice && nextButton != null)
                    nextButton.interactable = CloudFaceRuntime.Available || CloudFaceLocalTestFactory.IsLocalCaptureEnabled;
                titleText.text = CurrentStep switch
                {
                    RegistrationStep.BasicInfo => "1/3 기본 정보",
                    RegistrationStep.Skills => "2/3 주요 기술",
                    RegistrationStep.FaceChoice => "3/3 얼굴 등록",
                    _ => "프로필이 만들어졌습니다."
                };
                SetButtonLabel(nextButton, CurrentStep switch
                {
                    RegistrationStep.Skills => "등록하기",
                    RegistrationStep.Complete => "프로필 확인",
                    RegistrationStep.FaceChoice => CloudFaceRuntime.Available ? (cloudCaptureSnapshot == null ? "동의하고 얼굴 등록" : "동의하고 다시 등록") : CloudFaceLocalTestFactory.IsLocalCaptureEnabled
                        ? "개발용 로컬 서버 얼굴 등록" : "서버 얼굴 등록 준비 중",
                    _ => "다음"
                });
            }
            else
            {
                titleText.text = CurrentStep switch
                {
                    RegistrationStep.BasicInfo => "1/3 기본 정보",
                    RegistrationStep.Skills => "2/3 주요 기술",
                    RegistrationStep.FaceChoice => "3/3 얼굴 등록",
                    _ => "프로필이 만들어졌습니다."
                };
                SetButtonLabel(nextButton, CurrentStep switch
                {
                    RegistrationStep.FaceChoice => "얼굴 등록하기",
                    RegistrationStep.Complete => "프로필 보기",
                    _ => "다음"
                });
            }
            ShowMessage(string.Empty);
            var navigator = GetComponent<InputFieldFocusNavigator>();
            if (navigator != null)
                navigator.Configure(basic
                    ? (cloudRegistration
                        ? new[] { displayNameInput, headlineInput, bioInput, emailInput, websiteInput }
                        : new[] { displayNameInput, headlineInput, emailInput, websiteInput })
                    : CurrentStep == RegistrationStep.Skills ? new[] { skillSearch } : Array.Empty<InputField>(), nextButton);
            if (CurrentStep == RegistrationStep.Skills) RefreshSkills();
            if (CurrentStep == RegistrationStep.Complete) RefreshCompletionCard();
            LayoutRebuilder.ForceRebuildLayoutImmediate(formContent);
            if (formScroll != null) { formScroll.StopMovement(); formScroll.verticalNormalizedPosition = 1f; }
        }

        private void RefreshSkills()
        {
            if (recommendedGrid == null) return;
            ClearChildren(recommendedGrid);
            ClearChildren(selectedGrid);
            List<string> selected = cloudRegistration
                ? new List<string>(cloudSkills)
                : bootstrap.GetProfileUseCase.Execute(CurrentProfileId).Items
                    .Where(item => item.ItemType == ProfileItemType.Skill)
                    .Select(item => item.Title).ToList();
            skillCount.text = $"선택됨 {selected.Count}개 · 선택한 기술을 다시 누르면 해제됩니다.";
            string query = skillSearch.text.Trim();
            foreach (string name in skillChipPrefab.KnownSkillNames)
            {
                if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                SkillChipView chip = Instantiate(skillChipPrefab, recommendedGrid);
                chip.BindRecommended(name, ToggleSkill, selected.Any(item => skillChipPrefab.IsSameSkillName(item, name)));
                chip.ConfigureContentSizedLayout();
            }
            foreach (string skill in selected)
            {
                SkillChipView chip = Instantiate(skillChipPrefab, selectedGrid);
                chip.BindRecommended(skill, ToggleSkill, true);
                chip.ConfigureContentSizedLayout();
            }
            UpdateGrid(recommendedGrid);
            UpdateGrid(selectedGrid);
        }

        private void RefreshCompletionCard()
        {
            ClearCompletionCard();
            // Reuse the same authored, themed card and its existing Profile/Avatar binding.
            completionCard = cloudRegistration
                ? businessCardPanel.CreateCloudRecognitionCard(CloudProfileId, displayNameInput.text, headlineInput.text, cardSlot)
                : businessCardPanel.CreateRecognitionCard(CurrentProfileId, cardSlot);
            ResizeCompletionCard();
            if (cloudRegistration && CloudProfileApi.CanRequest && CloudServerConfiguration.RemoteFacesEnabled)
                StartCoroutine(LoadCloudCompletionCard(CloudProfileId, completionCard));
        }

        private IEnumerator LoadCloudCompletionCard(string id, BusinessCardView expectedCard)
        {
            CloudProfileDetail detail = null;
            yield return CloudProfileApi.FetchDetail(id, (data, _) => detail = data);
            if (!isActiveAndEnabled || !cloudRegistration || CloudProfileId != id ||
                CurrentStep != RegistrationStep.Complete || completionCard != expectedCard || detail == null ||
                !CloudProfileApi.CanRequest) yield break;
            ClearCompletionCard();
            completionCard = businessCardPanel.CreateCloudRecognitionCard(id, detail.display_name, detail.headline,
                cardSlot, detail.avatar_base64, detail.business_card_theme);
            if (completionCard != null) completionCard.BindCloud(detail);
            ResizeCompletionCard();
        }

        private void ClearCompletionCard()
        {
            if (completionCard == null) return;
            completionCard.gameObject.SetActive(false);
            Destroy(completionCard.gameObject);
            completionCard = null;
        }

        private void LateUpdate()
        {
            if (CurrentStep == RegistrationStep.Complete) ResizeCompletionCard();
        }

        private void ResizeCompletionCard()
        {
            if (completionCard == null || cardSlot == null) return;
            float scale = Mathf.Min(1f, Mathf.Max(1f, cardSlot.rect.width) / 900f);
            RectTransform card = (RectTransform)completionCard.transform;
            card.anchorMin = card.anchorMax = new Vector2(.5f, 1f);
            card.pivot = new Vector2(.5f, 1f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(900, 360);
            card.localScale = Vector3.one * scale;
            LayoutElement slotLayout = cardSlot.GetComponent<LayoutElement>();
            if (!Mathf.Approximately(slotLayout.preferredHeight, 360 * scale))
                slotLayout.minHeight = slotLayout.preferredHeight = 360 * scale;
        }

        public void OpenProfile()
        {
            if (cloudRegistration)
            {
                string id = CloudProfileId;
                if (string.IsNullOrEmpty(id)) return;
                CloseToList();
                profileListScreen.OpenCloudProfileDetail(id);
                return;
            }
            if (CurrentProfileId <= 0) return;
            profileDetailScreen.Show(CurrentProfileId);
            ClearCompletionCard();
            gameObject.SetActive(false);
        }

        private void OpenProject() { OpenProfile(); profileDetailScreen.OpenAddProjectPanel(); }
        private void OpenLink() { OpenProfile(); profileDetailScreen.OpenAddLinkPanel(); }
        private void OpenBio() { OpenProfile(); profileDetailScreen.OpenEdit(); }

        public void OpenRecognitionCamera()
        {
            CloseToList();
            profileListScreen.OpenRecognitionCamera();
        }

        private void CloseToList()
        {
            var returnToDetail = cloudEditing ? cloudEditClosed : null;
            string returnId = CloudProfileId;
            cloudEditClosed = null;
            ClearCompletionCard();
            gameObject.SetActive(false);
            cloudRegistration = false;
            CloudProfileId = null;
            if (cloudStoreConsent != null) cloudStoreConsent.isOn = false;
            if (cloudPublicConsent != null) cloudPublicConsent.isOn = false;
            if (cloudSubjectPresentConsent != null) cloudSubjectPresentConsent.isOn = false;
            cloudSkills.Clear();
            profileListScreenRoot.SetActive(true);
            // ProfileListScreen refreshes on re-entry; persisted Profile/Skills are never rolled back.
            CurrentProfileId = 0;
            CurrentStep = RegistrationStep.BasicInfo;
            ClearInputs();
            returnToDetail?.Invoke(returnId);
        }

        private void RestoreInputs()
        {
            if (CurrentProfileId <= 0) return;
            Profile profile = bootstrap.GetProfileUseCase.Execute(CurrentProfileId);
            displayNameInput.text = profile.DisplayName;
            headlineInput.text = profile.Headline;
            emailInput.text = profile.Email;
            websiteInput.text = profile.Website;
        }

        private void ClearInputs()
        {
            displayNameInput.text = headlineInput.text = bioInput.text = emailInput.text = websiteInput.text = string.Empty;
        }

        private void ShowMessage(string message)
        {
            if (messageText == null) return;
            messageText.text = message;
            messageText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        private void BuildWizard()
        {
            if (skillsRoot != null) return;
            if (formContent == null || nextButton == null || labelTemplate == null || titleText == null ||
                skillChipPrefab == null || faceRegistrationScreen == null || profileDetailScreen == null || businessCardPanel == null)
                throw new InvalidOperationException("최초 등록 화면의 UI / Navigation 연결이 필요합니다.");
            basicControls = formContent.Cast<Transform>().Select(child => child.gameObject).ToArray();
            messageText = MakeLabel(formContent, "ValidationMessage", string.Empty, 90);
            messageText.transform.SetAsFirstSibling();
            messageText.color = new Color(.65f, .15f, .22f);
            skillsRoot = MakeStep("SkillsStep");
            MakeLabel(skillsRoot, "SkillGuide", "주요 기술을 선택하세요. 기술 입력은 나중에도 가능합니다.", 90);
            skillSearch = Instantiate(headlineInput, skillsRoot);
            skillSearch.name = "SkillSearch";
            skillSearch.text = string.Empty;
            skillSearch.onValueChanged = new InputField.OnChangeEvent();
            skillSearch.onEndEdit = new InputField.EndEditEvent();
            if (skillSearch.placeholder is Text placeholder) placeholder.text = "기술 검색 또는 직접 입력";
            skillSearch.onValueChanged.AddListener(_ => RefreshSkills());
            MakeButton(skillsRoot, "직접 추가", AddDirectSkill);
            skillCount = MakeLabel(skillsRoot, "SkillCount", string.Empty, 90);
            MakeLabel(skillsRoot, "SelectedLabel", "선택한 기술", 60);
            selectedGrid = MakeGrid(skillsRoot, "SelectedSkillGrid");
            MakeLabel(skillsRoot, "RecommendedLabel", "추천 기술", 60);
            recommendedGrid = MakeGrid(skillsRoot, "RecommendedSkillGrid");
            faceRoot = MakeStep("FaceChoiceStep");
            faceBenefitLabel = MakeLabel(faceRoot, "FaceBenefit", "얼굴을 등록하면 카메라에서 사용자를 인식해 명함을 자동으로 표시할 수 있습니다.\n\n얼굴 등록은 선택 사항입니다. 나중에 프로필에서 등록할 수 있습니다.", 240);
            cloudStoreConsent = MakeConsentToggle(faceRoot, "CloudFaceStoreConsent",
                "얼굴 특징 정보를 서버에 보관하는 데 동의합니다.");
            cloudPublicConsent = MakeConsentToggle(faceRoot, "CloudFaceRecognitionConsent",
                "다른 참가자의 카메라에서 내 얼굴을 인식하고 공개 명함을 표시하는 데 동의합니다.");
            cloudSubjectPresentConsent = MakeConsentToggle(faceRoot,
                "CloudFaceSubjectPresentConsent",
                "촬영 대상자 본인이 안내를 확인하고 얼굴 등록에 동의합니다.");
            MakeButton(faceRoot, "나중에 하기", SkipFaceRegistration, true);
            completeRoot = MakeStep("ProfileCompleteStep");
            MakeLabel(completeRoot, "CompleteGuide", "등록한 정보를 확인해 주세요. 얼굴 등록과 공개 인식에 동의한 명함은 카메라에서도 확인할 수 있습니다.", 130);
            cardSlot = new GameObject("ExistingBusinessCardPreview", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
            cardSlot.gameObject.layer = gameObject.layer;
            cardSlot.SetParent(completeRoot, false);
            cardSlot.GetComponent<LayoutElement>().preferredHeight = 360;
            MakeButton(completeRoot, "카메라로 명함 보기", OpenRecognitionCamera);
            MakeLabel(completeRoot, "EnrichmentLabel", "프로필 더 풍부하게 만들기 · 선택 사항", 90);
            MakeButton(completeRoot, "프로젝트 추가", OpenProject, true);
            MakeButton(completeRoot, "링크 추가", OpenLink, true);
            MakeButton(completeRoot, "자기소개 작성", OpenBio, true);
        }

        private Toggle MakeConsentToggle(Transform parent, string objectName, string caption)
        {
            var row = new GameObject(objectName, typeof(RectTransform), typeof(LayoutElement),
                typeof(HorizontalLayoutGroup), typeof(Image), typeof(Toggle));
            row.layer = gameObject.layer;
            row.transform.SetParent(parent, false);
            var background = row.GetComponent<Image>();
            background.color = Color.clear;
            background.raycastTarget = true;
            var layout = row.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 110;
            var horizontal = row.GetComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 14;
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlHeight = true;
            horizontal.childControlWidth = true;
            horizontal.childForceExpandHeight = false;
            horizontal.childForceExpandWidth = false;

            var square = new GameObject("Checkbox", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            square.layer = row.layer;
            square.transform.SetParent(row.transform, false);
            var squareLayout = square.GetComponent<LayoutElement>();
            squareLayout.minWidth = squareLayout.preferredWidth = 48;
            squareLayout.minHeight = squareLayout.preferredHeight = 48;
            var image = square.GetComponent<Image>();
            image.color = new Color(.86f, .88f, .95f);
            var mark = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            mark.layer = row.layer;
            mark.transform.SetParent(square.transform, false);
            var markRect = (RectTransform)mark.transform;
            markRect.anchorMin = Vector2.zero;
            markRect.anchorMax = Vector2.one;
            markRect.offsetMin = new Vector2(9, 9);
            markRect.offsetMax = new Vector2(-9, -9);
            var checkImage = mark.GetComponent<Image>();
            checkImage.color = new Color(.30f, .28f, .64f);
            var toggle = row.GetComponent<Toggle>();
            toggle.targetGraphic = image;
            toggle.graphic = checkImage;
            toggle.isOn = false;
            Text label = Instantiate(labelTemplate, row.transform);
            label.name = "ConsentDescription";
            label.text = caption;
            label.fontSize = 22;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            var textLayout = label.GetComponent<LayoutElement>() ?? label.gameObject.AddComponent<LayoutElement>();
            textLayout.flexibleWidth = 1;
            textLayout.minHeight = textLayout.preferredHeight = 110;
            label.gameObject.SetActive(true);
            return toggle;
        }

        private RectTransform MakeStep(string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            root.gameObject.layer = gameObject.layer;
            root.SetParent(formContent, false);
            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 20;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return root;
        }

        private Text MakeLabel(Transform parent, string name, string value, float height)
        {
            Text label = Instantiate(labelTemplate, parent);
            label.name = name;
            label.text = value;
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            LayoutElement layout = label.GetComponent<LayoutElement>() ?? label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
            label.gameObject.SetActive(true);
            return label;
        }

        private Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction action, bool secondary = false)
        {
            Button button = Instantiate(nextButton, parent);
            button.name = label;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            SetButtonLabel(button, label);
            LayoutElement layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 86;
            if (secondary && button.targetGraphic != null) button.targetGraphic.color = new Color(.85f, .95f, 1f);
            button.gameObject.SetActive(true);
            return button;
        }

        private static void SetButtonLabel(Button button, string value)
        {
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.text = value;
        }

        private RectTransform MakeGrid(Transform parent, string name)
        {
            var grid = new GameObject(name, typeof(RectTransform), typeof(AppPofo.Presentation.Common.SkillChipFlowLayout)).GetComponent<RectTransform>();
            grid.gameObject.layer = gameObject.layer;
            grid.SetParent(parent, false);
            return grid;
        }

        private static void UpdateGrid(RectTransform grid)
        {
            if (grid == null) return;
            LayoutRebuilder.MarkLayoutForRebuild(grid);
        }

        private static void ClearChildren(Transform root)
        {
            foreach (Transform child in root)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
    }
}
