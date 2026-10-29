using System;
using System.Collections.Generic;
using System.Reflection;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.FaceRegistration.Selection;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AppPofo.Tests.EditMode
{
    public sealed class FaceRegistrationScreenTests
    {
        private const string ScenePath =
            "Assets/AppPofo/Presentation/Scenes/Bootstrap.unity";

        private FaceRegistrationScreen screen;
        private GameObject photoReviewFixture;

        [SetUp]
        public void SetUp()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            screen = FindScreen(scene);
            Assert.That(screen, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            if (screen != null)
            {
                screen.ResetSession();
                screen.gameObject.SetActive(false);
            }
            if (photoReviewFixture != null)
                UnityEngine.Object.DestroyImmediate(photoReviewFixture);
        }

        [Test]
        public void Scene_MainPanelReferencesMatchCameraController()
        {
            const string previewPath = "SafeAreaRoot/MainPanel/Body/CameraSection/CameraContainer/PreviewViewport/DisplayFrame/CameraPreview";
            RawImage preview = screen.transform.Find(previewPath).GetComponent<RawImage>();
            FaceRegistrationCamera camera = screen.transform.Find("CameraController")
                .GetComponent<FaceRegistrationCamera>();
            Assert.That(GetField<RawImage>(screen, "cameraPreview"), Is.SameAs(preview));
            Assert.That(GetField<RawImage>(camera, "cameraPreview"), Is.SameAs(preview));
            Assert.That(GetField<FaceRegistrationImageSourceAdapter>(camera, "imageSource"),
                Is.SameAs(GetField<FaceRegistrationImageSourceAdapter>(screen, "imageSource")));
            Assert.That(GetField<GameObject>(screen, "faceGuide"),
                Is.SameAs(screen.transform.Find("SafeAreaRoot/MainPanel/Body/CameraSection/CameraContainer/PreviewViewport/DisplayFrame/PoseOverlay").gameObject));
            Assert.That(GetField<GameObject>(screen, "statusPanel"),
                Is.SameAs(screen.transform.Find("SafeAreaRoot/MainPanel/Body/CameraSection/CameraContainer/InstructionChip").gameObject));
            Assert.That(GetField<Text>(screen, "statusText"),
                Is.SameAs(screen.transform.Find("SafeAreaRoot/MainPanel/Body/CameraSection/CameraContainer/InstructionChip/InstructionText").GetComponent<Text>()));
            Assert.That(GetField<GameObject>(screen, "returnScreenRoot"),
                Is.SameAs(screen.transform.parent.Find("ProfileDetailScreen").gameObject));
            Assert.That(screen.HasPhotoReviewUI, Is.False);
            Assert.That(GetField<FaceSampleSlotView[]>(screen, "sampleSlots"), Is.Empty);
            Assert.That(screen.transform.Find("SafeAreaRoot/MainPanel/Body/GuideSection/StepProgress/FrontStep"), Is.Not.Null);
            Assert.That(screen.transform.Find("SafeAreaRoot/MainPanel/Body/GuideSection/StepProgress/LeftStep"), Is.Not.Null);
            Assert.That(screen.transform.Find("SafeAreaRoot/MainPanel/Body/GuideSection/StepProgress/RightStep"), Is.Not.Null);
            Assert.That(screen.transform.Find("SampleSlots"), Is.Null);
            Assert.That(GetField<Button>(screen, "completeButton"), Is.Null,
                "RegisterButton starts registration; it is not the old five-photo CompleteButton.");
            foreach (GameObject root in screen.gameObject.scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    Assert.That(UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject),
                        Is.Zero, child.GetHierarchyPath());
        }

        [Test]
        public void RecognitionMode_ReusesPreviewAndHidesRegistrationUi_WithoutOverwritingGuidance()
        {
            FaceRegistrationCamera registrationCamera = GetField<FaceRegistrationCamera>(screen, "registrationCamera");
            Assert.That(registrationCamera, Is.SameAs(screen.transform.Find("CameraController")
                .GetComponent<FaceRegistrationCamera>()));
            var recognitionCamera = GetField<AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera>(screen, "recognitionCamera");
            Assert.That(recognitionCamera, Is.Not.Null);
            Assert.That(recognitionCamera.enabled, Is.False);
            Assert.That(GetField<RawImage>(recognitionCamera, "previewRawImage"),
                Is.SameAs(GetField<RawImage>(screen, "cameraPreview")));
            Assert.That(GetField<BusinessCardPanelController>(recognitionCamera, "businessCardPanelController"),
                Is.SameAs(GetField<BusinessCardPanelController>(screen, "businessCardPanel")));

            // Exercise the navigation presentation without starting native camera/model workers.
            SetField(screen, "recognitionMode", true);
            MethodInfo applyMode = typeof(FaceRegistrationScreen).GetMethod(
                "ApplyCameraModeUi", BindingFlags.Instance | BindingFlags.NonPublic);
            applyMode.Invoke(screen, null);
            Assert.That(GetField<GameObject>(screen, "registrationIntroduction").activeSelf, Is.False);
            Assert.That(GetField<GameObject>(screen, "registrationGuideSection").activeSelf, Is.False);
            Assert.That(GetField<GameObject>(screen, "faceGuide").activeSelf, Is.False);
            Assert.That(GetField<RawImage>(screen, "cameraPreview").gameObject.activeSelf, Is.True);
            string guidance = GetField<Text>(screen, "statusText").text;
            Assert.That(guidance, Is.EqualTo("카메라에 얼굴을 비춰 명함을 확인하세요."));
            screen.RefreshRegistrationProgressUi(registrationCamera);
            Assert.That(GetField<Text>(screen, "statusText").text, Is.EqualTo(guidance));

            SetField(screen, "recognitionMode", false);
            applyMode.Invoke(screen, null);
            screen.RefreshRegistrationProgressUi(registrationCamera);
            Assert.That(GetField<GameObject>(screen, "registrationGuideSection").activeSelf, Is.True);
            Assert.That(GetField<GameObject>(screen, "faceGuide").activeSelf, Is.True);
            Assert.That(GetField<Text>(screen, "statusText").text.Replace('\n', ' '),
                Is.EqualTo("정면을 봐주세요 (0/10)"));
        }

        private static bool EvaluateQualityPredicate(string method, params object[] values)
        {
            var gate = new FrameQualityGate();
            MethodInfo predicate = typeof(FrameQualityGate).GetMethod(
                method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(predicate, Is.Not.Null, method + " must remain testable.");
            return (bool)predicate.Invoke(gate, values);
        }

        [TestCase(.49f, false, false)]
        [TestCase(.50f, false, true)]
        [TestCase(.64f, false, true)]
        [TestCase(.65f, true, true)]
        [TestCase(.95f, true, true)]
        [TestCase(.96f, false, false)]
        public void SideRegistrationFaceSizeWiderOnlyThanFront(float ratio,
            bool frontAccepted, bool sideAccepted)
        {
            Assert.That(EvaluateQualityPredicate("PassFaceSize", ratio,
                FaceRegistrationStep.Front), Is.EqualTo(frontAccepted));
            Assert.That(EvaluateQualityPredicate("PassFaceSize", ratio,
                FaceRegistrationStep.Left), Is.EqualTo(sideAccepted));
            Assert.That(EvaluateQualityPredicate("PassFaceSize", ratio,
                FaceRegistrationStep.Right), Is.EqualTo(sideAccepted));
        }

        [TestCase(.44f, false, false)]
        [TestCase(.45f, false, true)]
        [TestCase(.49f, false, true)]
        [TestCase(.50f, true, true)]
        [TestCase(.70f, true, true)]
        [TestCase(.75f, false, true)]
        [TestCase(.76f, false, false)]
        public void SideRegistrationPitchWiderWithoutChangingFrontalPose(float pitch,
            bool frontAccepted, bool sideAccepted)
        {
            Assert.That(EvaluateQualityPredicate("PassCommonPose", 0f, pitch,
                FaceRegistrationStep.Front), Is.EqualTo(frontAccepted));
            foreach (FaceRegistrationStep side in new[] {
                FaceRegistrationStep.Left, FaceRegistrationStep.Right })
            {
                Assert.That(EvaluateQualityPredicate("PassCommonPose", 0f, pitch, side),
                    Is.EqualTo(sideAccepted));
                Assert.That(EvaluateQualityPredicate("PassCommonPose", 15.01f, pitch, side),
                    Is.False, "Roll guard must not be bypassed for side registration.");
            }
        }

        [TestCase(.21f, false)]
        [TestCase(.22f, true)]
        [TestCase(.30f, true)]
        [TestCase(.50f, true)]
        [TestCase(.82f, true)]
        [TestCase(.83f, false)]
        public void SideYawRangeIsSymmetricAndStillRejectsFrontFacingSamples(
            float yaw, bool sideAccepted)
        {
            Assert.That(EvaluateQualityPredicate("PassYawForStep", yaw,
                FaceRegistrationStep.Left), Is.EqualTo(sideAccepted));
            Assert.That(EvaluateQualityPredicate("PassYawForStep", -yaw,
                FaceRegistrationStep.Right), Is.EqualTo(sideAccepted));
            Assert.That(EvaluateQualityPredicate("PassYawForStep", -yaw,
                FaceRegistrationStep.Left), Is.False);
            Assert.That(EvaluateQualityPredicate("PassYawForStep", yaw,
                FaceRegistrationStep.Right), Is.False);
        }

        [Test]
        public void FrontalYawBoundaryAndSideGuidanceRemainCorrect()
        {
            Assert.That(EvaluateQualityPredicate("PassYawForStep", .35f,
                FaceRegistrationStep.Front), Is.True);
            Assert.That(EvaluateQualityPredicate("PassYawForStep", -.35f,
                FaceRegistrationStep.Front), Is.True);
            Assert.That(EvaluateQualityPredicate("PassYawForStep", .36f,
                FaceRegistrationStep.Front), Is.False);
            Assert.That(EvaluateQualityPredicate("PassYawForStep", -.36f,
                FaceRegistrationStep.Front), Is.False);

            var frontTooSmall = new FrameQualityMetrics { RejectionStage = "FaceSize",
                FaceSizeRatio = .60f };
            Assert.That(FrameQualityGate.DescribeRejection(frontTooSmall,
                FaceRegistrationStep.Front), Does.Contain("가까이"));
            var sideTooSmall = new FrameQualityMetrics { RejectionStage = "FaceSize",
                FaceSizeRatio = .49f };
            Assert.That(FrameQualityGate.DescribeRejection(sideTooSmall,
                FaceRegistrationStep.Left), Does.Contain("가까이"));
            var sideTooLarge = new FrameQualityMetrics { RejectionStage = "FaceSize",
                FaceSizeRatio = .96f };
            Assert.That(FrameQualityGate.DescribeRejection(sideTooLarge,
                FaceRegistrationStep.Right), Does.Contain("떨어져"));

            var tooFrontal = new FrameQualityMetrics { RejectionStage = "Yaw",
                YawScore = .15f };
            Assert.That(FrameQualityGate.DescribeRejection(tooFrontal,
                FaceRegistrationStep.Left), Does.Contain("왼쪽"));
            tooFrontal.YawScore = -.15f;
            Assert.That(FrameQualityGate.DescribeRejection(tooFrontal,
                FaceRegistrationStep.Right), Does.Contain("오른쪽"));

            var tooFar = new FrameQualityMetrics { RejectionStage = "Yaw",
                YawScore = .90f };
            Assert.That(FrameQualityGate.DescribeRejection(tooFar,
                FaceRegistrationStep.Left), Does.Contain("덜 돌려"));
            tooFar.YawScore = -.90f;
            Assert.That(FrameQualityGate.DescribeRejection(tooFar,
                FaceRegistrationStep.Right), Does.Contain("덜 돌려"));
        }

        [Test]
        public void LiveProgress_UsesCameraStepAndCandidateCount_AndRefreshesImmediately()
        {
            FaceRegistrationCamera camera = screen.transform.Find("CameraController")
                .GetComponent<FaceRegistrationCamera>();
            Image[] circles = GetField<Image[]>(screen, "registrationStepCircles");
            Sprite active = GetField<Sprite>(screen, "currentStepSprite");
            Sprite pending = GetField<Sprite>(screen, "pendingStepSprite");
            Color completed = GetField<Color>(screen, "completedStepColor");
            Text instruction = GetField<Text>(screen, "statusText");
            const string progressPath = "SafeAreaRoot/MainPanel/Body/GuideSection/StepProgress/";
            string[] steps = { "FrontStep", "LeftStep", "RightStep" };
            Assert.That(circles.Length, Is.EqualTo(3));
            Assert.That(active, Is.Not.Null);
            Assert.That(pending, Is.Not.Null);
            for (int i = 0; i < steps.Length; i++)
                Assert.That(circles[i], Is.SameAs(screen.transform.Find(
                    progressPath + steps[i] + "/StepCircle").GetComponent<Image>()));

            screen.RefreshRegistrationProgressUi(camera);
            Assert.That(instruction.text.Replace('\n', ' '), Is.EqualTo("정면을 봐주세요 (0/10)"));
            Assert.That(circles[0].sprite, Is.SameAs(active));
            Assert.That(circles[1].sprite, Is.SameAs(pending));
            Assert.That(circles[2].sprite, Is.SameAs(pending));

            // Two quality-passing candidates cannot complete the ten-candidate Front stage.
            SetField(camera, "validCandidateCount", 2);
            screen.RefreshRegistrationProgressUi(camera);
            Assert.That(camera.CurrentStep, Is.EqualTo(FaceRegistrationStep.Front));
            Assert.That(instruction.text.Replace('\n', ' '), Is.EqualTo("정면을 봐주세요 (2/10)"));
            Assert.That(circles[0].color, Is.EqualTo(Color.white));

            MethodInfo enterStep = typeof(FaceRegistrationCamera).GetMethod(
                "EnterStep", BindingFlags.Instance | BindingFlags.NonPublic);
            SetField(camera, "validCandidateCount", 0);
            enterStep.Invoke(camera, new object[] { FaceRegistrationStep.Left });
            screen.RefreshRegistrationProgressUi(camera); // The authored screen is inactive in this EditMode fixture.
            Assert.That(instruction.text.Replace('\n', ' '), Is.EqualTo("고개를 왼쪽으로 돌려 주세요 (0/10)"));
            Assert.That(circles[0].color, Is.EqualTo(completed));
            Assert.That(circles[1].sprite, Is.SameAs(active));
            Assert.That(circles[1].color, Is.EqualTo(Color.white));
            Assert.That(circles[2].sprite, Is.SameAs(pending));

            enterStep.Invoke(camera, new object[] { FaceRegistrationStep.Right });
            screen.RefreshRegistrationProgressUi(camera);
            Assert.That(instruction.text.Replace('\n', ' '), Is.EqualTo("고개를 오른쪽으로 돌려 주세요 (0/10)"));
            Assert.That(circles[1].color, Is.EqualTo(completed));
            Assert.That(circles[2].sprite, Is.SameAs(active));
            enterStep.Invoke(camera, new object[] { FaceRegistrationStep.Complete });
            screen.RefreshRegistrationProgressUi(camera);
            Assert.That(instruction.text, Is.EqualTo("얼굴 등록이 완료되었습니다."));
            foreach (Image circle in circles)
                Assert.That(circle.color, Is.EqualTo(completed));

            camera.StopSession();
            screen.RefreshRegistrationProgressUi(camera);
            Assert.That(instruction.text.Replace('\n', ' '), Is.EqualTo("정면을 봐주세요 (0/10)"));
            Assert.That(circles[0].color, Is.EqualTo(Color.white));
            Assert.That(circles[1].sprite, Is.SameAs(pending));
            Assert.That(circles[2].sprite, Is.SameAs(pending));
        }

        [Test]
        public void MainPanel_ShowResetAndApplyState_ClearsStalePreviewAndPreservesGuidance()
        {
            FakeFaceRegistrationImageSource source = InstallFakeSource();
            RawImage preview = GetField<RawImage>(screen, "cameraPreview");
            Texture2D texture = CreateTexture("ExistingLivePreview");
            preview.texture = texture;
            screen.RefreshRegistrationProgressUi(screen.transform.Find("CameraController")
                .GetComponent<FaceRegistrationCamera>());
            string guidance = GetField<Text>(screen, "statusText").text;
            try
            {
                screen.Show(101);
                // Starting a new camera registration session discards an old frame.
                // The CameraController will supply the next live preview in PlayMode.
                Assert.That(preview.texture, Is.Null);
                screen.Show(202);
                screen.ResetSession();
                foreach (FaceRegistrationViewState state in Enum.GetValues(typeof(FaceRegistrationViewState)))
                    screen.ApplyState(state);
                screen.UpdateProgress();
                Assert.That(preview.texture, Is.Null,
                    "Reset must not resurrect a previous camera session's frame.");
                Assert.That(preview.gameObject.activeSelf, Is.True);
                Assert.That(source.StartCameraCalls, Is.Zero);
                Assert.That(source.StopCameraCalls, Is.Zero);
                Assert.That(GetField<Text>(screen, "statusText").text, Is.EqualTo(guidance));
                Assert.That(screen.CurrentProfileId, Is.EqualTo(202));
            }
            finally
            {
                preview.texture = null;
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [TestCase("cameraPreview")]
        [TestCase("imageSource")]
        [TestCase("returnScreenRoot")]
        [TestCase("faceGuide")]
        [TestCase("statusPanel")]
        [TestCase("statusText")]
        public void MainPanel_MissingRequiredReference_IsReported(string fieldName)
        {
            UnityEngine.Object original = GetField<UnityEngine.Object>(screen, fieldName);
            try
            {
                SetField<UnityEngine.Object>(screen, fieldName, null);
                Assert.That(() => screen.Show(101),
                    Throws.TypeOf<InvalidOperationException>().With.Message.Contains(fieldName));
            }
            finally { SetField(screen, fieldName, original); }
        }

        [Test]
        public void MainPanel_PhotoReviewCommands_ReportUnsupportedUIWithoutTouchingCamera()
        {
            FakeFaceRegistrationImageSource source = InstallFakeSource();
            screen.Show(101);
            Action[] commands = { screen.SelectFromGallery, screen.CaptureFromCamera,
                screen.AutoFit, screen.Retry, screen.UsePhoto, screen.Complete };
            foreach (Action command in commands)
                Assert.That(() => command(),
                    Throws.TypeOf<InvalidOperationException>().With.Message.Contains("optional photo review UI"));
            Assert.That(source.PickCalls, Is.Zero);
            Assert.That(source.CaptureCalls, Is.Zero);
            Assert.That(source.StartCameraCalls, Is.Zero);
            Assert.That(source.StopCameraCalls, Is.Zero);
        }

        [Test]
        public void PhotoReview_PartialConnections_AreNotSilentlyIgnored()
        {
            InstallPhotoReviewFixture();
            Button original = GetField<Button>(screen, "usePhotoButton");
            try
            {
                SetField<Button>(screen, "usePhotoButton", null);
                Assert.That(() => screen.Show(101),
                    Throws.TypeOf<InvalidOperationException>().With.Message.Contains("usePhotoButton"));
            }
            finally { SetField(screen, "usePhotoButton", original); }
        }

        [Test]
        public void ImageSourceHost_IsActiveAndSharedWhileBusinessCardIsInactive()
        {
            GameObject services = GameObject.Find("AppServices");
            Assert.That(services, Is.Not.Null);
            Transform host = services.transform.Find("ImageSourceHost");
            Assert.That(host, Is.Not.Null);
            Assert.That(host.gameObject.activeInHierarchy, Is.True);

            EditorAvatarImageSource editorSource =
                host.GetComponent<EditorAvatarImageSource>();
            AndroidAvatarImageSource androidSource =
                host.GetComponent<AndroidAvatarImageSource>();
            AvatarImageSourceResolver resolver =
                host.GetComponent<AvatarImageSourceResolver>();
            Assert.That(editorSource, Is.Not.Null);
            Assert.That(androidSource, Is.Not.Null);
            Assert.That(resolver, Is.Not.Null);
            Assert.That(editorSource.isActiveAndEnabled, Is.True);
            Assert.That(androidSource.isActiveAndEnabled, Is.True);

            BusinessCardPanelController business =
                UnityEngine.Object.FindFirstObjectByType<BusinessCardPanelController>(
                    FindObjectsInactive.Include);
            Assert.That(business, Is.Not.Null);
            business.gameObject.SetActive(false);
            Assert.That(business.gameObject.activeInHierarchy, Is.False);
            Assert.That(
                GetField<AvatarImageSourceResolver>(business, "avatarImageSourceResolver"),
                Is.SameAs(resolver));

            FaceRegistrationImageSourceAdapter adapter =
                GetField<FaceRegistrationImageSourceAdapter>(screen, "imageSource");
            Assert.That(
                GetField<AvatarImageSourceResolver>(adapter, "transportResolver"),
                Is.SameAs(resolver));
            Assert.That(adapter.IsSourceHostActive, Is.True);

            Assert.That(
                business.GetComponent<EditorAvatarImageSource>(),
                Is.Null);
            Assert.That(
                business.GetComponent<AndroidAvatarImageSource>(),
                Is.Null);
            Assert.That(
                business.GetComponent<AvatarImageSourceResolver>(),
                Is.Null);
        }

        [Test]
        public void Show_WithDifferentProfile_ResetsPreviousSamplesAndProgress()
        {
            InstallPhotoReviewFixture();
            screen.Show(101);
            AddCandidateSample();
            AddCandidateSample();
            Assert.That(screen.SampleCount, Is.EqualTo(2));

            screen.Show(202);

            Assert.That(screen.CurrentProfileId, Is.EqualTo(202));
            Assert.That(screen.SampleCount, Is.Zero);
            Assert.That(screen.CurrentState, Is.EqualTo(FaceRegistrationViewState.Empty));
            Assert.That(GetField<Text>(screen, "progressText").text, Is.EqualTo("0 / 5"));
            Assert.That(GetField<Button>(screen, "completeButton").interactable, Is.False);
        }

        [Test]
        public void UsePhoto_EnablesCompleteOnlyAfterFifthSample()
        {
            InstallPhotoReviewFixture();
            screen.Show(303);

            for (int i = 0; i < 4; i++)
            {
                AddCandidateSample();
                Assert.That(GetField<Button>(screen, "completeButton").interactable, Is.False);
            }

            AddCandidateSample();

            Assert.That(screen.SampleCount, Is.EqualTo(5));
            Assert.That(screen.CurrentState, Is.EqualTo(FaceRegistrationViewState.ReadyToComplete));
            Assert.That(GetField<Text>(screen, "progressText").text, Is.EqualTo("5 / 5"));
            Assert.That(GetField<Button>(screen, "completeButton").interactable, Is.True);
        }

        [Test]
        public void PresentDetectionResult_SelectsFaceNearestImageCenter()
        {
            InstallPhotoReviewFixture();
            screen.Show(404);
            Texture2D texture = CreateTexture("DetectionCandidate");
            SetField(screen, "candidateTexture", texture);
            GetField<RawImage>(screen, "adjustableImage").texture = texture;

            screen.PresentDetectionResult(
                new List<Rect>
                {
                    new Rect(0.02f, 0.05f, 0.18f, 0.22f),
                    new Rect(0.40f, 0.38f, 0.22f, 0.28f),
                    new Rect(0.78f, 0.72f, 0.18f, 0.22f)
                });

            Assert.That(screen.SelectedFaceIndex, Is.EqualTo(1));
            Assert.That(screen.CurrentState, Is.EqualTo(FaceRegistrationViewState.Reviewing));
            Assert.That(GetField<Button>(screen, "usePhotoButton").interactable, Is.True);
            Assert.That(
                GetField<RectTransform>(screen, "faceOverlayLayer").childCount,
                Is.EqualTo(3));
        }

        [Test]
        public void GalleryInput_WithInactiveBusinessCard_ShowsAdjustableCandidate()
        {
            InstallPhotoReviewFixture();
            FakeFaceRegistrationImageSource fakeSource = InstallFakeSource();
            screen.Show(450);

            screen.SelectFromGallery();

            Assert.That(fakeSource.PickCalls, Is.EqualTo(1));
            Assert.That(screen.CandidateTexture, Is.Not.Null);
            Assert.That(screen.CurrentState, Is.EqualTo(FaceRegistrationViewState.Reviewing));
            Assert.That(
                GetField<RawImage>(screen, "adjustableImage").texture,
                Is.SameAs(screen.CandidateTexture));
            Assert.That(
                GetField<Text>(screen, "statusText").text,
                Does.Contain("얼굴 분석 준비 완료"));
            Assert.That(
                GetField<RectTransform>(screen, "faceOverlayLayer").childCount,
                Is.Zero,
                "FaceDetector 연결 전에는 가짜 얼굴 박스를 만들면 안 됩니다.");
        }

        [Test]
        public void CameraInput_FirstClickShowsPreview_SecondClickCapturesCandidate()
        {
            InstallPhotoReviewFixture();
            FakeFaceRegistrationImageSource fakeSource = InstallFakeSource();
            screen.Show(451);

            screen.CaptureFromCamera();

            Assert.That(fakeSource.StartCameraCalls, Is.EqualTo(1));
            Assert.That(fakeSource.CaptureCalls, Is.Zero);
            Assert.That(screen.CurrentState, Is.EqualTo(FaceRegistrationViewState.Analyzing));
            Assert.That(GetField<RawImage>(screen, "cameraPreview").texture, Is.Not.Null);
            Assert.That(GetField<Button>(screen, "cameraButton").interactable, Is.True);
            Assert.That(GetField<Text>(screen, "cameraButtonLabel").text, Is.EqualTo("촬영"));

            screen.CaptureFromCamera();

            Assert.That(fakeSource.CaptureCalls, Is.EqualTo(1));
            Assert.That(fakeSource.StopCameraCalls, Is.GreaterThanOrEqualTo(2));
            Assert.That(screen.CandidateTexture, Is.Not.Null);
            Assert.That(screen.CurrentState, Is.EqualTo(FaceRegistrationViewState.Reviewing));
            Assert.That(
                GetField<RawImage>(screen, "cameraPreview").texture == null,
                Is.True);
            Assert.That(GetField<RawImage>(screen, "adjustableImage").texture, Is.Not.Null);
        }

        [Test]
        public void ImageAdjust_MouseDragWheelAndAutoFit_UpdateExpectedTransform()
        {
            InstallPhotoReviewFixture();
            screen.Show(505);
            Texture2D texture = new Texture2D(256, 128, TextureFormat.RGBA32, false);
            SetField(screen, "candidateTexture", texture);
            SetField(screen, "candidateApproved", true);
            GetField<RawImage>(screen, "adjustableImage").texture = texture;

            FaceImageAdjustView adjustView =
                GetField<FaceImageAdjustView>(screen, "imageAdjustView");
            adjustView.SetContentAspect(texture.width, texture.height);
            screen.ApplyState(FaceRegistrationViewState.Reviewing);
            Canvas.ForceUpdateCanvases();

            EventSystem eventSystem = FindEventSystem();
            var dragEvent = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                delta = new Vector2(120f, 0f)
            };

            adjustView.OnBeginDrag(dragEvent);
            adjustView.OnDrag(dragEvent);
            adjustView.OnEndDrag(dragEvent);
            Assert.That(adjustView.CurrentPosition.x, Is.GreaterThan(0f));

            var scrollEvent = new PointerEventData(eventSystem)
            {
                position = RectTransformUtility.WorldToScreenPoint(
                    null,
                    adjustView.transform.position),
                scrollDelta = new Vector2(0f, 1f)
            };

            adjustView.OnScroll(scrollEvent);
            Assert.That(adjustView.CurrentScale, Is.GreaterThan(1f));

            screen.AutoFit();
            Assert.That(adjustView.CurrentPosition, Is.EqualTo(Vector2.zero));
            Assert.That(adjustView.CurrentScale, Is.EqualTo(1f));
        }

        [TestCase("body", "SafeAreaRoot/MainPanel/Body")]
        [TestCase("bodyLayout", "SafeAreaRoot/MainPanel/Body")]
        [TestCase("cameraSection", "SafeAreaRoot/MainPanel/Body/CameraSection")]
        [TestCase("guideSection", "SafeAreaRoot/MainPanel/Body/GuideSection")]
        public void ResponsiveLayout_ReferencesMatchCurrentHierarchy(string fieldName, string path)
        {
            var layout = screen.GetComponent<FaceRegistrationResponsiveLayout>();
            Assert.That(layout.enabled, Is.True);
            var target = GetField<Component>(layout, fieldName);
            Assert.That(target.transform, Is.SameAs(screen.transform.Find(path)));
            layout.ApplyLayout();
            Assert.That(GetField<Button>(screen, "completeButton"), Is.Null);
        }

        [Test]
        public void MainPanel_BackButton_ReturnsToProfileWithoutChangingPreviewGeometry()
        {
            Button back = screen.transform.Find("SafeAreaRoot/MainPanel/Header/BackButton").GetComponent<Button>();
            AssertPersistentCall(back, "Hide");
            Assert.That(back.onClick.GetPersistentTarget(0), Is.SameAs(screen));
            RawImage preview = GetField<RawImage>(screen, "cameraPreview");
            Vector2 size = preview.rectTransform.sizeDelta;
            screen.Show(101);
            // RuntimeOnly persistent listeners execute in PlayMode; their binding is checked above.
            screen.Hide();
            Assert.That(screen.gameObject.activeSelf, Is.False);
            Assert.That(GetField<GameObject>(screen, "returnScreenRoot").activeSelf, Is.True);
            Assert.That(preview.rectTransform.sizeDelta, Is.EqualTo(size));
            Assert.That(preview.raycastTarget, Is.False);
        }

        // Isolated unit fixture for the retained optional photo-review controller.
        // No objects are added to MainPanel and the scene is never saved.
        private void InstallPhotoReviewFixture()
        {
            photoReviewFixture = new GameObject("PhotoReviewUnitFixture", typeof(RectTransform));
            RectTransform area = photoReviewFixture.transform as RectTransform;
            area.sizeDelta = new Vector2(400f, 300f);
            foreach (string field in new[] { "progressText", "emptyText", "cameraButtonLabel" })
                SetField(screen, field, CreateReviewComponent<Text>(field));
            foreach (string field in new[] { "sourceButtons", "reviewActions" })
                SetField(screen, field, CreateReviewComponent<RectTransform>(field).gameObject);
            foreach (string field in new[] { "galleryButton", "cameraButton", "autoFitButton",
                "retryButton", "usePhotoButton", "completeButton" })
                SetField(screen, field, CreateReviewComponent<Button>(field));
            RawImage adjustable = CreateReviewComponent<RawImage>("adjustableImage");
            RectTransform overlay = CreateReviewComponent<RectTransform>("faceOverlayLayer");
            FaceImageAdjustView adjust = photoReviewFixture.AddComponent<FaceImageAdjustView>();
            SetField(adjust, "previewArea", area);
            SetField(adjust, "adjustableImage", adjustable.rectTransform);
            SetField(adjust, "faceOverlayLayer", overlay);
            SetField(screen, "adjustableImage", adjustable);
            SetField(screen, "faceOverlayLayer", overlay);
            SetField(screen, "imageAdjustView", adjust);
            var slots = new FaceSampleSlotView[FaceRegistrationScreen.RequiredSampleCount];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = CreateReviewComponent<FaceSampleSlotView>("PhotoSlot" + i);
            SetField(screen, "sampleSlots", slots);
        }

        private T CreateReviewComponent<T>(string name) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(photoReviewFixture.transform, false);
            return typeof(T) == typeof(RectTransform) ? go.GetComponent<T>() : go.AddComponent<T>();
        }

        private void AddCandidateSample()
        {
            Texture2D texture = CreateTexture($"Sample_{screen.SampleCount + 1}");
            SetField(screen, "candidateTexture", texture);
            SetField(screen, "candidateApproved", true);
            GetField<RawImage>(screen, "adjustableImage").texture = texture;
            screen.ApplyState(FaceRegistrationViewState.Reviewing);
            screen.UsePhoto();
        }

        private FakeFaceRegistrationImageSource InstallFakeSource()
        {
            Transform host = GameObject.Find("AppServices").transform.Find("ImageSourceHost");
            FakeFaceRegistrationImageSource fake =
                host.gameObject.AddComponent<FakeFaceRegistrationImageSource>();
            FaceRegistrationImageSourceAdapter adapter =
                GetField<FaceRegistrationImageSourceAdapter>(screen, "imageSource");
            SetField<IAvatarImageSource>(adapter, "source", fake);
            return fake;
        }

        private static void AssertPersistentCall(Button button, string methodName)
        {
            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo(methodName));
        }

        private static void RebuildLayoutGroups(Transform root)
        {
            foreach (LayoutGroup layout in root.GetComponentsInChildren<LayoutGroup>(true))
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(layout.transform as RectTransform);
            }

            Canvas.ForceUpdateCanvases();
        }

        private static Texture2D CreateTexture(string name)
        {
            return new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                name = name
            };
        }

        private static T GetField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(target);
        }

        private static void SetField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static FaceRegistrationScreen FindScreen(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                FaceRegistrationScreen result =
                    root.GetComponentInChildren<FaceRegistrationScreen>(true);

                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static EventSystem FindEventSystem()
        {
            EventSystem[] eventSystems =
                Resources.FindObjectsOfTypeAll<EventSystem>();

            Assert.That(eventSystems, Is.Not.Empty);
            return eventSystems[0];
        }

        private static void InvokeLayoutBranch(
            FaceRegistrationResponsiveLayout layout,
            string methodName)
        {
            MethodInfo method = layout.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(layout, new object[] { Vector4.zero });
        }
    }

    public static class TransformTestExtensions
    {
        public static string GetHierarchyPath(this Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = $"{transform.name}/{path}";
            }

            return path;
        }
    }

    public sealed class FakeFaceRegistrationImageSource : MonoBehaviour, IAvatarImageSource
    {
        private Texture2D previewTexture;

        public int PickCalls { get; private set; }
        public int StartCameraCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int StopCameraCalls { get; private set; }
        public bool IsCameraRunning { get; private set; }
        public bool IsCameraReady { get; private set; }

        public void PickImage(Action<byte[]> onSuccess, Action<string> onFailure)
        {
            PickCalls++;
            onSuccess?.Invoke(CreateImageBytes());
        }

        public void StartCamera(Action<Texture> onPreviewReady, Action<string> onFailure)
        {
            StartCameraCalls++;
            IsCameraRunning = true;
            IsCameraReady = true;
            previewTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            onPreviewReady?.Invoke(previewTexture);
        }

        public void CaptureImage(Action<byte[]> onSuccess, Action<string> onFailure)
        {
            CaptureCalls++;
            onSuccess?.Invoke(CreateImageBytes());
        }

        public void StopCamera()
        {
            StopCameraCalls++;
            IsCameraRunning = false;
            IsCameraReady = false;

            if (previewTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }

        private static byte[] CreateImageBytes()
        {
            var texture = new Texture2D(32, 24, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(0.2f, 0.6f, 0.8f, 1f);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            byte[] bytes = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            return bytes;
        }
    }
}
