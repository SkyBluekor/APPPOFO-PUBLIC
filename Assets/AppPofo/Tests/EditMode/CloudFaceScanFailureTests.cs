using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.FaceRecognition;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudFaceScanFailureTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [TestCase("stale")]
        [TestCase("fault")]
        [TestCase("permission-without-frame")]
        public void FailedCloudScan_ClearsTrackedCardState_BeforeHttp(string failure)
        {
            var host = new GameObject("inactive camera");
            host.SetActive(false); // Never start a camera or inference worker.
            var root = new GameObject("root", typeof(RectTransform));
            var overlay = new GameObject("overlay", typeof(RectTransform));
            var preview = new GameObject("preview", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
            ProfileDataMode previousMode = CloudModeSession.Mode;
            try
            {
                var camera = host.AddComponent<FaceRecognitionCamera>();
                var cards = host.AddComponent<BusinessCardPanelController>();
                var orientation = preview.AddComponent<CameraPreviewOrientationEffect>();
                Type presenterType = typeof(FaceRecognitionCamera).Assembly.GetType(
                    "AppPofo.Presentation.FaceRecognition.CloudFaceRecognitionPresenter", true);
                object presenter = Activator.CreateInstance(presenterType, Instance, null,
                    new object[] { root.transform, overlay.transform, cards, orientation }, null);
                var tracked = (IDictionary)presenterType.GetField("tracked", Instance).GetValue(presenter);
                // Seed presentation state without instantiating scene card prefabs in EditMode.
                Type trackedType = presenterType.GetNestedType("Tracked", BindingFlags.NonPublic);
                tracked.Add("11111111-1111-4111-8111-111111111111", Activator.CreateInstance(trackedType, true));
                Assert.That(tracked.Count, Is.EqualTo(1));
                presenterType.GetMethod("Tick", Instance).Invoke(presenter, new object[] { 5.5f });
                Assert.That(tracked.Count, Is.EqualTo(1), "A fresh track survives until expiry or explicit clearing.");

                typeof(CloudModeSession).GetProperty("Mode").SetValue(null, ProfileDataMode.Cloud);
                Set(camera, "previewMode", ProfileDataMode.Cloud);
                Set(camera, "previewActive", true);
                Set(camera, "cameraReady", true);
                Set(camera, "cloudPresenter", presenter);
                if (failure == "stale")
                {
                    bool invalid = (bool)typeof(FaceRecognitionCamera).GetMethod("AsyncFrameInvalid", Instance)
                        .Invoke(camera, new object[] { new CameraFrameOrientation(0, false, 100, 100),
                            Time.realtimeSinceStartupAsDouble - 5.1d, 0 });
                    Assert.That(invalid, Is.True);
                }
                else if (failure == "fault")
                {
                    typeof(FaceRecognitionCamera).GetMethod("ReportAsyncFailure", Instance).Invoke(camera,
                        new object[] { "synthetic", new InvalidOperationException("synthetic test") });
                }
                else
                {
                    if (CloudFaceMatchApi.CanQuery) Assert.Ignore("Developer opted in to local face scan.");
                    Set(camera, "cameraReady", false);
                    typeof(FaceRecognitionCamera).GetMethod("LateUpdate", Instance).Invoke(camera, null);
                }
                Assert.That(tracked.Count, Is.Zero, "Previously stale pre-HTTP branches retained old confirmation/card state.");
            }
            finally
            {
                typeof(CloudModeSession).GetProperty("Mode").SetValue(null, previousMode);
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(overlay);
                UnityEngine.Object.DestroyImmediate(preview);
            }
        }

        [Test]
        public void Diagnostics_EditorOptIn_AndFixedOperationalMetadata()
        {
            string previous = Environment.GetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS");
            try
            {
                Environment.SetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS", null);
                Assert.That(CloudFaceScanDiagnostics.Enabled, Is.False);
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.MATCH);
                LogAssert.NoUnexpectedReceived();
#if UNITY_EDITOR_WIN
                Environment.SetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS", "enabled");
                LogAssert.Expect(LogType.Log, new Regex(@"^\[LocalFaceScan\] stage=HttpCompleted count=\d+ elapsedMs=42\.0 http=200( sincePreviewMs=\d+\.\d)?$"));
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.HttpCompleted, durationMs: 42d, httpStatus: 200);
#endif
            }
            finally { Environment.SetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS", previous); }
        }

        [TestCase(CloudFaceScanStage.WaitingForScan, CloudFaceScanStage.ScanWait)]
        [TestCase(CloudFaceScanStage.WaitingForBudget, CloudFaceScanStage.BudgetWait)]
        [TestCase(CloudFaceScanStage.QueryBlocked, CloudFaceScanStage.QueryWait)]
        public void Diagnostics_CountsResetPerPreview_AndWaitIsClosedOnce(
            CloudFaceScanStage waiting, CloudFaceScanStage completed)
        {
#if UNITY_EDITOR_WIN
            string previous = Environment.GetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS");
            try
            {
                Environment.SetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS", "enabled");
                for (int session = 0; session < 2; session++)
                {
                    ExpectStage("PreviewStarted", 1);
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.PreviewStarted);
                    ExpectStage("RequestSent", 1);
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.RequestSent);
                    ExpectStage("RequestSent", 2);
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.RequestSent);
                    ExpectStage(waiting.ToString(), 1);
                    typeof(CloudFaceScanDiagnostics).GetMethod("Gate", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { waiting });
                    ExpectStage(completed.ToString(), 1, true);
                    typeof(CloudFaceScanDiagnostics).GetMethod("EndGate", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, null);
                    typeof(CloudFaceScanDiagnostics).GetMethod("EndGate", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, null);
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally { Environment.SetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS", previous); }
#endif
        }

        private static void ExpectStage(string stage, int count, bool duration = false) =>
            LogAssert.Expect(LogType.Log, new Regex(@"^\[LocalFaceScan\] stage=" + stage +
                " count=" + count + (duration ? @" elapsedMs=\d+\.\d" : "") + @" sincePreviewMs=\d+\.\d$"));

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Instance).SetValue(target, value);
    }
}
