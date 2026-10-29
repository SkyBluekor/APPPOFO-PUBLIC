using System.Collections;
using System.Linq;
using System.Reflection;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AppPofo.Tests.PlayMode
{
    public sealed class WelcomeCloudStartNavigationTests
    {
        [UnityTest]
        public IEnumerator ServerSelected_StartNeverDisplaysLocalCards()
        {
            LogAssert.Expect(LogType.Log, "[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            var welcome = Resources.FindObjectsOfTypeAll<RectTransform>()
                .Single(r => r.name == "WelcomeScreen" && r.gameObject.scene.IsValid());
            var list = Object.FindAnyObjectByType<ProfileListScreen>(FindObjectsInactive.Include);
            list.gameObject.SetActive(false);
            welcome.gameObject.SetActive(true);
            var center = welcome.Find("SafeAreaRoot/CenteredContent");
            center.Find("ServerConnectionToggle/ServerButton")
                .GetComponent<Button>().onClick.Invoke();
            center.Find("BottomActions/StartButton")
                .GetComponent<Button>().onClick.Invoke();
            yield return null;

            Assert.That(CloudModeSession.Mode, Is.EqualTo(ProfileDataMode.Cloud));
            Assert.That(list.gameObject.activeSelf, Is.True);
            var content = list.transform.Find(
                "SafeAreaRoot/CenteredContent/Body/Scroll View/Viewport/Content");
            Assert.That(content.GetComponentsInChildren<
                AppPofo.Presentation.Components.ProfileListItemView>(true),
                Is.Empty, "Cloud mode must clear every local SQLite card before a remote request.");
            var panel = content.Find("ProfileSearchPanel");
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.GetComponent<LayoutElement>().preferredHeight,
                Is.EqualTo(108f).Within(.01f));
            Assert.That(panel.Find("NameSearchRow").gameObject.activeSelf, Is.False);
            Assert.That(panel.Find("SkillSearchRow").gameObject.activeSelf, Is.False);
            Assert.That(content.Find("CloudStateMessage"), Is.Not.Null);
            Assert.That(list.transform.Find(
                "SafeAreaRoot/CenteredContent/TopBar/CreateProfileButton")
                .GetComponent<Button>().interactable, Is.False,
                "Server mode must not accidentally open the local create-profile form.");
            CloudConnectionRuntime.BeginFromWelcome(false);
        }


        [UnityTest]
        public IEnumerator ProfileListBackToWelcome_CanSwitchModesRepeatedly()
        {
            LogAssert.Expect(LogType.Log, "[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;

            var welcome = Resources.FindObjectsOfTypeAll<RectTransform>()
                .Single(r => r.name == "WelcomeScreen" && r.gameObject.scene.IsValid());
            var list = Object.FindAnyObjectByType<ProfileListScreen>(FindObjectsInactive.Include);
            list.gameObject.SetActive(false);
            welcome.gameObject.SetActive(true);

            var center = welcome.Find("SafeAreaRoot/CenteredContent");
            var start = center.Find("BottomActions/StartButton").GetComponent<Button>();
            var server = center.Find("ServerConnectionToggle/ServerButton").GetComponent<Button>();
            var local = center.Find("ServerConnectionToggle/LocalButton").GetComponent<Button>();
            server.onClick.Invoke();
            start.onClick.Invoke();
            Assert.That(CloudModeSession.Mode, Is.EqualTo(ProfileDataMode.Cloud));
            Assert.That(list.gameObject.activeSelf, Is.True);
            Assert.That(welcome.gameObject.activeSelf, Is.False);

            var back = list.transform.Find(
                "SafeAreaRoot/CenteredContent/TopBar/BackToWelcomeButton");
            Assert.That(back, Is.Not.Null, "Profile list needs a visible path back to Welcome.");
            var backButton = back.GetComponent<Button>();
            Assert.That(backButton.interactable, Is.True);
            backButton.onClick.Invoke();
            Assert.That(welcome.gameObject.activeSelf, Is.True);
            Assert.That(list.gameObject.activeSelf, Is.False);
            Assert.That(CloudModeSession.Mode, Is.EqualTo(ProfileDataMode.Local));
            Assert.That(CloudModeSession.Status, Is.EqualTo(CloudConnectionStatus.Off));
            Assert.That(CloudModeSession.AccessRole, Is.EqualTo(CloudAccessRole.None));

            local.onClick.Invoke();
            start.onClick.Invoke();
            Assert.That(CloudModeSession.Mode, Is.EqualTo(ProfileDataMode.Local));
            Assert.That(list.gameObject.activeSelf, Is.True);
            backButton.onClick.Invoke();
            Assert.That(welcome.gameObject.activeSelf, Is.True);

            server.onClick.Invoke();
            start.onClick.Invoke();
            Assert.That(CloudModeSession.Mode, Is.EqualTo(ProfileDataMode.Cloud));
            Assert.That(list.gameObject.activeSelf, Is.True);
            backButton.onClick.Invoke();
            Assert.That(CloudModeSession.Status, Is.EqualTo(CloudConnectionStatus.Off));
            Assert.That(welcome.gameObject.activeSelf, Is.True);
        }

#if UNITY_EDITOR_WIN
        [UnityTest]
        public IEnumerator AdminCloudOnboardingStagesInputsWithoutCreatingLocalSQLiteProfile()
        {
            LogAssert.Expect(LogType.Log, "[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;

            var list = Object.FindAnyObjectByType<ProfileListScreen>(FindObjectsInactive.Include);
            var creator = Object.FindAnyObjectByType<CreateProfileScreen>(FindObjectsInactive.Include);
            var bootstrap = Object.FindAnyObjectByType<AppPofoBootstrap>(FindObjectsInactive.Include);
            list.gameObject.SetActive(false);

            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var sessionType = typeof(CloudModeSession);
            var mode = sessionType.GetField("<Mode>k__BackingField", flags);
            var status = sessionType.GetField("<Status>k__BackingField", flags);
            var role = sessionType.GetField("<AccessRole>k__BackingField", flags);
            object previousMode = mode.GetValue(null);
            object previousStatus = status.GetValue(null);
            object previousRole = role.GetValue(null);

            var tokenType = sessionType.Assembly.GetType(
                "AppPofo.Presentation.Common.DesktopCloudAdminToken");
            var token = tokenType.GetField("activeToken", flags);
            var loaded = tokenType.GetField("loaded", flags);
            var pending = tokenType.GetField("pendingValidation", flags);
            object previousToken = token.GetValue(null);
            object previousLoaded = loaded.GetValue(null);
            object previousPending = pending.GetValue(null);
            try
            {
                mode.SetValue(null, ProfileDataMode.Cloud);
                status.SetValue(null, CloudConnectionStatus.ServerReachable);
                role.SetValue(null, CloudAccessRole.Administrator);
                // An in-memory fake token prevents reading real Windows DPAPI data.
                token.SetValue(null, new string('x', 32));
                loaded.SetValue(null, true);
                pending.SetValue(null, false);

                int beforeCount = bootstrap.GetProfilesUseCase.Execute().Count;
                creator.Begin();
                Assert.That(creator.gameObject.activeSelf, Is.True);
                var nameField = typeof(CreateProfileScreen).GetField("displayNameInput",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var name = (InputField)nameField.GetValue(creator);
                name.text = "Cloud draft only";
                creator.Save(); // Cloud basic info advances, but must not save locally.
                Assert.That(creator.CurrentStep,
                    Is.EqualTo(CreateProfileScreen.RegistrationStep.Skills));
                Assert.That(creator.CurrentProfileId, Is.Zero);
                creator.ToggleSkill("Unity");
                Assert.That(creator.CurrentProfileId, Is.Zero);
                Assert.That(bootstrap.GetProfilesUseCase.Execute().Count,
                    Is.EqualTo(beforeCount), "Cloud draft must never write SQLite.");
                creator.Back();
                Assert.That(creator.CurrentStep,
                    Is.EqualTo(CreateProfileScreen.RegistrationStep.BasicInfo));
                var detail = new CloudProfileDetail {
                    id="11111111-1111-4111-8111-111111111111",display_name="Synthetic edit",headline="Role",
                    bio="Bio",email="synthetic@example.invalid",website="",business_card_theme="BLUSH",is_active=true,
                    items=new[]{new CloudProfileItem {item_type="SKILL",title="Unity"},
                        new CloudProfileItem {item_type="PROJECT",title="Keep project",description="Keep details"},
                        new CloudProfileItem {item_type="LINK",title="Keep link",url="https://example.invalid"}} };
                creator.BeginCloudEdit(detail);
                Assert.That(creator.CloudProfileId,Is.EqualTo(detail.id));
                Assert.That(creator.CurrentProfileId,Is.Zero);
                Assert.That(name.text,Is.EqualTo("Synthetic edit"));
                var original=(CloudProfileWritePayload)typeof(CreateProfileScreen).GetField("cloudOriginal",
                    BindingFlags.Instance|BindingFlags.NonPublic).GetValue(creator);
                Assert.That(original.items.Length,Is.EqualTo(3));
                Assert.That(original.business_card_theme,Is.EqualTo("BLUSH"));
                Assert.That(bootstrap.GetProfilesUseCase.Execute().Count,Is.EqualTo(beforeCount));
            }
            finally
            {
                creator.gameObject.SetActive(false);
                mode.SetValue(null, previousMode);
                status.SetValue(null, previousStatus);
                role.SetValue(null, previousRole);
                token.SetValue(null, previousToken);
                loaded.SetValue(null, previousLoaded);
                pending.SetValue(null, previousPending);
            }
        }
#endif

        [UnityTest]
        public IEnumerator ToggleDoesNotConnectBeforeStart_AndOffKeepsOriginalNavigation()
        {
            LogAssert.Expect(LogType.Log, "[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;

            var welcome = Resources.FindObjectsOfTypeAll<RectTransform>()
                .Single(r => r.name == "WelcomeScreen" && r.gameObject.scene.IsValid());
            var controller = welcome.Find("SafeAreaRoot")
                .GetComponent<WelcomeCloudModeController>();
            Assert.That(controller, Is.Not.Null, "Cloud selector must be attached in Play mode.");
            var actions = welcome.Find("SafeAreaRoot/CenteredContent/BottomActions");
            var selector = welcome.Find("SafeAreaRoot/CenteredContent/ServerConnectionToggle");
            Assert.That(selector, Is.Not.Null);
            Assert.That(actions.Find("ServerConnectionToggle"), Is.Null,
                "Connection selector belongs in the header, not below the actions.");
            var server = selector.Find("ServerButton").GetComponent<Button>();
            var local = selector.Find("LocalButton").GetComponent<Button>();
            var caption = selector.Find("ConnectionStatus/ModeValue").GetComponent<Text>();
            var rect = (RectTransform)selector;
            Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(rect.pivot, Is.EqualTo(Vector2.one));
            Assert.That(rect.rect.width, Is.LessThanOrEqualTo(332));
            Assert.That(controller.WantsCloud, Is.False);
            Assert.That(caption.text, Is.EqualTo("기기에 저장"));

            var beforeMode = CloudModeSession.Mode;
            var beforeStatus = CloudModeSession.Status;
            server.onClick.Invoke(); // Select server; no connection yet.
            Assert.That(controller.WantsCloud, Is.True);
            Assert.That(caption.text, Is.EqualTo("시작 시 연결"));
            server.onClick.Invoke(); // Selecting the active segment keeps its mode.
            Assert.That(controller.WantsCloud, Is.True);
            Assert.That(CloudModeSession.Mode, Is.EqualTo(beforeMode));
            Assert.That(CloudModeSession.Status, Is.EqualTo(beforeStatus));

            local.onClick.Invoke(); // Return to local before Start.
            Assert.That(controller.WantsCloud, Is.False);
            Assert.That(caption.text, Is.EqualTo("기기에 저장"));
            var list = Object.FindAnyObjectByType<ProfileListScreen>(FindObjectsInactive.Include);
            list.gameObject.SetActive(false);
            welcome.gameObject.SetActive(true);
            actions.Find("StartButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(CloudModeSession.Mode, Is.EqualTo(ProfileDataMode.Local));
            Assert.That(CloudModeSession.Status, Is.EqualTo(CloudConnectionStatus.Off));
            Assert.That(welcome.gameObject.activeSelf, Is.False);
            Assert.That(list.gameObject.activeSelf, Is.True);
        }
    }
}
