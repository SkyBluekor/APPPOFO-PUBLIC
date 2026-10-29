using System.Linq;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.FaceRegistration;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Tests.EditMode
{
    public sealed class FaceRegistrationResponsiveTests
    {
        [TestCase(720,1280)]
        [TestCase(1080,1920)]
        [TestCase(1080,2340)]
        [TestCase(1080,2400)]
        [TestCase(1440,2560)]
        [TestCase(1536,2048)]
        [TestCase(1600,2560)]
        public void AvailableCanvas_ContainsContent_WithSafeInsets(int width,int height)
        {
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            var screen=Object.FindFirstObjectByType<FaceRegistrationScreen>(FindObjectsInactive.Include);
            var canvas=screen.GetComponentInParent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled=false;
            canvas.renderMode=RenderMode.WorldSpace;
            var canvasRect=(RectTransform)canvas.transform;
            canvasRect.localScale=Vector3.one;
            float scale=Mathf.Sqrt(width/1080f * height/1920f);
            var safe=screen.GetComponentInChildren<SafeAreaRoot>(true);
            safe.enabled=false;
            canvasRect.sizeDelta=new Vector2(width/scale,height/scale);
            screen.gameObject.SetActive(true);
            safe.ApplyViewport(new Rect(0,0,width,height),new Rect(48,36,width-96,height-108));
            var context=safe.GetComponent<ResponsiveUIContext>();
            context.Refresh();
            screen.GetComponent<FaceRegistrationResponsiveLayout>().ApplyLayout();
            var panel=(RectTransform)safe.transform.Find("MainPanel");
            for(int i=0;i<3;i++) LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.Find("Body/CameraSection/CameraContainer"));
            var body=(RectTransform)panel.Find("Body");
            var camera=(RectTransform)body.Find("CameraSection");
            var guide=(RectTransform)body.Find("GuideSection");
            Assert.That(body.GetComponent<AdaptiveLinearLayoutGroup>().Vertical,Is.True);
            Assert.That(panel.rect.width,Is.LessThanOrEqualTo(1020));
            AssertInside((RectTransform)safe.transform,panel);
            Assert.That(panel.GetComponent<Image>().preserveAspect,Is.True);
            AssertInside(panel,(RectTransform)panel.Find("Header"));
            AssertInside(panel,body);
            AssertInside(body,camera); AssertInside(body,guide);
            AssertInside((RectTransform)safe.transform,(RectTransform)safe.transform.Find("FooterInfo"));
            AssertSeparated(camera,guide);
            RectTransform[] sections=guide.Cast<Transform>().Select(x=>(RectTransform)x).ToArray();
            foreach(var r in sections) AssertInside(guide,r);
            for(int i=1;i<sections.Length;i++) AssertSeparated(sections[i-1],sections[i]);
            var preview=(RectTransform)camera.Find("CameraContainer/PreviewViewport/DisplayFrame/CameraPreview");
            Assert.That(preview.rect.width/preview.rect.height,Is.EqualTo(844f/459f).Within(.002f));
            Assert.That(preview.rect.height,Is.GreaterThan(60));
            AssertInside(camera,preview);
            AssertInside(camera,(RectTransform)camera.Find("CameraContainer/InstructionChip"));
            Assert.That(context.AvailableSize.x,Is.EqualTo((width-96)/scale).Within(.1f));
            Assert.That(context.AvailableSize.y,Is.EqualTo((height-108)/scale).Within(.1f));
            screen.gameObject.SetActive(false);
        }
        [Test]
        public void AndroidPlayerSettings_UsePortraitOnly()
        {
            Assert.That(UnityEditor.PlayerSettings.defaultInterfaceOrientation,
                Is.EqualTo(UnityEditor.UIOrientation.Portrait));
            Assert.That(UnityEditor.PlayerSettings.allowedAutorotateToPortrait, Is.True);
            Assert.That(UnityEditor.PlayerSettings.allowedAutorotateToPortraitUpsideDown, Is.False);
            Assert.That(UnityEditor.PlayerSettings.allowedAutorotateToLandscapeLeft, Is.False);
            Assert.That(UnityEditor.PlayerSettings.allowedAutorotateToLandscapeRight, Is.False);
        }
        private static Rect Bounds(RectTransform r)
        {
            var c=new Vector3[4]; r.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x,c[0].y,c[2].x,c[2].y);
        }
        [Test]
        public void ReferencePortrait_PreservesAuthoredCardSizesAndPositions()
        {
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            var screen=Object.FindFirstObjectByType<FaceRegistrationScreen>(FindObjectsInactive.Include);
            var canvas=screen.GetComponentInParent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled=false;
            canvas.renderMode=RenderMode.WorldSpace;
            var canvasRect=(RectTransform)canvas.transform;
            canvasRect.localScale=Vector3.one;
            var safe=screen.GetComponentInChildren<SafeAreaRoot>(true);
            safe.enabled=false;
            canvasRect.sizeDelta=new Vector2(1080,1920);
            screen.gameObject.SetActive(true);
            safe.ApplyViewport(new Rect(0,0,1080,1920),new Rect(0,0,1080,1920));
            safe.GetComponent<ResponsiveUIContext>().Refresh();
            var panel=(RectTransform)safe.transform.Find("MainPanel");
            screen.GetComponent<FaceRegistrationResponsiveLayout>().ApplyLayout();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.rect.size,Is.EqualTo(new Vector2(1020,1700)));
            var camera=(RectTransform)panel.Find("Body/CameraSection/CameraContainer");
            Assert.That(camera.rect.size,Is.EqualTo(new Vector2(940,680)));
            var button=(RectTransform)panel.Find("Body/GuideSection/RegisterButton");
            Assert.That(button.rect.size,Is.EqualTo(new Vector2(760,110)));
            Assert.That(panel.InverseTransformPoint(button.position).y,Is.EqualTo(-725).Within(.1f));
            var subtitle=(RectTransform)panel.Find("Body/Introduction/SubtitleText");
            Assert.That(panel.InverseTransformPoint(subtitle.position).y,Is.EqualTo(600).Within(.1f));
            var chip=(RectTransform)panel.Find("Body/Introduction/AutoRegistrationBadge");
            Assert.That(chip.rect.size,Is.EqualTo(new Vector2(300,70)));
            Assert.That(panel.InverseTransformPoint(chip.position).y,Is.EqualTo(510).Within(.1f));
            Assert.That(panel.Find("Body/CameraSection").GetComponent<LayoutElement>().flexibleHeight,Is.Zero);
            screen.gameObject.SetActive(false);
        }
        private static void AssertInside(RectTransform parent,RectTransform child)
        {
            Rect a=Bounds(parent),b=Bounds(child);
            Assert.That(b.width,Is.GreaterThan(0),child.name);
            Assert.That(b.height,Is.GreaterThan(0),child.name);
            Assert.That(b.xMin,Is.GreaterThanOrEqualTo(a.xMin-.2f),child.name);
            Assert.That(b.xMax,Is.LessThanOrEqualTo(a.xMax+.2f),child.name);
            Assert.That(b.yMin,Is.GreaterThanOrEqualTo(a.yMin-.2f),child.name);
            Assert.That(b.yMax,Is.LessThanOrEqualTo(a.yMax+.2f),child.name);
        }
        private static void AssertSeparated(RectTransform first,RectTransform second)
        {
            Rect a=Bounds(first),b=Bounds(second);
            Assert.That(a.Overlaps(b),Is.False,first.name+" overlaps "+second.name);
        }
        [Test]
        public void SafeArea_ClipsToCanvasViewport_AndNotifiesOnlyOnChange()
        {
            var root=new GameObject("Viewport",typeof(RectTransform));
            var child=new GameObject("Safe",typeof(RectTransform));
            child.transform.SetParent(root.transform,false);
            var safe=child.AddComponent<SafeAreaRoot>();
            var context=child.AddComponent<ResponsiveUIContext>();
            int events=0; context.Changed+=()=>events++;
            try
            {
                var viewport=new Rect(100,50,800,600);
                var insets=new Rect(0,100,1000,500);
                safe.ApplyViewport(viewport,insets);
                Assert.That(((RectTransform)child.transform).anchorMin.y,Is.EqualTo(50f/600).Within(.001));
                int first=events;
                safe.ApplyViewport(viewport,insets);
                Assert.That(events,Is.EqualTo(first));
                safe.ApplyViewport(viewport,new Rect(0,120,1000,480));
                Assert.That(events,Is.GreaterThan(first));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}

