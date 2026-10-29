using System;
using System.Collections;
using System.Linq;
using System.IO;
using AppPofo.Domain.Entities;
using AppPofo.Application.Abstractions;
using AppPofo.Application.UseCases;
using System.Collections.Generic;
using AppPofo.Domain.ValueObjects;
using System.Reflection;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Components;
using AppPofo.Presentation.Screens;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.Avatar;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AppPofo.Tests.PlayMode
{
    public sealed class CloudProfileDetailFlowTests
    {
        private ProfileDetailScreen view;
        private CloudProfileDetailOverlay presenter;
        private GameObject host;
        private object previousCloudSettings;
        [UnitySetUp] public IEnumerator Setup()
        {
            previousCloudSettings = typeof(CloudServerConfiguration).GetField("settings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            LogAssert.Expect(LogType.Log, "[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single); yield return null;
            view = Object.FindAnyObjectByType<ProfileDetailScreen>(FindObjectsInactive.Include);
            Field<GameObject>(view, "profileListScreenRoot").SetActive(false);
            host = new GameObject("SyntheticCloudDetailPresenter");
            presenter = host.AddComponent<CloudProfileDetailOverlay>(); presenter.Initialize(view);
            typeof(CloudModeSession).GetMethod("SetStartedMode", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { CloudModeSession.Plan(true) });
            typeof(CloudModeSession).GetMethod("SetProbeResult", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { true, CloudAccessRole.User });
            typeof(CloudDeviceAuthenticator).GetProperty("AccessToken").SetValue(null, "appdev_synthetic_ui_only");
            typeof(CloudDeviceAuthenticator).GetProperty("ExpiresAtMs").SetValue(null, DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds());
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            CloudDeviceAuthenticator.ClearToken();
            var camera = Object.FindAnyObjectByType<FaceRegistrationScreen>(FindObjectsInactive.Include);
            if (camera != null && camera.IsRecognitionMode) { ExpectCameraTrace("view-exit"); camera.Hide(); }
            CloudFaceRuntime.Reset();
            typeof(CloudServerConfiguration).GetField("settings", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousCloudSettings);
            presenter.Hide(); Object.Destroy(host); CloudDeviceAuthenticator.ClearToken();
            typeof(CloudModeSession).GetMethod("SetStartedMode", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { CloudModeSession.Plan(false) });
            yield return null;
        }
        private static CloudProfileDetail Data(int count = 3) => new CloudProfileDetail {
            id = "11111111-2222-4333-8444-555555555555", display_name = "Staging 테스트", headline = "Unity 개발자",
            bio = "<b>서버 소개</b> " + new string('가', 500), email = "mail@example.com", website = "https://example.com",
            items = Enumerable.Range(0, count).Select(i => new CloudProfileItem {
                item_type = i % 3 == 0 ? "SKILL" : i % 3 == 1 ? "PROJECT" : "LINK",
                title = i % 3 == 0 ? "Unity" : "긴 제목 " + i, description = new string('나', 1000),
                url = "https://example.com/" + new string('a', 200), sort_order = i
            }).ToArray()
        };
        [UnityTest] public IEnumerator SavedServerConsentRestoresOnlyForCurrentPolicyAndAuthentication()
        {
            var settings = typeof(CloudServerConfiguration).GetField("settings",BindingFlags.NonPublic|BindingFlags.Static);
            settings.SetValue(null, new CloudServerConfiguration.Settings {environment="production",production_faces_enabled=true});
            typeof(CloudFaceRuntime).GetProperty("Config").SetValue(null, new CloudFaceRuntime.Configuration {
                environment="production",model_version="sface_2021dec",decision_version="production-v1",
                enrollment_policy="storage",scan_policy="current-policy"
            });
            var restore = typeof(CloudFaceRuntime).GetMethod("RestoreScanConsent",BindingFlags.NonPublic|BindingFlags.Static);
            Assert.That(restore.Invoke(null,new object[]{"{\"approved\":true,\"policy_version\":\"current-policy\"}","current-policy"}),Is.True);
            Assert.That(CloudFaceRuntime.CanScan,Is.True);
            CloudFaceRuntime.StopScanning(); Assert.That(CloudFaceRuntime.CanScan,Is.False);
            restore.Invoke(null,new object[]{"{\"approved\":true,\"policy_version\":\"current-policy\"}","current-policy"});
            Assert.That(CloudFaceRuntime.CanScan,Is.True);
            restore.Invoke(null,new object[]{"{\"approved\":true,\"policy_version\":\"old-policy\"}","current-policy"});
            Assert.That(CloudFaceRuntime.CanScan,Is.False);
            restore.Invoke(null,new object[]{"{\"approved\":false,\"policy_version\":\"current-policy\"}","current-policy"});
            Assert.That(CloudFaceRuntime.CanScan,Is.False);
            CloudDeviceAuthenticator.ClearToken();
            Assert.That(restore.Invoke(null,new object[]{"{\"approved\":true,\"policy_version\":\"current-policy\"}","current-policy"}),Is.False);
            Assert.That(CloudFaceRuntime.CanScan,Is.False);
            yield return null;
        }
        [UnityTest] public IEnumerator CloudDeleteRequiresConfirmationPreventsDuplicatesAndRecoversFromFailure()
        {
            int deletes = 0; string deletedId = null;
            presenter.ShowLoading(); presenter.ShowProfile(Data(), null, null, null, id => { deletes++; deletedId = id; });
            yield return null;
            view.DeleteProfile(); Assert.That(deletes, Is.Zero);
            view.OpenDeleteConfirm();
            Assert.That(Field<GameObject>(view, "deleteConfirmPanel").activeSelf, Is.True);
            Assert.That(Field<Text>(view, "confirmationMessage").text, Does.Contain("얼굴정보"));
            view.ConfirmProfileAction(); view.ConfirmProfileAction();
            Assert.That(deletes, Is.EqualTo(1)); Assert.That(deletedId, Is.EqualTo(Data().id));
            presenter.ShowDeleteError("synthetic unavailable");
            Assert.That(Field<GameObject>(view, "deleteConfirmPanel").activeSelf, Is.False);
            view.OpenDeleteConfirm(); view.ConfirmProfileAction(); Assert.That(deletes, Is.EqualTo(2));
            presenter.ShowDeleteError("synthetic retry");
            CloudDeviceAuthenticator.ClearToken(); yield return null;
            view.OpenDeleteConfirm(); view.ConfirmProfileAction(); Assert.That(deletes, Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator CloudCardPreviewUsesUuidSkillsPhotoAndCannotAcceptLatePhotoFromAnotherProfile()
        {
            var panel = Object.FindAnyObjectByType<BusinessCardPanelController>(FindObjectsInactive.Include);
            var photo = new Texture2D(2, 2); photo.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); photo.Apply();
            var data = Data(); data.avatar_base64 = Convert.ToBase64String(photo.EncodeToJPG());
            Object.Destroy(photo);
            panel.ShowCloud(data); yield return null;
            Assert.That(Field<long>(panel, "currentProfileId"), Is.Zero);
            var card = Field<BusinessCardView>(panel, "businessCardView");
            Assert.That(Field<Text>(card, "displayNameText").text, Is.EqualTo(data.display_name));
            Assert.That(Field<Text>(card, "skillText").text, Does.Contain("Unity"));
            Assert.That(Field<Image>(card, "avatarImage").sprite, Is.Not.Null);
            int previousVersion = Field<int>(panel, "avatarContextVersion");
            var next = Data(); next.id = Guid.NewGuid().ToString(); next.avatar_base64 = null;
            panel.ShowCloud(next);
            Assert.That(Field<Image>(card, "avatarImage").sprite, Is.Null);
            Assert.That(typeof(BusinessCardPanelController).GetMethod("IsCurrentAvatarContext",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(panel,new object[]{0L,previousVersion}), Is.EqualTo(false));
            panel.Close();
        }
        [UnityTest] public IEnumerator CloudListPreviewButtonPassesCloudUuidAndShowsProfilePhoto()
        {
            var list = Object.FindAnyObjectByType<ProfileListScreen>(FindObjectsInactive.Include);
            var row = Object.Instantiate(Field<ProfileListItemView>(list, "profileItemPrefab"), view.transform);
            var photo = new Texture2D(2, 2); photo.Apply();
            string id = Data().id, opened = null;
            row.BindCloud(new CloudProfileSummary {id=id,display_name="Synthetic",avatar_base64=Convert.ToBase64String(photo.EncodeToJPG())}, _=>{}, value=>opened=value);
            Object.Destroy(photo);
            var button = Field<Button>(row, "businessCardButton");
            Assert.That(button.interactable, Is.True); button.onClick.Invoke();
            Assert.That(opened, Is.EqualTo(id)); Assert.That(row.ProfileId, Is.Zero);
            Assert.That(Field<Image>(row, "avatarImage").sprite, Is.Not.Null);
            Object.Destroy(row.gameObject); yield return null;
        }
        [UnityTest] public IEnumerator ExistingCardsReadOnlyCloudCommandsAndPermissions()
        {
            int edits = 0, faces = 0, codes = 0;
            presenter.ShowLoading(); presenter.ShowProfile(Data(45), id => codes++, id => faces++, detail => edits++);
            yield return null; yield return null;
            Assert.That(Field<long>(view, "currentProfileId"), Is.Zero);
            Assert.That(ButtonNamed("IssueLinkCode").image.sprite,Is.SameAs(Field<Button>(view,"projectDetailOpenUrlButton").image.sprite));
            Assert.That(Field<Text>(view, "displayNameText").text, Is.EqualTo("Staging 테스트"));
            Assert.That(Field<Text>(view, "bioText").supportRichText, Is.False);
            Assert.That(view.GetComponentsInChildren<SkillChipView>().Length, Is.EqualTo(15));
            Assert.That(view.GetComponentsInChildren<ProjectItemView>().Length, Is.EqualTo(15));
            Assert.That(view.GetComponentsInChildren<LinkItemView>().Length, Is.EqualTo(15));
            Assert.That(view.GetComponentsInChildren<SkillChipView>().All(chip => !chip.GetComponent<Button>().interactable), Is.True);
            foreach (var link in view.GetComponentsInChildren<LinkItemView>())
                Assert.That(Field<Button>(link, "deleteButton").interactable, Is.False);
            var project = view.GetComponentsInChildren<ProjectItemView>().First();
            project.GetComponent<Button>().onClick.Invoke();
            Assert.That(Field<GameObject>(view, "projectDetailPanel").activeSelf, Is.True);
            Assert.That(Field<Text>(view, "projectDetailDescriptionText").text.Length, Is.EqualTo(1000));
            view.OpenEditProjectPanel(); Assert.That(Field<GameObject>(view, "addProjectPanel").activeSelf, Is.False);
            view.CloseProjectDetail(); view.OpenEdit(); view.OpenFaceRegistration();
            Assert.That(edits, Is.EqualTo(1)); Assert.That(faces, Is.EqualTo(1));
            Assert.That(ButtonNamed("IssueLinkCode").gameObject.activeSelf, Is.False);
            Assert.That(ButtonNamed("CopyLinkCode").gameObject.activeSelf, Is.False);
            Assert.That(codes, Is.Zero);
            presenter.ShowLinkCode("applink_" + new string('a', 32));
            Assert.That(ButtonNamed("IssueLinkCode").gameObject.activeInHierarchy, Is.False);
            Assert.That(ButtonNamed("CopyLinkCode").gameObject.activeInHierarchy, Is.False);
            CloudDeviceAuthenticator.ClearToken(); yield return null;
            Assert.That(ButtonNamed("IssueLinkCode").interactable, Is.False);
            view.OpenEdit(); view.OpenFaceRegistration();
            Assert.That(edits, Is.EqualTo(1)); Assert.That(faces, Is.EqualTo(1));
            view.DeleteProfile(); view.OpenAddSkillPanel();
            Assert.That(Field<GameObject>(view, "addSkillPanel").activeSelf, Is.False);
        }
        [UnityTest] public IEnumerator ReopenLoadingFailureAndCloseClearAllOldRowsAndCallbacks()
        {
            for (int i = 0; i < 3; i++)
            {
                presenter.ShowLoading(); presenter.ShowProfile(Data(6), null); yield return null;
                int version = presenter.Version;
                presenter.Hide(); Assert.That(presenter.Version, Is.GreaterThan(version));
                presenter.ShowLoading(); yield return null;
                Assert.That(view.GetComponentsInChildren<SkillChipView>(), Is.Empty);
                Assert.That(view.GetComponentsInChildren<ProjectItemView>(), Is.Empty);
                Assert.That(view.GetComponentsInChildren<LinkItemView>(), Is.Empty);
                presenter.ShowError("서버 연결 실패"); yield return null;
                Assert.That(Field<Text>(view, "bioText").text, Is.Empty);
                Assert.That(ButtonNamed("CopyLinkCode").interactable, Is.False);
                Assert.That(view.GetComponentsInChildren<Text>().Any(t => t.text == "서버 연결 실패"), Is.True);
                view.Back(); Assert.That(presenter.IsVisible, Is.False);
            }
            Assert.That(view.GetComponentsInChildren<Transform>(true).Count(t => t.name == "CloudProfileActions"), Is.EqualTo(1));
            Assert.That(Field<Text>(view,"projectDetailDescriptionText").text,Is.Empty);
        }
        [UnityTest] public IEnumerator CloudEditorCancelReturnsTheSameUuidWithoutSqliteWrites()
        {
            var editor = Object.FindAnyObjectByType<CreateProfileScreen>(FindObjectsInactive.Include);
            string returned = null;
            editor.BeginCloudEdit(Data(), id => returned = id); yield return null;
            Assert.That(editor.CurrentProfileId, Is.Zero);
            Assert.That(editor.CloudProfileId, Is.EqualTo(Data().id));
            Assert.That(Field<InputField>(editor, "displayNameInput").text, Is.EqualTo(Data().display_name));
            editor.Back(); yield return null;
            Assert.That(returned, Is.EqualTo(Data().id));
            Field<GameObject>(view, "profileListScreenRoot").SetActive(false);
        }
        [UnityTest] public IEnumerator CloudToLocalRestoresLocalActionsAndMissingLocalProfileClearsOldData()
        {
            presenter.ShowLoading(); presenter.ShowProfile(Data(),id=>{}); yield return null;
            var repo=new LocalProfiles();
            var bootstrap=Field<AppPofoBootstrap>(view,"bootstrap");
            typeof(AppPofoBootstrap).GetField("<GetProfileUseCase>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)
                .SetValue(bootstrap,new GetProfileUsecase(repo));
            typeof(CloudModeSession).GetMethod("SetStartedMode",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{CloudModeSession.Plan(false)});
            view.Show(repo.Profile.Id); yield return null; yield return null;
            Assert.That(view.gameObject.activeSelf,Is.True);
            Assert.That(Field<long>(view,"currentProfileId"),Is.EqualTo(901));
            Assert.That(Field<Text>(view,"displayNameText").text,Is.EqualTo("로컬 전환"));
            var actions=view.transform.Find("SafeAreaRoot/CenteredContent/ProfileActions");
            Assert.That(actions.Find("DeleteButton").GetComponent<Button>().interactable,Is.True);
            Assert.That(view.GetComponentsInChildren<SkillChipView>().Single().GetComponent<Button>().interactable,Is.True);
            view.Show(999); yield return null;
            Assert.That(Field<long>(view,"currentProfileId"),Is.Zero);
            Assert.That(view.GetComponentsInChildren<SkillChipView>(),Is.Empty);
            Assert.That(Field<Text>(view,"bioText").text,Is.Empty);
        }
        private sealed class LocalProfiles : IProfileRepository
        {
            public Profile Profile=new Profile(901,"로컬 전환","개발자","로컬 소개","","","",true,
                DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,new[]{new ProfileItem(1,901,ProfileItemType.Skill,"C#","","",0)});
            public Profile Find(long id)=>id==Profile.Id?Profile:null;
            public IReadOnlyList<Profile> GetAll()=>new[]{Profile};
            public Profile Save(Profile value)=>value;
            public void Delete(long id)=>throw new InvalidOperationException("No writes expected.");
        }
        [UnityTest] public IEnumerator DelayedFailureCannotOverwriteAnotherProfileAfterReopen()
        {
            var list = Field<ProfileListScreen>(view,"profileListScreen");
            // Enable the list in local mode, then step cloud query iterators manually.
            // Their nested transports are never executed; no server is contacted.
            typeof(CloudModeSession).GetMethod("SetStartedMode",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{CloudModeSession.Plan(false)});
            list.gameObject.SetActive(true);
            typeof(CloudModeSession).GetMethod("SetStartedMode",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{CloudModeSession.Plan(true)});
            typeof(CloudModeSession).GetMethod("SetProbeResult",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{true,CloudAccessRole.User});
            typeof(ProfileListScreen).GetField("cloudDetails",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(list,presenter);
            presenter.ShowLoading();
            int generation=Field<int>(list,"cloudGeneration"), version=presenter.Version;
            var detailQuery=(IEnumerator)typeof(ProfileListScreen).GetMethod("LoadCloudDetail",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(list,new object[]{Data().id,generation,version});
            var codeQuery=(IEnumerator)typeof(ProfileListScreen).GetMethod("IssueLinkCode",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(list,new object[]{Data().id,version});
            Assert.That(detailQuery.MoveNext(),Is.True); Assert.That(codeQuery.MoveNext(),Is.True);
            presenter.Hide(); presenter.ShowLoading();
            var replacement=Data(); replacement.id="aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"; replacement.display_name="다른 프로필";
            presenter.ShowProfile(replacement,id=>{});
            Assert.That(detailQuery.MoveNext(),Is.False); Assert.That(codeQuery.MoveNext(),Is.False);
            Assert.That(Field<Text>(view,"displayNameText").text,Is.EqualTo("다른 프로필"));
            Assert.That(Field<Text>(view,"cloudStatus").text,Does.Contain("항목별"));
            yield return null;
            list.gameObject.SetActive(false);
        }
        [UnityTest] public IEnumerator ModeOrRoleSwitchClosesOldCloudDetail()
        {
            presenter.ShowLoading(); presenter.ShowProfile(Data(),id=>{});
            typeof(CloudModeSession).GetMethod("SetProbeResult",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{true,CloudAccessRole.Administrator});
            yield return null;
            Assert.That(presenter.IsVisible,Is.False);
            presenter.ShowLoading(); presenter.ShowProfile(Data(),id=>{});
            typeof(CloudModeSession).GetMethod("SetStartedMode",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{CloudModeSession.Plan(false)});
            yield return null;
            Assert.That(presenter.IsVisible,Is.False);
        }
        [UnityTest] public IEnumerator SharedSceneVisualsAndCloudActionsRemainScrollableAtPortraitAndLandscapeSizes()
        {
            presenter.ShowLoading(); presenter.ShowProfile(Data(9), id => {});
            var canvas = view.GetComponentInParent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            foreach (Transform sibling in view.transform.parent) sibling.gameObject.SetActive(sibling == view.transform);
            var safe = view.transform.Find("SafeAreaRoot");
            safe.GetComponent<SafeAreaRoot>().enabled = false;
            foreach (var size in new[] { new Vector2Int(1080,1920), new Vector2Int(720,1280), new Vector2Int(1600,900) })
            {
                // Match Bootstrap's 1080x1920 CanvasScaler at width/height match 0.5.
                float scale = Mathf.Sqrt(size.x / 1080f * size.y / 1920f);
                ((RectTransform)canvas.transform).sizeDelta = (Vector2)size / scale;
                canvas.transform.localScale = Vector3.one;
                safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,size.x,size.y), new Rect(24,24,size.x-48,size.y-48));
                yield return null; yield return null; Canvas.ForceUpdateCanvases();
                safe.GetComponent<ProfileDetailPortraitLayout>().ApplyLayout();
                var actions=view.transform.Find("SafeAreaRoot/CenteredContent/ProfileActions");
                Rect? previousAction=null;
                foreach(Transform action in actions)
                {
                    var points=action.GetComponentsInChildren<RectTransform>().SelectMany(rect=>{
                        var corners=new Vector3[4];rect.GetWorldCorners(corners);return corners;
                    }).ToArray();
                    var bounds=Rect.MinMaxRect(points.Min(v=>v.x),points.Min(v=>v.y),points.Max(v=>v.x),points.Max(v=>v.y));
                    if(previousAction.HasValue) Assert.That(bounds.xMin,Is.GreaterThanOrEqualTo(previousAction.Value.xMax-.5f),"Footer actions overlap at "+size);
                    previousAction=bounds;
                }
                var scroll = Field<ScrollRect>(view,"profileScrollView");
                scroll.verticalNormalizedPosition = 0; yield return null; Canvas.ForceUpdateCanvases();
                foreach (string name in new[] { "IssueLinkCode", "CopyLinkCode" })
                {
                    Assert.That(ButtonNamed(name).gameObject.activeSelf, Is.False, name + " must stay hidden at " + size);
                    Assert.That(ButtonNamed(name).gameObject.activeInHierarchy, Is.False);
                }
                Capture(canvas, "Cloud-actions-"+size.x+"x"+size.y, size.x, size.y);
                scroll.verticalNormalizedPosition = 1; yield return null;
                Capture(canvas, "Cloud-"+size.x+"x"+size.y, size.x, size.y);
            }
            // Identical fields/items through the local adapter on the very same scene instance.
            var data = Data(9);
            var local = new Profile(901, data.display_name, data.headline, data.bio, data.email, data.website, "", true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, data.items.Select((item,i) => new ProfileItem(i+1,901,
                    item.item_type == "SKILL" ? ProfileItemType.Skill : item.item_type == "PROJECT" ? ProfileItemType.Project : ProfileItemType.Link,
                    item.title,item.description,item.url,i)).ToArray());
            typeof(ProfileDetailScreen).GetMethod("PreparePresentation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{false});
            typeof(ProfileDetailScreen).GetMethod("BindProfile",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{ProfileDetailViewModel.FromLocal(local)});
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1080,1920);
            safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,1080,1920),new Rect(24,24,1032,1872));
            yield return null; yield return null;
            Field<ScrollRect>(view,"profileScrollView").verticalNormalizedPosition = 1;
            yield return null; Capture(canvas,"Local-1080x1920",1080,1920);
        }
        [UnityTest] public IEnumerator CloudFaceReturnAndSkipShowConfirmationWithCameraAction()
        {
            var wizard = Object.FindAnyObjectByType<CreateProfileScreen>(FindObjectsInactive.Include);
            wizard.BeginCloudFaceRegistration(Data().id, Data());
            wizard.SkipFaceRegistration();
            yield return null;
            Assert.That(wizard.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.Complete));
            Assert.That(wizard.CloudProfileId, Is.EqualTo(Data().id));
            Assert.That(wizard.CurrentProfileId, Is.Zero);
            Assert.That(Field<AppPofo.Presentation.Components.BusinessCardView>(wizard,"completionCard"), Is.Not.Null);
            Assert.That(wizard.GetComponentsInChildren<Button>().Any(b => b.name == "카메라로 명함 보기"), Is.True);
            Assert.That(Field<RectTransform>(wizard,"completeRoot").Find("프로젝트 추가").gameObject.activeSelf, Is.False);
            typeof(CreateProfileScreen).GetMethod("OnCloudFaceRegistrationReturned",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(wizard,new object[]{Data().id,false});
            Assert.That(wizard.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.FaceChoice));
            typeof(CreateProfileScreen).GetMethod("OnCloudFaceRegistrationReturned",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(wizard,new object[]{Data().id,true});
            Assert.That(wizard.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.Complete));
            wizard.gameObject.SetActive(false);
        }

        [UnityTest] public IEnumerator CameraConsentUsesExistingArtworkAndFitsViewport()
        {
            presenter.ShowLoading(); presenter.ShowProfile(Data(3), id => {});
            var panel = view.gameObject.AddComponent<CloudFaceConsentPanel>();
            var theme = CloudPanelTheme.Current;
            Assert.That(theme, Is.Not.Null);
            Assert.That(theme.background, Is.Not.Null);
            Assert.That(theme.primaryButton, Is.Not.Null);
            Assert.That(theme.secondaryButton, Is.Not.Null);
            Assert.That(theme.card, Is.Not.Null);
            var canvas=view.GetComponentInParent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled=false;
            canvas.renderMode=RenderMode.WorldSpace;
            foreach(Transform sibling in view.transform.parent) sibling.gameObject.SetActive(sibling==view.transform);
            var safe=view.transform.Find("SafeAreaRoot");
            safe.GetComponent<SafeAreaRoot>().enabled=false;
            foreach(var size in new[]{new Vector2Int(1080,1920),new Vector2Int(720,1280),new Vector2Int(1600,900)})
            {
                float scale = Mathf.Sqrt(size.x / 1080f * size.y / 1920f);
                ((RectTransform)canvas.transform).sizeDelta=(Vector2)size/scale;canvas.transform.localScale=Vector3.one;
                safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,size.x,size.y),new Rect(24,24,size.x-48,size.y-48));
                panel.ShowScan(()=>Assert.Fail("Consent must never auto-start the camera"));
                yield return null;yield return null;Canvas.ForceUpdateCanvases();
                var surface=safe.Find("CloudScanConsent/ConsentSurface").GetComponent<Image>();
                Assert.That(surface.sprite,Is.SameAs(theme.background));
                var approve=surface.GetComponentsInChildren<Button>().Single(b=>b.name=="ApproveScan");
                Assert.That(approve.image.sprite,Is.SameAs(theme.primaryButton));
                Assert.That(surface.GetComponentsInChildren<Toggle>().All(t=>!t.isOn),Is.True);
                var corners=new Vector3[4];surface.rectTransform.GetWorldCorners(corners);
                foreach(var point in corners)
                    Assert.That(((RectTransform)safe).rect.Contains(safe.InverseTransformPoint(point)),Is.True);
                var viewport=surface.transform.Find("Viewport").GetComponent<RectTransform>();
                foreach(var button in surface.GetComponentsInChildren<Button>())
                {
                    ((RectTransform)button.transform).GetWorldCorners(corners);
                    foreach(var point in corners)
                        Assert.That(viewport.rect.Contains(viewport.InverseTransformPoint(point)),Is.True,button.name+" requires scrolling at "+size);
                }
                Capture(canvas,"Camera-consent-"+size.x+"x"+size.y,size.x,size.y);
                panel.Close();yield return null;
            }
        }

        [UnityTest] public IEnumerator LocalRegistrationConfirmationDetailAndCameraRoundTrip()
        {
            SetMode(false);
            var wizard = Object.FindAnyObjectByType<CreateProfileScreen>(FindObjectsInactive.Include);
            var repository = InstallMemoryProfiles(wizard);
            var camera = InstallCameraStub(wizard);
            wizard.Begin();
            Field<InputField>(wizard,"displayNameInput").text = "R2 로컬 명함";
            Field<Button>(wizard,"nextButton").onClick.Invoke();
            Assert.That(wizard.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.Skills));
            Assert.That(Field<Button>(wizard,"nextButton").interactable, Is.True, "Local save must unlock the next step");
            Field<Button>(wizard,"nextButton").onClick.Invoke();
            Assert.That(wizard.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.FaceChoice));
            Assert.That(Field<Button>(wizard,"nextButton").interactable, Is.True);
            wizard.SkipFaceRegistration();
            long id = wizard.CurrentProfileId;
            Assert.That(repository.Inserts, Is.EqualTo(1));
            yield return VerifyCompletion(wizard, "R2 로컬 명함", "Staging 테스트", "Local-complete");
            Field<Button>(wizard,"nextButton").onClick.Invoke();
            yield return null;
            Assert.That(view.gameObject.activeInHierarchy, Is.True);
            Assert.That(Field<long>(view,"currentProfileId"), Is.EqualTo(id));
            view.Back();
            var list = Field<ProfileListScreen>(wizard,"profileListScreen");
            Assert.That(list.gameObject.activeInHierarchy, Is.True);
            ExpectCameraEntry(); list.OpenRecognitionCamera();
            Assert.That(camera.IsRecognitionMode, Is.True);
            ExpectCameraTrace("view-exit"); camera.Hide();
            Assert.That(list.gameObject.activeInHierarchy, Is.True);
            AssertCameraClosed(camera);
            // Exercise the completion camera button itself as well as the list route.
            list.gameObject.SetActive(false); wizard.gameObject.SetActive(true);
            ExpectCameraEntry(); CameraButton(wizard).onClick.Invoke();
            Assert.That(camera.IsRecognitionMode, Is.True);
            ExpectCameraTrace("view-exit"); camera.Hide();
            Assert.That(list.gameObject.activeInHierarchy, Is.True);
            AssertCameraClosed(camera);
            Assert.That(repository.Find(id), Is.Not.Null);
            Assert.That(repository.Inserts, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator CloudRegistrationConfirmationDetailConsentAndCameraRoundTrip()
        {
            var wizard = Object.FindAnyObjectByType<CreateProfileScreen>(FindObjectsInactive.Include);
            var repository = InstallMemoryProfiles(wizard);
            var camera = InstallCameraStub(wizard);
            wizard.Begin();
            Field<InputField>(wizard,"displayNameInput").text = Data().display_name;
            Field<InputField>(wizard,"headlineInput").text = Data().headline;
            Field<Button>(wizard,"nextButton").onClick.Invoke();
            Assert.That(wizard.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.Skills));
            // Reuse the existing iterator-boundary technique; never execute a transport.
            var create = (IEnumerator)Invoke(wizard,"CreateCloudProfile", CloudProfileWritePayload.FromRegistration(
                Data().display_name, Data().headline, "", "", "", Array.Empty<string>()));
            Assert.That(create.MoveNext(), Is.True);
            Assert.That(CloudProfileWriteJson.TryReadAck("{\"data\":{\"id\":\""+Data().id+"\"}}", out var id), Is.True);
            Callback<Action<string,string>>(create.Current)(id,null);
            Assert.That(create.MoveNext(), Is.False);
            Assert.That(wizard.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.FaceChoice));
            wizard.SkipFaceRegistration();
            yield return VerifyCompletion(wizard, Data().display_name, "R2 로컬 명함", "Cloud-complete");
            var list = Field<ProfileListScreen>(wizard,"profileListScreen");
            // Suppress all real queries during list activation, then feed the real loader.
            typeof(CloudModeSession).GetMethod("SetProbeResult", BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{false,CloudAccessRole.User});
            Field<Button>(wizard,"nextButton").onClick.Invoke(); list.StopAllCoroutines();
            Assert.That(list.gameObject.activeInHierarchy, Is.True, "Detail coroutine owner must remain active");
            SetMode(true);
            var detail = Field<CloudProfileDetailOverlay>(list,"cloudDetails");
            Assert.That(detail.IsVisible, Is.True);
            var load=(IEnumerator)Invoke(list,"LoadCloudDetail",id,Field<int>(list,"cloudGeneration"),detail.Version);
            Assert.That(load.MoveNext(), Is.True);
            Callback<Action<CloudProfileDetail,string>>(load.Current)(Data(),null);
            Assert.That(load.MoveNext(), Is.False);
            Assert.That(detail.SelectedDetail.id, Is.EqualTo(id));
            Assert.That(Field<Text>(view,"displayNameText").text, Is.EqualTo(Data().display_name));
            Assert.That(Field<long>(view,"currentProfileId"), Is.Zero);
            view.Back(); Assert.That(list.gameObject.activeInHierarchy, Is.True);
            ConfigureSyntheticScan();
            list.OpenRecognitionCamera();
            var consent=list.GetComponent<CloudFaceConsentPanel>();
            CompleteMissingScanConsentLookup(consent, list.OpenRecognitionCamera);
            var modal=Field<GameObject>(consent,"modal");
            Assert.That(modal.GetComponentsInChildren<Toggle>().All(t=>!t.isOn), Is.True);
            modal.GetComponentsInChildren<Button>().Single(b=>b.name=="CancelScan").onClick.Invoke();
            Assert.That(Field<GameObject>(consent,"modal"), Is.Null);
            Assert.That(list.gameObject.activeInHierarchy, Is.True);
            // Reopen the same persisted UUID without creating or modifying a profile.
            wizard.BeginCloudFaceRegistration(id,Data()); list.gameObject.SetActive(false);
            Set(list,"hasStarted",false); // No background list transport in this UI fixture.
            wizard.SkipFaceRegistration(); CameraButton(wizard).onClick.Invoke();
            list.StopAllCoroutines();
            CompleteMissingScanConsentLookup(consent, list.OpenRecognitionCamera);
            modal=Field<GameObject>(consent,"modal");
            modal.GetComponentsInChildren<Button>().Single(b=>b.name=="ApproveScan").onClick.Invoke();
            Assert.That(camera.IsRecognitionMode, Is.False, "Unchecked consent cannot enter camera");
            foreach(var toggle in modal.GetComponentsInChildren<Toggle>()) toggle.isOn=true;
            var approve=(IEnumerator)Invoke(consent,"ApproveScan",(Action)list.OpenRecognitionCamera);
            Assert.That(approve.MoveNext(), Is.True);
            var scan=(IEnumerator)approve.Current;
            Assert.That(scan.MoveNext(), Is.True);
            Callback<Action<CloudFaceTransportResult,string>>(scan.Current)(
                new CloudFaceTransportResult(CloudFaceTransportStatus.Success),null);
            Assert.That(scan.MoveNext(), Is.False);
            ExpectCameraEntry(); Assert.That(approve.MoveNext(), Is.False);
            Assert.That(camera.IsRecognitionMode, Is.True);
            typeof(CloudModeSession).GetMethod("SetProbeResult",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{false,CloudAccessRole.User});
            ExpectCameraTrace("view-exit"); camera.Hide();
            Assert.That(list.gameObject.activeInHierarchy, Is.True);
            AssertCameraClosed(camera);
            Assert.That(repository.Inserts, Is.Zero, "Server flow must never insert a SQLite stand-in");
        }

        private static void CompleteMissingScanConsentLookup(CloudFaceConsentPanel consent, Action openCamera)
        {
            consent.StopAllCoroutines(); // Supply the response at the iterator boundary; no network is required.
            var resume = (IEnumerator)Invoke(consent,"ResumeSavedScan",openCamera);
            Assert.That(resume.MoveNext(),Is.True);
            var lookup = (IEnumerator)resume.Current;
            Assert.That(lookup.MoveNext(),Is.True);
            Callback<Action<CloudFaceTransportResult,string>>(lookup.Current)(
                new CloudFaceTransportResult(CloudFaceTransportStatus.Success),
                "{\"approved\":false,\"policy_version\":\""+CloudFaceRuntime.Config.scan_policy+"\"}");
            Assert.That(lookup.MoveNext(),Is.False);
            Assert.That(resume.MoveNext(),Is.False);
        }

        private IEnumerator VerifyCompletion(CreateProfileScreen wizard,string expected,string other,string prefix)
        {
            Assert.That(wizard.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.Complete));
            var card=Field<BusinessCardView>(wizard,"completionCard");
            Assert.That(card, Is.Not.Null);
            Assert.That(card.GetComponentsInChildren<Text>().Any(t=>t.text==expected), Is.True);
            Assert.That(card.GetComponentsInChildren<Text>().Any(t=>t.text==other), Is.False);
            var canvas=wizard.GetComponentInParent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled=false; canvas.renderMode=RenderMode.WorldSpace;
            var safe=wizard.GetComponentInChildren<SafeAreaRoot>(true); safe.enabled=false;
            var scroll=Field<ScrollRect>(wizard,"formScroll");
            foreach(var size in new[]{new Vector2Int(1080,1920),new Vector2Int(720,1280),new Vector2Int(1600,900)})
            {
                float scale=Mathf.Sqrt(size.x/1080f*size.y/1920f);
                ((RectTransform)canvas.transform).sizeDelta=(Vector2)size/scale; canvas.transform.localScale=Vector3.one;
                safe.ApplyViewport(new Rect(0,0,size.x,size.y),new Rect(24,24,size.x-48,size.y-48));
                yield return null; yield return null; Canvas.ForceUpdateCanvases();
                var next=Field<Button>(wizard,"nextButton"); var camera=CameraButton(wizard);
                Assert.That(next.IsActive() && next.interactable && camera.IsActive() && camera.interactable, Is.True);
                AssertInside((RectTransform)safe.transform,(RectTransform)next.transform);
                scroll.StopMovement(); scroll.verticalNormalizedPosition=1;
                yield return null; Canvas.ForceUpdateCanvases();
                var bounds=Bounds((RectTransform)camera.transform,scroll.viewport);
                if(bounds.yMin<scroll.viewport.rect.yMin)
                    scroll.content.anchoredPosition+=Vector2.up*(scroll.viewport.rect.yMin-bounds.yMin+2);
                yield return null; Canvas.ForceUpdateCanvases();
                AssertInside(scroll.viewport,(RectTransform)camera.transform);
                Assert.That(Bounds((RectTransform)next.transform,(RectTransform)safe.transform)
                    .Overlaps(Bounds((RectTransform)camera.transform,(RectTransform)safe.transform)), Is.False);
                if(scroll.content.rect.height>scroll.viewport.rect.height+1) Assert.That(scroll.vertical, Is.True);
                Capture(canvas,prefix+"-"+size.x+"x"+size.y,size.x,size.y);
            }
        }
        private static Button CameraButton(CreateProfileScreen wizard)=>wizard.GetComponentsInChildren<Button>().Single(b=>b.name=="카메라로 명함 보기");
        private static object Invoke(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
        private static T Callback<T>(object iterator)=> (T)iterator.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(f=>f.FieldType==typeof(T)).GetValue(iterator);
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static void SetMode(bool cloud)
        {
            typeof(CloudModeSession).GetMethod("SetStartedMode",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{CloudModeSession.Plan(cloud)});
            if(cloud) typeof(CloudModeSession).GetMethod("SetProbeResult",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{true,CloudAccessRole.User});
        }
        private static void ConfigureSyntheticScan()
        {
            typeof(CloudServerConfiguration).GetField("settings",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,
                new CloudServerConfiguration.Settings{environment="staging",staging_base_url="https://r2-synthetic-ui-only.workers.dev",staging_faces_enabled=true});
            typeof(CloudFaceRuntime).GetProperty("Config").SetValue(null,new CloudFaceRuntime.Configuration{scan_policy="synthetic-ui-only"});
        }
        private static FaceRegistrationScreen InstallCameraStub(CreateProfileScreen wizard)
        {
            var camera=Field<FaceRegistrationScreen>(wizard,"faceRegistrationScreen"); camera.gameObject.SetActive(false);
            var stub=GameObject.Find("AppServices").transform.Find("ImageSourceHost").gameObject.AddComponent<PlayModeImageSourceStub>();
            Set(Field<FaceRegistrationImageSourceAdapter>(camera,"imageSource"),"source",stub);
            return camera;
        }
        private static void ExpectCameraTrace(string stage)=>typeof(FaceRegistrationInputPlayModeTests).GetMethod("ExpectRecognitionStartupTrace",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{stage});
        private static void ExpectCameraEntry() { ExpectCameraTrace("view-enter"); ExpectCameraTrace("camera-request"); ExpectCameraTrace("camera-callback"); }
        private static void AssertCameraClosed(FaceRegistrationScreen camera)
        {
            Assert.That(camera.gameObject.activeSelf, Is.False);
            var stub=Field<IAvatarImageSource>(Field<FaceRegistrationImageSourceAdapter>(camera,"imageSource"),"source") as PlayModeImageSourceStub;
            Assert.That(stub.IsCameraRunning, Is.False);
        }
        private static MemoryFlowProfiles InstallMemoryProfiles(CreateProfileScreen wizard)
        {
            var repository=new MemoryFlowProfiles(); var bootstrap=Field<AppPofoBootstrap>(wizard,"bootstrap");
            Set(bootstrap,"<CreateProfileUseCase>k__BackingField",new CreateProfileUseCase(repository));
            Set(bootstrap,"<GetProfileUseCase>k__BackingField",new GetProfileUsecase(repository));
            Set(bootstrap,"<GetProfilesUseCase>k__BackingField",new GetProfilesUseCase(repository));
            Set(bootstrap,"<SearchProfilesUseCase>k__BackingField",null);
            Set(bootstrap,"<LoadAvatarUseCase>k__BackingField",new LoadAvatarUseCase(new EmptyAvatars()));
            return repository;
        }
        private sealed class EmptyAvatars : AppPofo.Application.Interfaces.IAvatarStorage
        {
            public byte[] Load(string path)=>null;
            public string Save(long id,byte[] bytes)=>throw new InvalidOperationException("No avatar writes expected");
            public void Delete(string path)=>throw new InvalidOperationException("No avatar deletes expected");
        }
        private sealed class MemoryFlowProfiles : IProfileRepository
        {
            private Profile profile;
            public int Inserts;
            public Profile Find(long id)=>profile!=null&&profile.Id==id?profile:null;
            public IReadOnlyList<Profile> GetAll()=>profile==null?Array.Empty<Profile>():new[]{profile};
            public void Delete(long id)=>throw new InvalidOperationException("No deletes expected");
            public Profile Save(Profile value)
            {
                if(value.Id==0) Inserts++;
                return profile=new Profile(901,value.DisplayName,value.Headline,value.Bio,value.AvatarPath,value.Email,value.Website,
                    value.IsActive,value.CreatedAt,value.UpdatedAt,value.Items,value.BusinessCardTheme);
            }
        }
        private static Rect Bounds(RectTransform child,RectTransform parent)
        {
            var corners=new Vector3[4]; child.GetWorldCorners(corners);
            var points=corners.Select(p=>parent.InverseTransformPoint(p)).ToArray();
            return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));
        }
        private static void AssertInside(RectTransform parent,RectTransform child)
        {
            var outer=parent.rect; var inner=Bounds(child,parent);
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin-.5f),child.name);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax+.5f),child.name);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin-.5f),child.name);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax+.5f),child.name);
        }

        private static void Capture(Canvas canvas, string name, int width, int height)
        {
            var host = new GameObject("SyntheticDetailCapture",typeof(Camera)); var camera = host.GetComponent<Camera>();
            camera.enabled=false; camera.orthographic=true; camera.orthographicSize=((RectTransform)canvas.transform).rect.height/2;
            camera.aspect=(float)width/height; camera.transform.position=canvas.transform.position-Vector3.forward*100;
            var target=RenderTexture.GetTemporary(width,height,24); var previous=RenderTexture.active;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try {
                var oldCamera=canvas.worldCamera; canvas.worldCamera=camera;
                camera.targetTexture=target; Canvas.ForceUpdateCanvases(); camera.Render(); canvas.worldCamera=oldCamera;
                RenderTexture.active=target; image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                string folder=Path.GetFullPath(".utmp/s5-11/captures"); Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());
            }
            finally { RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target); Object.Destroy(host); Object.Destroy(image); }
        }
        private Button ButtonNamed(string name) => view.GetComponentsInChildren<Button>(true).Single(button => button.name == name);
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }
}
