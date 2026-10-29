using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AppPofo.Application.Abstractions;
using AppPofo.Application.UseCases;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Components;
using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

namespace AppPofo.Tests.PlayMode
{
    public sealed class ProfileSummaryPanelTests
    {
        [UnityTest]
        public IEnumerator LocalCardClick_OpensReadOnlySummary_CloseDisposesIt_AndCameraWorkIsInvalidated()
        {
            LogAssert.Expect(LogType.Log,"[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap",LoadSceneMode.Single);yield return null;
            var panel=Object.FindAnyObjectByType<BusinessCardPanelController>(FindObjectsInactive.Include);
            var bootstrap=Object.FindAnyObjectByType<AppPofoBootstrap>();var repository=new MemoryProfiles();
            typeof(AppPofoBootstrap).GetProperty("GetProfileUseCase").SetValue(bootstrap,new GetProfileUsecase(repository));
            var canvas=panel.GetComponentInParent<Canvas>();
            var card=panel.CreateRecognitionCard(73,canvas.transform,()=>panel.ShowRecognizedLocalProfile(73));
            try
            {
                Assert.That(card.GetComponent<Button>().interactable,Is.True);
                Assert.That(card.GetComponent<Image>().raycastTarget,Is.True);
                var rect=(RectTransform)card.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(900,360);
                Canvas.ForceUpdateCanvases();yield return null;
                var results=new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,rect.position)},results);
                Assert.That(results.Count,Is.GreaterThan(0));Assert.That(results[0].gameObject,Is.SameAs(card.gameObject),"A real UI raycast must hit the card, not pass through it.");
                card.GetComponent<Button>().onClick.Invoke();yield return null;
                var summary=Object.FindAnyObjectByType<ProfileSummaryPanel>();
                Assert.That(summary,Is.Not.Null);Assert.That(summary.IsOpen,Is.True);
                Assert.That(summary.Current.LocalProfile.Id,Is.EqualTo(73));
                Assert.That(summary.Current.bio,Is.EqualTo("Short introduction"));
                Assert.That(summary.GetComponentsInChildren<Button>().Any(b=>b.name.Contains("Delete")||b.name.Contains("Edit")||b.name.Contains("Save")),Is.False);
                var camera=Object.FindAnyObjectByType<AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera>(FindObjectsInactive.Include);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                int before=(int)camera.GetType().GetField("recognitionGeneration",flags).GetValue(camera);
                camera.GetType().GetMethod("OnSummaryVisibilityChanged",flags).Invoke(camera,new object[]{true});
                Assert.That((int)camera.GetType().GetField("recognitionGeneration",flags).GetValue(camera),Is.GreaterThan(before));
                summary.transform.Find("SafeAreaRoot/SummarySheet/ReturnToCamera").GetComponent<Button>().onClick.Invoke();
                Assert.That(summary.IsOpen,Is.False);Assert.That(summary.Current,Is.Null);Assert.That(repository.Writes,Is.Zero);
            }
            finally {panel.CloseRecognizedSummary();Object.Destroy(card.gameObject);}
        }

        [UnityTest]
        public IEnumerator Summary_FitsPortraitAndLandscape_ShowsOnlySafeLinks_AndDismissesOnModeChange()
        {
            LogAssert.Expect(LogType.Log,"[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap",LoadSceneMode.Single);yield return null;
            var controller=Object.FindAnyObjectByType<BusinessCardPanelController>(FindObjectsInactive.Include);
            var source=(BusinessCardView)controller.GetType().GetField("businessCardView",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
            var panel=ProfileSummaryPanel.Create(controller.transform,source,_=>{},controller.StyleSummaryIdentity);
            var data=ProfileSummaryData.FromLocal(new MemoryProfiles().Profile,null);
            try
            {
                foreach(var size in new[]{new Vector2(720,1280),new Vector2(1200,2000),new Vector2(1600,900)})
                {
                    panel.ShowLocal(data);var safe=(RectTransform)panel.transform.Find("SafeAreaRoot");
                    safe.GetComponent<SafeAreaRoot>().enabled=false;
                    safe.anchorMin=safe.anchorMax=new Vector2(.5f,.5f);safe.sizeDelta=size;
                    panel.GetType().GetMethod("Fit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panel,null);
                    Canvas.ForceUpdateCanvases();
                    var sheet=(RectTransform)safe.Find("SummarySheet");var corners=new Vector3[4];sheet.GetWorldCorners(corners);
                    foreach(var corner in corners){var point=safe.InverseTransformPoint(corner);Assert.That(safe.rect.Contains(new Vector2(point.x,point.y)),Is.True,size.ToString());}
                    Assert.That(panel.GetComponentsInChildren<ScrollRect>(),Is.Empty);
                    Assert.That(panel.transform.Find("SafeAreaRoot/SummarySheet/ProjectCard/ProjectLink").gameObject.activeSelf,Is.False);
                    if(size.x==1200 || size.x==1600) Capture(panel,size);
                    panel.Close();
                }
                panel.ShowLocal(data);
                var mode=typeof(CloudModeSession).GetProperty("Mode");var previous=mode.GetValue(null);
                try{mode.SetValue(null,ProfileDataMode.Cloud);yield return null;Assert.That(panel.IsOpen,Is.False);}
                finally{mode.SetValue(null,previous);}
            }
            finally{panel.Close();Object.Destroy(panel.gameObject);}
        }
        private sealed class MemoryProfiles:IProfileRepository
        {
            public int Writes;
            public readonly Profile Profile=new Profile(73,"Synthetic profile","Developer","Short introduction","","demo@example.invalid","",true,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,
                new[]{new ProfileItem(1,73,ProfileItemType.Skill,"Unity","","",0),new ProfileItem(2,73,ProfileItemType.Project,"AppPofo","Example","javascript:alert(1)",1)});
            public Profile Find(long id)=>id==73?Profile:null;
            public IReadOnlyList<Profile> GetAll()=>new[]{Profile};public Profile Save(Profile value){Writes++;return value;}public void Delete(long id){Writes++;}
        }
        private static void Capture(ProfileSummaryPanel panel,Vector2 size)
        {
            var canvas=panel.GetComponentInParent<Canvas>();canvas.GetComponent<CanvasScaler>().enabled=false;canvas.renderMode=RenderMode.WorldSpace;
            var root=(RectTransform)canvas.transform;root.localScale=Vector3.one;root.sizeDelta=size;
            var go=new GameObject("SummaryCapture",typeof(Camera));var camera=go.GetComponent<Camera>();
            var target=RenderTexture.GetTemporary((int)size.x,(int)size.y,24);var previous=RenderTexture.active;
            var image=new Texture2D((int)size.x,(int)size.y,TextureFormat.RGB24,false);
            try
            {
                camera.enabled=false;camera.orthographic=true;camera.orthographicSize=size.y/2;camera.aspect=size.x/size.y;
                camera.transform.SetPositionAndRotation(root.TransformPoint(root.rect.center)-canvas.transform.forward*100,canvas.transform.rotation);
                camera.cullingMask=1<<panel.gameObject.layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.gray;camera.targetTexture=target;
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                string directory=System.IO.Path.GetFullPath(".utmp/profile-summary-captures");System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,$"summary-{size.x}x{size.y}.png"),image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.Destroy(image);Object.Destroy(go);}
        }
    }
}
