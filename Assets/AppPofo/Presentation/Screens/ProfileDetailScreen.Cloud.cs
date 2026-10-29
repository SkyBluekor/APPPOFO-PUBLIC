using System;
using System.Collections.Generic;
using AppPofo.Presentation.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    public partial class ProfileDetailScreen
    {
        private bool isCloudPresentation;
        private Action cloudBack, cloudEdit, cloudFace, cloudDelete;
        private GameObject cloudActions;
        private Text cloudStatus;
        private Button issueCodeButton, copyCodeButton;
        private readonly Dictionary<Button, bool> authoredButtonStates = new Dictionary<Button, bool>();
        private static readonly HashSet<string> LocalOnlyActions = new HashSet<string> {
            "OpenDeleteConfirm", "DeleteProfile", "OpenAddSkillPanel", "OpenAddProjectPanel",
            "OpenAddLinkPanel", "OpenEditProjectPanel", "DeleteProject", "ConfirmProfileAction"
        };

        private void PreparePresentation(bool cloud)
        {
            StopAllCoroutines();
            profileLayoutRebuildScheduled = false;
            isCloudPresentation = cloud;
            currentProfileId = 0;
            cloudBack = cloudEdit = cloudFace = cloudDelete = null;
            CloseDeleteConfirm();
            ResetProjectInteractionState();
            if (addSkillPanel != null) addSkillPanel.SetActive(false);
            if (editSkillPanel != null) editSkillPanel.SetActive(false);
            if (addLinkPanel != null) addLinkPanel.SetActive(false);
            currentEditingSkill = null;
            if (cloudActions != null) cloudActions.SetActive(cloud);
            if (!cloud)
                foreach (var state in authoredButtonStates)
                    if (state.Key != null) state.Key.interactable = state.Value;
            RefreshFaceAction();
        }

        private void BindProfile(ProfileDetailViewModel profile)
        {
            SetPlainText(displayNameText, profile.DisplayName);
            SetPlainText(helineText, profile.Headline);
            SetPlainText(bioText, profile.Bio);
            SetPlainText(emailText, profile.Email);
            SetPlainText(websiteText, profile.Website);
            BindSkills(profile.Items); BindProjects(profile.Items); BindLinks(profile.Items);
            SetPlainText(projectDetailTitleText, "");
            SetPlainText(projectDetailDescriptionText, "");
            SetPlainText(projectDetailUrlText, "");
        }

        private static void SetPlainText(Text text, string value)
        {
            if (text == null) return;
            text.supportRichText = false;
            text.text = value ?? "";
        }

        private void ClearProfileDisplay()
        {
            foreach (var text in new[] { displayNameText, helineText, bioText, emailText, websiteText,
                projectDetailTitleText, projectDetailDescriptionText, projectDetailUrlText }) SetPlainText(text, "");
            var empty = Array.Empty<ProfileDetailItemViewModel>();
            BindSkills(empty); BindProjects(empty); BindLinks(empty);
        }

        public void ShowCloudLoading(Action onBack, string state = "프로필을 불러오는 중...")
        {
            PreparePresentation(true);
            cloudBack = onBack;
            ClearProfileDisplay();
            SetPlainText(displayNameText, "서버 프로필");
            SetPlainText(helineText, state);
            EnsureCloudActions();
            cloudActions.SetActive(true);
            SetCloudStatus("");
            issueCodeButton.interactable = copyCodeButton.interactable = false;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            RefreshCloudPermissions(false);
            StartCoroutine(ResetScrollToTop());
        }

        public void ShowCloudProfile(CloudProfileDetail detail, Action onEdit, Action onFace,
            Action onIssueCode, Action onCopyCode, Action onDelete = null)
        {
            var profile = ProfileDetailViewModel.FromCloud(detail);
            BindProfile(profile);
            cloudEdit = onEdit; cloudFace = onFace;
            cloudDelete = onDelete;
            issueCodeButton.onClick = new Button.ButtonClickedEvent();
            copyCodeButton.onClick = new Button.ButtonClickedEvent();
            if (onIssueCode != null) issueCodeButton.onClick.AddListener(() => onIssueCode());
            if (onCopyCode != null) copyCodeButton.onClick.AddListener(() => onCopyCode());
            SetCloudStatus("서버 프로필의 항목별 추가·수정·삭제는 지원되지 않습니다. 기술은 프로필 수정에서 관리할 수 있습니다.");
            RefreshCloudPermissions(CloudProfileApi.CanRequest, onIssueCode != null, false);
            StartCoroutine(ResetScrollToTop());
        }

        public void RefreshCloudPermissions(bool authorized, bool canIssue = false, bool canCopy = false)
        {
            if (!isCloudPresentation) return;
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    string method = button.onClick.GetPersistentMethodName(i);
                    bool? enabled = method == "OpenDeleteConfirm" || method == "DeleteProfile" || method == "ConfirmProfileAction"
                        ? authorized && cloudDelete != null : LocalOnlyActions.Contains(method) ? false :
                        method == "OpenEdit" ? authorized && cloudEdit != null :
                        method == "OpenFaceCamera" || method == "OpenFaceRegistration" ? authorized && cloudFace != null : (bool?)null;
                    if (!enabled.HasValue) continue;
                    if (!authoredButtonStates.ContainsKey(button)) authoredButtonStates.Add(button, button.interactable);
                    button.interactable = enabled.Value;
                }
            }
            if (issueCodeButton != null) issueCodeButton.interactable = authorized && canIssue;
            if (copyCodeButton != null) copyCodeButton.interactable = authorized && canCopy;
        }

        public void SetCloudStatus(string message)
        {
            if (cloudStatus == null) return;
            SetPlainText(cloudStatus, message);
            cloudStatus.gameObject.SetActive(!string.IsNullOrEmpty(message));
            ScheduleProfileLayoutRebuild();
        }

        public void HideCloudPresentation()
        {
            if (!isCloudPresentation) return;
            PreparePresentation(true);
            var management = GetComponent<CloudFaceConsentPanel>();
            if (management != null) management.Close();
            ClearProfileDisplay();
            SetCloudStatus("");
            gameObject.SetActive(false);
        }

        private void EnsureCloudActions()
        {
            if (cloudActions != null) return;
            // Keep all authored cards, artwork, footer and SafeArea geometry. Only the
            // additional server commands are placed in the existing scrolling content.
            cloudActions = new GameObject("CloudProfileActions", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var fitter = cloudActions.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            cloudActions.layer = profileContent.gameObject.layer;
            cloudActions.transform.SetParent(profileContent, false);
            ((RectTransform)cloudActions.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, linkList.parent.GetComponent<RectTransform>().rect.width);
            var portrait = GetComponentInChildren<ProfileDetailPortraitLayout>(true);
            if (portrait != null) portrait.SetSupplementarySection((RectTransform)cloudActions.transform);
            var group = cloudActions.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(20, 20, 10, 20); group.spacing = 16;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            cloudStatus = Instantiate(bioText, cloudActions.transform, false);
            cloudStatus.name = "CloudDetailStatus";
            cloudStatus.horizontalOverflow = HorizontalWrapMode.Wrap;
            cloudStatus.verticalOverflow = VerticalWrapMode.Overflow;
            var template = projectDetailOpenUrlButton;
            issueCodeButton = CloneCloudButton(template, "IssueLinkCode", "새 연결 코드 발급");
            copyCodeButton = CloneCloudButton(template, "CopyLinkCode", "연결 코드 복사");
            // R2: Keep legacy handlers available for R5 review, but do not show
            // account-link management as part of the ordinary profile details.
            issueCodeButton.gameObject.SetActive(false);
            copyCodeButton.gameObject.SetActive(false);
        }

        private Button CloneCloudButton(Button template, string name, string caption)
        {
            var button = Instantiate(template, cloudActions.transform, false);
            button.name = name;
            button.onClick = new Button.ButtonClickedEvent();
            var element = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = false; element.minHeight = element.preferredHeight = 90;
            var label = button.GetComponentInChildren<Text>(true);
            SetPlainText(label, caption);
            label.fontSize = 26;
            label.resizeTextForBestFit = false;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(20, 4); label.rectTransform.offsetMax = new Vector2(-20, -4);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
    }
}
