using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AppPofo.Domain.Entities;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Components;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AppPofo.Tests.PlayMode
{
    public sealed class BusinessCardPortraitFlowTests
    {
        [UnityTest]
        public IEnumerator AvatarCropThemesGalleryCameraAndCancel_SurviveLayoutChanges()
        {
            LogAssert.Expect(LogType.Log,"[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap",LoadSceneMode.Single); yield return null;
            var controller=Object.FindAnyObjectByType<BusinessCardPanelController>(FindObjectsInactive.Include);
            controller.gameObject.SetActive(true);
            var source=new LayoutImageSource(); Set(controller,"avatarImageSource",source);
            Texture2D input=new Texture2D(8,4,TextureFormat.RGBA32,false); Texture2D square=null;
            try
            {
                for(int y=0;y<4;y++) for(int x=0;x<8;x++) input.SetPixel(x,y,x<2?Color.red:x>=6?Color.blue:Color.green);
                input.Apply(); source.Preview=input;
                var args=new object[]{input.EncodeToPNG(),null,null};
                Assert.That(Call(controller,"TryCreateSquareAvatar",args),Is.EqualTo(true));
                square=(Texture2D)args[1]; var bytes=(byte[])args[2];
                Assert.That(square.width,Is.EqualTo(4)); Assert.That(square.height,Is.EqualTo(4));
                foreach(Color pixel in square.GetPixels()) Assert.That(pixel,Is.EqualTo(Color.green));
                var time=DateTimeOffset.UtcNow;
                var profile=new Profile(123,"Portrait Test","Unity Developer","","","","",true,time,time);
                var view=Get<BusinessCardView>(controller,"businessCardView"); view.Bind(profile,bytes);
                Assert.That(view.transform.Find("DisplayNameText").GetComponent<Text>().text,Is.EqualTo(profile.DisplayName));
                Assert.That(view.transform.Find("HeadlineText").GetComponent<Text>().text,Is.EqualTo(profile.Headline));
                var avatar=Get<Image>(view,"avatarImage");
                Assert.That(avatar.sprite.texture.width,Is.EqualTo(4)); Assert.That(avatar.preserveAspect,Is.True);
                Assert.That(view.transform.Find("AvatarButton/EmptyAvatarText").gameObject.activeSelf,Is.False);
                controller.ApplyAuroraTheme();var aurora=Get<Image>(controller,"businessCardBackground").sprite;
                Assert.That(Get<Outline>(controller,"auroraThemeOutline").enabled,Is.True);
                controller.ApplyBlushTheme();Assert.That(Get<Image>(controller,"businessCardBackground").sprite,Is.Not.SameAs(aurora));
                Assert.That(Get<Outline>(controller,"blushThemeOutline").enabled,Is.True);Assert.That(Get<Outline>(controller,"auroraThemeOutline").enabled,Is.False);
                controller.ApplyNeutralTheme();Assert.That(Get<Outline>(controller,"neutralThemeOutline").enabled,Is.True);
                Assert.That(Get<Outline>(controller,"blushThemeOutline").enabled,Is.False);
                Set(controller,"currentProfileId",123L);
                controller.StartGalleryFromBusinessCard(); Assert.That(source.PickCalls,Is.EqualTo(1));
                controller.StartCameraFromBusinessCard(); Assert.That(source.StartCalls,Is.EqualTo(1));
                var preview=Get<RawImage>(controller,"cameraPreview");
                Assert.That(preview.texture,Is.SameAs(input));Assert.That(preview.uvRect,Is.EqualTo(new Rect(.25f,0,.5f,1)));
                Assert.That(preview.gameObject.activeSelf,Is.True);
                Rect uv=preview.uvRect; var safe=controller.transform.Find("SafeAreaRoot");
                var layout=safe.GetComponent<BusinessCardPortraitLayout>();
                var safeArea=safe.GetComponent<SafeAreaRoot>();safeArea.enabled=false;
                safeArea.ApplyViewport(new Rect(0,0,1080,1920),new Rect(90,600,900,700)); layout.ApplyLayout(); yield return null;
                Assert.That(preview.uvRect,Is.EqualTo(uv));Assert.That(preview.texture,Is.SameAs(input));
                Assert.That(source.StartCalls,Is.EqualTo(1));Assert.That(source.StopCalls,Is.EqualTo(0));
                Assert.That(preview.transform.parent,Is.SameAs(avatar.transform.parent));
                Assert.That(Get<GameObject>(controller,"avatarSourcePanel").activeSelf,Is.False);
                controller.StartCameraFromBusinessCard();Assert.That(source.CaptureCalls,Is.EqualTo(1));
                controller.CloseAvatarSourcePanel();Assert.That(Get<GameObject>(controller,"avatarSourcePanel").activeSelf,Is.False);
                Assert.That(preview.texture,Is.Null);Assert.That(preview.gameObject.activeSelf,Is.False);
                controller.OpenAvatarSourcePanel();Assert.That(Get<GameObject>(controller,"galleryButton").activeSelf,Is.True);
                Assert.That(Get<GameObject>(controller,"cameraButton").activeSelf,Is.True);
                controller.Close();Assert.That(controller.gameObject.activeSelf,Is.False);
            }
            finally
            {
                Set(controller,"avatarImageSource",null);
                if(square!=null)Object.Destroy(square);Object.Destroy(input);
            }
        }
        private static object Call(object target,string name,object[] args)=>target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,args);
        private static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,value);
        private sealed class LayoutImageSource:IAvatarImageSource
        {
            public Texture Preview; public int PickCalls,StartCalls,CaptureCalls,StopCalls;
            public bool IsCameraRunning{get;private set;}public bool IsCameraReady=>IsCameraRunning;
            // Real file pickers and capture hardware are outside this deterministic layout regression.
            public void PickImage(Action<byte[]> success,Action<string> failure){PickCalls++;}
            public void StartCamera(Action<Texture> success,Action<string> failure){StartCalls++;IsCameraRunning=true;success(Preview);}
            public void CaptureImage(Action<byte[]> success,Action<string> failure){CaptureCalls++;}
            public void StopCamera(){StopCalls++;IsCameraRunning=false;}
        }
    }
}
