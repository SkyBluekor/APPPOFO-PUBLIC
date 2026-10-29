using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using AppPofo.Presentation.Common;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AppPofo.Tests.EditMode
{
    public sealed class ProfileFormResponsiveTests
    {
        private RectTransform screen;
        private RectTransform canvasRect;
        private SafeAreaRoot safe;
        private const string RestorePlayModeSettings = "AppPofo.ProfileFormResponsive.RestorePlayModeSettings";

        [TearDown]
        public void RestoreEditorSettings()
        {
            if (!UnityEditor.SessionState.GetBool(RestorePlayModeSettings, false)) return;
            UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = UnityEditor.SessionState.GetBool(RestorePlayModeSettings + ".enabled", false);
            UnityEditor.SessionState.EraseBool(RestorePlayModeSettings);
            UnityEditor.SessionState.EraseBool(RestorePlayModeSettings + ".enabled");
        }

        private void Open(string name)
        {
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            Configure(name);
        }

        private void Configure(string name)
        {
            var candidates = Resources.FindObjectsOfTypeAll<RectTransform>()
                .Where(r => r.name == name && r.gameObject.scene.IsValid()).ToArray();
            Assert.That(candidates.Length, Is.EqualTo(1), name + " in scene " +
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().path +
                "; loaded roots: " + string.Join(",", UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                    .GetRootGameObjects().Select(root => root.name)));
            screen = candidates.Single();
            var canvas = screen.GetComponentInParent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            canvasRect = (RectTransform)canvas.transform;
            canvasRect.localScale = Vector3.one;
            foreach (Transform sibling in screen.parent)
                sibling.gameObject.SetActive(sibling == screen);
            safe = screen.GetComponentInChildren<SafeAreaRoot>(true);
            if (safe != null) safe.enabled = false;
        }

        private void Resize(float width, float height, bool insets)
        {
            float scale = Mathf.Sqrt(width / 1080f * height / 1920f);
            canvasRect.sizeDelta = new Vector2(width / scale, height / scale);
            if (safe != null)
                safe.ApplyViewport(new Rect(0, 0, width, height), insets
                    ? new Rect(48, 36, width - 96, height - 108)
                    : new Rect(0, 0, width, height));
            for (int i = 0; i < 3; i++)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(screen);
                Canvas.ForceUpdateCanvases();
            }
        }

        private RectTransform Find(string name) => screen.GetComponentsInChildren<RectTransform>(true)
            .Single(r => r.name == name || (name == "TitleText" && r.name == "Title"));
        private static Rect Bounds(RectTransform r, RectTransform relative)
        {
            var corners = new Vector3[4];
            r.GetWorldCorners(corners);
            Vector3 min = relative.InverseTransformPoint(corners[0]);
            Vector3 max = relative.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private static void Inside(RectTransform parent, RectTransform child)
        {
            Rect a = parent.rect, b = Bounds(child, parent);
            Assert.That(b.width, Is.GreaterThan(0), child.name);
            Assert.That(b.height, Is.GreaterThan(0), child.name);
            Assert.That(b.xMin, Is.GreaterThanOrEqualTo(a.xMin - .2f), child.name);
            Assert.That(b.xMax, Is.LessThanOrEqualTo(a.xMax + .2f),
                child.name + " screen=" + Screen.width + "x" + Screen.height +
                " parent=" + a + " child=" + b +
                " scale=" + (child.GetComponentInParent<Canvas>()?.scaleFactor ?? 0f));
            Assert.That(b.yMin, Is.GreaterThanOrEqualTo(a.yMin - .2f), child.name);
            Assert.That(b.yMax, Is.LessThanOrEqualTo(a.yMax + .2f), child.name);
        }
        private void Verify()
        {
            var container = Find("ScreenContainer");
            var header = Find("Header");
            var body = Find("ScrollBody");
            var footer = Find("Footer");
            Inside((RectTransform)safe.transform, container);
            foreach (var r in new[] {header, body, footer}) Inside(container, r);
            Assert.That(Bounds(header, container).yMin, Is.GreaterThanOrEqualTo(Bounds(body, container).yMax - .2f));
            Assert.That(Bounds(body, container).yMin, Is.GreaterThanOrEqualTo(Bounds(footer, container).yMax - .2f));
            Inside(header, Find("TitleText"));
            Inside(header, Find("BackButton"));
            Inside(body, Find("FormScrollView"));
            Inside(footer, Find(screen.name == "CreateProfileScreen" ? "SaveButton" : "UpdateButton"));
            Assert.That(container.rect.width, Is.LessThanOrEqualTo(1080.1f));
            var scroll = Find("FormScrollView").GetComponent<ScrollRect>();
            Assert.That(scroll.content.parent, Is.EqualTo(scroll.viewport));
            Assert.That(scroll.vertical && !scroll.horizontal, Is.True);
            // Verify the serialized scene policy, not the component's C# defaults:
            // Bootstrap.unity intentionally overrides both landscape and portrait
            // spacing to 20 and both horizontal paddings to 40.
            var responsive = scroll.content.GetComponent<ResponsiveFormLayout>();
            Assert.That(responsive, Is.Not.Null);
            var group = scroll.content.GetComponent<VerticalLayoutGroup>();
            bool landscape = scroll.content.parent is RectTransform orientationRect &&
                orientationRect.rect.width > orientationRect.rect.height;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string spacingField = landscape ? "landscapeSpacing" : "portraitSpacing";
            string paddingField = landscape ? "landscapeHorizontalPadding" : "portraitHorizontalPadding";
            var serializedSpacing = typeof(ResponsiveFormLayout).GetField(spacingField, flags);
            var serializedPadding = typeof(ResponsiveFormLayout).GetField(paddingField, flags);
            Assert.That(serializedSpacing, Is.Not.Null);
            Assert.That(serializedPadding, Is.Not.Null);
            Assert.That(group.spacing, Is.EqualTo((float)serializedSpacing.GetValue(responsive)).Within(.01f));
            int expectedPadding = (int)serializedPadding.GetValue(responsive);
            Assert.That(group.padding.left, Is.EqualTo(expectedPadding));
            Assert.That(group.padding.right, Is.EqualTo(expectedPadding));
            foreach (var input in scroll.content.GetComponentsInChildren<InputField>())
                Assert.That(((RectTransform)input.transform).rect.height,
                    Is.EqualTo(input.name == "BioInput" ? 170 : 86).Within(.1f));
            scroll.verticalNormalizedPosition = 0;
            Canvas.ForceUpdateCanvases();
            Inside(scroll.viewport, Find("WebsiteInput"));
            scroll.verticalNormalizedPosition = 1;
            Canvas.ForceUpdateCanvases();
            Inside(scroll.viewport, Find("DisplayNameInput"));
        }

        [TestCase(1080,1920)] [TestCase(2400,1080)]
        [TestCase(1536,2048)] [TestCase(2560,1600)]
        [TestCase(1920,1080)] [TestCase(1366,768)]
        [TestCase(1000,700)] [TestCase(800,600)]
        public void BothForms_ContainControlsAndScrollWithSafeInsets(int width, int height)
        {
            foreach (string name in new[] {"CreateProfileScreen", "EditProfileScreen"})
            {
                Open(name);
                Resize(width, height, true);
                Verify();
            }
        }

        [TestCase("CreateProfileScreen")]
        [TestCase("EditProfileScreen")]
        public void ReferencePortrait_PreservesAuthoredGeometry_AndCaptures(string name)
        {
            Open(name);
            Resize(1080,1920,false);
            Assert.That(Bounds(Find("TitleText"),screen).center.y, Is.EqualTo(795).Within(.1f));
            var button = Find(name == "CreateProfileScreen" ? "SaveButton" : "UpdateButton");
            Assert.That(Bounds(button, screen).yMin, Is.EqualTo(-602).Within(.1f));
            Assert.That(button.rect.size.x, Is.EqualTo(737.316f).Within(.1f));
            Assert.That(button.rect.size.y, Is.EqualTo(100).Within(.1f));
            var form = Find("FormScrollView");
            Assert.That(form.rect.width, Is.EqualTo(796.6904f).Within(.1f));
            Assert.That(form.rect.height, Is.EqualTo(1035.4292f).Within(.1f));
            Assert.That(Bounds(form,screen).center.y, Is.EqualTo(148.2854f).Within(.1f));
            Assert.That(Find("DisplayNameInput").rect.width, Is.EqualTo(716.6904f).Within(.1f));
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var cameraObject = new GameObject("PortraitCapture",typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            var target = new RenderTexture(1080,1920,24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1080,1920,TextureFormat.RGB24,false);
            try
            {
                camera.transform.position = screen.TransformPoint(new Vector3(0,0,-100));
                camera.transform.rotation = screen.rotation;
                camera.orthographic = true;
                camera.orthographicSize = 960;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0,0,1080,1920),0,0);
                texture.Apply();
                Directory.CreateDirectory("Logs/Phase2A");
                File.WriteAllBytes($"Logs/Phase2A/{name}.{(safe == null ? "before" : "after")}.png",texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator PlayMode_ContinuousSimulatedViewportResize_RestoresBothForms()
        {
            // An Editor GameView may be docked into a window smaller than a chosen
            // fixed resolution. Screen.width then reports the actual constrained
            // viewport (e.g. 1021) rather than the selected 1920-pixel preset.
            // Exercise the real PlayMode responsive components through an explicit
            // canvas viewport instead of depending on Editor window geometry.
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            var cameraScreen = Resources.FindObjectsOfTypeAll<AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen>()
                .SingleOrDefault(item => item.gameObject.scene.IsValid());
            Assert.That(cameraScreen, Is.Not.Null, "Bootstrap camera screen must be present.");
            cameraScreen.gameObject.SetActive(false);
            // EnterPlayMode reloads the domain. Create loop locals afterwards so
            // the runner never resumes a foreach with a lost screen-name local.
            // The runner can recompile player defines on entry even when fast
            // play mode disables domain reload. Use its standard reload contract
            // for this test, and restore the user's option even on failure.
            UnityEditor.SessionState.SetBool(RestorePlayModeSettings + ".enabled", UnityEditor.EditorSettings.enterPlayModeOptionsEnabled);
            UnityEditor.SessionState.SetBool(RestorePlayModeSettings, true);
            UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = false;
            yield return new EnterPlayMode();
            yield return null;
            Assert.That(UnityEngine.Application.isPlaying, Is.True, "Responsive resize must execute in PlayMode");
            foreach (string name in new[] { "CreateProfileScreen", "EditProfileScreen" })
            {
                Configure(name);

                var sizes = new[] {
                    new Vector2Int(800, 600), new Vector2Int(1080, 1920),
                    new Vector2Int(1366, 768), new Vector2Int(1920, 1080),
                    new Vector2Int(1000, 700), new Vector2Int(1080, 1920)
                };
                foreach (Vector2Int size in sizes)
                {
                    // Update the live RectTransform and safe-area calculations.
                    // No UnityEditor.GameView reflection and no fixed-size monitor needed.
                    Resize(size.x, size.y, false);
                    for (int frame = 0; frame < 4; frame++)
                    {
                        yield return null;
                        LayoutRebuilder.ForceRebuildLayoutImmediate(screen);
                        Canvas.ForceUpdateCanvases();
                    }
                    Assert.That(safe.AppliedSafeArea.width,
                        Is.EqualTo(size.x).Within(.01f), "Simulated safe-area width.");
                    Assert.That(safe.AppliedSafeArea.height,
                        Is.EqualTo(size.y).Within(.01f), "Simulated safe-area height.");
                    Assert.That(canvasRect.rect.width, Is.GreaterThan(0));
                    Assert.That(canvasRect.rect.height, Is.GreaterThan(0));
                    Verify();
                }

                // Restore the reference portrait geometry after switching orientations.
                Resize(1080, 1920, false);
                yield return null;
                Assert.That(Find("FormScrollView").rect.height,
                    Is.EqualTo(1035.4292f).Within(.1f));
            }
            yield return new ExitPlayMode();
        }

    }
}
