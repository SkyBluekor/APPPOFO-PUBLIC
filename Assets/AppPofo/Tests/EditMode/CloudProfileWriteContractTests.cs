using AppPofo.Presentation.Common;
using NUnit.Framework;
using UnityEngine;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudProfileWriteContractTests
    {
        private const string Id = "11111111-1111-4111-8111-111111111111";

        [Test]
        public void FullReplacementRejectsMissingItems()
        {
            var payload = new CloudProfileWritePayload { display_name = "김유리" };
            Assert.That(CloudProfileWritePayload.TryToJson(payload, out var json, out _), Is.False);
            Assert.That(json, Is.Null);
        }

        [Test]
        public void EmptyItemsCanBeSavedOnlyWhenExplicitlyProvided()
        {
            var payload = new CloudProfileWritePayload
            {
                display_name = "김유리", headline = "Unity 개발자",
                is_active = false, items = new CloudProfileItem[0]
            };
            Assert.That(CloudProfileWritePayload.TryToJson(payload, out var json, out _), Is.True);
            Assert.That(json, Does.Contain("\"items\":[]"));
            var parsed = JsonUtility.FromJson<CloudProfileWritePayload>(json);
            Assert.That(parsed.display_name, Is.EqualTo("김유리"));
            Assert.That(parsed.is_active, Is.False);
        }

        [Test]
        public void FullReplacementPreservesPreviousItemsAndActiveState()
        {
            var detail = new CloudProfileDetail
            {
                id = Id, display_name = "김유리", is_active = false,
                business_card_theme = "BLUSH",
                items = new[] { new CloudProfileItem { item_type = "PROJECT",
                    title = "AppPoFo", sort_order = 2 } }
            };
            var payload = CloudProfileWritePayload.FromDetail(detail);
            Assert.That(CloudProfileWritePayload.TryToJson(payload, out var json, out _), Is.True);
            Assert.That(json, Does.Contain("AppPoFo"));
            Assert.That(payload.is_active, Is.False);
            Assert.That(payload.items, Has.Length.EqualTo(1));
        }

        [TestCase("{\"data\":{\"id\":\"11111111-1111-4111-8111-111111111111\"}}", true)]
        [TestCase("{\"data\":{\"id\":\"invalid\"}}", false)]
        [TestCase("{\"data\":null}", false)]
        [TestCase("", false)]
        public void MutationResponseRequiresValidCloudId(string json, bool valid)
        {
            Assert.That(CloudProfileWriteJson.TryReadAck(json, out var id), Is.EqualTo(valid));
            if (valid) Assert.That(id, Is.EqualTo(Id));
            else Assert.That(id, Is.Null);
        }

        [Test]
        public void CloudRegistrationBuildsCompletePayloadWithoutLocalProfileId()
        {
            var skills = new[] { " C# ", "Unity" };
            var draft = CloudProfileWritePayload.FromRegistration(
                " 김유리 ", " 개발자 ", "자기소개",
                "user@example.test", "https://example.test", skills);

            Assert.That(draft.display_name, Is.EqualTo("김유리"));
            Assert.That(draft.headline, Is.EqualTo("개발자"));
            Assert.That(draft.bio, Is.EqualTo("자기소개"));
            Assert.That(draft.email, Is.EqualTo("user@example.test"));
            Assert.That(draft.website, Is.EqualTo("https://example.test"));
            Assert.That(draft.items, Has.Length.EqualTo(2));
            Assert.That(draft.items[0].title, Is.EqualTo("C#"));
            Assert.That(draft.items[0].item_type, Is.EqualTo("SKILL"));
            Assert.That(draft.items[0].sort_order, Is.EqualTo(0));
            Assert.That(draft.items[1].title, Is.EqualTo("Unity"));
            Assert.That(draft.items[1].sort_order, Is.EqualTo(1));
            Assert.That(CloudProfileWritePayload.TryToJson(draft, out var json, out _), Is.True);
            Assert.That(json, Does.Contain("\"items\":"));
            Assert.That(json, Does.Not.Contain("owner_device_id"));
        }

        [Test]
        public void CloudRegistrationAcceptsExplicitEmptySkillsAndRejectsInvalidDraft()
        {
            var empty = CloudProfileWritePayload.FromRegistration(
                "김유리", null, null, null, null, new string[0]);
            Assert.That(CloudProfileWritePayload.TryToJson(empty, out var json, out _), Is.True);
            Assert.That(json, Does.Contain("\"items\":[]"));

            var invalid = CloudProfileWritePayload.FromRegistration(
                " ", null, null, null, null, null);
            Assert.That(CloudProfileWritePayload.TryToJson(invalid, out _, out _), Is.False);
            var tooMany = CloudProfileWritePayload.FromRegistration(
                "김유리", "", "", "", "", new string[46]);
            Assert.That(CloudProfileWritePayload.TryToJson(tooMany, out _, out _), Is.False);
        }

        [Test]
        public void LocalModeCannotWriteCloudProfile()
        {
            CloudConnectionRuntime.BeginFromWelcome(false);
            Assert.That(CloudProfileApi.CanRequest, Is.False);
        }
    }
}
