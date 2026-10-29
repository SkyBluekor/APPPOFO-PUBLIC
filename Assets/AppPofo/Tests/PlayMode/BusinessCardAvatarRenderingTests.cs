using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AppPofo.Application.Abstractions;
using AppPofo.Application.Interfaces;
using AppPofo.Application.UseCases;
using AppPofo.Domain.Entities;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Components;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace AppPofo.Tests.PlayMode
{
    public sealed class BusinessCardAvatarRenderingTests
    {
        [UnityTest]
        public IEnumerator InlineOrientation_AllQuarterTurnsAndMirrors_MatchUprightCaptureAndRestart()
        {
            LogAssert.Expect(LogType.Log,"[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap",LoadSceneMode.Single);yield return null;
            var controller=Object.FindAnyObjectByType<BusinessCardPanelController>(FindObjectsInactive.Include);
            var bootstrap=Object.FindAnyObjectByType<AppPofoBootstrap>();
            var repository=new MemoryProfiles();var storage=new MemoryAvatars();
            Property(bootstrap,"GetProfileUseCase",new GetProfileUsecase(repository));
            Property(bootstrap,"UpdateAvatarUseCase",new UpdateAvatarUseCase(repository,storage));
            Property(bootstrap,"LoadAvatarUseCase",new LoadAvatarUseCase(storage));
            var input=new Texture2D(8,4,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};
            for(int y=0;y<4;y++)for(int x=0;x<8;x++)
                input.SetPixel(x,y,x<2||x>=6?Color.magenta:y<2?(x<4?Color.red:Color.green):(x<4?Color.blue:Color.yellow));
            input.Apply();
            var tall=new CameraFrameOrientation(90,false,8,4).CreateUprightTexture(input.GetPixels32());
            tall.filterMode=FilterMode.Point;
            var source=new OrientedImageSource{Preview=input,Orientation=new CameraFrameOrientation(0,false,8,4)};
            var canvas=controller.GetComponentInParent<Canvas>();
            var host=new GameObject("OrientationRenderCamera",typeof(Camera));var camera=host.GetComponent<Camera>();
            var target=RenderTexture.GetTemporary(1080,1920,24);
            try
            {
                controller.Show(repository.Profile.Id);Field(controller,"avatarImageSource",source);
                foreach(Transform sibling in controller.transform.parent)sibling.gameObject.SetActive(sibling==controller.transform);
                camera.enabled=false;camera.orthographic=true;camera.orthographicSize=960;camera.aspect=1080f/1920;
                camera.cullingMask=1<<5;camera.targetTexture=target;
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
                yield return null;
                var preview=Field<RawImage>(controller,"cameraPreview");var home=preview.transform.parent;
                var avatar=Field<Image>(Field<BusinessCardView>(controller,"businessCardView"),"avatarImage");
                Vector2 maskSize=((RectTransform)avatar.transform.parent).rect.size;
                foreach(int angle in new[]{0,90,180,270})foreach(bool mirror in new[]{false,true})
                {
                    controller.StartCameraFromBusinessCard();yield return null;
                    // Metadata changes while already displaying a frame, as on a camera switch.
                    source.Preview=mirror?tall:input;
                    source.Orientation=new CameraFrameOrientation(angle,mirror,source.Preview.width,source.Preview.height);
                    yield return null;Canvas.ForceUpdateCanvases();
                    var effect=preview.GetComponent<CameraPreviewOrientationEffect>();
                    Assert.That(effect.HasOrientation,Is.True);
                    Assert.That(effect.Orientation.RotationAngle,Is.EqualTo(angle));
                    Assert.That(effect.Orientation.VerticallyMirrored,Is.EqualTo(mirror));
                    Assert.That(preview.texture,Is.SameAs(source.Preview));
                    bool wide=source.Orientation.EffectiveWidth>source.Orientation.EffectiveHeight;
                    Assert.That(preview.uvRect,Is.EqualTo(wide?new Rect(.25f,0,.5f,1):new Rect(0,.25f,1,.5f)));
                    Assert.That(((RectTransform)avatar.transform.parent).rect.size,Is.EqualTo(maskSize));
                    Assert.That(preview.transform.parent,Is.SameAs(avatar.transform.parent));
                    var upright=source.Orientation.CreateUprightTexture(source.Preview.GetPixels32());
                    try
                    {
                        var crop=upright.GetPixels((upright.width-4)/2,(upright.height-4)/2,4,4);
                        AssertRenderedQuadrants(camera,target,preview,crop);
                        controller.StartCameraFromBusinessCard();yield return null;
                        Assert.That(preview.transform.parent,Is.SameAs(home));
                        Assert.That(effect.HasOrientation,Is.False);
                        Assert.That(avatar.sprite.texture.GetPixels(),Is.EqualTo(crop));
                        AssertRenderedQuadrants(camera,target,avatar,crop);
                    }
                    finally{Object.Destroy(upright);}
                    controller.StartCameraFromBusinessCard();yield return null;
                    Assert.That(effect.HasOrientation,Is.True);
                    controller.Close();controller.Show(repository.Profile.Id);yield return null;
                    Assert.That(effect.HasOrientation,Is.False);
                    Assert.That(preview.transform.parent,Is.SameAs(home));
                }
                Assert.That(source.UprightCaptures,Is.EqualTo(8));Assert.That(source.RawCaptures,Is.Zero);
            }
            finally
            {
                Field(controller,"avatarImageSource",null);camera.targetTexture=null;canvas.worldCamera=null;
                RenderTexture.ReleaseTemporary(target);Object.Destroy(host);Object.Destroy(input);Object.Destroy(tall);
            }
        }

        private static void AssertRenderedQuadrants(Camera camera,RenderTexture target,MaskableGraphic image,Color[] crop)
        {
            Canvas.ForceUpdateCanvases();camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try
            {
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                Rect rect=image.rectTransform.rect;
                foreach(int y in new[]{1,3})foreach(int x in new[]{1,3})
                {
                    Vector3 point=camera.WorldToScreenPoint(image.rectTransform.TransformPoint(
                        new Vector2(rect.xMin+(x+.25f)/4*rect.width,rect.yMin+(y+.25f)/4*rect.height)));
                    Color actual=pixels.GetPixel(Mathf.RoundToInt(point.x),Mathf.RoundToInt(point.y));
                    Color expected=crop[y*4+x];
                    Assert.That(actual.r,Is.EqualTo(expected.r).Within(.03f));
                    Assert.That(actual.g,Is.EqualTo(expected.g).Within(.03f));
                    Assert.That(actual.b,Is.EqualTo(expected.b).Within(.03f));
                }
            }
            finally{RenderTexture.active=previous;Object.Destroy(pixels);}
        }

        [UnityTest]
        public IEnumerator StoredGalleryAndCameraAvatars_RenderInBothMasks_AfterThemeAndReentry()
        {
            LogAssert.Expect(LogType.Log,"[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap",LoadSceneMode.Single);yield return null;
            var controller=Object.FindAnyObjectByType<BusinessCardPanelController>(FindObjectsInactive.Include);
            var bootstrap=Object.FindAnyObjectByType<AppPofoBootstrap>();
            var repository=new MemoryProfiles();var storage=new MemoryAvatars();
            Property(bootstrap,"GetProfileUseCase",new GetProfileUsecase(repository));
            Property(bootstrap,"GetProfilesUseCase",new GetProfilesUseCase(repository));
            // ProfileList now prefers the paged SearchProfilesUseCase over GetProfilesUseCase.
            // Both must read the same in-memory repository as the avatar writer.
            Property(bootstrap,"SearchProfilesUseCase",new SearchProfilesUseCase(repository));
            Property(bootstrap,"UpdateAvatarUseCase",new UpdateAvatarUseCase(repository,storage));
            Property(bootstrap,"LoadAvatarUseCase",new LoadAvatarUseCase(storage));
            Property(bootstrap,"UpdateBusinessCardThemeUseCase",new UpdateBusinessCardThemeUseCase(repository));
            var input=new Texture2D(8,4,TextureFormat.RGBA32,false);
            for(int y=0;y<4;y++)for(int x=0;x<8;x++)input.SetPixel(x,y,x<2?Color.red:x>=6?Color.blue:Color.green);
            input.Apply();byte[] bytes=input.EncodeToPNG();
            var source=new ImageSource{Preview=input,Bytes=bytes};
            var canvas=controller.GetComponentInParent<Canvas>();
            var host=new GameObject("AvatarRenderingCamera",typeof(Camera));var camera=host.GetComponent<Camera>();
            var safe=controller.transform.Find("SafeAreaRoot").GetComponent<SafeAreaRoot>();
            RenderTexture target=null;
            try
            {
                controller.Show(repository.Profile.Id);Field(controller,"avatarImageSource",source);
                foreach(Transform sibling in controller.transform.parent)sibling.gameObject.SetActive(sibling==controller.transform);
                var list=Object.FindAnyObjectByType<ProfileListScreen>(FindObjectsInactive.Include);
                list.gameObject.SetActive(true);yield return null;
                camera.enabled=false;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=Color.black;camera.cullingMask=1<<5;
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
                foreach(var size in new[]{new Vector2Int(1080,1920),new Vector2Int(720,1280),new Vector2Int(1600,2560)})
                {
                    target=RenderTexture.GetTemporary(size.x,size.y,24);camera.targetTexture=target;
                    camera.orthographicSize=size.y/2f;camera.aspect=(float)size.x/size.y;
                    Canvas.ForceUpdateCanvases();yield return null;
                    safe.enabled=false;safe.ApplyViewport(new Rect(0,0,size.x,size.y),new Rect(24,24,size.x-48,size.y-48));
                    safe.GetComponent<BusinessCardPortraitLayout>().ApplyLayout();Canvas.ForceUpdateCanvases();yield return null;
                    controller.StartGalleryFromBusinessCard();Assert.That(source.PickCalls,Is.GreaterThan(0));
                    source.PickSuccess(bytes);yield return null;Canvas.ForceUpdateCanvases();
                    var view=Field<BusinessCardView>(controller,"businessCardView");
                    var avatar=Field<Image>(view,"avatarImage");var preview=Field<Image>(controller,"previewImage");
                    AssertSquareGreen(avatar.sprite.texture);AssertSquareGreen(preview.sprite.texture);
                    AssertSquareGreen(ListAvatar(list).sprite.texture);
                    Assert.That(Field<GameObject>(controller,"avatarSourcePanel").activeSelf,Is.False);
                    AssertRendered(camera,target,avatar);
                    controller.CloseAvatarSourcePanel();yield return null;
                    foreach(Action theme in new Action[]{controller.ApplyAuroraTheme,controller.ApplyBlushTheme,controller.ApplyNeutralTheme})
                    {
                        theme();Canvas.ForceUpdateCanvases();
                        Assert.That(Field<Image>(controller,"avatarFrameImage").sprite,Is.Not.Null);
                        AssertRendered(camera,target,avatar);
                    }
                    controller.Close();controller.Show(repository.Profile.Id);yield return null;Canvas.ForceUpdateCanvases();
                    AssertSquareGreen(Field<Image>(view,"avatarImage").sprite.texture);AssertRendered(camera,target,avatar);
                    controller.StartCameraFromBusinessCard();yield return null;Canvas.ForceUpdateCanvases();
                    var cameraPreview=Field<RawImage>(controller,"cameraPreview");
                    Assert.That(Field<GameObject>(controller,"avatarSourcePanel").activeSelf,Is.False);
                    Assert.That(cameraPreview.transform.parent,Is.SameAs(avatar.transform.parent));
                    Assert.That(Field<Text>(controller,"businessCameraButtonLabel").text,Is.EqualTo("완료"));
                    Assert.That(cameraPreview.texture,Is.SameAs(input));Assert.That(cameraPreview.uvRect,Is.EqualTo(new Rect(.25f,0,.5f,1)));
                    AssertRendered(camera,target,cameraPreview);
                    var previousListSprite=ListAvatar(list).sprite;
                    controller.StartCameraFromBusinessCard();yield return null;Canvas.ForceUpdateCanvases();
                    Assert.That(source.CaptureCalls,Is.GreaterThan(0));
                    Assert.That(Field<GameObject>(controller,"avatarSourcePanel").activeSelf,Is.False);
                    Assert.That(cameraPreview.gameObject.activeSelf,Is.False);
                    Assert.That(Field<Text>(controller,"businessCameraButtonLabel").text,Is.EqualTo("촬영"));
                    AssertSquareGreen(avatar.sprite.texture);AssertRendered(camera,target,avatar);
                    Assert.That(ListAvatar(list).sprite,Is.Not.SameAs(previousListSprite),"The visible list must reload the captured avatar.");
                    AssertSquareGreen(ListAvatar(list).sprite.texture);
                    list.gameObject.SetActive(false);
                    controller.StartGalleryFromBusinessCard();source.PickSuccess(bytes);yield return null;
                    list.gameObject.SetActive(true);yield return null;
                    AssertSquareGreen(ListAvatar(list).sprite.texture);
                    // A new first click restarts the inline preview; closing cancels it.
                    controller.StartCameraFromBusinessCard();yield return null;
                    controller.Close();controller.Show(repository.Profile.Id);yield return null;
                    Assert.That(cameraPreview.gameObject.activeSelf,Is.False);
                    Assert.That(cameraPreview.transform.parent,Is.Not.SameAs(avatar.transform.parent));
                    controller.CloseAvatarSourcePanel();yield return null;AssertRendered(camera,target,avatar);
                    camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);target=null;
                }
            }
            finally
            {
                Field(controller,"avatarImageSource",null);camera.targetTexture=null;canvas.worldCamera=null;
                if(target!=null)RenderTexture.ReleaseTemporary(target);Object.Destroy(host);Object.Destroy(input);
            }
        }
        private static void AssertSquareGreen(Texture2D texture)
        {
            Assert.That(texture.width,Is.EqualTo(4));Assert.That(texture.height,Is.EqualTo(4));
            foreach(Color pixel in texture.GetPixels())Assert.That(pixel,Is.EqualTo(Color.green));
        }
        private static Image ListAvatar(ProfileListScreen list)
        {
            return Field<Transform>(list,"contentRoot").GetComponentsInChildren<ProfileListItemView>()
                .Select(row=>Field<Image>(row,"avatarImage")).Single();
        }
        private static void AssertRendered(Camera camera,RenderTexture target,MaskableGraphic image)
        {
            Assert.That(image.isActiveAndEnabled,Is.True,image.name);Assert.That(image.color.a,Is.EqualTo(1));
            var rect=image.rectTransform;var mask=(RectTransform)rect.parent;
            Assert.That(rect.anchorMin,Is.EqualTo(Vector2.zero));Assert.That(rect.anchorMax,Is.EqualTo(Vector2.one));
            Assert.That(rect.sizeDelta,Is.EqualTo(Vector2.zero));Assert.That(rect.anchoredPosition,Is.EqualTo(Vector2.zero));
            Assert.That(rect.rect.size,Is.EqualTo(mask.rect.size));
            Canvas.ForceUpdateCanvases();camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try
            {
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                Vector3 center=camera.WorldToScreenPoint(rect.TransformPoint(rect.rect.center));
                var corners=new Vector3[4];rect.GetWorldCorners(corners);
                Vector3 left=camera.WorldToScreenPoint(corners[0]),right=camera.WorldToScreenPoint(corners[2]);
                // Check center and four points inside the circle, not just Sprite assignment or bounds.
                foreach(var offset in new[]{Vector2.zero,Vector2.left,Vector2.right,Vector2.up,Vector2.down})
                {
                    int x=Mathf.RoundToInt(center.x+offset.x*(right.x-left.x)*.25f);
                    int y=Mathf.RoundToInt(center.y+offset.y*(right.y-left.y)*.25f);
                    Color pixel=pixels.GetPixel(x,y);
                    Assert.That(pixel.g,Is.GreaterThan(.95f),image.name+" visible green G");
                    Assert.That(pixel.r,Is.LessThan(.05f),image.name+" visible green R");
                    Assert.That(pixel.b,Is.LessThan(.05f),image.name+" visible green B");
                }
            }
            finally{RenderTexture.active=previous;Object.Destroy(pixels);}
        }
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static void Field(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static void Property(object target,string name,object value)=>Field(target,"<"+name+">k__BackingField",value);
        private sealed class MemoryProfiles:IProfileRepository,IProfileSearchRepository
        {
            public Profile Profile=new Profile(73,"Avatar rendering","Developer","","","","",true,DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddMinutes(-1));
            public Profile Find(long id)=>id==Profile.Id?Profile:null;
            public IReadOnlyList<Profile> GetAll()=>new[]{Profile};public Profile Save(Profile profile)=>Profile=profile;
            public void Delete(long id){}
            public ProfileSearchPage Search(string name, IReadOnlyList<string> skills, int limit, int offset)
            {
                bool matchesName=string.IsNullOrEmpty(name) ||
                    Profile.DisplayName.IndexOf(name,StringComparison.OrdinalIgnoreCase)>=0;
                bool matchesSkills=skills.Count==0 ||
                    skills.All(skill=>Profile.Items.Any(item=>
                        item.ItemType==AppPofo.Domain.ValueObjects.ProfileItemType.Skill &&
                        string.Equals(item.Title,skill,StringComparison.OrdinalIgnoreCase)));
                int total=matchesName && matchesSkills ? 1 : 0;
                return new ProfileSearchPage(total==1 && offset==0
                    ? new[]{Profile} : Array.Empty<Profile>(),total);
            }
        }
        private sealed class MemoryAvatars:IAvatarStorage
        {
            private byte[] bytes;
            public string Save(long id,byte[] data){bytes=data;return "memory://avatar";}
            public byte[] Load(string path)=>bytes;public void Delete(string path){bytes=null;}
        }
        private sealed class ImageSource:IAvatarImageSource
        {
            public Texture Preview;public byte[] Bytes;public Action<byte[]> PickSuccess;public int PickCalls,CaptureCalls;
            public bool IsCameraRunning{get;private set;}public bool IsCameraReady=>IsCameraRunning;
            public void PickImage(Action<byte[]> success,Action<string> failure){PickCalls++;PickSuccess=success;}
            public void StartCamera(Action<Texture> success,Action<string> failure){IsCameraRunning=true;success(Preview);}
            public void CaptureImage(Action<byte[]> success,Action<string> failure){CaptureCalls++;success(Bytes);}
            public void StopCamera(){IsCameraRunning=false;}
        }
        private sealed class OrientedImageSource:IAvatarImageSource,IOrientedCameraSource
        {
            public Texture2D Preview;public CameraFrameOrientation Orientation;public int UprightCaptures,RawCaptures;
            public bool IsCameraRunning{get;private set;}public bool IsCameraReady=>IsCameraRunning;
            public void PickImage(Action<byte[]> success,Action<string> failure){}
            public void StartCamera(Action<Texture> success,Action<string> failure){IsCameraRunning=true;success(Preview);}
            public bool TryGetPreviewFrame(out Texture texture,out CameraFrameOrientation orientation)
            {texture=Preview;orientation=Orientation;return IsCameraReady;}
            public void CaptureUprightImage(Action<byte[]> success,Action<string> failure)
            {
                UprightCaptures++;var upright=Orientation.CreateUprightTexture(Preview.GetPixels32());
                try{success(upright.EncodeToPNG());}finally{Object.Destroy(upright);}
            }
            public void CaptureImage(Action<byte[]> success,Action<string> failure){RawCaptures++;success(Preview.EncodeToPNG());}
            public void StopCamera(){IsCameraRunning=false;}
        }
    }
}
