using System;
using System.Linq;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Presentation.Common;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class ProfileDetailViewModelTests
    {
        [Test] public void CloudUuidAndItemsRemainSeparateFromSqliteIdentity()
        {
            string id = Guid.NewGuid().ToString();
            var model = ProfileDetailViewModel.FromCloud(new CloudProfileDetail {
                id = id, display_name = "<b>개발자</b>", items = new[] {
                    new CloudProfileItem { item_type = "LINK", title = "링크", url = "https://example.com", sort_order = 3 },
                    new CloudProfileItem { item_type = "PROJECT", title = "프로젝트", description = new string('가', 3000), sort_order = 2 },
                    new CloudProfileItem { item_type = "SKILL", title = "C#", sort_order = 1 },
                    null, new CloudProfileItem { item_type = "UNKNOWN" }
                }
            });
            Assert.That(model.CloudId, Is.EqualTo(id)); Assert.That(model.LocalId, Is.Null);
            Assert.That(model.DisplayName, Is.EqualTo("<b>개발자</b>"));
            Assert.That(model.Items.Select(item => item.Type), Is.EqualTo(new[] { ProfileItemType.Skill, ProfileItemType.Project, ProfileItemType.Link }));
            Assert.That(model.Items.All(item => item.LocalItem == null), Is.True);
            Assert.That(model.Items[1].Description.Length, Is.EqualTo(3000));
            Assert.That(model.Items[2].Url, Is.EqualTo("https://example.com"));
        }
        [Test] public void LocalAdapterPreservesOriginalItemForLocalCommands()
        {
            var item = new ProfileItem(42, 901, ProfileItemType.Skill, "Unity", "", "", 0);
            var profile = new Profile(901, "로컬", "개발자", "소개", "", "", "", true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new[] { item });
            var model = ProfileDetailViewModel.FromLocal(profile);
            Assert.That(model.LocalId, Is.EqualTo(901)); Assert.That(model.CloudId, Is.Null);
            Assert.That(model.Items[0].LocalItem, Is.SameAs(item));
        }
        [Test] public void EmptyCloudFieldsAndNullCollectionsAreDisplayable()
        {
            var model = ProfileDetailViewModel.FromCloud(new CloudProfileDetail { id = Guid.NewGuid().ToString() });
            Assert.That(model.Items, Is.Empty); Assert.That(model.Bio, Is.Empty);
            Assert.That(model.Email, Is.Empty); Assert.That(model.Website, Is.Empty);
        }
        [TestCase("901")] [TestCase("")] [TestCase(null)]
        public void CloudAdapterRejectsLocalOrInvalidIdentity(string id) =>
            Assert.Throws<ArgumentException>(() => ProfileDetailViewModel.FromCloud(new CloudProfileDetail { id = id }));
        [TestCase("https://example.com/path", true)] [TestCase("http://example.com", true)]
        [TestCase("javascript:alert(1)", false)] [TestCase("file:///C:/secret", false)] [TestCase("", false)]
        public void LinksOnlyOpenHttpAndHttps(string url, bool allowed) =>
            Assert.That(ProfileDetailViewModel.CanOpenUrl(url), Is.EqualTo(allowed));
    }
}
