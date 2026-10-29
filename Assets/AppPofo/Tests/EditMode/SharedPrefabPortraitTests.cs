using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Presentation.Components;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AppPofo.Tests.EditMode
{
    public sealed class SharedPrefabPortraitTests
    {
        private const string PrefabFolder = "Assets/AppPofo/Presentation/Prefabs/";
        private const string LongText = "매우 긴 사용자 이름과 직무 및 프로젝트 제목 Advanced Distributed Systems Engineering";
        private const string LongUrl = "https://example.com/portfolio/projects/a-very-long-path-without-spaces?technology=distributed-systems-and-cloud";
        private GameObject canvas;
        private RectTransform card;

        private static readonly string[] Names = { "ProfileListItem", "ProjectItem", "LinkItem", "SkillChip" };
        private static readonly int[] ParentWidths = { 320, 360, 420, 540, 720, 900 };

        public static IEnumerable<TestCaseData> WidthCases()
        {
            foreach (string name in Names)
                foreach (int width in ParentWidths)
                    foreach (bool longData in new[] { false, true })
                        yield return new TestCaseData(name, width, longData);
        }

        private void Open(string name, int width)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            canvas = new GameObject("PrefabCanvas", typeof(RectTransform), typeof(Canvas));
            canvas.layer = 5;
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            float height = name == "ProfileListItem" ? 300 : name == "SkillChip" ? 84 : 140;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height + 40);
            string path = PrefabFolder + (name == "SkillChip" ? "UI/" : "") + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            card = (RectTransform)Object.Instantiate(prefab, canvas.transform, false).transform;
            card.anchorMin = new Vector2(0, .5f);
            card.anchorMax = new Vector2(1, .5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(0, height);
            Rebuild();
        }

        [TearDown]
        public void Cleanup() { if (canvas != null) Object.DestroyImmediate(canvas); }

        private static ProfileItem Item(ProfileItemType type, string title, int id = 7)
        {
            return new ProfileItem(id, 42, type, title, "", LongUrl, id);
        }

        private static Profile ProfileData(bool longData)
        {
            var time = new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
            return new Profile(42, longData ? LongText : "홍길동", longData ? LongText : "Software Developer",
                longData ? LongText : "간단한 소개 문구", "", "", "", true, time, time,
                new[] { Item(ProfileItemType.Skill, longData ? LongText : "C#", 1),
                    Item(ProfileItemType.Skill, longData ? LongText : "Python", 2),
                    Item(ProfileItemType.Skill, longData ? LongText : "Unity", 3) });
        }

        private void Bind(string name, bool longData)
        {
            switch (name)
            {
                case "ProfileListItem": card.GetComponent<ProfileListItemView>().Bind(ProfileData(longData), _ => { }, _ => { }, null); break;
                case "ProjectItem": card.GetComponent<ProjectItemView>().Bind(Item(ProfileItemType.Project, longData ? LongText : "New Text"), _ => { }); break;
                case "LinkItem": card.GetComponent<LinkItemView>().Bind(Item(ProfileItemType.Link, longData ? LongUrl : "New Text"), _ => { }, _ => { }); break;
                case "SkillChip": card.GetComponent<SkillChipView>().BindRecommended(longData ? LongText : "C#", _ => { }, longData); break;
            }
            Rebuild();
        }

        private void Rebuild()
        {
            for (int i = 0; i < 3; i++) LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            Canvas.ForceUpdateCanvases();
        }

        [TestCase(320)][TestCase(360)][TestCase(540)][TestCase(720)][TestCase(900)]
        public void ProfileAvatar_ResizesAsCenteredCircleWithResolvedFrame(int width)
        {
            Open("ProfileListItem", width);
            var frame = R("AvatarFrame");
            var mask = R("AvatarFrame/AvatarMask");
            var avatar = mask.Find("AvatarImage").GetComponent<Image>();
            Assert.That(frame.GetComponent<Image>().sprite, Is.Not.Null);
            Assert.That(mask.GetComponent<Image>().sprite, Is.Not.Null);
            Assert.That(frame.GetComponent<AspectRatioFitter>(), Is.Not.Null);
            Assert.That(frame.rect.height, Is.EqualTo(frame.rect.width).Within(.01f));
            Assert.That(mask.rect.height, Is.EqualTo(mask.rect.width).Within(.01f));
            Assert.That(mask.rect.width / frame.rect.width, Is.EqualTo(.808f).Within(.001f));
            Assert.That(avatar.rectTransform.rect.size, Is.EqualTo(mask.rect.size));
            Assert.That(Vector2.Distance(Bounds(frame).center, Bounds(avatar.rectTransform).center), Is.LessThan(.01f));
            var texture = new Texture2D(32,32);
            texture.SetPixels(Enumerable.Repeat(Color.green,1024).ToArray()); texture.Apply();
            var sprite = Sprite.Create(texture,new Rect(0,0,32,32),new Vector2(.5f,.5f));
            try
            {
                avatar.sprite = sprite; avatar.color = Color.white; avatar.enabled = true;
                Capture("ProfileAvatar-"+width);
                var image = new Texture2D(2,2);
                try
                {
                    image.LoadImage(File.ReadAllBytes(Path.GetFullPath(".utmp/PortraitP3/captures/ProfileAvatar-"+width+".png")));
                    Rect bounds = Bounds(mask); int height = image.height;
                    Vector2 center = bounds.center + new Vector2(width/2f,height/2f);
                    foreach(Vector2 offset in new[]{Vector2.zero,Vector2.left,Vector2.right,Vector2.up,Vector2.down})
                    {
                        Vector2 point = center + offset * bounds.width * .25f;
                        Color pixel = image.GetPixel(Mathf.RoundToInt(point.x),Mathf.RoundToInt(point.y));
                        Assert.That(pixel.g, Is.GreaterThan(.9f)); Assert.That(pixel.r, Is.LessThan(.1f));
                    }
                }
                finally { Object.DestroyImmediate(image); }
            }
            finally { avatar.sprite=null; Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }

        [TestCaseSource(nameof(WidthCases))]
        public void ParentWidthAndLongData_KeepChildrenAndActionsContained(string name, int width, bool longData)
        {
            Open(name, width); Bind(name, longData);
            Assert.That(card.rect.width, Is.EqualTo(width).Within(.01f));
            Assert.That(card.rect.height, Is.EqualTo(name == "ProfileListItem" ? 300 : name == "SkillChip" ? 84 : 140));
            Verify(name);
            if (width == 320 || width == 900) Capture(name + "-" + width + (longData ? "-long" : "-short"));
        }

        private void Verify(string name)
        {
            foreach (var rect in card.GetComponentsInChildren<RectTransform>(true))
                if (rect != card && rect.parent is RectTransform parent) AssertInside(parent, rect);
            foreach (var text in card.GetComponentsInChildren<Text>(true))
            {
                bool boundedBestFit = name == "SkillChip" && text.name == "TitleText";
                Assert.That(text.resizeTextForBestFit, Is.EqualTo(boundedBestFit), text.name);
                if (boundedBestFit)
                {
                    Assert.That(text.resizeTextMinSize, Is.GreaterThanOrEqualTo(14), text.name);
                    Assert.That(text.resizeTextMaxSize, Is.GreaterThanOrEqualTo(text.resizeTextMinSize), text.name);
                }
                Assert.That(text.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Wrap), text.name);
                Assert.That(text.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate), text.name);
                if (text.gameObject.activeInHierarchy) AssertGlyphsInside(text);
            }
            foreach (var button in card.GetComponentsInChildren<Button>())
            {
                Assert.That(button.interactable, Is.True, button.name);
                Assert.That(button.targetGraphic.raycastTarget, Is.True, button.name);
            }
            switch (name)
            {
                case "ProfileListItem":
                    Separate("MainInfo/DisplayNameText", "MainInfo/RoleChip");
                    Separate("MainInfo", "BusinessCardButton");
                    Separate("BioArea/BioText", "BusinessCardButton");
                    Separate("SkillRow", "ArrowText");
                    foreach (Transform chip in card.Find("SkillRow"))
                        AssertSeparated((RectTransform)chip.Find("SkillIcon"), (RectTransform)chip.Find("SkillText"));
                    break;
                case "ProjectItem": Separate("TitleText", "ArrowImg"); break;
                case "LinkItem": Separate("TitleText", "DeleteButton"); break;
                case "SkillChip":
                    Separate("SkillIcon", "TitleText");
                    if (card.Find("SelectionCheck").gameObject.activeSelf) Separate("TitleText", "SelectionCheck");
                    break;
            }
        }

        [TestCase("ProfileListItem")][TestCase("ProjectItem")][TestCase("LinkItem")][TestCase("SkillChip")]
        public void ActiveParentResize_ReflowsWithoutRebinding(string name)
        {
            Open(name, 900); Bind(name, true);
            foreach (int width in ParentWidths.AsEnumerable().Reverse())
            {
                ((RectTransform)canvas.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                Rebuild(); Verify(name);
            }
        }

        [Test]
        public void SkillOptionalSelection_ReleasesTextSpaceAndPreservesCallbacks()
        {
            Open("SkillChip", 320);
            var view = card.GetComponent<SkillChipView>();
            string clicked = null;
            view.BindRecommended(LongText, value => clicked = value, true); Rebuild();
            float selectedWidth = R("TitleText").rect.width;
            Assert.That(R("SelectionCheck").gameObject.activeSelf, Is.True);
            card.GetComponent<Button>().onClick.Invoke();
            Assert.That(clicked, Is.EqualTo(LongText));
            view.BindRecommended(LongText, value => clicked = value, false); Rebuild();
            Assert.That(R("SelectionCheck").gameObject.activeSelf, Is.False);
            Assert.That(R("TitleText").rect.width, Is.GreaterThan(selectedWidth));
            Verify("SkillChip");
        }

        [TestCase("C#")][TestCase("Java")][TestCase("JavaScript")]
        [TestCase("Machine Learning")][TestCase("Computer Vision")]
        public void WizardSkillCell_At72Height_ContainsTextIconAndSelection(string title)
        {
            Open("SkillChip", 320);
            card.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 72);
            card.GetComponent<SkillChipView>().BindRecommended(title, _ => { }, true);
            Rebuild();
            Verify("SkillChip");
            Assert.That(R("SkillIcon").rect.height, Is.EqualTo(32));
            Assert.That(R("SelectionCheck").rect.height, Is.EqualTo(36));
        }

        [Test]
        public void AllFourViews_PreserveSelectionOpenAndDeleteCallbacks()
        {
            Open("ProfileListItem", 320);
            long selectedProfile = 0, businessCard = 0;
            card.GetComponent<ProfileListItemView>().Bind(ProfileData(true), id => selectedProfile = id, id => businessCard = id, null);
            card.GetComponent<Button>().onClick.Invoke();
            card.Find("BusinessCardButton").GetComponent<Button>().onClick.Invoke();
            Assert.That(selectedProfile, Is.EqualTo(42)); Assert.That(businessCard, Is.EqualTo(42));
            Cleanup();
            Open("ProjectItem", 320);
            var project = Item(ProfileItemType.Project, LongText); ProfileItem selected = null;
            card.GetComponent<ProjectItemView>().Bind(project, value => selected = value);
            card.GetComponent<Button>().onClick.Invoke(); Assert.That(selected, Is.SameAs(project));
            Cleanup();
            Open("LinkItem", 320);
            var link = Item(ProfileItemType.Link, LongUrl); ProfileItem opened = null, deleted = null;
            card.GetComponent<LinkItemView>().Bind(link, value => opened = value, value => deleted = value);
            card.GetComponent<Button>().onClick.Invoke(); card.Find("DeleteButton").GetComponent<Button>().onClick.Invoke();
            Assert.That(opened, Is.SameAs(link)); Assert.That(deleted, Is.SameAs(link));
            Cleanup();
            Open("SkillChip", 320);
            var skill = Item(ProfileItemType.Skill, LongText);
            card.GetComponent<SkillChipView>().Bind(skill, value => selected = value);
            card.GetComponent<Button>().onClick.Invoke(); Assert.That(selected, Is.SameAs(skill));
        }

        [Test]
        public void ReferenceWidths_PreserveCardHeightsAvatarRoleAndActions()
        {
            Open("ProfileListItem", 1000);
            Assert.That(R("AvatarFrame").rect.size, Is.EqualTo(new Vector2(125,125)));
            Assert.That(R("MainInfo/RoleChip").rect.size, Is.EqualTo(new Vector2(330,54)));
            Assert.That(card.InverseTransformPoint(R("MainInfo/RoleChip").position).x, Is.EqualTo(-197).Within(.01f));
            Assert.That(R("BioArea/BioText").rect.width, Is.EqualTo(500).Within(.01f));
            Assert.That(R("SkillRow").rect.width, Is.EqualTo(650).Within(.01f));
            Assert.That(card.InverseTransformPoint(R("BusinessCardButton").TransformPoint(R("BusinessCardButton").rect.center)).x,
                Is.EqualTo(384).Within(.01f));
            foreach (Transform chip in card.Find("SkillRow"))
                Assert.That(((RectTransform)chip).rect.size, Is.EqualTo(new Vector2(190,50)));
            Cleanup(); Open("LinkItem",900);
            Assert.That(R("TitleText").rect.width, Is.EqualTo(680).Within(.01f));
            Assert.That(R("DeleteButton").rect.size, Is.EqualTo(new Vector2(90,90)));
            Cleanup(); Open("ProjectItem",900);
            Assert.That(R("ArrowImg").rect.size, Is.EqualTo(new Vector2(70,70)));
            Cleanup(); Open("SkillChip",360);
            Assert.That(R("SkillIcon").rect.size, Is.EqualTo(new Vector2(32,32)));
        }

        private RectTransform R(string path) => (RectTransform)card.Find(path);

        [TestCase(320)][TestCase(540)][TestCase(900)]
        public void OnboardingSkillFlow_SizesToTextAndWrapsWithinParent(int width)
        {
            Open("SkillChip", width);
            var flow = canvas.AddComponent<AppPofo.Presentation.Common.SkillChipFlowLayout>();
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, 600);
            string[] titles = { "C#", "Java", "JavaScript", "Machine Learning", "Computer Vision", new string('W', 100) };
            var chips = new System.Collections.Generic.List<RectTransform>();
            string clicked = null;
            for (int i = 0; i < titles.Length; i++)
            {
                var chip = i == 0 ? card : (RectTransform)Object.Instantiate(card, canvas.transform, false).transform;
                var view = chip.GetComponent<SkillChipView>();
                view.BindRecommended(titles[i], value => clicked = value, i % 2 == 0);
                view.ConfigureContentSizedLayout();
                chips.Add(chip);
            }
            for (int i = 0; i < 3; i++) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
            Canvas.ForceUpdateCanvases();
            Assert.That(chips[4].rect.width, Is.GreaterThan(chips[0].rect.width));
            foreach (RectTransform chip in chips)
            {
                AssertInside((RectTransform)canvas.transform, chip);
                Assert.That(chip.rect.width, Is.LessThanOrEqualTo(width));
                Assert.That(chip.rect.height, Is.EqualTo(72));
                Text title = chip.Find("TitleText").GetComponent<Text>();
                Assert.That(title.fontSize, Is.EqualTo(28));
                Assert.That(title.resizeTextForBestFit, Is.False);
                AssertInside(chip, title.rectTransform);
            }
            chips[4].GetComponent<Button>().onClick.Invoke();
            Assert.That(clicked, Is.EqualTo("Computer Vision"));
            Assert.That(LayoutUtility.GetPreferredHeight((RectTransform)canvas.transform), Is.GreaterThanOrEqualTo(72));
            // Check actual rendered background coverage, not only RectTransform bounds.
            string captureName = "SkillFlow-" + width;
            Capture(captureName);
            var rendered = new Texture2D(2, 2);
            try
            {
                rendered.LoadImage(File.ReadAllBytes(Path.GetFullPath(".utmp/PortraitP3/captures/" + captureName + ".png")));
                Rect bounds = Bounds(chips[0]);
                foreach (float offset in new[] { -24f, 24f })
                {
                    int x = Mathf.RoundToInt(bounds.center.x + width / 2f);
                    int y = Mathf.RoundToInt(bounds.center.y + offset + 300);
                    Assert.That(rendered.GetPixel(x, y).r, Is.LessThan(.97f),
                        "The visible chip background must cover the area above and below its label.");
                }
            }
            finally { Object.DestroyImmediate(rendered); }
        }
        private void Separate(string a, string b) => AssertSeparated(R(a), R(b));

        private static Rect Bounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static void AssertInside(RectTransform parent, RectTransform child)
        {
            Rect p = Bounds(parent), c = Bounds(child);
            Assert.That(c.width, Is.GreaterThan(0), child.name); Assert.That(c.height, Is.GreaterThan(0), child.name);
            Assert.That(c.xMin, Is.GreaterThanOrEqualTo(p.xMin - .1f), child.name);
            Assert.That(c.xMax, Is.LessThanOrEqualTo(p.xMax + .1f), child.name);
            Assert.That(c.yMin, Is.GreaterThanOrEqualTo(p.yMin - .1f), child.name);
            Assert.That(c.yMax, Is.LessThanOrEqualTo(p.yMax + .1f), child.name);
        }

        private static void AssertSeparated(RectTransform a, RectTransform b)
        {
            Rect first = ContentBounds(a), second = ContentBounds(b);
            float overlapX = Mathf.Min(first.xMax, second.xMax) - Mathf.Max(first.xMin, second.xMin);
            float overlapY = Mathf.Min(first.yMax, second.yMax) - Mathf.Max(first.yMin, second.yMin);
            Assert.That(overlapX <= .1f || overlapY <= .1f, Is.True, a.name + " overlaps " + b.name);
        }

        private static Rect ContentBounds(RectTransform rect)
        {
            Rect bounds = Bounds(rect);
            foreach (var text in rect.GetComponentsInChildren<Text>())
            {
                text.cachedTextGenerator.Populate(text.text, text.GetGenerationSettings(text.rectTransform.rect.size));
                var vertices = text.cachedTextGenerator.verts;
                for (int i = 0; i < vertices.Count - 4; i++)
                {
                    Vector3 point = text.rectTransform.TransformPoint(vertices[i].position / text.pixelsPerUnit);
                    bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, point.x), Mathf.Min(bounds.yMin, point.y),
                        Mathf.Max(bounds.xMax, point.x), Mathf.Max(bounds.yMax, point.y));
                }
            }
            return bounds;
        }

        private static void AssertGlyphsInside(Text text)
        {
            var rect = text.rectTransform;
            text.cachedTextGenerator.Populate(text.text, text.GetGenerationSettings(rect.rect.size));
            var vertices = text.cachedTextGenerator.verts;
            // Font bearings can extend past advance widths; check the containing UI region.
            Rect bounds = Bounds((RectTransform)rect.parent);
            for (int i = 0; i < vertices.Count - 4; i++)
            {
                Vector3 point = rect.TransformPoint(vertices[i].position / text.pixelsPerUnit);
                Assert.That(point.x, Is.InRange(bounds.xMin - 2, bounds.xMax + 2), text.name);
                Assert.That(point.y, Is.InRange(bounds.yMin - 2, bounds.yMax + 2), text.name);
            }
        }

        private void Capture(string name)
        {
            int width = Mathf.RoundToInt(((RectTransform)canvas.transform).rect.width);
            int height = Mathf.RoundToInt(((RectTransform)canvas.transform).rect.height);
            var host = new GameObject("PrefabCaptureCamera", typeof(Camera));
            var camera = host.GetComponent<Camera>();
            var target = RenderTexture.GetTemporary(width, height, 24);
            var previous = RenderTexture.active;
            var image = new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = height / 2f;
                camera.aspect = (float)width / height; camera.transform.position = canvas.transform.position - Vector3.forward * 100;
                camera.cullingMask = (1 << 5) | 1; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white;
                camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target; image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                string folder = Path.GetFullPath(".utmp/PortraitP3/captures"); Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image); Object.DestroyImmediate(host); }
        }
    }
}
