using System;
using System.IO;
using System.Linq;
using AppPofo.Presentation.Common;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AppPofo.Tests.EditMode
{
    public sealed class BusinessCardPortraitTests
    {
        private RectTransform screen, safe, editor, dialog;
        private BusinessCardPortraitLayout layout;

        [Test]
        public void RecognitionCards_UnchangedMembershipReusesLayout_ResizeInvalidatesIt()
        {
            var fixture = new GameObject("RecognitionLayoutFixture", typeof(RectTransform));
            fixture.SetActive(false); // Never start a camera or touch the database.
            var root = (RectTransform)fixture.transform;
            root.sizeDelta = new Vector2(540, 700);
            var camera = fixture.AddComponent<AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera>();
            var type = camera.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            type.GetField("faceOverlayRoot", flags).SetValue(camera, root);
            var cards = (System.Collections.Generic.Dictionary<long, AppPofo.Presentation.Components.BusinessCardView>)type.GetField("businessCardsByProfile", flags).GetValue(camera);
            var overlays = (System.Collections.Generic.Dictionary<long, RectTransform>)type.GetField("faceOverlaysByProfile", flags).GetValue(camera);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var faceObject = new GameObject("Face", typeof(RectTransform));
                    faceObject.transform.SetParent(root, false);
                    var face = (RectTransform)faceObject.transform;
                    face.sizeDelta = new Vector2(60, 80); face.anchoredPosition = new Vector2(i * 20, 150);
                    overlays.Add(i + 1, face);
                    var cardObject = new GameObject("Card", typeof(RectTransform), typeof(AppPofo.Presentation.Components.BusinessCardView));
                    cardObject.transform.SetParent(root, false);
                    cards.Add(i + 1, cardObject.GetComponent<AppPofo.Presentation.Components.BusinessCardView>());
                }
                var update = type.GetMethod("UpdateBusinessCardPositions", flags);
                update.Invoke(camera, null);
                var firstLayout = type.GetField("cardLayoutBounds", flags).GetValue(camera);
                var positions = cards.Values.Select(c => ((RectTransform)c.transform).anchoredPosition).ToArray();
                overlays[1].anchoredPosition += new Vector2(60, -20);
                for (int i = 0; i < 30; i++) update.Invoke(camera, null);
                Assert.That(type.GetField("cardLayoutBounds", flags).GetValue(camera), Is.SameAs(firstLayout), "Rendered frames must not rerun the placement search.");
                Assert.That(cards.Values.Select(c => ((RectTransform)c.transform).anchoredPosition), Is.EqualTo(positions), "Face jitter must not reshuffle multiple cards.");
                root.sizeDelta = new Vector2(320, 500);
                update.Invoke(camera, null);
                Assert.That(type.GetField("cardLayoutBounds", flags).GetValue(camera), Is.Not.SameAs(firstLayout));
                var stop = type.GetMethod("StopRecognitionProcessing", flags);
                type.GetField("recognitionProcessing", flags).SetValue(camera, true);
                stop.Invoke(camera, null); stop.Invoke(camera, null);
                Assert.That(type.GetField("recognitionProcessing", flags).GetValue(camera), Is.False);
            }
            finally
            {
                cards.Clear(); overlays.Clear();
                Object.DestroyImmediate(fixture);
            }
        }

        [TestCase(320, 500, 3)]
        [TestCase(540, 700, 4)]
        [TestCase(1080, 1200, 8)]
        [TestCase(320, 160, 12)]
        public void RecognitionCards_ClusteredFacesStaySeparateAndInsidePreview(int width, int height, int count)
        {
            var area = new Rect(-width * .5f, -height * .5f, width, height);
            var faces = new System.Collections.Generic.List<Rect>();
            for (int i = 0; i < count; i++) faces.Add(new Rect(-30 + i * 2, height * .1f, 60, 80));
            var cards = RecognitionBounds(area, faces);
            Assert.That(cards.Count, Is.EqualTo(count));
            for (int i = 0; i < cards.Count; i++)
            {
                Assert.That(cards[i].width, Is.GreaterThan(0));
                Assert.That(cards[i].width / cards[i].height, Is.EqualTo(2.5f).Within(.001f));
                Assert.That(cards[i].xMin, Is.GreaterThanOrEqualTo(area.xMin + 24f - .01f));
                Assert.That(cards[i].xMax, Is.LessThanOrEqualTo(area.xMax - 24f + .01f));
                Assert.That(cards[i].yMin, Is.GreaterThanOrEqualTo(area.yMin + 24f - .01f));
                Assert.That(cards[i].yMax, Is.LessThanOrEqualTo(area.yMax - 24f + .01f));
                for (int j = 0; j < i; j++) Assert.That(cards[i].Overlaps(cards[j]), Is.False, $"Cards {i}/{j}");
            }
            Assert.That(RecognitionBounds(area, faces), Is.EqualTo(cards), "An unchanged frame must keep its placement.");
            faces.RemoveAt(0);
            Assert.That(RecognitionBounds(area, faces).Count, Is.EqualTo(count - 1));
        }

        [Test]
        public void RecognitionCards_SingleFaceKeepsBelowPlacement_EmptyFrameHasNoCards()
        {
            var area = new Rect(-500, -600, 1000, 1200);
            var faces = new System.Collections.Generic.List<Rect> { new Rect(-60, 100, 120, 140) };
            var cards = RecognitionBounds(area, faces);
            Assert.That(cards.Single().center.x, Is.EqualTo(0f).Within(.01f));
            Assert.That(cards.Single().yMax, Is.EqualTo(88f).Within(.01f));
            faces.Clear();
            Assert.That(RecognitionBounds(area, faces), Is.Empty);
        }

        private static System.Collections.Generic.List<Rect> RecognitionBounds(Rect area, System.Collections.Generic.List<Rect> faces) =>
            (System.Collections.Generic.List<Rect>)typeof(AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera)
                .GetMethod("CalculateBusinessCardBounds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { area, faces });

        private void Open(int width, int height, bool insets)
        {
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            screen = Resources.FindObjectsOfTypeAll<RectTransform>().Single(r => r.name == "BusinessCardPanel" && r.gameObject.scene.IsValid());
            var canvas = screen.GetComponentInParent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled = false; canvas.renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)canvas.transform; root.localScale = Vector3.one;
            float scale = Mathf.Sqrt(width / 1080f * height / 1920f);
            root.sizeDelta = new Vector2(width / scale, height / scale);
            foreach (Transform sibling in screen.parent) sibling.gameObject.SetActive(sibling == screen);
            safe = (RectTransform)screen.Find("SafeAreaRoot");
            safe.GetComponent<SafeAreaRoot>().enabled = false;
            safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,width,height), insets ? new Rect(48,36,width-96,height-108) : new Rect(0,0,width,height));
            layout = safe.GetComponent<BusinessCardPortraitLayout>();
            safe.GetComponent<ResponsiveUIContext>().Refresh(); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
            editor = (RectTransform)safe.Find("BusinessCardEditorPanel");
            dialog = (RectTransform)safe.Find("AvatarSourcePanel/Dialog");
        }

        [TestCase(720,1280,false)][TestCase(720,1280,true)]
        [TestCase(1080,1920,false)][TestCase(1080,1920,true)]
        [TestCase(1080,2340,false)][TestCase(1080,2340,true)]
        [TestCase(1080,2400,false)][TestCase(1080,2400,true)]
        [TestCase(1440,2560,false)][TestCase(1440,2560,true)]
        [TestCase(1536,2048,false)][TestCase(1536,2048,true)]
        [TestCase(1600,2560,false)][TestCase(1600,2560,true)]
        public void PortraitSizes_ContainEditorCardThemesAndDialog(int width, int height, bool insets)
        {
            Open(width,height,insets); VerifyEditor();
            dialog.parent.gameObject.SetActive(true); layout.ApplyLayout(); Canvas.ForceUpdateCanvases(); VerifyDialog();
            if (!insets && width == 1080 && height == 1920)
            {
                dialog.parent.gameObject.SetActive(false); Capture("after-editor",width,height);
                dialog.parent.gameObject.SetActive(true); Capture("after-dialog",width,height);
            }
            if (insets) Capture("dialog-"+width+"x"+height,width,height);
        }

        private void VerifyEditor()
        {
            Inside(screen,safe); Inside(safe,editor); Assert.That(editor.rect.width,Is.LessThanOrEqualTo(960));
            Assert.That(editor.localScale,Is.EqualTo(Vector3.one)); Tree((RectTransform)editor.Find("Header"));
            var body=editor.Find("Body").GetComponent<ScrollRect>(); Inside(editor,body.viewport);
            foreach(RectTransform child in body.content) Inside(body.content,child);
            var slot=(RectTransform)body.content.Find("CardPreview");
            var card=(RectTransform)slot.Find("BusinessCardView");
            Inside(slot,card); Rect bounds=Bounds(card); Assert.That(bounds.width/bounds.height,Is.EqualTo(2.5f).Within(.001f));
            // The original frame extends beyond the AvatarButton hit box, and SkillText below the card background.
            // Preserve that authored artwork; the enclosing preview slot contains every art rectangle.
            foreach(var art in card.GetComponentsInChildren<RectTransform>(true)) Inside(slot,art);
            Tree((RectTransform)body.content.Find("PhotoActions"));
            var themes=(RectTransform)body.content.Find("ThemeSection");
            Inside(themes,(RectTransform)themes.Find("ThemeTitleText")); Inside(themes,(RectTransform)themes.Find("ThemeIcon"));
            var selector=(RectTransform)themes.Find("ThemeSelector"); Inside(themes,selector);
            var scroll=selector.Find("Viewport").GetComponent<ScrollRect>(); Inside(selector,scroll.viewport);
            Assert.That(scroll.horizontal,Is.EqualTo(scroll.viewport.rect.width < 900-.1f));
            Assert.That(scroll.vertical,Is.False); TreeChildren(scroll.content);
            Assert.That(scroll.content.childCount,Is.EqualTo(3));
            foreach(RectTransform theme in scroll.content) Assert.That(theme.rect.size,Is.EqualTo(new Vector2(260,150)));
            VerifyScroll(body);
        }
        private void VerifyDialog()
        {
            Inside(safe,dialog); Assert.That(dialog.rect.width,Is.LessThanOrEqualTo(700)); Assert.That(dialog.localScale,Is.EqualTo(Vector3.one));
            Tree((RectTransform)dialog.Find("Header")); Tree((RectTransform)dialog.Find("Footer"));
            var scroll=dialog.Find("Body").GetComponent<ScrollRect>(); Inside(dialog,scroll.viewport); TreeChildren(scroll.content); VerifyScroll(scroll);
            var frame=(RectTransform)scroll.content.Find("PreviewFrame");
            Assert.That(frame.rect.size,Is.EqualTo(new Vector2(320,320)));
            Assert.That(((RectTransform)frame.Find("AvatarPreviewMask")).rect.size,Is.EqualTo(new Vector2(280,280)));
            Assert.That(((RectTransform)frame.Find("CameraPreviewMask")).rect.size,Is.EqualTo(new Vector2(320,320)));
        }
        [Test] public void NarrowPortrait_HeaderStaysInsidePreservedPanelArtwork()
        {
            foreach (int height in new[] { 2340, 2400 })
            {
                Open(1080, height, true);
                var image = editor.GetComponent<Image>();
                Rect panelBounds = Bounds(editor);
                float artworkHeight = Mathf.Min(panelBounds.height,
                    panelBounds.width * image.sprite.rect.height / image.sprite.rect.width);
                Rect headerBounds = Bounds((RectTransform)editor.Find("Header"));
                Assert.That(headerBounds.yMax, Is.LessThanOrEqualTo(
                    panelBounds.center.y + artworkHeight / 2 + .2f), "Header must remain over the panel artwork");
            }
        }

        [Test] public void ShortSafeArea_UsesBodyScrollAndKeepsDialogFooterAccessible()
        {
            Open(1080,1920,false);
            safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,1080,1920),new Rect(0,700,1080,520));
            layout.ApplyLayout(); dialog.parent.gameObject.SetActive(true); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
            VerifyEditor(); VerifyDialog();
            var body=editor.Find("Body").GetComponent<ScrollRect>(); Assert.That(body.vertical,Is.True);
            body.verticalNormalizedPosition=0; Inside(body.viewport,(RectTransform)body.content.Find("ThemeSection"));
            Assert.That(dialog.Find("Body").GetComponent<ScrollRect>().vertical,Is.True);
            foreach(var button in dialog.Find("Footer").GetComponentsInChildren<Button>(true)) Assert.That(button.interactable,Is.True);
        }
        [Test] public void LiveInsets_ReflowWithoutChangingCardMaskGeometryOrFonts()
        {
            Open(1080,1920,false);
            var card=editor.Find("Body/Content/CardPreview/BusinessCardView");
            var original=card.GetComponentsInChildren<RectTransform>(true).Select(r=>r.rect.size).ToArray();
            var fontSizes=screen.GetComponentsInChildren<Text>(true).Select(t=>t.fontSize).ToArray();
            safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,1080,1920),new Rect(90,60,900,1770));
            Assert.That(editor.rect.width,Is.EqualTo(852).Within(.1f)); VerifyEditor();
            Assert.That(card.GetComponentsInChildren<RectTransform>(true).Select(r=>r.rect.size),Is.EqualTo(original));
            Assert.That(screen.GetComponentsInChildren<Text>(true).Select(t=>t.fontSize),Is.EqualTo(fontSizes));
            safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,1080,1920),new Rect(0,0,1080,1920));
            Assert.That(editor.rect.size,Is.EqualTo(new Vector2(960,1200))); VerifyEditor();
        }
        [Test] public void ReferencePortrait_PreservesAuthoredArtworkAndControlPositions()
        {
            Open(1080,1920,false);
            Position(editor.Find("Header/TitleText"),-174.6941f,484);
            Position(editor.Find("Header/CloseButton"),374,484);
            Position(editor.Find("Body/Content/CardPreview/BusinessCardView"),0,180);
            Position(editor.Find("Body/Content/PhotoActions"),0,-157);
            Position(editor.Find("Body/Content/ThemeSection"),0,-350);
            Assert.That(((RectTransform)dialog).rect.size,Is.EqualTo(new Vector2(700,760)));
            var title=(RectTransform)dialog.Find("Header/TitleText"); Assert.That(dialog.InverseTransformPoint(title.position).y,Is.EqualTo(325).Within(.01));
        }
        [Test] public void ThemesRemainReachableAtBothHorizontalScrollEnds()
        {
            Open(1080,1920,true);
            var scroll=editor.Find("Body/Content/ThemeSection/ThemeSelector/Viewport").GetComponent<ScrollRect>();
            Assert.That(scroll.horizontal,Is.True);
            scroll.horizontalNormalizedPosition=0; Inside(scroll.viewport,(RectTransform)scroll.content.GetChild(0));
            scroll.horizontalNormalizedPosition=1; Inside(scroll.viewport,(RectTransform)scroll.content.GetChild(2));
            foreach(var button in screen.GetComponentsInChildren<Button>(true).Where(b => b.name != "AvatarButton"))
            {
                Assert.That(button.interactable,Is.True);
                Assert.That(button.onClick.GetPersistentEventCount(),Is.GreaterThan(0),button.name);
                Assert.That(button.onClick.GetPersistentTarget(0),Is.Not.Null,button.name);
            }
        }

        [TestCase(1080,1920)] [TestCase(720,1280)] [TestCase(1600,2560)]
        public void AssignedAvatars_RenderCenteredInsideBothMasks_AcrossThemes(int width,int height)
        {
            Open(width,height,true);
            var controller=screen.GetComponent<AppPofo.Presentation.Screens.BusinessCardPanelController>();
            var card=editor.Find("Body/Content/CardPreview/BusinessCardView");
            var avatar=card.Find("AvatarButton/AvatarMask/AvatarImage").GetComponent<Image>();
            var preview=dialog.Find("Body/Content/PreviewFrame/AvatarPreviewMask/PreviewImage").GetComponent<Image>();
            var texture=new Texture2D(32,32,TextureFormat.RGBA32,false);
            texture.SetPixels(Enumerable.Repeat(Color.green,1024).ToArray()); texture.Apply();
            var sprite=Sprite.Create(texture,new Rect(0,0,32,32),new Vector2(.5f,.5f));
            try
            {
                avatar.sprite=sprite; avatar.color=Color.white; avatar.enabled=true;
                preview.sprite=sprite; preview.color=Color.white; preview.enabled=true; preview.gameObject.SetActive(true);
                card.Find("AvatarButton/EmptyAvatarText").gameObject.SetActive(false);
                dialog.Find("Body/Content/PreviewFrame/CameraPreviewMask/CameraPreview").gameObject.SetActive(false);
                foreach(Action theme in new Action[]{controller.ApplyAuroraTheme,controller.ApplyBlushTheme,controller.ApplyNeutralTheme})
                {
                    theme();
                    var frame=card.Find("AvatarButton/AvatarFrameImage").GetComponent<Image>();
                    Assert.That(frame.sprite,Is.Not.Null,"Theme frame must resolve to a real sprite, not an opaque white quad.");
                    Assert.That(avatar.sprite,Is.SameAs(sprite));
                    Assert.That(Vector2.Distance(Bounds(frame.rectTransform).center,Bounds((RectTransform)avatar.transform.parent).center),Is.LessThan(.01f));
                    Assert.That(Bounds(frame.rectTransform).xMax,Is.LessThanOrEqualTo(Bounds((RectTransform)card.Find("DisplayNameText")).xMin+.2f));
                    dialog.parent.gameObject.SetActive(false);
                    VerifyAvatarPixels(avatar,"avatar-card-"+width+"x"+height,width,height);
                    dialog.parent.gameObject.SetActive(true);
                    VerifyAvatarPixels(preview,"avatar-preview-"+width+"x"+height,width,height);
                    Assert.That(Vector2.Distance(Bounds((RectTransform)preview.transform.parent).center,
                        Bounds((RectTransform)preview.transform.parent.parent).center),Is.LessThan(.01f));
                }
            }
            finally
            {
                avatar.sprite=null; preview.sprite=null;
                Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);
            }
        }

        private void VerifyAvatarPixels(Image avatar,string name,int width,int height)
        {
            Canvas.ForceUpdateCanvases();
            Assert.That(avatar.isActiveAndEnabled,Is.True); Assert.That(avatar.color.a,Is.EqualTo(1));
            var mask=(RectTransform)avatar.transform.parent; Inside(mask,avatar.rectTransform);
            Assert.That(avatar.rectTransform.rect.size,Is.EqualTo(mask.rect.size));
            Capture(name,width,height);
            var image=new Texture2D(2,2);
            try
            {
                image.LoadImage(File.ReadAllBytes(Path.GetFullPath(".utmp/PortraitP4/captures/"+name+".png")));
                var root=(RectTransform)screen.GetComponentInParent<Canvas>().transform;
                Rect r=Bounds(mask); Vector3 origin=root.TransformPoint(root.rect.min);
                float sx=width/root.rect.width,sy=height/root.rect.height;
                int left=Mathf.FloorToInt((r.xMin-origin.x)*sx), right=Mathf.CeilToInt((r.xMax-origin.x)*sx);
                int bottom=Mathf.FloorToInt((r.yMin-origin.y)*sy),top=Mathf.CeilToInt((r.yMax-origin.y)*sy);
                int minX=width,maxX=-1,minY=height,maxY=-1;
                for(int y=bottom-2;y<=top+2;y++) for(int x=left-2;x<=right+2;x++)
                {
                    Color p=image.GetPixel(x,y);
                    if(p.g>.95f&&p.r<.05f&&p.b<.05f){minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);}
                }
                Assert.That(maxX-minX,Is.GreaterThan((right-left)*.75f),avatar.name+" must visibly fill its mask");
                Assert.That(maxY-minY,Is.GreaterThan((top-bottom)*.75f),avatar.name+" must visibly fill its mask");
                Assert.That((minX+maxX)/2f,Is.EqualTo((left+right)/2f).Within(2),avatar.name+" center X");
                Assert.That((minY+maxY)/2f,Is.EqualTo((bottom+top)/2f).Within(2),avatar.name+" center Y");
                Assert.That(minX,Is.GreaterThanOrEqualTo(left));Assert.That(maxX,Is.LessThanOrEqualTo(right));
                Assert.That(minY,Is.GreaterThanOrEqualTo(bottom));Assert.That(maxY,Is.LessThanOrEqualTo(top));
            }
            finally{Object.DestroyImmediate(image);}
        }
        private void Position(Transform t,float x,float y)
        {
            var rect=(RectTransform)t;Vector3 p=editor.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            Assert.That(p.x,Is.EqualTo(x).Within(.1),t.name);Assert.That(p.y,Is.EqualTo(y).Within(.1),t.name);
        }
        private static void Tree(RectTransform rect){Inside((RectTransform)rect.parent,rect);TreeChildren(rect);}
        private static void TreeChildren(RectTransform parent){foreach(RectTransform child in parent)Tree(child);}
        private static Rect Bounds(RectTransform rect){var c=new Vector3[4];rect.GetWorldCorners(c);return Rect.MinMaxRect(c[0].x,c[0].y,c[2].x,c[2].y);}
        private static void Inside(RectTransform parent,RectTransform child)
        {
            Rect p=Bounds(parent),c=Bounds(child);Assert.That(c.width,Is.GreaterThan(0),child.name);Assert.That(c.height,Is.GreaterThan(0),child.name);
            Assert.That(c.xMin,Is.GreaterThanOrEqualTo(p.xMin-.2f),child.name);Assert.That(c.xMax,Is.LessThanOrEqualTo(p.xMax+.2f),child.name);
            Assert.That(c.yMin,Is.GreaterThanOrEqualTo(p.yMin-.2f),child.name);Assert.That(c.yMax,Is.LessThanOrEqualTo(p.yMax+.2f),child.name);
        }
        private static void VerifyScroll(ScrollRect scroll)
        {
            Assert.That(scroll.movementType,Is.EqualTo(ScrollRect.MovementType.Clamped));Assert.That(scroll.horizontal,Is.False);
            Assert.That(scroll.vertical,Is.EqualTo(scroll.content.rect.height>scroll.viewport.rect.height+.1f));
            if(!scroll.vertical){Inside(scroll.viewport,scroll.content);return;}
            scroll.verticalNormalizedPosition=1;Assert.That(Bounds(scroll.content).yMax,Is.EqualTo(Bounds(scroll.viewport).yMax).Within(.2));
            scroll.verticalNormalizedPosition=0;Assert.That(Bounds(scroll.content).yMin,Is.EqualTo(Bounds(scroll.viewport).yMin).Within(.2));scroll.verticalNormalizedPosition=1;
        }
        private void Capture(string name,int width,int height)
        {
            var canvas=screen.GetComponentInParent<Canvas>();var root=(RectTransform)canvas.transform;
            var host=new GameObject("BusinessCapture",typeof(Camera));var camera=host.GetComponent<Camera>();
            var target=RenderTexture.GetTemporary(width,height,24);var previous=RenderTexture.active;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.enabled=false;camera.orthographic=true;camera.orthographicSize=root.rect.height/2;camera.aspect=(float)width/height;
                camera.transform.SetPositionAndRotation(root.TransformPoint(root.rect.center)-canvas.transform.forward*100,canvas.transform.rotation);
                camera.cullingMask=1<<5;camera.backgroundColor=Color.black;camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                string path=Path.GetFullPath(".utmp/PortraitP4/captures");Directory.CreateDirectory(path);File.WriteAllBytes(Path.Combine(path,name+".png"),image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);Object.DestroyImmediate(host);}
        }
    }
}
