using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AppPofo.Application.Abstractions;
using AppPofo.Application.UseCases;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Tests.EditMode
{
    public sealed class ProfileOnboardingTests
    {
        private CreateProfileScreen screen;
        private GameObject services;
        private MemoryProfiles repository;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            screen = Resources.FindObjectsOfTypeAll<CreateProfileScreen>().Single(s => s.gameObject.scene.IsValid());
            services = new GameObject("OnboardingTestServices");
            services.SetActive(false); // Never run Bootstrap.Awake or touch the user's database.
            var bootstrap = services.AddComponent<AppPofoBootstrap>();
            repository = new MemoryProfiles();
            SetProperty(bootstrap, "CreateProfileUseCase", new CreateProfileUseCase(repository));
            SetProperty(bootstrap, "UpdateProfileUseCase", new UpdateProfileUseCase(repository));
            SetProperty(bootstrap, "GetProfileUseCase", new GetProfileUsecase(repository));
            SetProperty(bootstrap, "AddSkillUseCase", new AddSkillUseCase(repository));
            SetProperty(bootstrap, "DeleteProfileItemUseCase", new DeleteProfileItemUseCase(repository));
            SetField(screen, "bootstrap", bootstrap);
            Input("displayNameInput").text = "테스트 프로필";
            Input("headlineInput").text = string.Empty; // Optional in the existing Domain.
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(services);
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
        }

        [Test]
        public void BasicInfo_BackAndForward_UpdatesSameProfileInsteadOfInsertingAgain()
        {
            screen.Save();
            long id = screen.CurrentProfileId;
            screen.Back();
            Input("displayNameInput").text = "수정한 이름";
            screen.Save();
            Assert.That(screen.CurrentProfileId, Is.EqualTo(id));
            Assert.That(repository.InsertCount, Is.EqualTo(1));
            Assert.That(repository.Find(id).DisplayName, Is.EqualTo("수정한 이름"));
            Assert.That(screen.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.Skills));
        }

        [Test]
        public void SkillToggle_UsesExistingAliasComparison_AndCanDeselectAfterBack()
        {
            screen.Save();
            screen.ToggleSkill("C#");
            Assert.That(repository.Find(screen.CurrentProfileId).Items.Single().Title, Is.EqualTo("C#"));
            screen.Save();
            screen.Back();
            screen.ToggleSkill("CSHARP");
            Assert.That(repository.Find(screen.CurrentProfileId).Items, Is.Empty);
            Assert.That(repository.InsertCount, Is.EqualTo(1));
        }

        [Test]
        public void Skip_VisitsFaceStep_AndKeepsProfileAndSkillsWithoutFaceDependencies()
        {
            screen.Save();
            screen.ToggleSkill("Unity");
            screen.Save();
            Assert.That(screen.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.FaceChoice));
            long id = screen.CurrentProfileId;
            screen.SkipFaceRegistration();
            Assert.That(screen.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.Complete));
            Assert.That(screen.CurrentProfileId, Is.EqualTo(id));
            Assert.That(repository.Find(id).IsActive, Is.True);
            Assert.That(repository.Find(id).Items.Single().Title, Is.EqualTo("Unity"));
            Assert.That(repository.InsertCount, Is.EqualTo(1));
            // Bootstrap has no FaceEmbeddingRepository: Skip must not query or require it.
        }

        [Test]
        public void FaceReturn_CompletionUsesSameProfile_CancelReturnsToOptionalChoice()
        {
            screen.Save(); screen.Save();
            long id = screen.CurrentProfileId;
            InvokeReturn(id, false);
            Assert.That(screen.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.FaceChoice));
            InvokeReturn(id, true);
            Assert.That(screen.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.Complete));
            Assert.That(screen.CurrentProfileId, Is.EqualTo(id));
            Assert.That(repository.InsertCount, Is.EqualTo(1));
        }

        [Test]
        public void FaceReturn_FromAnotherProfile_DoesNotAdvanceWizard()
        {
            screen.Save(); screen.Save();
            InvokeReturn(screen.CurrentProfileId + 1, true);
            Assert.That(screen.CurrentStep, Is.EqualTo(CreateProfileScreen.RegistrationStep.FaceChoice));
        }

        [Test]
        public void ClosingSavedWizard_PreservesProfileAndSkills()
        {
            screen.Save(); screen.ToggleSkill("Python"); screen.Back();
            long id = screen.CurrentProfileId;
            screen.Back();
            Assert.That(repository.Find(id), Is.Not.Null);
            Assert.That(repository.Find(id).Items.Single().Title, Is.EqualTo("Python"));
            Assert.That(screen.CurrentProfileId, Is.Zero);
        }

        [Test]
        public void SceneWiring_ReferencesExistingControlsAndServices()
        {
            foreach (string name in new[] { "profileDetailScreen", "faceRegistrationScreen", "businessCardPanel",
                "skillChipPrefab", "formContent", "formScroll", "titleText", "labelTemplate", "nextButton" })
                Assert.That(GetField(screen, name), Is.Not.Null, name);
            Assert.That(((Component)GetField(screen, "formScroll")).GetComponent<ScrollRect>().content,
                Is.SameAs(GetField(screen, "formContent")));
        }

        [Test]
        public void WizardBuild_ReusesExistingInputs_ProvidesSkipAndCompletionPreviewSlot()
        {
            typeof(CreateProfileScreen).GetMethod("BuildWizard", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(screen, null);
            var faceRoot = (RectTransform)GetField(screen, "faceRoot");
            Assert.That(faceRoot.GetComponentsInChildren<Button>(true).Single().GetComponentInChildren<Text>(true).text,
                Is.EqualTo("나중에 하기"));
            Assert.That(GetField(screen, "cardSlot"), Is.Not.Null);
            Assert.That(((RectTransform)GetField(screen, "skillsRoot")).GetComponentsInChildren<InputField>(true).Length, Is.EqualTo(1));
            // Exercise real layout/step rendering without starting a camera or creating a completion card.
            screen.Save();
            Assert.That(Input("displayNameInput").gameObject.activeSelf, Is.False);
            Assert.That(((RectTransform)GetField(screen, "skillsRoot")).gameObject.activeSelf, Is.True);
            screen.Back();
            Assert.That(Input("displayNameInput").gameObject.activeSelf, Is.True);
            Assert.That(Input("bioInput").gameObject.activeSelf, Is.False);
        }

        [Test]
        public void FrontAvatar_SelectedCropSurvivesFrameDisposal_AndCanBeReplacedManually()
        {
            screen.Save();
            long id = screen.CurrentProfileId;
            var bootstrap = services.GetComponent<AppPofoBootstrap>();
            var storage = new MemoryAvatars();
            SetProperty(bootstrap, "UpdateAvatarUseCase", new UpdateAvatarUseCase(repository, storage));
            var camera = services.AddComponent<FaceRegistrationCamera>();
            SetField(camera, "bootstrap", bootstrap);
            using (var first = new OpenCvSharp.Mat(220, 240, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(255, 0, 0)))
                InvokeCamera(camera, "CaptureFrontAvatar", first);
            using (var selected = new OpenCvSharp.Mat(260, 280, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(0, 0, 255)))
                InvokeCamera(camera, "CaptureFrontAvatar", selected);
            Assert.That(InvokeCamera(camera, "TrySaveDefaultFrontAvatar", id), Is.True);
            Assert.That(repository.Find(id).AvatarPath, Is.EqualTo("memory/avatar.png"));
            using (var decoded = OpenCvSharp.Cv2.ImDecode(storage.Bytes, OpenCvSharp.ImreadModes.Color))
            {
                Assert.That(decoded.Cols, Is.EqualTo(280));
                Assert.That(decoded.Rows, Is.EqualTo(260));
                Assert.That(decoded.At<OpenCvSharp.Vec3b>(0, 0).Item2, Is.EqualTo(255));
            }
            Assert.That(storage.SaveCount, Is.EqualTo(1));
            bootstrap.UpdateAvatarUseCase.Execute(id, new byte[] { 7, 8, 9 }, DateTimeOffset.UtcNow);
            Assert.That(storage.Bytes, Is.EqualTo(new byte[] { 7, 8, 9 }));
            Assert.That(InvokeCamera(camera, "TrySaveDefaultFrontAvatar", id), Is.True);
            Assert.That(storage.SaveCount, Is.EqualTo(2), "Re-registration must preserve the manually changed photo.");
            InvokeCamera(camera, "StopPreview");
            InvokeCamera(camera, "StopPreview");
            Assert.That(GetField(camera, "frontAvatarMat"), Is.Null);
        }

        [Test]
        public void FrontAvatar_ExistingPhotoNeedsNoFrontCapture()
        {
            screen.Save();
            long id = screen.CurrentProfileId;
            var bootstrap = services.GetComponent<AppPofoBootstrap>();
            var storage = new MemoryAvatars();
            SetProperty(bootstrap, "UpdateAvatarUseCase", new UpdateAvatarUseCase(repository, storage));
            bootstrap.UpdateAvatarUseCase.Execute(id, new byte[] { 1 }, DateTimeOffset.UtcNow);
            var camera = services.AddComponent<FaceRegistrationCamera>();
            SetField(camera, "bootstrap", bootstrap);
            Assert.That(InvokeCamera(camera, "TrySaveDefaultFrontAvatar", id), Is.True);
            Assert.That(storage.SaveCount, Is.EqualTo(1));
        }

        private static object InvokeCamera(FaceRegistrationCamera camera, string method, params object[] args) =>
            typeof(FaceRegistrationCamera).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(camera, args);

        private sealed class MemoryAvatars : AppPofo.Application.Interfaces.IAvatarStorage
        {
            public byte[] Bytes;
            public int SaveCount;
            public string Save(long id, byte[] bytes) { SaveCount++; Bytes = (byte[])bytes.Clone(); return "memory/avatar.png"; }
            public byte[] Load(string path) => Bytes;
            public void Delete(string path) { Bytes = null; }
        }

        private InputField Input(string name) => (InputField)GetField(screen, name);
        private void InvokeReturn(long id, bool complete) => typeof(CreateProfileScreen)
            .GetMethod("OnFaceRegistrationReturned", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(screen, new object[] { id, complete });
        private static object GetField(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void SetProperty(object target, string name, object value) => target.GetType()
            .GetProperty(name).SetValue(target, value);

        private sealed class MemoryProfiles : IProfileRepository
        {
            private readonly Dictionary<long, Profile> profiles = new Dictionary<long, Profile>();
            private long nextItemId = 1;
            public int InsertCount { get; private set; }
            public Profile Find(long id) => profiles.TryGetValue(id, out Profile profile) ? profile : null;
            public IReadOnlyList<Profile> GetAll() => profiles.Values.ToList();
            public void Delete(long id) => profiles.Remove(id);
            public Profile Save(Profile profile)
            {
                long id = profile.Id;
                if (id == 0) id = ++InsertCount;
                var items = profile.Items.Select(item => new ProfileItem(item.Id == 0 ? nextItemId++ : item.Id,
                    id, item.ItemType, item.Title, item.Description, item.Url, item.SortOrder)).ToList();
                Profile saved = new Profile(id, profile.DisplayName, profile.Headline, profile.Bio, profile.AvatarPath,
                    profile.Email, profile.Website, profile.IsActive, profile.CreatedAt, profile.UpdatedAt, items, profile.BusinessCardTheme);
                profiles[id] = saved;
                return saved;
            }
        }
    }
}

namespace AppPofo.Tests.EditMode
{
    public sealed class RegistrationUxRegressionTests
    {
        private GameObject inputFixture;

        [TearDown]
        public void TearDown()
        {
            if (inputFixture != null)
            {
                var system = inputFixture.GetComponent<UnityEngine.EventSystems.EventSystem>();
                // The synthetic EditMode module is only an IME input provider,
                // not an activated pointer module. Detach it before cleanup.
                typeof(UnityEngine.EventSystems.EventSystem).GetField("m_CurrentInputModule", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(system, null);
                typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(system, null);
                UnityEngine.Object.DestroyImmediate(inputFixture);
                inputFixture = null;
            }
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
        }

        [TestCase("김태우")][TestCase("Anna11")][TestCase("10022")]
        public void LateDeselect_AfterEditHasEnded_DoesNotCommitCompositionAgain(string value)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            inputFixture = new GameObject("CompositionFixture", typeof(UnityEngine.EventSystems.EventSystem));
            var system = inputFixture.GetComponent<UnityEngine.EventSystems.EventSystem>();
            if (UnityEngine.EventSystems.EventSystem.current != system)
                typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(system, null);
            UnityEngine.EventSystems.EventSystem.current = system;
            var module = inputFixture.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            module.inputOverride = inputFixture.AddComponent<CompositionInput>();
            typeof(UnityEngine.EventSystems.EventSystem).GetField("m_CurrentInputModule", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(system, module);
            var fieldObject = new GameObject("Input", typeof(RectTransform), typeof(AppPofo.Presentation.Common.CompositionSafeInputField));
            fieldObject.transform.SetParent(inputFixture.transform);
            var field = fieldObject.GetComponent<AppPofo.Presentation.Common.CompositionSafeInputField>();
            field.text = value;
            field.OnDeselect(new UnityEngine.EventSystems.BaseEventData(system));
            field.OnDeselect(new UnityEngine.EventSystems.BaseEventData(system));
            Assert.That(field.text, Is.EqualTo(value));
        }

        [Test]
        public void LegacyInputField_LateDeselect_ReproducesStaleCompositionAppend()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            inputFixture = new GameObject("CompositionFixture", typeof(UnityEngine.EventSystems.EventSystem));
            var system = inputFixture.GetComponent<UnityEngine.EventSystems.EventSystem>();
            if (UnityEngine.EventSystems.EventSystem.current != system)
                typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(system, null);
            UnityEngine.EventSystems.EventSystem.current = system;
            var module = inputFixture.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            module.inputOverride = inputFixture.AddComponent<CompositionInput>();
            typeof(UnityEngine.EventSystems.EventSystem).GetField("m_CurrentInputModule", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(system, module);
            var fieldObject = new GameObject("LegacyInput", typeof(RectTransform), typeof(InputField));
            fieldObject.transform.SetParent(inputFixture.transform);
            var field = fieldObject.GetComponent<InputField>();
            field.text = "김태우";
            Assert.That(typeof(InputField).GetProperty("compositionString", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(field), Is.EqualTo("우"));
            typeof(InputField).GetField("m_CaretPosition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(field, 3);
            typeof(InputField).GetField("m_CaretSelectPosition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(field, 3);
            field.OnDeselect(new UnityEngine.EventSystems.BaseEventData(system));
            Assert.That(field.text, Is.EqualTo("김태우우"));
        }

        [TestCase(0.30f)][TestCase(0.29f)][TestCase(0.35f)][TestCase(0.5f)][TestCase(0.8f)][TestCase(0.34f)][TestCase(0.81f)]
        public void RegistrationYawGate_IsSymmetricForLeftAndRight(float yaw)
        {
            var gate = new AppPofo.Presentation.FaceRegistration.Selection.FrameQualityGate();
            MethodInfo method = gate.GetType().GetMethod("PassYawForStep", BindingFlags.Instance | BindingFlags.NonPublic);
            bool left = (bool)method.Invoke(gate, new object[] { yaw, FaceRegistrationStep.Left });
            bool right = (bool)method.Invoke(gate, new object[] { -yaw, FaceRegistrationStep.Right });
            Assert.That(right, Is.EqualTo(left));
            Assert.That(left, Is.EqualTo(yaw >= .22f && yaw <= .82f));
        }

        [TestCase(.34f, true)][TestCase(.36f, false)]
        public void FrontYawPolicy_RemainsUnchanged(float yaw, bool expected)
        {
            var gate = new AppPofo.Presentation.FaceRegistration.Selection.FrameQualityGate();
            var method = gate.GetType().GetMethod("PassYawForStep", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method.Invoke(gate, new object[] { yaw, FaceRegistrationStep.Front }), Is.EqualTo(expected));
            Assert.That(method.Invoke(gate, new object[] { -yaw, FaceRegistrationStep.Front }), Is.EqualTo(expected));
        }

        [TestCase("FaceSize", .49f, 0f, "가까이")]
        [TestCase("FaceSize", 1f, 0f, "떨어져")]
        [TestCase("Roll", .8f, 0f, "수평")]
        [TestCase("Pitch", .8f, 0f, "위아래")]
        [TestCase("Yaw", .8f, .29f, "왼쪽")]
        [TestCase("Yaw", .8f, .9f, "덜")]
        [TestCase("Blur", .8f, 0f, "움직임")]
        public void RegistrationFeedback_DescribesActualRejectedCondition(string stage, float size, float yaw, string expected)
        {
            var metrics = new AppPofo.Presentation.FaceRegistration.Selection.FrameQualityMetrics
                { RejectionStage = stage, FaceSizeRatio = size, YawScore = yaw };
            Assert.That(AppPofo.Presentation.FaceRegistration.Selection.FrameQualityGate.DescribeRejection(metrics, FaceRegistrationStep.Left),
                Does.Contain(expected));
        }

        [Test]
        public void QualityPolicy_FrontLimitsRemainAndSideLimitsMatchCurrentRegistration()
        {
            var gate = new AppPofo.Presentation.FaceRegistration.Selection.FrameQualityGate();
            var size = gate.GetType().GetMethod("PassFaceSize", BindingFlags.Instance | BindingFlags.NonPublic);
            var pose = gate.GetType().GetMethod("PassCommonPose", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(size, Is.Not.Null);
            Assert.That(pose, Is.Not.Null);

            // The front policy is unchanged; only side captures use wider size/pitch ranges.
            Assert.That(size.Invoke(gate, new object[] { .64f, FaceRegistrationStep.Front }), Is.False);
            Assert.That(size.Invoke(gate, new object[] { .65f, FaceRegistrationStep.Front }), Is.True);
            Assert.That(size.Invoke(gate, new object[] { .95f, FaceRegistrationStep.Front }), Is.True);
            Assert.That(size.Invoke(gate, new object[] { .96f, FaceRegistrationStep.Front }), Is.False);

            Assert.That(size.Invoke(gate, new object[] { .49f, FaceRegistrationStep.Left }), Is.False);
            Assert.That(size.Invoke(gate, new object[] { .50f, FaceRegistrationStep.Left }), Is.True);
            Assert.That(size.Invoke(gate, new object[] { .50f, FaceRegistrationStep.Right }), Is.True);
            Assert.That(size.Invoke(gate, new object[] { .96f, FaceRegistrationStep.Right }), Is.False);

            Assert.That(pose.Invoke(gate, new object[] { 15f, .5f, FaceRegistrationStep.Front }), Is.True);
            Assert.That(pose.Invoke(gate, new object[] { -15f, .7f, FaceRegistrationStep.Front }), Is.True);
            Assert.That(pose.Invoke(gate, new object[] { 16f, .6f, FaceRegistrationStep.Front }), Is.False);
            Assert.That(pose.Invoke(gate, new object[] { 0f, .49f, FaceRegistrationStep.Front }), Is.False);
            Assert.That(pose.Invoke(gate, new object[] { 0f, .71f, FaceRegistrationStep.Front }), Is.False);

            Assert.That(pose.Invoke(gate, new object[] { 0f, .44f, FaceRegistrationStep.Left }), Is.False);
            Assert.That(pose.Invoke(gate, new object[] { 0f, .45f, FaceRegistrationStep.Left }), Is.True);
            Assert.That(pose.Invoke(gate, new object[] { 0f, .75f, FaceRegistrationStep.Right }), Is.True);
            Assert.That(pose.Invoke(gate, new object[] { 0f, .76f, FaceRegistrationStep.Right }), Is.False);
            Assert.That(pose.Invoke(gate, new object[] { 16f, .60f, FaceRegistrationStep.Right }), Is.False);
        }

        [TestCase(false)][TestCase(true)]
        public void TabMove_KeepsNativeCompositionCommitInSourceField(bool backward)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            inputFixture = new GameObject("CompositionFixture", typeof(UnityEngine.EventSystems.EventSystem));
            var system = inputFixture.GetComponent<UnityEngine.EventSystems.EventSystem>();
            if (UnityEngine.EventSystems.EventSystem.current != system)
                typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(system, null);
            UnityEngine.EventSystems.EventSystem.current = system;
            var module = inputFixture.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            var ime = inputFixture.AddComponent<CompositionInput>();
            module.inputOverride = ime;
            typeof(UnityEngine.EventSystems.EventSystem).GetField("m_CurrentInputModule", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(system, module);
            var fields = new InputField[2];
            for (int i = 0; i < fields.Length; i++)
            {
                var go = new GameObject("Field" + i, typeof(RectTransform), typeof(AppPofo.Presentation.Common.CompositionSafeInputField));
                go.transform.SetParent(inputFixture.transform);
                fields[i] = go.GetComponent<InputField>();
            }
            int sourceIndex = backward ? 1 : 0;
            InputField source = fields[sourceIndex];
            InputField destination = fields[1 - sourceIndex];
            source.text = "강현";
            typeof(InputField).GetField("m_CaretPosition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, 2);
            typeof(InputField).GetField("m_CaretSelectPosition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, 2);
            system.SetSelectedGameObject(source.gameObject);
            typeof(InputField).GetField("m_AllowInput", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, true);
            var navigator = inputFixture.AddComponent<AppPofo.Presentation.Common.InputFieldFocusNavigator>();
            navigator.Configure(fields, null);
            var move = (System.Collections.IEnumerator)navigator.GetType()
                .GetMethod("MoveAfterCompositionCommit", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(navigator, new object[] { sourceIndex, backward });
            Assert.That(move.MoveNext(), Is.True);
            Assert.That(source.text, Is.EqualTo("강현우"), "Deselect must preserve the pending syllable before waiting.");
            Assert.That(system.currentSelectedGameObject, Is.Null);
            // The native commit follows uGUI's composition commit. There is no selected
            // destination to receive it while the transition is settling.
            ime.Value = "";
            Assert.That(move.MoveNext(), Is.True);
            Assert.That(system.currentSelectedGameObject, Is.Null);
            Assert.That(move.MoveNext(), Is.False);
            Assert.That(system.currentSelectedGameObject, Is.SameAs(destination.gameObject));
            Assert.That(source.text, Is.EqualTo("강현우"));
            Assert.That(destination.text, Is.Empty);
        }

        [Test]
        public void ExistingBusinessCard_LongBoundTextAndRects_RemainInsideAuthoredCard()
        {
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            var view = Resources.FindObjectsOfTypeAll<AppPofo.Presentation.Components.BusinessCardView>().Single(v => v.gameObject.scene.IsValid());
            RectTransform card = (RectTransform)view.transform;
            var time = DateTimeOffset.UtcNow;
            var profile = new Profile(7, new string('김', 100), new string('H', 300), "", "", "", "", true, time, time,
                new[] { new ProfileItem(1, 7, ProfileItemType.Skill, new string('S', 200), "", "", 0) });
            view.Bind(profile);
            foreach (string name in new[] { "DisplayNameText", "HeadlineText", "SkillText" })
            {
                Text text = card.Find(name).GetComponent<Text>();
                var corners = new Vector3[4];
                text.rectTransform.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector3 point = card.InverseTransformPoint(corner);
                    Assert.That(point.x, Is.InRange(card.rect.xMin - .1f, card.rect.xMax + .1f), name);
                    Assert.That(point.y, Is.InRange(card.rect.yMin - .1f, card.rect.yMax + .1f), name);
                }
                Assert.That(text.resizeTextForBestFit, Is.True);
                Assert.That(text.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate));
            }
        }

        [TestCase(320)][TestCase(540)][TestCase(900)]
        public void RegistrationInstruction_AllStepsStayInsideVisibleChip(int width)
        {
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            var screen = Resources.FindObjectsOfTypeAll<AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen>()
                .Single(value => value.gameObject.scene.IsValid());
            var camera = screen.transform.Find("CameraController").GetComponent<FaceRegistrationCamera>();
            var text = (Text)screen.GetType().GetField("statusText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(screen);
            var chip = (RectTransform)text.transform.parent;
            foreach (GameObject root in screen.gameObject.scene.GetRootGameObjects()) root.SetActive(false);
            var canvas = new GameObject("InstructionFixture", typeof(RectTransform), typeof(Canvas));
            canvas.layer = 5;
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, 240);
            chip.SetParent(canvas.transform, false);
            chip.gameObject.SetActive(true);
            try
            {
                foreach (FaceRegistrationStep step in new[] { FaceRegistrationStep.Front, FaceRegistrationStep.Left,
                    FaceRegistrationStep.Right, FaceRegistrationStep.Complete })
                {
                    camera.GetType().GetField("currentStep", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(camera, step);
                    camera.GetType().GetField("validCandidateCount", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(camera, 10);
                    screen.RefreshRegistrationProgressUi(camera);
                    Canvas.ForceUpdateCanvases();
                    Assert.That(chip.rect.width, Is.LessThanOrEqualTo(width - 48));
                    Assert.That(text.fontSize, Is.EqualTo(24));
                    Assert.That(text.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Wrap));
                    if (width == 320 && step == FaceRegistrationStep.Right) Assert.That(text.text, Does.Contain("\n(10/10)"));
                    text.cachedTextGenerator.Populate(text.text, text.GetGenerationSettings(text.rectTransform.rect.size));
                    var vertices = text.cachedTextGenerator.verts;
                    for (int i = 0; i < vertices.Count - 4; i++)
                    {
                        Vector3 local = chip.InverseTransformPoint(text.rectTransform.TransformPoint(vertices[i].position / text.pixelsPerUnit));
                        Assert.That(local.x, Is.InRange(chip.rect.xMin + 4, chip.rect.xMax - 4), step.ToString());
                        Assert.That(local.y, Is.InRange(chip.rect.yMin + 4, chip.rect.yMax - 4), step.ToString());
                    }
                    if (step == FaceRegistrationStep.Right) CaptureInstruction(canvas, width);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas); }
        }

        private static void CaptureInstruction(GameObject canvas, int width)
        {
            var cameraObject = new GameObject("InstructionCapture", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 120;
            camera.aspect = width / 240f;
            camera.transform.position = canvas.transform.position - Vector3.forward * 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .07f, .12f);
            camera.cullingMask = 1 << 5;
            var target = RenderTexture.GetTemporary(width, 240, 24);
            var previous = RenderTexture.active;
            var rendered = new Texture2D(width, 240, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                rendered.ReadPixels(new Rect(0, 0, width, 240), 0, 0);
                rendered.Apply();
                System.IO.Directory.CreateDirectory(".utmp/RegistrationInstruction");
                System.IO.File.WriteAllBytes($".utmp/RegistrationInstruction/Instruction-{width}.png", rendered.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(rendered);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private sealed class CompositionInput : UnityEngine.EventSystems.BaseInput
        {
            public string Value = "우";
            public override string compositionString => Value;
        }
    }
}
