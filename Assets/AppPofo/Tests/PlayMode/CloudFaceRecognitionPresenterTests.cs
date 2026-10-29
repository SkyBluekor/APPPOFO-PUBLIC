using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AppPofo.Presentation.Components;
using AppPofo.Presentation.FaceRecognition;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AppPofo.Tests.PlayMode
{
    public sealed class CloudFaceRecognitionPresenterTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator NoMatchGrace_HasSoftAndHardDeadlines_AndNeverExtendsOnRepeatedFailure()
        {
            LogAssert.Expect(LogType.Log, "[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            var camera = UnityEngine.Object.FindAnyObjectByType<FaceRecognitionCamera>(FindObjectsInactive.Include);
            var panel = UnityEngine.Object.FindAnyObjectByType<BusinessCardPanelController>(FindObjectsInactive.Include);
            var host = new GameObject("grace test", typeof(RectTransform), typeof(Canvas));
            var root = (RectTransform)host.transform;
            root.sizeDelta = new Vector2(1080,1920);
            var preview = new GameObject("grace preview", typeof(RectTransform), typeof(RawImage));
            var orientation = preview.AddComponent<CameraPreviewOrientationEffect>();
            Type type = typeof(FaceRecognitionCamera).Assembly.GetType(
                "AppPofo.Presentation.FaceRecognition.CloudFaceRecognitionPresenter", true);
            Type hitType = type.Assembly.GetType("AppPofo.Presentation.FaceRecognition.CloudRecognitionHit", true);
            var overlay = (RectTransform)typeof(FaceRecognitionCamera).GetField("faceOverlayPrefab", Instance).GetValue(camera);
            object presenter = Activator.CreateInstance(type, Instance, null, new object[]{root,overlay,panel,orientation},null);
            Rect bounds = new Rect(20,20,30,30);
            object hit = Activator.CreateInstance(hitType);
            hitType.GetField("Card").SetValue(hit,new CloudFacePublicCard {
                id="11111111-1111-4111-8111-111111111111",display_name="Synthetic grace",headline="Role"});
            hitType.GetField("FaceBounds").SetValue(hit,bounds);
            Array hits=Array.CreateInstance(hitType,1);hits.SetValue(hit,0);
            void Observe(float at) => type.GetMethod("Observe",Instance).Invoke(presenter,new object[]{hits,100,100,at});
            void Fail(float at) => type.GetMethod("ObserveNoMatch",Instance).Invoke(presenter,new object[]{bounds,100,100,at});
            void Tick(float at) => type.GetMethod("Tick",Instance).Invoke(presenter,new object[]{at});
            void Pending(bool value) => type.GetMethod("SetScanInFlight",Instance).Invoke(presenter,new object[]{value});
            try
            {
                Fail(0f);
                Assert.That(root.GetComponentsInChildren<BusinessCardView>().Length,Is.Zero,"NO_MATCH cannot create identity.");
                Observe(0f);
                var card=root.GetComponentInChildren<BusinessCardView>();
                Fail(1f);Tick(2.49f);
                Assert.That(card.gameObject.activeSelf,Is.True);
                Tick(2.5f);
                Assert.That(card.gameObject.activeSelf,Is.False,"Without a scan in flight, expiry is 1.5s after the first failure.");

                Observe(3f);card=root.GetComponentInChildren<BusinessCardView>();
                Fail(4f);Pending(true);Tick(5.5f);
                Assert.That(card.gameObject.activeSelf,Is.True,"A pending next scan gets the bounded extension.");
                Fail(5.8f);Tick(5.99f);
                Assert.That(card.gameObject.activeSelf,Is.True);
                Tick(6f);
                Assert.That(card.gameObject.activeSelf,Is.False,"Repeated failures and a pending scan cannot extend beyond 2s.");

                Pending(false);Observe(7f);card=root.GetComponentInChildren<BusinessCardView>();
                Fail(8f);Observe(9f);Tick(10.1f);
                Assert.That(root.GetComponentInChildren<BusinessCardView>(),Is.SameAs(card),"MATCH cancels the failure deadline and reuses the card.");
                Fail(10.2f);
                type.GetMethod("ObserveDetections",Instance).Invoke(presenter,new object[]{Array.Empty<FaceDetectionItem>(),100,100});
                Assert.That(card.gameObject.activeSelf,Is.False,"Face loss bypasses the grace.");

                Observe(11f);card=root.GetComponentInChildren<BusinessCardView>();
                object replacement=Activator.CreateInstance(hitType);
                hitType.GetField("Card").SetValue(replacement,new CloudFacePublicCard {
                    id="22222222-2222-4222-8222-222222222222",display_name="Replacement",headline="Role"});
                hitType.GetField("FaceBounds").SetValue(replacement,bounds);
                Array replacementHits=Array.CreateInstance(hitType,1);replacementHits.SetValue(replacement,0);
                type.GetMethod("Observe",Instance).Invoke(presenter,new object[]{replacementHits,100,100,11.2f});
                Assert.That(card.gameObject.activeSelf,Is.False,"A different verified UUID at the same position replaces immediately.");
                Assert.That(root.GetComponentsInChildren<BusinessCardView>().Length,Is.EqualTo(1));
                card=root.GetComponentInChildren<BusinessCardView>();Fail(11.3f);
                type.GetMethod("Clear",Instance).Invoke(presenter,null);
                Assert.That(card.gameObject.activeSelf,Is.False,"Errors, permission loss and camera close must clear immediately.");
            }
            finally
            {
                type.GetMethod("Clear",Instance).Invoke(presenter,null);
                UnityEngine.Object.Destroy(host);UnityEngine.Object.Destroy(preview);
            }
        }

        [UnityTest]
        public IEnumerator SingleMatch_ShowsPublicCard_AndClearOrExpiryRemovesIt()
        {
            LogAssert.Expect(LogType.Log, "[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            var camera = UnityEngine.Object.FindAnyObjectByType<FaceRecognitionCamera>(FindObjectsInactive.Include);
            var panel = UnityEngine.Object.FindAnyObjectByType<BusinessCardPanelController>(FindObjectsInactive.Include);
            var rootObject = new GameObject("cloud card test root", typeof(RectTransform), typeof(Canvas));
            var root = (RectTransform)rootObject.transform;
            root.sizeDelta = new Vector2(1080, 1920);
            var previewObject = new GameObject("test preview", typeof(RectTransform), typeof(RawImage));
            var orientation = previewObject.AddComponent<CameraPreviewOrientationEffect>();
            Type type = typeof(FaceRecognitionCamera).Assembly.GetType(
                "AppPofo.Presentation.FaceRecognition.CloudFaceRecognitionPresenter", true);
            var overlay = (RectTransform)typeof(FaceRecognitionCamera).GetField("faceOverlayPrefab", Instance).GetValue(camera);
            object presenter = Activator.CreateInstance(type, Instance, null,
                new object[] { root, overlay, panel, orientation }, null);
            Type hitType = type.Assembly.GetType("AppPofo.Presentation.FaceRecognition.CloudRecognitionHit", true);
            object hit = Activator.CreateInstance(hitType);
            hitType.GetField("Card").SetValue(hit, new CloudFacePublicCard {
                id = "11111111-1111-4111-8111-111111111111", display_name = "Synthetic public name", headline = "Synthetic role" });
            hitType.GetField("FaceBounds").SetValue(hit, new Rect(20, 20, 30, 30));
            Array hits = Array.CreateInstance(hitType, 1);
            hits.SetValue(hit, 0);
            string previousDiagnostics = Environment.GetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS");
            var diagnosticLines = new List<string>();
            UnityEngine.Application.LogCallback capture = (message, stack, typeOfLog) => {
                if (message.StartsWith("[LocalFaceScan]", StringComparison.Ordinal)) diagnosticLines.Add(message);
            };
            try
            {
#if UNITY_EDITOR_WIN
                Environment.SetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS", "enabled");
                UnityEngine.Application.logMessageReceived += capture;
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.PreviewStarted);
#endif
                type.GetMethod("Observe", Instance).Invoke(presenter, new object[] { hits, 100, 100, 0f });
                var card = root.GetComponentInChildren<BusinessCardView>();
                Assert.That(card, Is.Not.Null, "The first validated MATCH must display a card.");
                Assert.That(card.gameObject.activeInHierarchy, Is.True);
                Assert.That(card.transform.parent, Is.SameAs(root));
                Assert.That(card.transform.Find("DisplayNameText").GetComponent<Text>().text, Is.EqualTo("Synthetic public name"));
                Assert.That(card.transform.Find("HeadlineText").GetComponent<Text>().text, Is.EqualTo("Synthetic role"));
#if UNITY_EDITOR_WIN
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(diagnosticLines.Exists(line => line.Contains("stage=Confirmation")), Is.False);
                Assert.That(diagnosticLines.Exists(line => line.Contains("stage=CardCreated count=1 elapsedMs=")), Is.True);
                Assert.That(diagnosticLines.Exists(line => line.Contains("stage=CardPresented count=1 elapsedMs=")), Is.True);
                Assert.That(diagnosticLines.TrueForAll(line => !line.Contains("Synthetic") &&
                    !line.Contains("11111111-1111-4111-8111-111111111111")), Is.True);
#endif
                type.GetMethod("Observe", Instance).Invoke(presenter, new object[] { hits, 100, 100, .5f });
                Assert.That(root.GetComponentInChildren<BusinessCardView>(), Is.SameAs(card), "Repeated hits reuse the card.");
                // A second UUID displays on its first MATCH and has its own position.
                object second = Activator.CreateInstance(hitType);
                hitType.GetField("Card").SetValue(second, new CloudFacePublicCard {
                    id="22222222-2222-4222-8222-222222222222", display_name="Second synthetic", headline="Second role" });
                hitType.GetField("FaceBounds").SetValue(second, new Rect(65,20,25,30));
                Array pair = Array.CreateInstance(hitType,2);pair.SetValue(hit,0);pair.SetValue(second,1);
                type.GetMethod("Observe", Instance).Invoke(presenter,new object[]{pair,100,100,6f});
                Assert.That(root.GetComponentsInChildren<BusinessCardView>().Length, Is.EqualTo(2));
                type.GetMethod("Observe", Instance).Invoke(presenter,new object[]{pair,100,100,7f});
                var visible=root.GetComponentsInChildren<BusinessCardView>();
                Assert.That(visible.Length,Is.EqualTo(2));
                Assert.That(((RectTransform)visible[0].transform).anchoredPosition,
                    Is.Not.EqualTo(((RectTransform)visible[1].transform).anchoredPosition));
                type.GetMethod("ObserveNoMatch",Instance).Invoke(presenter,new object[]{new Rect(20,20,30,30),100,100,7.1f});
                type.GetMethod("Tick",Instance).Invoke(presenter,new object[]{8.7f});
                var remaining=root.GetComponentsInChildren<BusinessCardView>();
                Assert.That(remaining.Length,Is.EqualTo(1),"A queried face failure must not clear another person's card.");
                Assert.That(remaining[0].transform.Find("DisplayNameText").GetComponent<Text>().text,Is.EqualTo("Second synthetic"));
                type.GetMethod("Observe",Instance).Invoke(presenter,new object[]{hits,100,100,9f});
                card=Array.Find(root.GetComponentsInChildren<BusinessCardView>(),view=>view.transform.Find("DisplayNameText").GetComponent<Text>().text=="Synthetic public name");
                type.GetMethod("ObserveDetections",Instance).Invoke(presenter,new object[]{
                    new[]{new FaceDetectionItem(new Rect(20,20,30,30),default,1f)},100,100});
                Assert.That(root.GetComponentsInChildren<BusinessCardView>().Length,Is.EqualTo(1),
                    "A missing face must disappear even while a different face remains.");
                type.GetMethod("Clear", Instance).Invoke(presenter, null);
                Assert.That(card.gameObject.activeSelf, Is.False, "No-face and permission revoke must hide immediately.");
                yield return null;
                Assert.That(root.GetComponentsInChildren<BusinessCardView>(true).Length, Is.Zero);

                type.GetMethod("Observe", Instance).Invoke(presenter, new object[] { hits, 100, 100, 20f });
                hitType.GetField("FaceBounds").SetValue(hit, new Rect(70, 70, 20, 20));
                type.GetMethod("Observe", Instance).Invoke(presenter, new object[] { hits, 100, 100, 25.5f });
                Assert.That(root.GetComponentsInChildren<BusinessCardView>().Length, Is.EqualTo(1), "A moved face displays immediately after track replacement.");
                type.GetMethod("Observe", Instance).Invoke(presenter, new object[] { hits, 100, 100, 31f });
                card = root.GetComponentInChildren<BusinessCardView>();
                Assert.That(card, Is.Not.Null);
                type.GetMethod("Tick", Instance).Invoke(presenter, new object[] { 44.1f });
                Assert.That(card.gameObject.activeSelf, Is.False, "13-second expiry hides the card.");
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= capture;
                Environment.SetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS", previousDiagnostics);
                type.GetMethod("Clear", Instance).Invoke(presenter, null);
                UnityEngine.Object.Destroy(rootObject);
                UnityEngine.Object.Destroy(previewObject);
            }
        }
    }
}
