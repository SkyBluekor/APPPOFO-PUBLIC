using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace AppPofo.Tests.EditMode
{
    public sealed class ProfileDetailPortraitTests
    {
        private RectTransform screen,safe,root;
        private ProfileDetailPortraitLayout layout;
        private ProfileDetailScreen controller;
        private Canvas canvas;
        private static readonly Vector2Int[] Sizes={new Vector2Int(720,1280),new Vector2Int(1080,1920),
            new Vector2Int(1080,2340),new Vector2Int(1080,2400),new Vector2Int(1440,2560),new Vector2Int(1536,2048),new Vector2Int(1600,2560)};
        private const string LongText="아주 긴 사용자 이름 및 직무 Advanced Distributed Systems Engineering 프로젝트와 설명 ";
        public static System.Collections.IEnumerable Cases()
        {
            foreach(var size in Sizes)foreach(bool insets in new[]{false,true})foreach(bool longData in new[]{false,true})
                yield return new TestCaseData(size.x,size.y,insets,longData);
        }
        private void Open(int width,int height,bool insets)
        {
            EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
            controller=Object.FindAnyObjectByType<ProfileDetailScreen>(FindObjectsInactive.Include);
            screen=(RectTransform)controller.transform;canvas=screen.GetComponentInParent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled=false;canvas.renderMode=RenderMode.WorldSpace;
            float scale=Mathf.Sqrt(width/1080f*height/1920f);
            ((RectTransform)canvas.transform).sizeDelta=new Vector2(width/scale,height/scale);canvas.transform.localScale=Vector3.one;
            foreach(Transform sibling in screen.parent)sibling.gameObject.SetActive(sibling==screen);
            safe=(RectTransform)screen.Find("SafeAreaRoot");safe.GetComponent<SafeAreaRoot>().enabled=false;
            safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,width,height),insets?new Rect(48,36,width-96,height-108):new Rect(0,0,width,height));
            root=(RectTransform)safe.Find("CenteredContent");layout=safe.GetComponent<ProfileDetailPortraitLayout>();
            foreach(var modal in Modals())modal.gameObject.SetActive(false);
            Rebuild();
        }
        private RectTransform[] Modals()=>safe.Cast<Transform>().Where(t=>t!=root).Cast<RectTransform>().ToArray();
        private void Rebuild()
        {
            for(int i=0;i<3;i++)
            {
                safe.GetComponent<ResponsiveUIContext>().Refresh();Canvas.ForceUpdateCanvases();layout.ApplyLayout();
                foreach(var group in screen.GetComponentsInChildren<LayoutGroup>())LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
            }
            Canvas.ForceUpdateCanvases();
        }
        private void Populate()
        {
            foreach(string name in new[]{"displayNameText","helineText","bioText","emailText","websiteText"})
                Field<Text>(controller,name).text=string.Concat(Enumerable.Repeat(LongText, name=="bioText"?5:2));
            foreach(var pair in new[]{("skillGrid","UI/SkillChip",13),("projectList","ProjectItem",8),("linkList","LinkItem",8)})
            {
                var parent=Field<Transform>(controller,pair.Item1);parent.parent.gameObject.SetActive(true);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AppPofo/Presentation/Prefabs/"+pair.Item2+".prefab");
                for(int i=0;i<pair.Item3;i++)
                {
                    var row=Object.Instantiate(prefab,parent,false);
                    foreach(var text in row.GetComponentsInChildren<Text>())text.text=LongText+i;
                }
            }
            Field<Text>(controller,"projectDetailTitleText").text=LongText+LongText;
            Field<Text>(controller,"projectDetailDescriptionText").text=string.Concat(Enumerable.Repeat(LongText,16));
            Field<Text>(controller,"projectDetailUrlText").text="https://example.com/"+new string('a',260);
            var recommended=Field<Transform>(controller,"recommendedSkillGrid");
            var chip=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AppPofo/Presentation/Prefabs/UI/SkillChip.prefab");
            for(int i=0;i<18;i++)Object.Instantiate(chip,recommended,false);
            Rebuild();
        }
        [TestCaseSource(nameof(Cases))]
        public void PortraitSizes_SafeAreaLongContentAndSixModalsRemainAccessible(int width,int height,bool insets,bool longData)
        {
            Open(width,height,insets);if(longData)Populate();
            if(insets && !longData) Capture("Detail-"+width+"x"+height,width,height);
            if(insets && longData && width==1080 && height==1920)Capture("Detail-long-top",width,height);
            Inside(screen,safe);Inside(safe,root);Assert.That(root.rect.width,Is.LessThanOrEqualTo(1080));
            Inside(root,(RectTransform)root.Find("Header"));Inside(root,(RectTransform)root.Find("ProfileScrollView"));
            Tree((RectTransform)root.Find("Header"));ActionsInside();
            var scroll=Field<ScrollRect>(controller,"profileScrollView");
            foreach(RectTransform child in scroll.content)Inside(scroll.content,child);
            foreach(string card in new[]{"NameCard","HeadlineCard","BioCard","EmailCard","WebsiteCard"})Tree((RectTransform)scroll.content.Find(card));
            foreach(string field in new[]{"skillGrid","projectList","linkList"})
            {
                var container=Field<Transform>(controller,field);Inside((RectTransform)container.parent,(RectTransform)container);
                foreach(RectTransform row in container)Inside((RectTransform)container,row);
            }
            if(longData)
            {
                Assert.That(scroll.content.rect.height,Is.GreaterThan(scroll.viewport.rect.height));
                scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();
                var last=(RectTransform)Field<Transform>(controller,"linkList").GetChild(7);Inside(scroll.viewport,last);
                if(insets && width==1080 && height==1920)Capture("Detail-long-bottom",width,height);
            }
            Assert.That(Modals().Length,Is.EqualTo(6));
            foreach(var modal in Modals())
            {
                modal.gameObject.SetActive(true);Rebuild();var dialog=(RectTransform)modal.Find("Dialog");
                Inside(safe,dialog);Tree((RectTransform)dialog.Find("Header"));Tree((RectTransform)dialog.Find("Footer"));
                var body=dialog.GetComponentInChildren<ScrollRect>();Inside(dialog,(RectTransform)body.transform);Inside(dialog,body.viewport);
                foreach(RectTransform child in body.content)Inside(body.content,child);
                if(body.content.rect.height>body.viewport.rect.height+.1f)
                {
                    Assert.That(body.vertical,Is.True);body.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();
                    if(body.content.childCount>0)Inside(body.viewport,(RectTransform)body.content.GetChild(body.content.childCount-1));
                }
                foreach(var button in dialog.Find("Footer").GetComponentsInChildren<Button>())Assert.That(button.interactable,Is.True);
                modal.gameObject.SetActive(false);
            }
        }
        [TestCase(1080, 1920)]
        [TestCase(1920, 1080)]
        public void ProfileActions_CurrentLayoutKeepsButtonsInsideAtPortraitAndLandscape(int width, int height)
        {
            // The former FaceRegistrationScreenTests fixture expected the removed
            // ProfileDetailActionLayout. The current scene uses ProfileDetailPortraitLayout.
            Open(width, height, false);
            ActionsInside();
            var actions = (RectTransform)root.Find("ProfileActions");
            Assert.That(actions, Is.Not.Null);
            foreach (string buttonName in new[] { "EditButton", "FaceRegistrationButton", "DeleteButton" })
            {
                var button = (RectTransform)actions.Find(buttonName);
                Assert.That(button, Is.Not.Null, buttonName);
                Inside(root, button);
                Assert.That(button.GetComponent<Button>().interactable, Is.True, buttonName);
            }
        }

        [Test] public void ReferencePortrait_CapturesAllOriginalScreensForComparison()
        {
            Open(1080,1920,false);Capture("Detail",1080,1920);
            foreach(var modal in Modals()){modal.gameObject.SetActive(true);Rebuild();Capture(modal.name,1080,1920);modal.gameObject.SetActive(false);}
        }
        [Test] public void LiveSafeAreaAndNarrowParent_ReflowGridWithoutChangingPrefabFontsOrSprites()
        {
            Open(1080,1920,false);Populate();
            int[] fonts=screen.GetComponentsInChildren<Text>(true).Select(t=>t.fontSize).ToArray();
            var sprites=screen.GetComponentsInChildren<Image>(true).Select(i=>i.sprite).ToArray();
            safe.GetComponent<SafeAreaRoot>().ApplyViewport(new Rect(0,0,1080,1920),new Rect(210,300,660,1320));Rebuild();
            Assert.That(root.rect.width,Is.EqualTo(660).Within(.01f));
            Assert.That(Field<Transform>(controller,"skillGrid").GetComponent<GridLayoutGroup>().constraintCount,Is.EqualTo(1));
            Assert.That(screen.GetComponentsInChildren<Text>(true).Select(t=>t.fontSize),Is.EqualTo(fonts));
            Assert.That(screen.GetComponentsInChildren<Image>(true).Select(i=>i.sprite),Is.EqualTo(sprites));
            ActionsInside();
            foreach(var modal in Modals()){modal.gameObject.SetActive(true);Rebuild();Inside(safe,(RectTransform)modal.Find("Dialog"));Tree((RectTransform)modal.Find("Dialog/Footer"));modal.gameObject.SetActive(false);}
        }
        [Test] public void ExistingControllerReferencesAndActionBindingsArePreserved()
        {
            Open(1080,1920,false);
            foreach(var field in controller.GetType().GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Where(f=>f.IsDefined(typeof(SerializeField),false)))
                Assert.That(field.GetValue(controller),Is.Not.Null,field.Name);
            foreach(var button in screen.GetComponentsInChildren<Button>(true))
            {
                Assert.That(button.onClick.GetPersistentEventCount(),Is.GreaterThan(0),button.name);
                Assert.That(button.onClick.GetPersistentTarget(0),Is.Not.Null,button.name);
            }
        }
        private void Capture(string name,int width,int height)
        {
            var host=new GameObject("P5Capture",typeof(Camera));var camera=host.GetComponent<Camera>();
            camera.enabled=false;camera.orthographic=true;camera.orthographicSize=((RectTransform)canvas.transform).rect.height/2;
            camera.aspect=(float)width/height;camera.transform.position=canvas.transform.position-Vector3.forward*100;
            var target=RenderTexture.GetTemporary(width,height,24);var previous=RenderTexture.active;var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                var oldCamera=canvas.worldCamera;canvas.worldCamera=camera;
                camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();canvas.worldCamera=oldCamera;
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                string folder=Path.GetFullPath(".utmp/PortraitP5/after");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);Object.DestroyImmediate(host);}
        }
        private static T Field<T>(object obj,string name)=>(T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
        private void ActionsInside()
        {
            var actions=(RectTransform)root.Find("ProfileActions");Inside(root,actions);
            Assert.That(actions.gameObject.activeInHierarchy,Is.True,"Existing footer actions must be reachable.");
            // Authored captions sit beside the icon button's hit box; their common container owns their bounds.
            foreach(var rect in actions.GetComponentsInChildren<RectTransform>())Inside(actions,rect);
        }
        private static Rect Bounds(RectTransform rect){var c=new Vector3[4];rect.GetWorldCorners(c);return Rect.MinMaxRect(c[0].x,c[0].y,c[2].x,c[2].y);}
        private static void Inside(RectTransform parent,RectTransform child)
        {
            Rect a=Bounds(parent),b=Bounds(child);Assert.That(b.width,Is.GreaterThanOrEqualTo(0),child.name);
            Assert.That(b.xMin,Is.GreaterThanOrEqualTo(a.xMin-.6f),child.name+" left");Assert.That(b.xMax,Is.LessThanOrEqualTo(a.xMax+.6f),child.name+" right");
            Assert.That(b.yMin,Is.GreaterThanOrEqualTo(a.yMin-.6f),child.name+" bottom");Assert.That(b.yMax,Is.LessThanOrEqualTo(a.yMax+.6f),child.name+" top");
        }
        private static void Tree(RectTransform rect){foreach(RectTransform child in rect){Inside(rect,child);Tree(child);}}
    }
}
