using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AppPofo.Application.Abstractions;
using AppPofo.Application.Interfaces;
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
using Object=UnityEngine.Object;

namespace AppPofo.Tests.PlayMode
{
    public sealed class ProfileDetailPortraitFlowTests
    {
        [UnityTest]
        public IEnumerator ProfileDataSixModalsEditBackAndDelete_KeepExistingBindings()
        {
            LogAssert.Expect(LogType.Log,"[AppPofo Bootstrap] Initialization successfull");
            SceneManager.LoadScene("Bootstrap",LoadSceneMode.Single);yield return null;
            var bootstrap=Object.FindAnyObjectByType<AppPofoBootstrap>();var repository=new MemoryProfiles();
            Property(bootstrap,"GetProfileUseCase",new GetProfileUsecase(repository));
            Property(bootstrap,"GetProfilesUseCase",new GetProfilesUseCase(repository));
            Property(bootstrap,"DeleteProfileUsecase",new DeleteProfileUsecase(repository));
            Property(bootstrap,"LoadAvatarUseCase",new LoadAvatarUseCase(new NoAvatars()));
            var detail=Object.FindAnyObjectByType<ProfileDetailScreen>(FindObjectsInactive.Include);
            detail.Show(repository.Profile.Id);yield return null;yield return null;
            Assert.That(Field<Text>(detail,"displayNameText").text,Is.EqualTo(repository.Profile.DisplayName));
            Assert.That(Field<Text>(detail,"helineText").text,Is.EqualTo(repository.Profile.Headline));
            Assert.That(Field<Transform>(detail,"skillGrid").GetComponentsInChildren<SkillChipView>().Length,Is.EqualTo(13));
            Assert.That(Field<Transform>(detail,"projectList").GetComponentsInChildren<ProjectItemView>().Length,Is.EqualTo(4));
            Assert.That(Field<Transform>(detail,"linkList").GetComponentsInChildren<LinkItemView>().Length,Is.EqualTo(4));
            var scroll=Field<ScrollRect>(detail,"profileScrollView");
            Assert.That(scroll.content.rect.height,Is.GreaterThan(scroll.viewport.rect.height));
            detail.OpenAddSkillPanel();yield return null;Assert.That(Field<GameObject>(detail,"addSkillPanel").activeSelf,Is.True);detail.CloseAddSkillPanel();
            Field<Transform>(detail,"skillGrid").GetComponentsInChildren<SkillChipView>().First().GetComponent<Button>().onClick.Invoke();
            Assert.That(Field<GameObject>(detail,"editSkillPanel").activeSelf,Is.True);detail.CloseEditSkillPanel();
            detail.OpenAddProjectPanel();Assert.That(Field<GameObject>(detail,"addProjectPanel").activeSelf,Is.True);detail.CloseAddProjectPanel();
            detail.OpenAddLinkPanel();Assert.That(Field<GameObject>(detail,"addLinkPanel").activeSelf,Is.True);detail.CloseAddLinkPanel();
            Field<Transform>(detail,"projectList").GetComponentsInChildren<ProjectItemView>().First().GetComponent<Button>().onClick.Invoke();
            Assert.That(Field<GameObject>(detail,"projectDetailPanel").activeSelf,Is.True);
            detail.OpenEditProjectPanel();Assert.That(Field<GameObject>(detail,"addProjectPanel").activeSelf,Is.True);
            detail.CloseAddProjectPanel();Assert.That(Field<GameObject>(detail,"projectDetailPanel").activeSelf,Is.True);detail.CloseProjectDetail();
            var actions=detail.transform.Find("SafeAreaRoot/CenteredContent/ProfileActions");
            Assert.That(actions.gameObject.activeInHierarchy,Is.True);
            actions.Find("DeleteButton").GetComponent<Button>().onClick.Invoke();Assert.That(Field<GameObject>(detail,"deleteConfirmPanel").activeSelf,Is.True);detail.CloseDeleteConfirm();
            actions.Find("EditButton").GetComponent<Button>().onClick.Invoke();yield return null;
            var edit=Field<EditProfileScreen>(detail,"editProfileScreen");Assert.That(edit.gameObject.activeSelf,Is.True);Assert.That(detail.gameObject.activeSelf,Is.False);
            edit.Back();yield return null;Assert.That(detail.gameObject.activeSelf,Is.True);
            detail.Back();yield return null;Assert.That(Field<GameObject>(detail,"profileListScreenRoot").activeSelf,Is.True);
            detail.Show(repository.Profile.Id);yield return null;detail.OpenDeleteConfirm();detail.DeleteProfile();yield return null;
            Assert.That(repository.Profile,Is.Null);Assert.That(detail.gameObject.activeSelf,Is.False);
            Assert.That(Field<GameObject>(detail,"profileListScreenRoot").activeSelf,Is.True);
            Assert.That(Field<GameObject>(detail,"deleteConfirmPanel").activeSelf,Is.False);
        }
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static void Property(object target,string name,object value)=>target.GetType().GetField("<"+name+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private sealed class MemoryProfiles:IProfileRepository
        {
            public Profile Profile;
            public MemoryProfiles()
            {
                var items=Enumerable.Range(1,21).Select(i=>new ProfileItem(i,901,i<=13?ProfileItemType.Skill:i<=17?ProfileItemType.Project:ProfileItemType.Link,
                    "긴 제목 Advanced Distributed Systems Engineering "+i,"긴 프로젝트 설명","https://example.com/portfolio/"+i,i)).ToArray();
                Profile=new Profile(901,"긴 프로필 이름 Advanced Engineering","긴 직무 Software Developer","자기소개","mail@example.com","https://example.com","",true,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,items);
            }
            public Profile Find(long id)=>Profile!=null&&Profile.Id==id?Profile:null;
            public IReadOnlyList<Profile> GetAll()=>Profile==null?Array.Empty<Profile>():new[]{Profile};
            public Profile Save(Profile profile)=>Profile=profile;public void Delete(long id){if(Profile?.Id==id)Profile=null;}
        }
        private sealed class NoAvatars:IAvatarStorage
        {public byte[] Load(string path)=>null;public string Save(long id,byte[] bytes)=>throw new NotSupportedException();public void Delete(string path){}}
    }
}
