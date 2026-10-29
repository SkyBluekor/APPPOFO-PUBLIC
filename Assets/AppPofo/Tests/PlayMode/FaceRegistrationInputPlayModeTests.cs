using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.FaceRegistration.Selection;
using AppPofo.Presentation.Screens;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;
using AppPofo.Presentation.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AppPofo.Tests.PlayMode
{
    public sealed class FaceRegistrationInputPlayModeTests
    {
        private FaceRegistrationScreen screen;
        private ProfileDetailScreen profileDetail;
        private PlayModeImageSourceStub previewSource;
        private Action restoreGameView;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;

            screen = FindSceneObject<FaceRegistrationScreen>();
            profileDetail = FindSceneObject<ProfileDetailScreen>();
            Assert.That(screen, Is.Not.Null);
            Assert.That(profileDetail, Is.Not.Null);

            // Stop scene auto-preview before installing a deterministic transport.
            // Re-enable via Show so the unchanged CameraController owns startup.
            screen.gameObject.SetActive(false);
            previewSource = InstallStub();
            profileDetail.gameObject.SetActive(true);
            SetField(profileDetail, "currentProfileId", 7001L);
        }

        [TearDown]
        public void TearDown()
        {
            if (screen != null)
            {
                screen.Hide();
            }
            restoreGameView?.Invoke();
            restoreGameView = null;
        }

        [UnityTest]
        public IEnumerator CloudEnrollment_UsesUuidTarget_AndCannotClaimUnimplementedSave()
        {
            if (CloudFaceLocalTestFactory.IsLocalCaptureEnabled)
                Assert.Ignore("Disable the explicit loopback upload mode before testing the default Pending writer.");
            const string cloudId = "72d8d1df-62bd-443b-af3f-e63c2f6b810e";
            string returnedId = null;
            bool? returnedCompleted = null;
            screen.ShowCloud(cloudId, profileDetail.gameObject, (id, completed) =>
            {
                returnedId = id;
                returnedCompleted = completed;
            });
            yield return null;

            Assert.That(screen.CurrentProfileId, Is.Zero,
                "Cloud target must not fabricate a local SQLite numeric ID.");
            Assert.That(screen.CurrentCloudProfileId, Is.EqualTo(cloudId));
            Assert.That(screen.IsCloudEnrollment, Is.True);
            Assert.That(screen.HasEnrollmentTarget, Is.True);
            Assert.That(profileDetail.gameObject.activeSelf, Is.False);

            var controller = screen.GetComponentInChildren<FaceRegistrationCamera>(true);
            var writer = GetField<IFaceEnrollmentWriter>(controller, "enrollmentWriter");
            Assert.That(writer, Is.TypeOf<PendingCloudFaceEnrollmentWriter>());
            var front = new float[128]; front[0] = 1;
            var left = new float[128]; left[1] = 1;
            var right = new float[128]; right[2] = 1;
            var set = new FaceEmbeddingSet(front, left, right, "SFace-Test");
            FaceEnrollmentSaveResult result = null;
            IEnumerator routine = writer.Save(set, value => result = value);
            while (routine.MoveNext()) yield return routine.Current;
            Assert.That(result.Status, Is.EqualTo(FaceEnrollmentSaveStatus.Unavailable));

            screen.Hide();
            yield return null;
            Assert.That(returnedId, Is.EqualTo(cloudId));
            Assert.That(returnedCompleted, Is.False);
            Assert.That(screen.CurrentCloudProfileId, Is.Null);
            Assert.That(profileDetail.gameObject.activeSelf, Is.True);
            Assert.That(previewSource.IsCameraRunning, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

#if UNITY_EDITOR_WIN
        [UnityTest]
        public IEnumerator LocalFaceWriter_RequiresOptInAndNeverUsesSQLiteTarget()
        {
            const string testUuid = "d8c9c066-627c-42f9-bb39-6e521a74dde3";
            string[] keys =
            {
                "APPPOFO_FACE_LOCAL_ADMIN_TOKEN",
                "APPPOFO_FACE_LOCAL_POLICY_VERSION",
                "APPPOFO_FACE_LOCAL_CLIENT_ENABLED",
                "APPPOFO_FACE_LOCAL_CAPTURE_TEST_APPROVED",
                "APPPOFO_FACE_LOCAL_DISCOVERABLE_APPROVED"
            };
            string[] saved = Array.ConvertAll(keys, key =>
                Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process));
            // This is a loopback-only test. A production-enabled build config
            // intentionally blocks the local test writer in FaceRegistrationScreen.
            // Isolate the settings for this test, then restore the exact prior state.
            FieldInfo cloudSettings = typeof(CloudServerConfiguration).GetField(
                "settings", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(cloudSettings, Is.Not.Null);
            object previousCloudSettings = cloudSettings.GetValue(null);
            try
            {
                cloudSettings.SetValue(null, new CloudServerConfiguration.Settings
                {
                    environment = "production",
                    production_faces_enabled = false
                });
                Assert.That(CloudServerConfiguration.RemoteFacesEnabled, Is.False);
                Environment.SetEnvironmentVariable(keys[0], new string('x', 48),
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable(keys[1], "local-face-test-v1",
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable(keys[2], "enabled",
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable(keys[3], "YES_LOCAL_BIOMETRIC_TEST",
                    EnvironmentVariableTarget.Process);
                Assert.That(CloudFaceLocalTestFactory.IsLocalCaptureEnabled, Is.True);
                // Existing local storage consent must NEVER imply scan visibility.
                Environment.SetEnvironmentVariable(keys[4], null,
                    EnvironmentVariableTarget.Process);
                Assert.That(CloudFaceLocalTestFactory.CreateOrPending(
                    testUuid, true, true), Is.TypeOf<PendingCloudFaceEnrollmentWriter>());
                Assert.That(CloudFaceLocalTestFactory.CreateOrPending(
                    testUuid, false, true), Is.TypeOf<CloudFaceEnrollmentWriter>());
                Environment.SetEnvironmentVariable(keys[4], "YES_LOCAL_DISCOVERABLE_TEST",
                    EnvironmentVariableTarget.Process);
                Assert.That(CloudFaceLocalTestFactory.CreateOrPending(
                    testUuid, true, true), Is.TypeOf<CloudFaceEnrollmentWriter>());
                Environment.SetEnvironmentVariable(keys[4], null,
                    EnvironmentVariableTarget.Process);

                // Ineligible subject/consent must remain offline even with
                // developer test mode enabled. No HTTP POST is sent here.
                screen.ShowCloud(testUuid, profileDetail.gameObject, null,
                    false, false);
                yield return null;
                FaceRegistrationCamera controller =
                    screen.GetComponentInChildren<FaceRegistrationCamera>(true);
                Assert.That(GetField<IFaceEnrollmentWriter>(controller, "enrollmentWriter"),
                    Is.TypeOf<PendingCloudFaceEnrollmentWriter>());
                screen.Hide();

                // With the explicit subject-present assertion, only the
                // loopback writer may be constructed. Do not call Save here.
                screen.ShowCloud(testUuid, profileDetail.gameObject, null,
                    false, true);
                yield return null;
                Assert.That(screen.CurrentProfileId, Is.Zero);
                Assert.That(screen.CurrentCloudProfileId, Is.EqualTo(testUuid));
                var writer = GetField<IFaceEnrollmentWriter>(controller,
                    "enrollmentWriter");
                Assert.That(writer, Is.TypeOf<CloudFaceEnrollmentWriter>());
                object transport = typeof(CloudFaceEnrollmentWriter)
                    .GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(writer);
                Assert.That(transport.GetType().Name, Is.EqualTo("CloudFaceLoopbackTransport"));
                screen.Hide();

                // Exercise the actual Editor window bridge without rendering
                // the window or sending a single real HTTP request.
                Type windowType = Type.GetType(
                    "AppPofo.Editor.FaceLocalTestWindow, Assembly-CSharp-Editor");
                Assert.That(windowType, Is.Not.Null,
                    "The local-only Editor test window must compile into the Editor assembly.");
                MethodInfo editorEntry = windowType.GetMethod("OpenLocalEnrollment",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(editorEntry, Is.Not.Null);
                LogAssert.Expect(LogType.Warning,
                    "[FaceLocalTest] Local enrollment is not authorized.");
                editorEntry.Invoke(null, new object[] { testUuid, false, true });
                Assert.That(screen.CurrentCloudProfileId, Is.Null);

                MethodInfo discoverableEntry = windowType.GetMethod(
                    "OpenLocalDiscoverableEnrollment",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(discoverableEntry, Is.Not.Null);
                LogAssert.Expect(LogType.Warning,
                    "[FaceLocalTest] Local discoverability is not authorized.");
                discoverableEntry.Invoke(null, new object[]
                {
                    testUuid, true, true, false
                });
                Assert.That(screen.CurrentCloudProfileId, Is.Null);
                Assert.That(Environment.GetEnvironmentVariable(keys[4]), Is.Null);

                // Editor bridge synchronously calls ShowCloud, which starts the
                // stub camera and logs Camera ready BEFORE the bridge's
                // "screen opened" log. Explicitly register that real order;
                // otherwise the stub auto-enqueues Camera ready too late.
                previewSource.AutoExpectCameraReadyLog = false;
                try
                {
                    LogAssert.Expect(LogType.Log,
                        "[FaceRegistration] Camera ready");
                    LogAssert.Expect(LogType.Log,
                        "[FaceLocalTest] Local-only enrollment screen opened.");
                    editorEntry.Invoke(null, new object[] { testUuid, true, true });
                }
                finally
                {
                    previewSource.AutoExpectCameraReadyLog = true;
                }
                yield return null;
                Assert.That(screen.CurrentProfileId, Is.Zero);
                Assert.That(screen.CurrentCloudProfileId, Is.EqualTo(testUuid));
                Assert.That(GetField<IFaceEnrollmentWriter>(controller, "enrollmentWriter"),
                    Is.TypeOf<CloudFaceEnrollmentWriter>());
                screen.Hide();

                // Completing the editor test must NEVER reveal a stale local
                // ProfileDetailScreen and pretend it is the UUID we enrolled.
                GameObject localResult = GameObject.Find("LocalFaceTestResult");
                Assert.That(localResult, Is.Not.Null);
                Assert.That(localResult.activeInHierarchy, Is.True);
                Text status = localResult.GetComponentInChildren<Text>(true);
                Assert.That(status, Is.Not.Null);
                Assert.That(status.text, Does.Contain(testUuid));
                Assert.That(status.text, Does.Contain("기존 명함/프로필과 연결하지 않았습니다."));
                Assert.That(localResult.transform.GetSiblingIndex(),
                    Is.EqualTo(localResult.transform.parent.childCount - 1),
                    "Local test result must render above the stale profile detail.");
                Assert.That(CloudFaceLocalTestFactory.IsLocalCaptureEnabled, Is.False,
                    "Test authorization should be cleared when the camera returns.");
            }
            finally
            {
                for (int i = 0; i < keys.Length; i++)
                    Environment.SetEnvironmentVariable(keys[i], saved[i],
                        EnvironmentVariableTarget.Process);
                cloudSettings.SetValue(null, previousCloudSettings);
            }
            LogAssert.NoUnexpectedReceived();
        }
#endif

        [UnityTest]
        public IEnumerator ProfileEntry_UsesCameraControllerPreview_AndBackStopsCamera()
        {
            Button entry = profileDetail.transform
                .Find("SafeAreaRoot/CenteredContent/ProfileActions/FaceRegistrationButton").GetComponent<Button>();
            entry.onClick.Invoke();
            yield return null;

            Assert.That(screen.gameObject.activeInHierarchy, Is.True);
            Assert.That(profileDetail.gameObject.activeInHierarchy, Is.False);
            Assert.That(screen.HasPhotoReviewUI, Is.False);
            RawImage preview = screen.transform
                .Find("SafeAreaRoot/MainPanel/Body/CameraSection/CameraContainer/PreviewViewport/DisplayFrame/CameraPreview").GetComponent<RawImage>();
            FaceRegistrationCamera controller = screen.transform
                .Find("CameraController").GetComponent<FaceRegistrationCamera>();
            Assert.That(GetField<RawImage>(screen, "cameraPreview"), Is.SameAs(preview));
            Assert.That(GetField<RawImage>(controller, "cameraPreview"), Is.SameAs(preview));
            Assert.That(GetField<FaceRegistrationImageSourceAdapter>(controller, "imageSource"),
                Is.SameAs(GetField<FaceRegistrationImageSourceAdapter>(screen, "imageSource")));
            Assert.That(previewSource.StartCameraCalls, Is.EqualTo(1));
            Assert.That(previewSource.CaptureCalls, Is.Zero);
            Assert.That(preview.texture, Is.Not.Null);
            Assert.That(preview.gameObject.activeInHierarchy, Is.True);

            screen.transform.Find("SafeAreaRoot/MainPanel/Header/BackButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(screen.gameObject.activeInHierarchy, Is.False);
            Assert.That(profileDetail.gameObject.activeInHierarchy, Is.True);
            Assert.That(previewSource.IsCameraRunning, Is.False);
            Assert.That(preview.texture, Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AlreadyRegisteredFace_UsesExistingConfirmation_InsteadOfRecognitionCamera()
        {
            // The test must not depend on (or modify) the user's SQLite profiles.
            var bootstrap = GetField<AppPofoBootstrap>(profileDetail, "bootstrap");
            var repositoryField = typeof(AppPofoBootstrap).GetField(
                "<FaceEmbeddingRepository>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(repositoryField, Is.Not.Null);
            var original = bootstrap.FaceEmbeddingRepository;
            var registeredFaces = new RegisteredFaceRepositoryStub();
            repositoryField.SetValue(bootstrap, registeredFaces);
            try
            {
                profileDetail.gameObject.SetActive(false);
                profileDetail.gameObject.SetActive(true);
                Assert.That(GetField<Text>(profileDetail, "faceActionLabel").text,
                    Is.EqualTo("얼굴 등록하기"));

                var entry = profileDetail.transform.Find(
                    "SafeAreaRoot/CenteredContent/ProfileActions/FaceRegistrationButton")
                    .GetComponent<Button>();
                var confirm = GetField<GameObject>(profileDetail, "deleteConfirmPanel");

                entry.onClick.Invoke();
                Assert.That(confirm.activeInHierarchy, Is.True,
                    "A registered face must open confirmation, not recognition.");
                Assert.That(GetField<Text>(profileDetail, "confirmationMessage").text,
                    Does.Contain("다시 등록하시겠습니까"));
                Assert.That(screen.gameObject.activeInHierarchy, Is.False);
                Assert.That(previewSource.StartCameraCalls, Is.Zero);

                profileDetail.CloseDeleteConfirm();
                Assert.That(confirm.activeSelf, Is.False);
                Assert.That(screen.gameObject.activeInHierarchy, Is.False);
                Assert.That(previewSource.StartCameraCalls, Is.Zero);

                entry.onClick.Invoke();
                profileDetail.ConfirmProfileAction();
                yield return null;

                Assert.That(confirm.activeSelf, Is.False);
                Assert.That(screen.gameObject.activeInHierarchy, Is.True);
                Assert.That(screen.CurrentProfileId, Is.EqualTo(7001));
                Assert.That(previewSource.StartCameraCalls, Is.EqualTo(1));
                Assert.That(profileDetail.gameObject.activeInHierarchy, Is.False);
                Assert.That(registeredFaces.WriteCalls, Is.Zero);
            }
            finally
            {
                repositoryField.SetValue(bootstrap, original);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReenterActiveMainPanel_StartsFreshRegistrationSession()
        {
            screen.Show(7001);
            yield return null;
            RawImage preview = GetField<RawImage>(screen, "cameraPreview");
            int stops = previewSource.StopCameraCalls;
            string guidance = GetField<Text>(screen, "statusText").text;
            var controller = screen.GetComponentInChildren<FaceRegistrationCamera>(true);
            SetField(controller, "currentStep", FaceRegistrationStep.Left);
            SetField(controller, "validCandidateCount", 9);
            SetField(controller, "darkWarningLatched", true);
            SetField(controller, "darkSince", Time.unscaledTime);
            SetField(controller, "lastAnnouncedStep", (FaceRegistrationStep?)FaceRegistrationStep.Left);

            screen.Show(7002);
            foreach (FaceRegistrationViewState state in Enum.GetValues(typeof(FaceRegistrationViewState)))
                screen.ApplyState(state);
            yield return null;

            Assert.That(screen.CurrentProfileId, Is.EqualTo(7002));
            Assert.That(preview.texture, Is.Not.Null);
            Assert.That(preview.gameObject.activeInHierarchy, Is.True);
            Assert.That(previewSource.StartCameraCalls, Is.EqualTo(2));
            Assert.That(previewSource.StopCameraCalls, Is.GreaterThan(stops));
            Assert.That(GetField<FaceRegistrationStep>(controller, "currentStep"), Is.EqualTo(FaceRegistrationStep.Front));
            Assert.That(GetField<int>(controller, "validCandidateCount"), Is.Zero);
            Assert.That(GetField<FaceRegistrationStep?>(controller, "lastAnnouncedStep"), Is.Null);
            Assert.That(GetField<bool>(controller, "darkWarningLatched"), Is.False);
            Assert.That(GetField<float>(controller, "darkSince"), Is.LessThan(0f));
            Assert.That(GetField<AudioSource>(controller, "guidanceAudioSource").clip,
                Is.SameAs(GetField<AudioClip>(controller, "centerVoice")));
            // The responsive instruction chip may move the counter onto a new line.
            // Reentering must retain the same instruction, not identical layout wrapping.
            Assert.That(GetField<Text>(screen, "statusText").text.Replace('\n', ' '),
                Is.EqualTo(guidance.Replace('\n', ' ')));

            screen.Hide();
            yield return null;
            screen.Show(7003);
            yield return null;
            Assert.That(previewSource.StartCameraCalls, Is.EqualTo(3));
            Assert.That(preview.texture, Is.Not.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SideRejectionFeedback_UpdatesLiveChipWithoutCameraInference()
        {
            screen.Show(7001);
            yield return null;
            var controller = screen.GetComponentInChildren<FaceRegistrationCamera>(true);
            var instruction = GetField<Text>(screen, "statusText");

            // The camera transport and worker are irrelevant for this presentation test.
            // Disable only the controller; the parent screen remains active and discoverable.
            controller.enabled = false;
            SetField(controller, "currentStep", FaceRegistrationStep.Left);
            SetField(controller, "validCandidateCount", 3);
            screen.RefreshRegistrationProgressUi(controller);
            var feedback = typeof(FaceRegistrationCamera).GetMethod("UpdateRegistrationFeedback",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(feedback, Is.Not.Null);
            // The current diagnostic method has optional rear-exposure inputs.
            // Reflection.Invoke still requires an argument for every parameter.
            Action<FrameQualityMetrics, bool> diagnose = (metrics, accepted) =>
            {
                var parameters = feedback.GetParameters();
                var arguments = new object[parameters.Length];
                arguments[0] = default(CameraFrameOrientation);
                arguments[1] = metrics;
                arguments[2] = accepted;
                for (int i = 3; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    arguments[i] = parameter.ParameterType.IsValueType
                        ? Activator.CreateInstance(parameter.ParameterType)
                        : null;
                }
                feedback.Invoke(controller, arguments);
            };

            diagnose(new FrameQualityMetrics { RejectionStage = "Yaw", YawScore = .10f }, false);
            Assert.That(instruction.text.Replace('\n', ' '),
                Is.EqualTo("고개를 왼쪽으로 조금 더 돌려 주세요 (3/10)"));

            diagnose(new FrameQualityMetrics { RejectionStage = "FaceSize", FaceSizeRatio = .40f }, false);
            Assert.That(instruction.text.Replace('\n', ' '),
                Is.EqualTo("카메라에 조금 더 가까이 와주세요 (3/10)"));

            diagnose(new FrameQualityMetrics { RejectionStage = "Accepted" }, true);
            Assert.That(instruction.text.Replace('\n', ' '),
                Is.EqualTo("고개를 왼쪽으로 돌려 주세요 (3/10)"));

            SetField(controller, "currentStep", FaceRegistrationStep.Right);
            diagnose(new FrameQualityMetrics { RejectionStage = "Yaw", YawScore = -.10f }, false);
            Assert.That(instruction.text.Replace('\n', ' '),
                Is.EqualTo("고개를 오른쪽으로 조금 더 돌려 주세요 (3/10)"));

            diagnose(default(FrameQualityMetrics), false);
            Assert.That(instruction.text.Replace('\n', ' '),
                Is.EqualTo("카메라에 얼굴이 보이도록 맞춰 주세요 (3/10)"));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator VoiceGuidance_DoesNotReplaySameStep_AndNewStepInterruptsOldVoice()
        {
            screen.Show(7001);
            var controller = screen.GetComponentInChildren<FaceRegistrationCamera>(true);
            var source = GetField<AudioSource>(controller, "guidanceAudioSource");
            Assert.That(source.clip, Is.SameAs(GetField<AudioClip>(controller, "centerVoice")));
            InvokeCamera(controller, "PlayStepVoiceOnce", FaceRegistrationStep.Front);
            yield return new WaitForSecondsRealtime(.15f);
            float playedTime = source.time;
            Assert.That(playedTime, Is.GreaterThan(0f));
            InvokeCamera(controller, "PlayStepVoiceOnce", FaceRegistrationStep.Front);
            Assert.That(source.time, Is.GreaterThanOrEqualTo(playedTime - .01f));
            InvokeCamera(controller, "EnterStep", FaceRegistrationStep.Left);
            Assert.That(source.clip, Is.SameAs(GetField<AudioClip>(controller, "leftVoice")));
            Assert.That(source.time, Is.LessThan(.05f));
            Assert.That(controller.GetComponents<AudioSource>(), Has.Length.EqualTo(1));
            InvokeCamera(controller, "EnterStep", FaceRegistrationStep.Right);
            Assert.That(source.clip, Is.SameAs(GetField<AudioClip>(controller, "rightVoice")));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DarkWarning_Debounces_Latches_AndYieldsToStepVoice()
        {
            screen.Show(7001);
            yield return null;
            var controller = screen.GetComponentInChildren<FaceRegistrationCamera>(true);
            var source = GetField<AudioSource>(controller, "guidanceAudioSource");
            controller.enabled = false;
            // Isolate voice state from inference and use deterministic audio duration.
            var priorityClip = AudioClip.Create("GuidancePriorityTest", 44100 * 5, 1, 44100, false);
            InvokeCamera(controller, "PlayVoice", priorityClip);
            InvokeCamera(controller, "UpdateDarkWarning", true);
            Assert.That(GetField<bool>(controller, "darkWarningLatched"), Is.False);
            // Exercise the actual debounce duration while another announcement has priority.
            yield return new WaitForSecondsRealtime(1.6f);
            InvokeCamera(controller, "UpdateDarkWarning", true);
            Assert.That(GetField<bool>(controller, "darkWarningLatched"), Is.False);
            source.Stop();
            InvokeCamera(controller, "UpdateDarkWarning", true);
            Assert.That(GetField<bool>(controller, "darkWarningLatched"), Is.True);
            Assert.That(source.clip, Is.SameAs(GetField<AudioClip>(controller, "tooDarkVoice")));
            source.Stop();
            InvokeCamera(controller, "UpdateDarkWarning", true);
            Assert.That(source.isPlaying, Is.False);
            InvokeCamera(controller, "UpdateDarkWarning", false);
            Assert.That(GetField<bool>(controller, "darkWarningLatched"), Is.False);
            SetField(controller, "darkSince", Mathf.Max(0f, Time.unscaledTime - 1.6f));
            InvokeCamera(controller, "UpdateDarkWarning", true);
            Assert.That(GetField<bool>(controller, "darkWarningLatched"), Is.True);
            InvokeCamera(controller, "EnterStep", FaceRegistrationStep.Left);
            Assert.That(source.clip, Is.SameAs(GetField<AudioClip>(controller, "leftVoice")));
            UnityEngine.Object.Destroy(priorityClip);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CompleteFeedback_ReturnsToProfile_AndReopeningCancelsOldClose()
        {
            screen.Show(7001);
            yield return null;
            var controller = screen.GetComponentInChildren<FaceRegistrationCamera>(true);
            InvokeCamera(controller, "EnterStep", FaceRegistrationStep.Complete);
            var source = GetField<AudioSource>(controller, "guidanceAudioSource");
            Assert.That(source.clip, Is.SameAs(GetField<AudioClip>(controller, "completeVoice")));
            float deadline = GetField<float>(controller, "completionCloseTime");
            Assert.That(deadline - Time.unscaledTime,
                Is.EqualTo(GetField<AudioClip>(controller, "completeVoice").length).Within(.05f));
            SetField(controller, "completionCloseTime", Time.unscaledTime - .1f);
            yield return null;
            Assert.That(screen.gameObject.activeInHierarchy, Is.False);
            Assert.That(profileDetail.gameObject.activeInHierarchy, Is.True);
            Assert.That(source.isPlaying, Is.False);
            screen.Show(7002);
            InvokeCamera(controller, "EnterStep", FaceRegistrationStep.Complete);
            screen.Hide();
            screen.Show(7003);
            yield return null;
            Assert.That(screen.gameObject.activeInHierarchy, Is.True);
            Assert.That(GetField<float>(controller, "completionCloseTime"), Is.LessThan(0f));
            Assert.That(GetField<bool>(controller, "darkWarningLatched"), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RegisteredProfile_RequiresConfirmation_CancelAndRetryPreserveEmbeddings()
        {
            var repository = new RegisteredFaceRepositoryStub();
            var bootstrap = GetField<AppPofoBootstrap>(profileDetail, "bootstrap");
            SetField(bootstrap, "<FaceEmbeddingRepository>k__BackingField", repository);
            var panel = GetField<GameObject>(profileDetail, "deleteConfirmPanel");
            string deleteTitle = GetField<Text>(profileDetail, "confirmationTitle").text;
            profileDetail.OpenFaceRegistration();
            Assert.That(panel.activeInHierarchy, Is.True);
            Assert.That(screen.gameObject.activeInHierarchy, Is.False);
            Assert.That(GetField<Text>(profileDetail, "confirmationMessage").text,
                Is.EqualTo("얼굴 등록이 완료되어 있습니다.\n다시 등록하시겠습니까?"));
            Assert.That(GetField<Text>(profileDetail, "confirmationActionLabel").text, Is.EqualTo("다시 등록"));
            panel.transform.Find("Dialog/Footer/CancelButton").GetComponent<Button>().onClick.Invoke();
            Assert.That(panel.activeSelf, Is.False);
            Assert.That(profileDetail.gameObject.activeInHierarchy, Is.True);
            profileDetail.OpenFaceRegistration();
            panel.transform.Find("Dialog/Footer/ConfirmDeleteButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(screen.gameObject.activeInHierarchy, Is.True);
            Assert.That(panel.activeSelf, Is.False);
            screen.Hide();
            Assert.That(GetField<Text>(profileDetail, "confirmationTitle").text, Is.EqualTo(deleteTitle));
            profileDetail.OpenFaceRegistration();
            Assert.That(panel.activeInHierarchy, Is.True);
            Assert.That(repository.WriteCalls, Is.Zero);
            Assert.That(repository.LastProfileId, Is.EqualTo(7001));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RegistrationCamera_DeliversFirstPreviewBeforeConstructingAiWorkers()
        {
            screen.Show(7001);
            yield return null;
            var camera = screen.GetComponentInChildren<FaceRegistrationCamera>(true);
            Assert.That(GetField<RawImage>(screen, "cameraPreview").texture, Is.Not.Null);
            Assert.That(GetField<AppPofo.Presentation.FaceRegistration.Embedding.SFaceRecognizer>(
                camera, "sFaceRecognizer"), Is.Null,
                "The SFace Worker must not precede first visible preview.");
            Assert.That(GetField<AppPofo.Presentation.FaceRegistration.Detection.SentisFaceDetector>(
                camera, "detector"), Is.Null,
                "No oriented preview frame is available from this stub yet.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CameraSwitch_RegistrationIconStaysOutsidePreviewAndWithinSafeArea()
        {
            previewSource.SupportsRearCamera = true;
            screen.Show(7001);
            yield return null;
            Canvas.ForceUpdateCanvases();
            screen.GetComponent<FaceRegistrationResponsiveLayout>().ApplyLayout();
            yield return null;

            RectTransform safe = screen.transform.Find("SafeAreaRoot") as RectTransform;
            RectTransform preview = screen.transform.Find(
                "SafeAreaRoot/MainPanel/Body/CameraSection/CameraContainer/PreviewViewport")
                as RectTransform;
            RectTransform switchRect = screen.transform.Find(
                "SafeAreaRoot/CameraSwitchButton") as RectTransform;

            Assert.That(safe, Is.Not.Null);
            Assert.That(preview, Is.Not.Null);
            Assert.That(switchRect, Is.Not.Null);
            Assert.That(switchRect.GetComponent<Button>().interactable, Is.True);
            AssertButtonReceivesRaycast(switchRect.GetComponent<Button>());
            Assert.That(switchRect.GetComponent<CameraSwitchButtonArtwork>(), Is.Not.Null);
            Assert.That(switchRect.Find("CameraSwitchIcon"), Is.Not.Null);
            Assert.That(switchRect.GetComponentsInChildren<Text>(true), Is.Empty,
                "Camera switch must use an icon, not the old two-line label.");
            Assert.That(switchRect.rect.width, Is.EqualTo(96f).Within(.1f));
            AssertContained(safe, switchRect);

            Rect previewRect = RectCornersInSpace(safe, preview);
            Rect buttonRect = RectCornersInSpace(safe, switchRect);
            Assert.That(previewRect.Overlaps(buttonRect), Is.False,
                "Registration camera switch must be OUTSIDE the live face preview.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CameraSwitch_RecognitionIconIsSmallAndAnchoredToSafeAreaBottomRight()
        {
            previewSource.SupportsRearCamera = true;
            ExpectRecognitionStartupTrace("view-enter");
            ExpectRecognitionStartupTrace("camera-request");
            ExpectRecognitionStartupTrace("camera-callback");
            screen.ShowRecognition(profileDetail.gameObject);
            yield return null;
            Canvas.ForceUpdateCanvases();
            screen.GetComponent<FaceRegistrationResponsiveLayout>().ApplyLayout();
            yield return null;

            RectTransform safe = screen.transform.Find("SafeAreaRoot") as RectTransform;
            RectTransform switchRect = screen.transform.Find(
                "SafeAreaRoot/CameraSwitchButton") as RectTransform;
            Assert.That(switchRect, Is.Not.Null);
            Assert.That(switchRect.rect.width, Is.EqualTo(80f).Within(.1f));
            Assert.That(switchRect.Find("CameraSwitchIcon"), Is.Not.Null);
            Assert.That(switchRect.GetComponentsInChildren<Text>(true), Is.Empty);
            AssertContained(safe, switchRect);
            AssertButtonReceivesRaycast(switchRect.GetComponent<Button>());
            Vector3 local = safe.InverseTransformPoint(switchRect.position);
            Assert.That(safe.rect.xMax - local.x, Is.InRange(50f, 80f),
                "Recognition switch belongs at the safe area's right edge.");
            Assert.That(local.y - safe.rect.yMin, Is.InRange(50f, 80f),
                "Recognition switch belongs at the safe area's bottom edge.");
            Assert.That(screen.transform.Find("SafeAreaRoot/CameraSwitchConfirmation"),
                Is.Null);

            ExpectRecognitionStartupTrace("view-exit");
            screen.Hide();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CameraSwitch_UnsupportedRearDoesNotShowButton()
        {
            Assert.That(previewSource.SupportsRearCamera, Is.False);
            screen.Show(7001);
            yield return null;
            Assert.That(screen.transform.Find("SafeAreaRoot/CameraSwitchButton"), Is.Null);
            Assert.That(previewSource.CurrentFacing, Is.EqualTo(CameraFacing.Front));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CameraSwitch_RequiresConfirmationAndClearsStaleRegistrationCandidates()
        {
            previewSource.SupportsRearCamera = true;
            screen.Show(7001);
            yield return null;
            Button switchButton = screen.transform.Find("SafeAreaRoot/CameraSwitchButton")
                .GetComponent<Button>();
            var controller = screen.GetComponentInChildren<FaceRegistrationCamera>(true);
            Assert.That(switchButton, Is.Not.Null);
            Assert.That(previewSource.CurrentFacing, Is.EqualTo(CameraFacing.Front));
            int previousStarts = previewSource.StartCameraCalls;
            SetField(controller, "currentStep", FaceRegistrationStep.Left);
            SetField(controller, "validCandidateCount", 3);

            switchButton.onClick.Invoke();
            var dialog = screen.transform.Find("SafeAreaRoot/CameraSwitchConfirmation");
            Assert.That(dialog, Is.Not.Null);
            Assert.That(dialog.gameObject.activeSelf, Is.True);
            Assert.That(previewSource.CurrentFacing, Is.EqualTo(CameraFacing.Front));
            dialog.Find("ConfirmationCard/취소Button").GetComponent<Button>().onClick.Invoke();
            Assert.That(dialog.gameObject.activeSelf, Is.False);
            Assert.That(GetField<int>(controller, "validCandidateCount"), Is.EqualTo(3));
            Assert.That(previewSource.StartCameraCalls, Is.EqualTo(previousStarts));

            switchButton.onClick.Invoke();
            dialog.Find("ConfirmationCard/전환Button").GetComponent<Button>().onClick.Invoke();
            yield return null; // Allow the old WebCamTexture to be disposed.
            yield return null; // The new lens is then started.
            Assert.That(dialog.gameObject.activeSelf, Is.False);
            Assert.That(previewSource.CurrentFacing, Is.EqualTo(CameraFacing.Rear));
            Assert.That(GetField<FaceRegistrationStep>(controller, "currentStep"),
                Is.EqualTo(FaceRegistrationStep.Front));
            Assert.That(GetField<int>(controller, "validCandidateCount"), Is.Zero);
            Assert.That(previewSource.StartCameraCalls, Is.EqualTo(previousStarts + 1));
            Assert.That(previewSource.IsCameraReady, Is.True);
            Assert.That(switchButton.interactable, Is.True);
            screen.Hide();
            Assert.That(previewSource.CurrentFacing, Is.EqualTo(CameraFacing.Front),
                "Leaving the face screen must restore the shared avatar camera default.");
            screen.Show(7001);
            yield return null;
            Assert.That(previewSource.CurrentFacing, Is.EqualTo(CameraFacing.Rear),
                "Face screen lens preference survives a normal return.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CameraSwitch_RecognitionDoesNotRequireRegistrationConfirmation()
        {
            previewSource.SupportsRearCamera = true;
            // Only this test triggers two intentionally traced recognition camera
            // starts. Expect their exact PERF4 stages without ignoring other logs.
            ExpectRecognitionStartupTrace("view-enter");
            ExpectRecognitionStartupTrace("camera-request");
            ExpectRecognitionStartupTrace("camera-callback");
            screen.ShowRecognition(profileDetail.gameObject);
            yield return null;
            Button switchButton = screen.transform.Find("SafeAreaRoot/CameraSwitchButton")
                .GetComponent<Button>();
            Assert.That(switchButton.gameObject.activeInHierarchy, Is.True);
            int starts = previewSource.StartCameraCalls;
            ExpectRecognitionStartupTrace("view-exit");
            ExpectRecognitionStartupTrace("view-enter");
            ExpectRecognitionStartupTrace("camera-request");
            ExpectRecognitionStartupTrace("camera-callback");
            switchButton.onClick.Invoke();
            yield return null; // Camera teardown frame.
            yield return null; // Camera restart frame.
            Assert.That(previewSource.CurrentFacing, Is.EqualTo(CameraFacing.Rear));
            Assert.That(previewSource.StartCameraCalls, Is.EqualTo(starts + 1));
            Assert.That(screen.IsRecognitionMode, Is.True);
            Assert.That(screen.transform.Find("SafeAreaRoot/CameraSwitchConfirmation"),
                Is.Null, "Recognition lens switch must not open the registration reset dialog.");
            ExpectRecognitionStartupTrace("view-exit");
            screen.Hide();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RegistrationToRecognition_TransfersSharedCameraAndCanReturn()
        {
            screen.Show(7001);
            yield return null;
            RawImage preview = GetField<RawImage>(screen, "cameraPreview");
            Assert.That(previewSource.StartCameraCalls, Is.EqualTo(1));
            Assert.That(previewSource.IsCameraReady, Is.True);

            ExpectRecognitionStartupTrace("view-enter");
            ExpectRecognitionStartupTrace("camera-request");
            ExpectRecognitionStartupTrace("camera-callback");
            screen.ShowRecognition(profileDetail.gameObject);
            yield return null;
            Assert.That(screen.IsRecognitionMode, Is.True);
            Assert.That(previewSource.StopCameraCalls, Is.GreaterThan(0),
                "Registration must release its camera before recognition starts.");
            Assert.That(previewSource.StartCameraCalls, Is.EqualTo(2));
            Assert.That(previewSource.IsCameraReady, Is.True);
            Assert.That(preview.texture, Is.Not.Null);

            ExpectRecognitionStartupTrace("view-exit");
            screen.Hide();
            yield return null;
            Assert.That(previewSource.IsCameraRunning, Is.False);
            Assert.That(preview.texture, Is.Null);

            screen.Show(7001);
            yield return null;
            Assert.That(screen.IsRecognitionMode, Is.False);
            Assert.That(previewSource.StartCameraCalls, Is.EqualTo(3));
            Assert.That(previewSource.IsCameraReady, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        private static void ExpectRecognitionStartupTrace(string stage)
        {
#if APPPOFO_PERF_DIAGNOSTICS && (UNITY_EDITOR || DEVELOPMENT_BUILD)
            LogAssert.Expect(LogType.Log,
                new System.Text.RegularExpressions.Regex(
                    @"^\[PERF4\] Camera startup attempt=\d+ stage=" +
                    System.Text.RegularExpressions.Regex.Escape(stage) +
                    @" elapsedMs=\d+(?:\.\d+)?(?: durationMs=\d+(?:\.\d+)?)?$"));
#endif
        }

        private static void InvokeCamera(FaceRegistrationCamera controller, string method, object value)
        {
            typeof(FaceRegistrationCamera).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new[] { value });
        }

        private sealed class RegisteredFaceRepositoryStub : IFaceEmbeddingRepository
        {
            public int WriteCalls { get; private set; }
            public long LastProfileId { get; private set; }
            public bool HasByProfile(long profileId) { LastProfileId = profileId; return true; }
            public System.Collections.Generic.IReadOnlyList<FaceEmbedding> GetByModel(string modelVersion)
                => Array.Empty<FaceEmbedding>();
            public FaceEmbedding Add(FaceEmbedding embedding)
            {
                WriteCalls++;
                throw new AssertionException("Registration UX must not write an embedding.");
            }
            public void DeleteByProfile(long profileId)
            {
                WriteCalls++;
                throw new AssertionException("Re-registration must preserve the existing embedding.");
            }
            public void ReplaceByProfile(long profileId, System.Collections.Generic.IReadOnlyList<FaceEmbedding> embeddings)
            {
                WriteCalls++;
            }
        }


        [UnityTest]
        public IEnumerator PortraitGameViewSizes_ReflowWithoutRestartingCamera()
        {
#if UNITY_EDITOR
            previewSource.SupportsRearCamera = true;
            screen.Show(7001);
            yield return null;
            var preview = GetField<RawImage>(screen, "cameraPreview");
            Texture original = preview.texture;
            int starts = previewSource.StartCameraCalls;
            var sizes = new[] { new Vector2Int(720,1280), new Vector2Int(1080,1920),
                new Vector2Int(1080,2340), new Vector2Int(1080,2400),
                new Vector2Int(1440,2560), new Vector2Int(1536,2048),
                new Vector2Int(1600,2560), new Vector2Int(720,1280) };
            Vector2 firstPosition = default, firstSize = default;
            for (int i = 0; i < sizes.Length; i++)
            {
                SetGameViewSize(sizes[i].x, sizes[i].y);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.That(Screen.width, Is.EqualTo(sizes[i].x));
                Assert.That(Screen.height, Is.EqualTo(sizes[i].y));
                Assert.That(screen.transform.Find("SafeAreaRoot/MainPanel/Body")
                    .GetComponent<AppPofo.Presentation.Common.AdaptiveLinearLayoutGroup>().Vertical, Is.True);
                Assert.That(preview.texture, Is.SameAs(original));
                Assert.That(previewSource.StartCameraCalls, Is.EqualTo(starts));
                var safe = screen.GetComponentInChildren<AppPofo.Presentation.Common.SafeAreaRoot>();
                var panel = (RectTransform)safe.transform.Find("MainPanel");
                var body = (RectTransform)panel.Find("Body");
                var camera = (RectTransform)body.Find("CameraSection");
                var guide = (RectTransform)body.Find("GuideSection");
                AssertContained((RectTransform)safe.transform, panel);
                AssertContained(body, camera); AssertContained(body, guide);
                var switchRect = screen.transform.Find("SafeAreaRoot/CameraSwitchButton")
                    as RectTransform;
                Assert.That(switchRect, Is.Not.Null);
                screen.RefreshCameraSwitchPlacement();
                AssertContained((RectTransform)safe.transform, switchRect);
                Rect previewRect = RectCornersInSpace((RectTransform)safe.transform,
                    preview.rectTransform);
                Rect switchBounds = RectCornersInSpace((RectTransform)safe.transform, switchRect);
                Rect viewportBounds = RectCornersInSpace((RectTransform)safe.transform,
                    (RectTransform)screen.transform.Find(
                        "SafeAreaRoot/MainPanel/Body/CameraSection/CameraContainer/PreviewViewport"));
                Assert.That(previewRect.Overlaps(switchBounds), Is.False,
                    "Camera switch overlaps preview at " + sizes[i].x + "x" + sizes[i].y +
                    " | safe=" + ((RectTransform)safe.transform).rect +
                    " | viewport=" + viewportBounds +
                    " | actualPreview=" + previewRect +
                    " | switch=" + switchBounds +
                    " | canvasScale=" + screen.GetComponentInParent<Canvas>().scaleFactor);
                foreach (Transform child in guide) AssertContained(guide, (RectTransform)child);
                Assert.That(preview.rectTransform.rect.width / preview.rectTransform.rect.height,
                    Is.EqualTo(844f / 459f).Within(.002f));
                if (i == 0) { firstPosition = camera.anchoredPosition; firstSize = camera.rect.size; }
                if (i == sizes.Length - 1)
                {
                    Assert.That(Vector2.Distance(camera.anchoredPosition, firstPosition), Is.LessThan(.1f));
                    Assert.That(Vector2.Distance(camera.rect.size, firstSize), Is.LessThan(.1f));
                }
                string folder = Path.GetFullPath(".utmp/PortraitP1Screenshots");
                Directory.CreateDirectory(folder);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, sizes[i].x + "x" + sizes[i].y + ".png"));
                yield return null;
            }
            LogAssert.NoUnexpectedReceived();
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator CameraMetadataChanges_UpdateOnlyVideo_AndCaptureUpright()
        {
            screen.Show(7001);
            yield return null;
            RawImage preview = GetField<RawImage>(screen,"cameraPreview");
            var guide = GetField<GameObject>(screen,"faceGuide").GetComponent<RectTransform>();
            Vector2 previewSize = preview.rectTransform.sizeDelta;
            Vector2 guideSize = guide.sizeDelta;
            Quaternion guideRotation = guide.localRotation;
            var source = previewSource;
            // This test only validates orientation metadata and upright PNG capture.
            // Never start the real YuNet pipeline: its PERF4 startup logs and GPU
            // readbacks are unrelated, nondeterministic side effects of camera input.
            var registration = screen.GetComponentInChildren<FaceRegistrationCamera>(true);
            SetField<object>(registration, "faceDetectionModel", null);
            Assert.That(preview.GetComponent<CameraPreviewOrientationEffect>(),Is.Null);
            source.MetadataReady = true;
            foreach (int angle in new[] {0,90,180,270,0})
                foreach (bool mirror in new[] {false,true})
                {
                    source.FrameOrientation = new CameraFrameOrientation(angle,mirror,64,48);
                    yield return null;
                    yield return null;
                    var effect = preview.GetComponent<CameraPreviewOrientationEffect>();
                    Assert.That(effect,Is.Not.Null);
                    Assert.That(effect.Orientation,Is.EqualTo(source.FrameOrientation));
                    Assert.That(preview.rectTransform.sizeDelta,Is.EqualTo(previewSize));
                    Assert.That(guide.sizeDelta,Is.EqualTo(guideSize));
                    Assert.That(guide.localRotation,Is.EqualTo(guideRotation));
                    byte[] png = null;
                    GetField<FaceRegistrationImageSourceAdapter>(screen,"imageSource").CaptureImage(
                        bytes=>png=bytes, message=>Assert.Fail(message));
                    var decoded = new Texture2D(2,2);
                    Assert.That(decoded.LoadImage(png),Is.True);
                    Assert.That(decoded.width,Is.EqualTo(source.FrameOrientation.EffectiveWidth));
                    Assert.That(decoded.height,Is.EqualTo(source.FrameOrientation.EffectiveHeight));
                    UnityEngine.Object.Destroy(decoded);
                }
            Assert.That(source.StartCameraCalls,Is.EqualTo(1));
            Assert.That(source.CaptureCalls,Is.Zero,"Face input must use canonical capture, not the raw avatar path.");
            Assert.That(source.UprightCaptureCalls,Is.EqualTo(10));
            var applied = preview.GetComponent<CameraPreviewOrientationEffect>();
            screen.Hide();
            Assert.That(applied.HasOrientation,Is.False);
            yield return null;
            source.MetadataReady = false;
            screen.Show(7002);
            yield return null;
            Assert.That(applied.HasOrientation,Is.False,"Do not reuse a previous camera session's metadata.");
            source.MetadataReady = true;
            source.FrameOrientation = new CameraFrameOrientation(0,false,64,48);
            yield return null;
            yield return null;
            Assert.That(applied.Orientation,Is.EqualTo(source.FrameOrientation));
            Assert.That(source.StartCameraCalls,Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived();
        }

#if UNITY_EDITOR
        private void SetGameViewSize(int width, int height)
        {
            Assembly assembly = typeof(UnityEditor.Editor).Assembly;
            Type sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            Type singleton = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType);
            object sizes = singleton.GetProperty("instance").GetValue(null);
            object groupType = sizesType.GetProperty("currentGroupType").GetValue(sizes);
            object group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
            Type gameViewType = assembly.GetType("UnityEditor.GameView");
            var view = UnityEditor.EditorWindow.GetWindow(gameViewType);
            var selection = gameViewType.GetProperty("selectedSizeIndex",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (restoreGameView == null)
            {
                int previous = (int)selection.GetValue(view);
                int originalCount = (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null);
                int builtinCount = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null);
                restoreGameView = () =>
                {
                    selection.SetValue(view, previous);
                    int current = (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null);
                    for (int index = current - 1; index >= originalCount; index--)
                        group.GetType().GetMethod("RemoveCustomSize").Invoke(group, new object[] { builtinCount + index });
                    view.Repaint();
                };
            }
            Type sizeType = assembly.GetType("UnityEditor.GameViewSize");
            Type enumType = assembly.GetType("UnityEditor.GameViewSizeType");
            object size = Activator.CreateInstance(sizeType,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { Enum.ToObject(enumType, 1), (object)width, height, "Phase1 validation" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            selection.SetValue(view, count - 1);
            view.Repaint();
        }
#endif
        private static Rect RectCornersInSpace(RectTransform relativeTo, RectTransform child)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (Vector3 corner in corners)
            {
                Vector3 local = relativeTo.InverseTransformPoint(corner);
                var point = new Vector2(local.x, local.y);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void AssertContained(RectTransform parent, RectTransform child)
        {
            var corners = new Vector3[4]; child.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Vector3 local = parent.InverseTransformPoint(corner);
                Assert.That(local.x, Is.InRange(parent.rect.xMin - .2f, parent.rect.xMax + .2f), child.name);
                Assert.That(local.y, Is.InRange(parent.rect.yMin - .2f, parent.rect.yMax + .2f), child.name);
            }
        }

        [UnityTest]
        [Explicit("Windows 파일 선택 창과 사용자 세션이 필요합니다.")]
        [Category("Manual")]
        public IEnumerator ActualEditorFilePicker_ReturnsImageBytes()
        {
            EditorAvatarImageSource source = GetActualEditorSource();
            byte[] selectedBytes = null;
            string failure = null;

            source.PickImage(
                bytes => selectedBytes = bytes,
                message => failure = message);
            yield return null;

            Assert.That(failure, Is.Null.Or.Empty);
            Assert.That(selectedBytes, Is.Not.Null.And.Not.Empty);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.That(texture.LoadImage(selectedBytes), Is.True);
            Assert.That(texture.width, Is.GreaterThan(2));
            Assert.That(texture.height, Is.GreaterThan(2));
            UnityEngine.Object.Destroy(texture);
        }

        [UnityTest]
        [Explicit("실제 Windows WebCam 장치와 권한이 필요합니다.")]
        [Category("Manual")]
        [Timeout(90000)]
        public IEnumerator ActualEditorWebCam_PreviewsAndCaptures()
        {
            EditorAvatarImageSource source = GetActualEditorSource();
            source.StopCamera();
            Texture previewTexture = null;
            byte[] capturedBytes = null;
            string failure = null;

            source.StartCamera(
                texture => previewTexture = texture,
                message => failure = message);

            float deadline = Time.realtimeSinceStartup + 45f;
            while ((previewTexture == null || !source.IsCameraReady) &&
                string.IsNullOrEmpty(failure) &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(failure, Is.Null.Or.Empty);
            Assert.That(previewTexture, Is.Not.Null);
            Assert.That(source.IsCameraReady, Is.True);

            source.CaptureImage(
                bytes => capturedBytes = bytes,
                message => failure = message);

            deadline = Time.realtimeSinceStartup + 15f;
            while (capturedBytes == null &&
                string.IsNullOrEmpty(failure) &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(failure, Is.Null.Or.Empty);
            Assert.That(capturedBytes, Is.Not.Null.And.Not.Empty);
            source.StopCamera();
            Assert.That(source.IsCameraRunning, Is.False);
        }

        [UnityTest]
        [Explicit("UI 시각 검토용 캡처입니다.")]
        [Category("Manual")]
        public IEnumerator CaptureProfileDetailAndPortraitFaceRegistrationLayouts()
        {
            Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            string outputDirectory = Path.Combine(
                UnityEngine.Application.dataPath,
                "../LayoutCaptures");
            Directory.CreateDirectory(outputDirectory);

            yield return CaptureLayout(
                profileDetail.gameObject,
                screen.gameObject,
                1080,
                1920,
                Path.Combine(outputDirectory, "ProfileDetail_Portrait.png"));
            yield return CaptureLayout(
                profileDetail.gameObject,
                screen.gameObject,
                1920,
                1080,
                Path.Combine(outputDirectory, "ProfileDetail_Landscape.png"));
            yield return CaptureLayout(
                screen.gameObject,
                profileDetail.gameObject,
                1080,
                1920,
                Path.Combine(outputDirectory, "FaceRegistration_Portrait.png"));
        }

        private static IEnumerator CaptureLayout(
            GameObject visible,
            GameObject hidden,
            int width,
            int height,
            string outputPath)
        {
            hidden.SetActive(false);
            visible.SetActive(true);
            Screen.SetResolution(width, height, false);
            yield return null;
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();
            ScreenCapture.CaptureScreenshot(outputPath);

            float deadline = Time.realtimeSinceStartup + 10f;
            while (!File.Exists(outputPath) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(File.Exists(outputPath), Is.True, outputPath);
        }

        private PlayModeImageSourceStub InstallStub()
        {
            GameObject host = GameObject.Find("AppServices").transform
                .Find("ImageSourceHost").gameObject;
            PlayModeImageSourceStub stub = host.AddComponent<PlayModeImageSourceStub>();
            FaceRegistrationImageSourceAdapter adapter =
                GetField<FaceRegistrationImageSourceAdapter>(screen, "imageSource");
            SetField<IAvatarImageSource>(adapter, "source", stub);
            return stub;
        }

        private static EditorAvatarImageSource GetActualEditorSource()
        {
            GameObject host = GameObject.Find("AppServices").transform
                .Find("ImageSourceHost").gameObject;
            Assert.That(host.activeInHierarchy, Is.True);
            EditorAvatarImageSource source = host.GetComponent<EditorAvatarImageSource>();
            Assert.That(source, Is.Not.Null);
            Assert.That(source.isActiveAndEnabled, Is.True);
            return source;
        }

        private static void AssertButtonReceivesRaycast(Button button)
        {
            EventSystem eventSystem = EventSystem.current;
            Assert.That(eventSystem, Is.Not.Null);
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(
                null,
                button.transform.position);
            var pointer = new PointerEventData(eventSystem)
            {
                position = screenPosition
            };
            var results = new System.Collections.Generic.List<RaycastResult>();
            eventSystem.RaycastAll(pointer, results);

            Assert.That(
                results.Exists(result => result.gameObject == button.gameObject),
                Is.True,
                $"EventSystem raycast가 {button.name}에 도달하지 못했습니다.");
        }

        private static T FindSceneObject<T>() where T : Component
        {
            foreach (T candidate in Resources.FindObjectsOfTypeAll<T>())
            {
                if (candidate.gameObject.scene.IsValid())
                {
                    return candidate;
                }
            }

            return null;
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
    }

    public sealed class PlayModeImageSourceStub : MonoBehaviour, IAvatarImageSource, IOrientedCameraSource, ICameraFacingSource
    {
        private Texture2D preview;
        public bool MetadataReady { get; set; }
        // A caller that pre-registers the correct multi-log order can disable
        // only the stub's automatic expectation for that single camera start.
        public bool AutoExpectCameraReadyLog { get; set; } = true;
        public CameraFrameOrientation FrameOrientation { get; set; }
        public int UprightCaptureCalls { get; private set; }

        public bool TryGetPreviewFrame(out Texture texture, out CameraFrameOrientation orientation)
        {
            texture = preview;
            orientation = FrameOrientation;
            return MetadataReady && IsCameraReady;
        }

        public void CaptureUprightImage(Action<byte[]> onSuccess, Action<string> onFailure)
        {
            if (!MetadataReady) { CaptureImage(onSuccess,onFailure); return; }
            UprightCaptureCalls++;
            var upright = FrameOrientation.CreateUprightTexture(preview.GetPixels32());
            onSuccess?.Invoke(upright.EncodeToPNG());
            Destroy(upright);
        }

        public int PickCalls { get; private set; }
        public int StartCameraCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int StopCameraCalls { get; private set; }
        public bool IsCameraRunning { get; private set; }
        public bool IsCameraReady { get; private set; }
        public bool SupportsRearCamera { get; set; }
        public CameraFacing CurrentFacing { get; private set; } = CameraFacing.Front;
        public bool TrySelectFacing(CameraFacing facing)
        {
            if (IsCameraRunning || (facing == CameraFacing.Rear && !SupportsRearCamera))
                return false;
            CurrentFacing = facing;
            return true;
        }

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
            preview = new Texture2D(64, 48, TextureFormat.RGBA32, false);
            // The stub synchronously delivers one new preview, which makes the
            // production camera emit exactly one informational readiness log.
            // Expect only this exact line, never blanket-ignore test messages.
            if (onPreviewReady != null && AutoExpectCameraReadyLog)
            {
                bool recognition = onPreviewReady.Method.DeclaringType ==
                    typeof(AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera);
                LogAssert.Expect(LogType.Log, recognition
                    ? "[FaceRecognition] Camera ready"
                    : "[FaceRegistration] Camera ready");
            }
            onPreviewReady?.Invoke(preview);
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
            if (preview != null)
            {
                Destroy(preview);
                preview = null;
            }
        }

        private static byte[] CreateImageBytes()
        {
            var texture = new Texture2D(32, 24, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(0.15f, 0.55f, 0.75f, 1f);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            byte[] bytes = texture.EncodeToPNG();
            Destroy(texture);
            return bytes;
        }
    }
}
