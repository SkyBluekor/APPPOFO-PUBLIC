using AppPofo.Domain.Entities;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Components;
using AppPofo.Domain.ValueObjects;
using AppPofo.Presentation.FaceRegistration;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;

namespace AppPofo.Presentation.Screens
{
    public partial class ProfileDetailScreen : MonoBehaviour
    {
        [SerializeField] private AppPofoBootstrap bootstrap;
        [SerializeField] private Text displayNameText;
        [SerializeField] private Text helineText;
        [SerializeField] private Text bioText;
        [SerializeField] private Text emailText;
        [SerializeField] private Text websiteText;
        [SerializeField] private GameObject profileListScreenRoot;
        [SerializeField] private EditProfileScreen editProfileScreen;
        [SerializeField] private GameObject deleteConfirmPanel;
        [SerializeField] private Text confirmationTitle;
        [SerializeField] private Text confirmationMessage;
        [SerializeField] private Text confirmationActionLabel;
        private long pendingFaceRegistrationProfileId;
        private string deleteTitle, deleteMessage, deleteActionLabel;
        [SerializeField] private ProfileListScreen profileListScreen;
        [SerializeField] private GameObject skillSection;
        [SerializeField] private Transform skillGrid;
        [SerializeField] private SkillChipView skillChipPrefab;
        [SerializeField] private GameObject addSkillPanel;
        [SerializeField] private InputField skillInput;
        [SerializeField] private Transform recommendedSkillGrid;

        [SerializeField] private GameObject addProjectPanel;
        [SerializeField] private InputField projectTitleInput;
        [SerializeField] private InputField projectDescriptionInput;
        [SerializeField] private InputField projectUrlInput;

        [SerializeField] private Transform projectList;
        [SerializeField] private ProjectItemView projectItemPrefab;

        [SerializeField] private GameObject projectDetailPanel;
        [SerializeField] private Text projectDetailTitleText;
        [SerializeField] private Text projectDetailDescriptionText;
        [SerializeField] private Text projectDetailUrlText;
        [SerializeField] private Button projectDetailOpenUrlButton;
        [SerializeField] private GameObject addLinkPanel;
        [SerializeField] private InputField linkTitleInput;
        [SerializeField] private InputField linkUrlInput;

        [SerializeField] private GameObject editSkillPanel;
        [SerializeField] private InputField editSkillInput;
        [SerializeField] private Text projectFormTitleText;
        [SerializeField] private Text projectSubmitButtonText;

        [SerializeField] private ScrollRect profileScrollView;
        [SerializeField] private RectTransform profileContent;
        [SerializeField] private FaceRegistrationScreen faceRegistrationScreen;
        [SerializeField] private Text faceActionLabel;

        private void OnEnable()
        {
            RefreshFaceAction();
        }

        private void RefreshFaceAction()
        {
            // Registering a face never turns this profile-detail action into recognition.
            if (faceActionLabel != null)
                faceActionLabel.text = isCloudPresentation ? "얼굴 관리" : "얼굴 등록하기";
        }

        // Keep the existing serialized scene button binding for compatibility.
        // Both a first registration and a re-registration use the same entry point.
        public void OpenFaceCamera()
        {
            OpenFaceRegistration();
        }

        private bool isProjectEditMode;

        private ProfileItem currentEditingSkill;
        [SerializeField] private Transform linkList;
        [SerializeField] private LinkItemView linkItemPrefab;
        [SerializeField] private Sprite projectCardSprite;
        [SerializeField] private Sprite linkCardSprite;

        private ProfileItem currentViewingProject;
        private string currentProjectUrl;
        private long currentProfileId;
        private bool profileLayoutRebuildScheduled;

        public void Show(long profileId)
        {
            PreparePresentation(false);
            ClearProfileDisplay();
            Profile profile = bootstrap.GetProfileUseCase.Execute(profileId);

            if(profile == null)
            {
                Debug.Log($"프로필을 찾을 수 없습니다. profileId: {profileId}");
                SetPlainText(displayNameText, "프로필을 찾을 수 없습니다.");
                gameObject.SetActive(true);
                return;
            }

            currentProfileId = profile.Id;
            BindProfile(ProfileDetailViewModel.FromLocal(profile));
            gameObject.SetActive(true);
            StartCoroutine(ResetScrollToTop());
        }

        public void Back()
        {
            if (isCloudPresentation) { cloudBack?.Invoke(); return; }
            gameObject.SetActive(false);
            profileListScreenRoot.SetActive(true);
        }

        public void OpenEdit()
        {
            if (isCloudPresentation) { if (CloudProfileApi.CanRequest) cloudEdit?.Invoke(); return; }
            if (currentProfileId <= 0)
            {
                Debug.LogError("수정할 ProfileId가 설정되지 않았습니다.");
                return;
            }

            editProfileScreen.Show(currentProfileId);
        }

        public void OpenFaceRegistration()
        {
            if (isCloudPresentation) { if (CloudProfileApi.CanRequest) cloudFace?.Invoke(); return; }
            if (currentProfileId <= 0)
            {
                Debug.LogError("얼굴 등록을 열 ProfileId가 설정되지 않았습니다.");
                return;
            }

            if (faceRegistrationScreen == null)
            {
                Debug.LogError("FaceRegistrationScreen이 연결되지 않았습니다.");
                return;
            }

            try
            {
                if (bootstrap == null || !bootstrap.IsInitialized ||
                    bootstrap.FaceEmbeddingRepository == null)
                    throw new InvalidOperationException("얼굴 등록 정보를 조회할 수 없습니다.");

                if (bootstrap.FaceEmbeddingRepository.HasByProfile(currentProfileId))
                {
                    CacheConfirmationText();
                    pendingFaceRegistrationProfileId = currentProfileId;
                    confirmationTitle.text = "얼굴 재등록";
                    confirmationMessage.text = "얼굴 등록이 완료되어 있습니다.\n다시 등록하시겠습니까?";
                    confirmationActionLabel.text = "다시 등록";
                    deleteConfirmPanel.SetActive(true);
                    return;
                }

                CloseDeleteConfirm();
                faceRegistrationScreen.Show(currentProfileId);
            }
            catch (Exception exception)
            {
                Debug.LogError($"얼굴 등록을 열 수 없습니다.\n{exception.Message}", this);
            }
        }

        private void CacheConfirmationText()
        {
            if (deleteTitle != null) return;
            if (deleteConfirmPanel == null || confirmationTitle == null ||
                confirmationMessage == null || confirmationActionLabel == null)
                throw new InvalidOperationException("확인 Dialog가 연결되지 않았습니다.");
            deleteTitle = confirmationTitle.text;
            deleteMessage = confirmationMessage.text;
            deleteActionLabel = confirmationActionLabel.text;
        }

        public void ConfirmProfileAction()
        {
            if (deleteConfirmPanel == null || !deleteConfirmPanel.activeInHierarchy) return;
            if (pendingFaceRegistrationProfileId > 0)
            {
                long profileId = pendingFaceRegistrationProfileId;
                CloseDeleteConfirm();
                if (profileId == currentProfileId && faceRegistrationScreen != null)
                    faceRegistrationScreen.Show(profileId);
                return;
            }
            DeleteProfile();
        }

        public void OpenDeleteConfirm()
        {
            if (isCloudPresentation)
            {
                if (!CloudProfileApi.CanRequest || cloudDelete == null) return;
                CloseDeleteConfirm(); CacheConfirmationText();
                confirmationTitle.text = "서버 프로필 삭제";
                confirmationMessage.text = "이 프로필과 등록된 얼굴정보·프로필 사진을 서버에서 삭제할까요? 삭제 후에는 복구할 수 없습니다.";
                confirmationActionLabel.text = "삭제";
                deleteConfirmPanel.SetActive(true);
                return;
            }
            CloseDeleteConfirm();
            deleteConfirmPanel.SetActive(true);
        }

        public void CloseDeleteConfirm()
        {
            pendingFaceRegistrationProfileId = 0;
            if (deleteTitle != null)
            {
                confirmationTitle.text = deleteTitle;
                confirmationMessage.text = deleteMessage;
                confirmationActionLabel.text = deleteActionLabel;
            }
            if (deleteConfirmPanel != null) deleteConfirmPanel.SetActive(false);
        }

        private void OnDisable()
        {
            CloseDeleteConfirm();
            StopAllCoroutines();
            profileLayoutRebuildScheduled = false;
        }

        public void DeleteProfile()
        {
            if (isCloudPresentation)
            {
                if (CloudProfileApi.CanRequest && deleteConfirmPanel != null && deleteConfirmPanel.activeSelf)
                    cloudDelete?.Invoke();
                return;
            }
            // A re-registration confirmation must never invoke the destructive action.
            if (pendingFaceRegistrationProfileId > 0) return;
            if (currentProfileId <= 0)
            {
                Debug.LogError("삭제할 ProfileID가 설정되지 않았습니다.");
                return;
            }

            try
            {
                bootstrap.DeleteProfileUsecase.Execute(currentProfileId);

                deleteConfirmPanel.SetActive(false);
                gameObject.SetActive(false);

                profileListScreenRoot.SetActive(true);
                profileListScreen.Refresh();

                currentProfileId = 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"프로필 삭제에 실패했습니다. \n{ex.Message}");
            }
        }

        private void BindSkills(Profile profile) => BindSkills(ProfileDetailViewModel.FromLocal(profile).Items);

        private void BindSkills(System.Collections.Generic.IReadOnlyList<ProfileDetailItemViewModel> items)
        {
            // 기존 칩들을 먼저 제거합니다.
            for (int i = skillGrid.childCount - 1; i >= 0; i--)
            {
                GameObject child = skillGrid.GetChild(i).gameObject;

                // Destroy는 프레임 끝에 처리되므로
                // Layout 계산에서 바로 제외되도록 먼저 비활성화합니다.
                child.SetActive(false);
                Destroy(child);
            }

            int skillCount = 0;

            foreach (var item in items)
            {
                if (item.Type != ProfileItemType.Skill)
                {
                    continue;
                }

                SkillChipView chip =
                    Instantiate(skillChipPrefab, skillGrid);

                chip.BindDisplay(item.Title, item.LocalItem != null ? () => OpenEditSkillPanel(item.LocalItem) : (Action)null);
                skillCount++;
            }

            // GridLayoutGroup의 실제 행 수를 명시적으로 반영해
            // SkillSection 배경이 마지막 행까지 항상 감싸도록 합니다.
            UpdateSkillGridPreferredHeight(skillCount);
            ScheduleProfileLayoutRebuild();
        }

        public void OpenAddSkillPanel()
        {
            if (isCloudPresentation) return;
            skillInput.text = "";
            addSkillPanel.SetActive(true);

            Profile profile = bootstrap.GetProfileUseCase.Execute(currentProfileId);
            BindRecommendedSkills(profile);
        }

        public void CloseAddSkillPanel()
        {
            skillInput.text = "";
            addSkillPanel.SetActive(false);
        }        

        public void AddSkill()
        {
            if (isCloudPresentation) return;
            if (currentProfileId <= 0)
            {
                Debug.LogError("기술을 추가할 ProfileId가 설정되지 않았습니다.");
                return;
            }

            try
            {
                Profile updatedProfile = bootstrap.AddSkillUseCase.Execute(
                    currentProfileId,
                    skillInput.text,
                    DateTimeOffset.UtcNow
                );

                skillInput.text = "";
                addSkillPanel.SetActive(false);

                BindSkills(updatedProfile);
            }
            catch(Exception ex)
            {
                Debug.LogError($"기술 추가에 실패했습니다.\n{ex}", this);
            }
        }

        private void BindRecommendedSkills(Profile profile)
        {
            if (recommendedSkillGrid == null || skillChipPrefab == null)
            {
                return;
            }

            for (int i = recommendedSkillGrid.childCount - 1; i >= 0; i--)
            {
                GameObject child = recommendedSkillGrid.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            foreach (string skillName in skillChipPrefab.KnownSkillNames)
            {
                SkillChipView chip = Instantiate(
                    skillChipPrefab,
                    recommendedSkillGrid);

                chip.BindRecommended(
                    skillName,
                    ToggleRecommendedSkill,
                    FindSkill(profile, skillName) != null);
            }
        }

        private void ToggleRecommendedSkill(string skillName)
        {
            if (isCloudPresentation) return;
            if (currentProfileId <= 0 || string.IsNullOrWhiteSpace(skillName))
            {
                return;
            }

            Profile currentProfile =
                bootstrap.GetProfileUseCase.Execute(currentProfileId);
            ProfileItem existingSkill = FindSkill(currentProfile, skillName);

            try
            {
                Profile updatedProfile = existingSkill == null
                    ? bootstrap.AddSkillUseCase.Execute(
                        currentProfileId,
                        skillName,
                        DateTimeOffset.UtcNow)
                    : bootstrap.DeleteProfileItemUseCase.Execute(
                        currentProfileId,
                        existingSkill.Id,
                        DateTimeOffset.UtcNow);

                BindSkills(updatedProfile);
                BindRecommendedSkills(updatedProfile);
            }
            catch (Exception ex)
            {
                Debug.LogError($"추천 기술 변경에 실패했습니다.\n{ex.Message}");
            }
        }

        private ProfileItem FindSkill(Profile profile, string skillName)
        {
            if (profile == null || skillChipPrefab == null)
            {
                return null;
            }

            foreach (ProfileItem item in profile.Items)
            {
                if (item.ItemType == ProfileItemType.Skill &&
                    skillChipPrefab.IsSameSkillName(item.Title, skillName))
                {
                    return item;
                }
            }

            return null;
        }
    
        public void OpenAddProjectPanel()
        {
            if (isCloudPresentation) return;
            isProjectEditMode = false;
            currentViewingProject = null;
            ClearProjectFormInputs();
            SetProjectFormLabels("프로젝트 추가", "추가");

            addProjectPanel.SetActive(true);
        }

        public void CloseAddProjectPanel()
        {
            bool returnToProjectDetail =
                isProjectEditMode && currentViewingProject != null;

            addProjectPanel.SetActive(false);
            ClearProjectFormInputs();
            isProjectEditMode = false;

            if (returnToProjectDetail)
            {
                projectDetailPanel.SetActive(true);
            }
            else
            {
                currentViewingProject = null;
                currentProjectUrl = string.Empty;
            }
        }

        private void BindProjects(Profile profile) => BindProjects(ProfileDetailViewModel.FromLocal(profile).Items);

        private void BindProjects(System.Collections.Generic.IReadOnlyList<ProfileDetailItemViewModel> items)
        {
            for(int i = projectList.childCount -1; i >= 0 ; i--)
            {
                GameObject child = projectList.GetChild(i).gameObject;

                child.SetActive(false);
                Destroy(child);
            }

            foreach(var item in items)
            {
                if(item.Type != ProfileItemType.Project)
                {
                    continue;
                }

                ProjectItemView projectItem = Instantiate(projectItemPrefab, projectList);

                projectItem.BindDisplay(item.Title, () => OpenProjectDetail(item));
                ApplyItemCardSprite(projectItem, projectCardSprite);
            }

            ScheduleProfileLayoutRebuild();
        }

        public void SubmitProject()
        {
            if (isCloudPresentation) return;
            if (isProjectEditMode)
            {
                UpdateProject();
                return;
            }

            AddNewProject();
        }

        // Keeps the existing UnityEvent binding working while the form uses
        // SubmitProject as its common add/edit entry point.
        public void AddProject()
        {
            SubmitProject();
        }

        private void AddNewProject()
        {
            if(currentProfileId <= 0)
            {
                Debug.LogError("프로젝트를 추가할 ProfileId가 설정되지 않았습니다.");
                return;
            }

            try
            {
                Profile updatedProfile = bootstrap.AddProjectUseCase.Execute(
                    currentProfileId,
                    projectTitleInput.text,
                    projectDescriptionInput.text,
                    projectUrlInput.text,
                    DateTimeOffset.UtcNow
                );
                CloseProjectFormAfterSave();

                BindProjects(updatedProfile);
            }
            catch(Exception ex)
            {
                Debug.LogError($"프로젝트 추가에 실패했습니다.\n{ex.Message}");
            }
        }

        private void UpdateProject()
        {
            if (currentProfileId <= 0)
            {
                Debug.LogError("프로젝트를 수정할 ProfileId가 설정되지 않았습니다.");
                return;
            }

            if (currentViewingProject == null ||
                currentViewingProject.ItemType != ProfileItemType.Project)
            {
                Debug.LogError("수정할 프로젝트가 선택되지 않았습니다.");
                return;
            }

            try
            {
                Profile updatedProfile =
                    bootstrap.UpdateProfileItemUseCase.Execute(
                        currentProfileId,
                        currentViewingProject.Id,
                        projectTitleInput.text,
                        projectDescriptionInput.text,
                        projectUrlInput.text,
                        DateTimeOffset.UtcNow);

                CloseProjectFormAfterSave();
                BindProjects(updatedProfile);
            }
            catch (Exception ex)
            {
                Debug.LogError($"프로젝트 수정에 실패했습니다.\n{ex.Message}");
            }
        }

        private void OpenProjectDetail(ProfileDetailItemViewModel item)
        {
            if(item == null)
            {
                Debug.LogError("표시할 프로젝트 정보가 없습니다.");
                return;
            }

            currentViewingProject = item.LocalItem;

            projectDetailTitleText.text = item.Title;
            projectDetailDescriptionText.text = item.Description;

            currentProjectUrl = item.Url;

            if(!ProfileDetailViewModel.CanOpenUrl(currentProjectUrl))
            {
                projectDetailUrlText.text = "등록된 링크가 없습니다.";
                projectDetailOpenUrlButton.interactable = false;    
            }
            else
            {
                projectDetailUrlText.text = currentProjectUrl;
                projectDetailOpenUrlButton.interactable = true;    
            }

            projectDetailPanel.SetActive(true);
        }

        public void CloseProjectDetail()
        {
            projectDetailPanel.SetActive(false);

            currentViewingProject = null;
            currentProjectUrl = string.Empty;
        }

        public void OpenEditProjectPanel()
        {
            if (isCloudPresentation) return;
            if (currentViewingProject == null ||
                currentViewingProject.ItemType != ProfileItemType.Project)
            {
                Debug.LogError("수정할 프로젝트가 선택되지 않았습니다.");
                return;
            }

            isProjectEditMode = true;
            projectTitleInput.text = currentViewingProject.Title;
            projectDescriptionInput.text = currentViewingProject.Description;
            projectUrlInput.text = currentViewingProject.Url;
            SetProjectFormLabels("프로젝트 수정", "수정");

            projectDetailPanel.SetActive(false);
            addProjectPanel.SetActive(true);
        }

        public void DeleteProject()
        {
            if (isCloudPresentation) return;
            if (currentProfileId <= 0)
            {
                Debug.LogError("프로젝트를 삭제할 ProfileId가 설정되지 않았습니다.");
                return;
            }

            if (currentViewingProject == null ||
                currentViewingProject.ItemType != ProfileItemType.Project)
            {
                Debug.LogError("삭제할 프로젝트가 선택되지 않았습니다.");
                return;
            }

            try
            {
                Profile updatedProfile =
                    bootstrap.DeleteProfileItemUseCase.Execute(
                        currentProfileId,
                        currentViewingProject.Id,
                        DateTimeOffset.UtcNow);

                projectDetailPanel.SetActive(false);
                currentViewingProject = null;
                currentProjectUrl = string.Empty;
                BindProjects(updatedProfile);
            }
            catch (Exception ex)
            {
                Debug.LogError($"프로젝트 삭제에 실패했습니다.\n{ex.Message}");
            }
        }

        public void OpenProjectUrl()
        {
            if(!ProfileDetailViewModel.CanOpenUrl(currentProjectUrl))
            {
                return;
            }

            UnityEngine.Application.OpenURL(currentProjectUrl);
        }

        private void ClearProjectFormInputs()
        {
            projectTitleInput.text = string.Empty;
            projectDescriptionInput.text = string.Empty;
            projectUrlInput.text = string.Empty;
        }

        private void SetProjectFormLabels(string title, string submitButtonText)
        {
            if (projectFormTitleText != null)
            {
                projectFormTitleText.text = title;
            }

            if (projectSubmitButtonText != null)
            {
                projectSubmitButtonText.text = submitButtonText;
            }
        }

        private void CloseProjectFormAfterSave()
        {
            addProjectPanel.SetActive(false);
            ClearProjectFormInputs();
            isProjectEditMode = false;
            currentViewingProject = null;
            currentProjectUrl = string.Empty;
        }

        private void ResetProjectInteractionState()
        {
            addProjectPanel.SetActive(false);
            projectDetailPanel.SetActive(false);
            ClearProjectFormInputs();
            isProjectEditMode = false;
            currentViewingProject = null;
            currentProjectUrl = string.Empty;
        }

        public void OpenAddLinkPanel()
        {
            if (isCloudPresentation) return;
            linkTitleInput.text = "";
            linkUrlInput.text = "";

            addLinkPanel.SetActive(true);
        }

        public void CloseAddLinkPanel()
        {
            linkTitleInput.text = "";
            linkUrlInput.text = "";

            addLinkPanel.SetActive(false);
        }   
        private void BindLinks(Profile profile) => BindLinks(ProfileDetailViewModel.FromLocal(profile).Items);

        private void BindLinks(System.Collections.Generic.IReadOnlyList<ProfileDetailItemViewModel> items)
        {
            for (int i = linkList.childCount - 1; i >= 0; i--)
            {
                GameObject child = linkList.GetChild(i).gameObject;

                child.SetActive(false);
                Destroy(child);
            }

            foreach (var item in items)
            {
                if (item.Type != ProfileItemType.Link)
                {
                    continue;
                }

                LinkItemView linkItem =
                    Instantiate(linkItemPrefab, linkList);

                linkItem.BindDisplay(item.Title,
                    ProfileDetailViewModel.CanOpenUrl(item.Url) ? () => UnityEngine.Application.OpenURL(item.Url) : (Action)null,
                    item.LocalItem != null ? () => DeleteLink(item.LocalItem) : (Action)null);
                ApplyItemCardSprite(linkItem, linkCardSprite);
            }

            ScheduleProfileLayoutRebuild();
        }           
        private void OpenLinkUrl(ProfileItem item)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.Url))
            {
                return;
            }

            UnityEngine.Application.OpenURL(item.Url);
        }         
        public void AddLink()
        {
            if (isCloudPresentation) return;
            if (currentProfileId <= 0)
            {
                Debug.LogError("링크를 추가할 ProfileId가 설정되지 않았습니다.");
                return;
            }

            try
            {
                Profile updatedProfile =
                    bootstrap.AddLinkUseCase.Execute(
                        currentProfileId,
                        linkTitleInput.text,
                        linkUrlInput.text,
                        DateTimeOffset.UtcNow);

                linkTitleInput.text = "";
                linkUrlInput.text = "";

                addLinkPanel.SetActive(false);

                BindLinks(updatedProfile);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"링크 추가에 실패했습니다.\n{exception.Message}");
            }
        }         
        private void DeleteLink(ProfileItem item)
        {
            if (isCloudPresentation) return;
            if(item == null || item.ItemType != ProfileItemType.Link)
            {
                 Debug.LogError("삭제할 링크 정보가 올바르지 않습니다.");
                 return;
            }

            if(currentProfileId <= 0)
            {
                Debug.LogError("링크를 삭제할 ProfileID가 설정되지 않았습니다.");
                return;
            }

            try
            {
                Profile updatedProfile = 
                    bootstrap.DeleteProfileItemUseCase.Execute(
                        currentProfileId,
                        item.Id,
                        DateTimeOffset.UtcNow
                    );
                BindLinks(updatedProfile);
            }
            catch(Exception ex)
            {
                Debug.LogError($"링크 삭제에 실패했습니다.\n{ex}");
            }
        }

        private void OpenEditSkillPanel(ProfileItem item)
        {
            if (isCloudPresentation) return;

            if(item == null || item.ItemType != ProfileItemType.Skill)
            {
                Debug.LogError("수정할 기술 정보가 올바르지 않습니다.");
                return;
            }

            currentEditingSkill = item;

            editSkillInput.text = item.Title;
            editSkillPanel.SetActive(true);
        }

        public void CloseEditSkillPanel()
        {
            editSkillInput.text = "";
            currentEditingSkill = null;

            editSkillPanel.SetActive(false);
        }

        public void DeleteSkill()
        {
            if (isCloudPresentation) return;
            if(currentEditingSkill == null)
            {
                Debug.LogError("삭제할 기술이 선택되지 않았습니다.");
                return;
            }

            try
            {
                Profile updatedProfile =
                    bootstrap.DeleteProfileItemUseCase.Execute(
                        currentProfileId,
                        currentEditingSkill.Id,
                        DateTimeOffset.UtcNow
                    );

                currentEditingSkill = null;
                editSkillInput.text = "";
                editSkillPanel.SetActive(false);

                BindSkills(updatedProfile);
            }
            catch(Exception ex)
            {
                Debug.LogError($"기술 삭제에 실패했습니다.\n{ex}");
            }
        }

        public void UpdateSkill()
        {
            if (isCloudPresentation) return;
            if(currentEditingSkill == null)
            {
                Debug.LogError("수정할 기술이 선택되지 않았습니다.");
                return;
            }

            try
            {
                Profile updatedProfile =
                    bootstrap.UpdateProfileItemUseCase.Execute(
                        currentProfileId,
                        currentEditingSkill.Id,
                        editSkillInput.text,
                        string.Empty,
                        string.Empty,
                        DateTimeOffset.UtcNow
                    );

                    currentEditingSkill = null;
                    editSkillInput.text = "";
                    editSkillPanel.SetActive(false);

                    BindSkills(updatedProfile);
            }
            catch(Exception ex)
            {
                Debug.LogError($"기술 수정에 실패했습니다.\n{ex}");
            }
        }

        private IEnumerator ResetScrollToTop()
        {
            yield return null;

            RebuildProfileLayoutNow();

            if(profileScrollView == null)
                yield break;
            
            profileScrollView.StopMovement();
            profileScrollView.velocity = Vector2.zero;

            profileScrollView.verticalNormalizedPosition = 1f;
        }

        private void UpdateSkillGridPreferredHeight(int skillCount)
        {
            RectTransform gridRect = skillGrid as RectTransform;
            GridLayoutGroup grid = skillGrid != null
                ? skillGrid.GetComponent<GridLayoutGroup>()
                : null;

            if (gridRect == null || grid == null)
            {
                return;
            }

            int columns = grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount
                ? Mathf.Max(1, grid.constraintCount)
                : 1;
            int rows = skillCount <= 0
                ? 0
                : Mathf.CeilToInt(skillCount / (float)columns);

            float preferredHeight = grid.padding.top + grid.padding.bottom;
            if (rows > 0)
            {
                preferredHeight += rows * grid.cellSize.y;
                preferredHeight += (rows - 1) * grid.spacing.y;
            }

            // SkillSection의 VerticalLayoutGroup이 이 값을 확실히 사용하도록
            // LayoutElement에 실제 Grid 높이를 전달합니다.
            LayoutElement layoutElement = skillGrid.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = skillGrid.gameObject.AddComponent<LayoutElement>();
            }

            layoutElement.minHeight = preferredHeight;
            layoutElement.preferredHeight = preferredHeight;
            gridRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight);
        }

        private static void ApplyLinkDeleteButtonTheme(LinkItemView itemView)
        {
            if (itemView == null)
            {
                return;
            }

            Button deleteButton = null;
            Button[] buttons = itemView.GetComponentsInChildren<Button>(true);

            foreach (Button button in buttons)
            {
                Text text = button.GetComponentInChildren<Text>(true);
                bool nameMatches =
                    button.name.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    button.name.Contains("삭제");
                bool labelMatches =
                    text != null && !string.IsNullOrEmpty(text.text) && text.text.Contains("삭제");

                if (nameMatches || labelMatches)
                {
                    deleteButton = button;
                    break;
                }
            }

            if (deleteButton == null)
            {
                return;
            }

            Image background = deleteButton.GetComponent<Image>();
            if (background == null)
            {
                background = deleteButton.gameObject.AddComponent<Image>();
            }

            // 강한 핑크/보라 아이콘 대신 AppPofo Aurora 계열의
            // 조용한 프로스트 버튼으로 통일합니다.
            background.sprite = null;
            background.type = Image.Type.Simple;
            background.preserveAspect = false;
            background.color = new Color(0.86f, 0.96f, 1f, 0.94f);
            deleteButton.targetGraphic = background;

            foreach (Image image in deleteButton.GetComponentsInChildren<Image>(true))
            {
                if (image != background)
                {
                    image.enabled = false;
                }
            }

            Text label = deleteButton.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                GameObject labelObject = new GameObject(
                    "ThemeDeleteLabel",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                labelObject.transform.SetParent(deleteButton.transform, false);

                RectTransform labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                label = labelObject.GetComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize = 18;
            }

            label.gameObject.SetActive(true);
            label.enabled = true;
            label.text = "삭제";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.18f, 0.36f, 0.48f, 1f);
            label.raycastTarget = false;

            ColorBlock colors = deleteButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.78f, 0.9f, 0.96f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.8f, 0.88f, 0.9f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            deleteButton.colors = colors;
        }

        private void ScheduleProfileLayoutRebuild()
        {
            if (!gameObject.activeInHierarchy || profileLayoutRebuildScheduled)
            {
                return;
            }

            profileLayoutRebuildScheduled = true;
            StartCoroutine(RebuildProfileLayoutNextFrame());
        }

        private IEnumerator RebuildProfileLayoutNextFrame()
        {
            yield return null;
            RebuildProfileLayoutNow();
            profileLayoutRebuildScheduled = false;
        }

        private void RebuildProfileLayoutNow()
        {
            Canvas.ForceUpdateCanvases();

            ForceRebuild(skillGrid as RectTransform);
            ForceRebuild(skillSection != null ? skillSection.transform as RectTransform : null);
            ForceRebuild(projectList as RectTransform);
            ForceRebuild(projectList != null ? projectList.parent as RectTransform : null);
            ForceRebuild(linkList as RectTransform);
            ForceRebuild(linkList != null ? linkList.parent as RectTransform : null);
            ForceRebuild(cloudActions != null ? cloudActions.transform as RectTransform : null);
            ForceRebuild(profileContent);

            Canvas.ForceUpdateCanvases();
        }

        private static void ForceRebuild(RectTransform rectTransform)
        {
            if (rectTransform != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            }
        }

        private static void ApplyItemCardSprite(Component itemView, Sprite sprite)
        {
            if (itemView == null || sprite == null)
            {
                return;
            }

            Image background = itemView.GetComponent<Image>();
            if (background == null)
            {
                Image[] images = itemView.GetComponentsInChildren<Image>(true);
                float largestArea = -1f;

                foreach (Image image in images)
                {
                    Rect rect = image.rectTransform.rect;
                    float area = Mathf.Abs(rect.width * rect.height);
                    if (area > largestArea)
                    {
                        largestArea = area;
                        background = image;
                    }
                }
            }

            if (background == null)
            {
                return;
            }

            background.sprite = sprite;
            background.type = Image.Type.Simple;
            background.preserveAspect = false;
            background.color = Color.white;
        }
    }
}
