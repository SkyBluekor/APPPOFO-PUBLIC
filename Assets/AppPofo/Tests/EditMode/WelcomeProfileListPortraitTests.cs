using System;
using System.Linq;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AppPofo.Tests.EditMode
{
    public sealed class WelcomeProfileListPortraitTests
    {
        private RectTransform screen, safe, centered;

        private void Open(string name, int width, int height, bool insets)
        {
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            screen = Resources.FindObjectsOfTypeAll<RectTransform>()
                .Single(r => r.name == name && r.gameObject.scene.IsValid());
            var canvas = screen.GetComponentInParent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvas.transform;
            rect.localScale = Vector3.one;
            float scale = Mathf.Sqrt(width / 1080f * height / 1920f);
            rect.sizeDelta = new Vector2(width / scale, height / scale);
            foreach (Transform sibling in screen.parent) sibling.gameObject.SetActive(sibling == screen);
            safe = (RectTransform)screen.Find("SafeAreaRoot");
            safe.GetComponent<SafeAreaRoot>().enabled = false;
            safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0, 0, width, height),
                insets ? new Rect(48, 36, width - 96, height - 108) : new Rect(0, 0, width, height));
            safe.GetComponent<ResponsiveUIContext>().Refresh();
            safe.GetComponent<WelcomePortraitLayout>()?.ApplyLayout();
            safe.GetComponent<ProfileListPortraitLayout>()?.ApplyLayout();
            centered = (RectTransform)safe.Find("CenteredContent");
            for (int i = 0; i < 3; i++) LayoutRebuilder.ForceRebuildLayoutImmediate(centered);
            Canvas.ForceUpdateCanvases();
        }

        [Test]
        public void SearchPanelPrefab_HasPretendardFontsWithoutStretchedIllustrationReferences()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AppPofo/Presentation/Resources/ProfileSearchPanel.prefab");
            Assert.That(prefab, Is.Not.Null, "Search panel prefab must be included in the project.");
            var view = prefab.GetComponent<ProfileSearchPanelView>();
            Assert.That(view, Is.Not.Null, "Search prefab requires ProfileSearchPanelView.");
            var serialized = new UnityEditor.SerializedObject(view);
            foreach (string name in new[] { "regularFont", "mediumFont", "boldFont", "inputFieldSprite" })
            {
                var property = serialized.FindProperty(name);
                Assert.That(property, Is.Not.Null, name);
                Assert.That(property.objectReferenceValue, Is.Not.Null,
                    name + " must reference an existing Pretendard font.");
            }
            var fieldSprite = serialized.FindProperty("inputFieldSprite").objectReferenceValue as Sprite;
            Assert.That(fieldSprite, Is.Not.Null);
            Assert.That(fieldSprite.name, Does.StartWith("input_profile_add_field"));
            var boldFont = serialized.FindProperty("boldFont").objectReferenceValue as Font;
            Assert.That(boldFont, Is.Not.Null);
            Assert.That(boldFont.name, Does.Contain("Pretendard-Bold"));
            foreach (string obsolete in new[]
                { "glassPanelSprite", "inputBackgroundSprite",
                  "secondaryButtonSprite", "accentButtonSprite" })
                Assert.That(serialized.FindProperty(obsolete), Is.Null,
                    obsolete + " used an oversized decorative sprite as a small UI control.");
        }

        [TestCase(720,1280)] [TestCase(1080,1920)] [TestCase(1080,2340)]
        [TestCase(1080,2400)] [TestCase(1440,2560)] [TestCase(1536,2048)] [TestCase(1600,2560)]
        public void Welcome_PortraitSafeArea_ContainsBodyAndActions(int width, int height)
        {
            Open("WelcomeScreen", width, height, true);
            AssertInside(screen, safe); AssertInside(safe, centered);
            Assert.That(centered.rect.width, Is.LessThanOrEqualTo(936));
            var body = (RectTransform)centered.Find("Body");
            AssertInside(centered, body);
            AssertTree((RectTransform)centered.Find("BottomActions"));
            var scroll = body.GetComponent<ScrollRect>();
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Clamped));
            foreach (RectTransform child in scroll.content) AssertTree(child);
            Assert.That(scroll.vertical, Is.EqualTo(body.rect.height < scroll.content.rect.height));
            VerifyScrollEnds(scroll);
            Capture(width, height);
        }

        [TestCase(720,1280)] [TestCase(1080,1920)] [TestCase(1080,2340)]
        [TestCase(1080,2400)] [TestCase(1440,2560)] [TestCase(1536,2048)] [TestCase(1600,2560)]
        public void ProfileList_PortraitSafeArea_ContainsHeaderBodyActionsAndEmptyState(int width, int height)
        {
            Open("ProfileListScreen", width, height, true);
            AssertInside(screen, safe); AssertInside(safe, centered);
            Assert.That(centered.rect.width, Is.LessThanOrEqualTo(1080));
            AssertTree((RectTransform)centered.Find("TopBar"));
            AssertTree((RectTransform)centered.Find("BottomActions"));
            var body = (RectTransform)centered.Find("Body");
            AssertInside(centered, body);
            var list = (RectTransform)body.Find("Scroll View");
            var empty = (RectTransform)body.Find("EmptyContent");
            AssertInside(body, list); AssertInside(body, empty);
            Assert.That(list.rect.height, Is.EqualTo(body.rect.height).Within(.01f));
            Assert.That(empty.rect.height, Is.EqualTo(body.rect.height).Within(.01f));
            AssertInside(list, (RectTransform)list.Find("Viewport"));
            var scroll = empty.GetComponent<ScrollRect>();
            foreach (RectTransform child in scroll.content) AssertTree(child);
            VerifyScrollEnds(scroll);
            var actions = (RectTransform)centered.Find("BottomActions");
            Assert.That(actions.rect.height, Is.EqualTo(320));
            Capture(width, height);
        }

        [Test]
        public void ReferencePortrait_PreservesWelcomeArtworkAndButtons()
        {
            Open("WelcomeScreen", 1080, 1920, false);
            var content = centered.Find("Body/Content");
            AssertPosition(content.Find("AppIcon"), 0, 795);
            AssertPosition(content.Find("AppNameText"), 0, 695);
            AssertPosition(content.Find("HeroArea/HeroIllustration"), 14.3927f, 215.6432f);
            AssertPosition(content.Find("DescriptionText"), 0, -165);
            AssertPosition(centered.Find("BottomActions/StartButton"), 0, -590);
            AssertPosition(centered.Find("BottomActions/SkipButton"), 0, -697.5f);
            Assert.That(((RectTransform)content.Find("HeroArea/HeroIllustration")).rect.size,
                Is.EqualTo(new Vector2(651.2146f, 631.2864f)));
        }

        [Test]
        public void ReferencePortrait_PreservesProfileListArtworkAndActionPositions()
        {
            Open("ProfileListScreen", 1080, 1920, false);
            AssertPosition(centered.Find("TopBar/CreateProfileButton"), 456, 860);
            AssertPosition(centered.Find("TopBar/LogoIcon"), -330, 860);
            var content = centered.Find("Body/EmptyContent/Content");
            AssertPosition(content.Find("HeroIllustration"), 0, 460);
            AssertPosition(content.Find("EmptyState"), 0, -55);
            AssertPosition(content.Find("DescriptionText"), 0, -185);
            AssertPosition(content.Find("FeatureRow"), 0, -325);
            AssertPosition(content.Find("PrimaryAddButton"), 0, -516);
            AssertPosition(centered.Find("BottomActions/CameraSearchButton"), 0, -716);
        }

        [Test]
        public void ResizingActiveScreens_UpdatesScrollHeightAndSafeInsets()
        {
            foreach (string name in new[] { "WelcomeScreen", "ProfileListScreen" })
            {
                Open(name, 1080, 1920, false);
                float first = ((RectTransform)centered.Find("Body")).rect.height;
                safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,1080,1920), new Rect(0,150,1080,1500));
                Assert.That(((RectTransform)centered.Find("Body")).rect.height, Is.LessThan(first));
                AssertTree((RectTransform)centered.Find("BottomActions"));
                safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,1080,1920), new Rect(0,0,1080,1920));
                Assert.That(((RectTransform)centered.Find("Body")).rect.height, Is.EqualTo(first).Within(.01f));
            }
        }

        [Test]
        public void WelcomeStartAndProfileCreate_PreserveNavigationEvents()
        {
            Open("WelcomeScreen", 1080, 1920, false);
            var start = centered.Find("BottomActions/StartButton").GetComponent<Button>();
            Assert.That(start.onClick.GetPersistentEventCount(), Is.EqualTo(2));
            var list = Object.FindAnyObjectByType<ProfileListScreen>(FindObjectsInactive.Include);
            Assert.That(start.onClick.GetPersistentTarget(0), Is.SameAs(list.gameObject));
            Assert.That(start.onClick.GetPersistentTarget(1), Is.SameAs(screen.gameObject));
            Assert.That(start.onClick.GetPersistentMethodName(0), Is.EqualTo("SetActive"));
            Assert.That(start.onClick.GetPersistentMethodName(1), Is.EqualTo("SetActive"));
            var serialized = new UnityEditor.SerializedObject(start);
            var calls = serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            Assert.That(calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue, Is.True);
            Assert.That(calls.GetArrayElementAtIndex(1).FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue, Is.False);
            var create = list.transform.Find("SafeAreaRoot/CenteredContent/TopBar/CreateProfileButton").GetComponent<Button>();
            Assert.That(create.onClick.GetPersistentTarget(0), Is.SameAs(list));
            Assert.That(create.onClick.GetPersistentMethodName(0), Is.EqualTo("OpenCreateProfileScreen"));
            foreach (string path in new[] {
                "SafeAreaRoot/CenteredContent/Body/EmptyContent/Content/PrimaryAddButton" })
            {
                var button = list.transform.Find(path).GetComponent<Button>();
                Assert.That(button.onClick.GetPersistentTarget(0), Is.SameAs(list));
                Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo("OpenCreateProfileScreen"));
            }
        }

        [Test]
        public void FilledProfileList_ScrollsWithinBody_AndKeepsActionsAccessible()
        {
            Open("ProfileListScreen", 1080, 1920, true);
            var body = centered.Find("Body");
            var empty = body.Find("EmptyContent");
            var list = body.Find("Scroll View").GetComponent<ScrollRect>();
            empty.gameObject.SetActive(false);
            var rows = new System.Collections.Generic.List<GameObject>();
            try
            {
                for (int i = 0; i < 8; i++)
                {
                    var row = new GameObject("Test profile row", typeof(RectTransform), typeof(LayoutElement));
                    row.transform.SetParent(list.content, false);
                    row.GetComponent<LayoutElement>().preferredHeight = 400;
                    rows.Add(row);
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(list.content);
                Canvas.ForceUpdateCanvases();
                Assert.That(list.content.rect.height, Is.GreaterThan(list.viewport.rect.height));
                list.verticalNormalizedPosition = 1;
                Vector2 before = list.content.anchoredPosition;
                list.OnScroll(new UnityEngine.EventSystems.PointerEventData(null) { scrollDelta = new Vector2(0, -100) });
                Assert.That(list.content.anchoredPosition.y, Is.GreaterThan(before.y));
                list.verticalNormalizedPosition = 0;
                Assert.That(Bounds(list.content).yMin, Is.EqualTo(Bounds(list.viewport).yMin).Within(.2f));
                AssertTree((RectTransform)centered.Find("BottomActions"));
            }
            finally { foreach (var row in rows) Object.DestroyImmediate(row); }
        }

        private void Capture(int width, int height)
        {
            var canvas = screen.GetComponentInParent<Canvas>();
            var canvasRect = (RectTransform)canvas.transform;
            var host = new GameObject("PortraitCaptureCamera", typeof(Camera));
            var camera = host.GetComponent<Camera>();
            var target = RenderTexture.GetTemporary(width, height, 24);
            var previous = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = canvasRect.rect.height / 2;
                camera.aspect = (float)width / height;
                camera.transform.SetPositionAndRotation(canvasRect.TransformPoint(canvasRect.rect.center) - canvas.transform.forward * 100,
                    canvas.transform.rotation);
                camera.cullingMask = 1 << screen.gameObject.layer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,width,height),0,0);
                image.Apply();
                string folder = System.IO.Path.GetFullPath(".utmp/PortraitP2/captures");
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, screen.name + "-" + width + "x" + height + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(host);
            }
        }

        private void AssertPosition(Transform transform, float x, float y)
        {
            var rect = (RectTransform)transform;
            Vector3 position = centered.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            Assert.That(position.x, Is.EqualTo(x).Within(.1f), transform.name);
            Assert.That(position.y, Is.EqualTo(y).Within(.1f), transform.name);
        }

        private static void AssertTree(RectTransform root)
        {
            if (root.parent is RectTransform parent) AssertInside(parent, root);
            foreach (RectTransform child in root) AssertTree(child);
        }

        private static Rect Bounds(RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static void AssertInside(RectTransform parent, RectTransform child)
        {
            Rect p = Bounds(parent), c = Bounds(child);
            Assert.That(c.width, Is.GreaterThan(0), child.name);
            Assert.That(c.height, Is.GreaterThan(0), child.name);
            Assert.That(c.xMin, Is.GreaterThanOrEqualTo(p.xMin - .2f), child.name);
            Assert.That(c.xMax, Is.LessThanOrEqualTo(p.xMax + .2f), child.name);
            Assert.That(c.yMin, Is.GreaterThanOrEqualTo(p.yMin - .2f), child.name);
            Assert.That(c.yMax, Is.LessThanOrEqualTo(p.yMax + .2f), child.name);
        }

        private static void VerifyScrollEnds(ScrollRect scroll)
        {
            // Overflow belongs to the clipped scroll content; verify both ends remain reachable.
            if (!scroll.vertical) { AssertInside(scroll.viewport, scroll.content); return; }
            scroll.verticalNormalizedPosition = 1;
            Assert.That(Bounds(scroll.content).yMax, Is.EqualTo(Bounds(scroll.viewport).yMax).Within(.2f));
            scroll.verticalNormalizedPosition = 0;
            Assert.That(Bounds(scroll.content).yMin, Is.EqualTo(Bounds(scroll.viewport).yMin).Within(.2f));
            scroll.verticalNormalizedPosition = 1;
        }
    }
}
