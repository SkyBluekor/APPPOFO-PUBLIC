using System;
using AppPofo.Presentation.Common;
using NUnit.Framework;
using UnityEngine;

namespace AppPofo.Tests.EditMode
{
    public sealed class ProfileSummaryCodecTests
    {
        private const string Id = "11111111-1111-4111-8111-111111111111";
        [Serializable] private sealed class Envelope { public ProfileSummaryData data; }
        private static ProfileSummaryData Data() => new ProfileSummaryData { id=Id, display_name="Public name",headline="Role",bio="Intro",
            skills=new[]{"Unity","C#"}, project=new ProfileSummaryProject{title="AppPofo",description="Summary",url="https://example.org/project"},
            links=new[]{new ProfileSummaryLink{title="Portfolio",url="https://example.org"}} };
        private static bool Read(ProfileSummaryData data, string id=Id) => ProfileSummaryCodec.TryRead(JsonUtility.ToJson(new Envelope{data=data}), id, out _);
        [Test] public void PublicSummary_RequiresSelectedUuid_AndAcceptsEmptyOptionalSections()
        {
            Assert.That(Read(Data()),Is.True);
            Assert.That(Read(Data(),"22222222-2222-4222-8222-222222222222"),Is.False);
            var data=Data();data.bio="";data.project=null;data.skills=Array.Empty<string>();data.links=Array.Empty<ProfileSummaryLink>();
            Assert.That(Read(data),Is.True);
        }
        [Test] public void RejectsUnsafeLinks_AndOversizedOrMissingFields()
        {
            foreach(var url in new[]{"javascript:alert(1)","file:///etc/passwd","https://user:demo@example.invalid","data:text/plain,x","intent://example"})
            {var data=Data();data.links[0].url=url;Assert.That(Read(data),Is.False,url);}
            var big=Data();big.skills=new[]{"A","B","C","D","E","F"};Assert.That(Read(big),Is.False);
            big=Data();big.bio=new string('x',281);Assert.That(Read(big),Is.False);
            big=Data();big.avatar_base64=new string('A',32772);Assert.That(Read(big),Is.False);
            big=Data();big.display_name="";Assert.That(Read(big),Is.False);
            Assert.That(ProfileSummaryCodec.TryRead("{}",Id,out _),Is.False);
        }
        [Test] public void LocalSnapshot_IgnoresEmailWebsite_AndLimitsSectionsAndUnsafeUrls()
        {
            var items=new System.Collections.Generic.List<AppPofo.Domain.Entities.ProfileItem>();
            for(int i=0;i<8;i++)items.Add(new AppPofo.Domain.Entities.ProfileItem(i+1,73,AppPofo.Domain.ValueObjects.ProfileItemType.Skill,"Skill "+i,"","",i));
            items.Add(new AppPofo.Domain.Entities.ProfileItem(20,73,AppPofo.Domain.ValueObjects.ProfileItemType.Project,"Project","Desc","javascript:alert(1)",20));
            var time=DateTimeOffset.UtcNow;
            var profile=new AppPofo.Domain.Entities.Profile(73,"Name","Role","Intro","","demo@example.invalid","https://private.example.org",true,time,time,items);
            var data=ProfileSummaryData.FromLocal(profile,null);
            Assert.That(data.skills.Length,Is.EqualTo(5));Assert.That(data.project.url,Is.Null);Assert.That(data.links,Is.Empty);
            Assert.That(JsonUtility.ToJson(data),Does.Not.Contain("demo@example.invalid").And.Not.Contain("private.example.org"));
        }
    }
}
