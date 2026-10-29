using AppPofo.Presentation.Common;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudProfileJsonTests
    {
        [Test]
        public void ServerPage_ParsesCloudUuidInsteadOfLocalLongId()
        {
            const string json =
                "{\"data\":[{\"id\":\"11111111-1111-4111-8111-111111111111\"," +
                "\"display_name\":\"김유리\",\"headline\":\"Unity 개발자\"}]," +
                "\"total\":1,\"limit\":12,\"offset\":0}";
            Assert.That(CloudProfileJson.TryReadPage(json, out var page), Is.True);
            Assert.That(page.data.Length, Is.EqualTo(1));
            Assert.That(page.data[0].id, Is.EqualTo("11111111-1111-4111-8111-111111111111"));
            Assert.That(page.data[0].display_name, Is.EqualTo("김유리"));
        }

        [Test]
        public void EmptyServerPage_DoesNotFallBackToSqlite()
        {
            const string json = "{\"data\":[],\"total\":0,\"limit\":12,\"offset\":0}";
            Assert.That(CloudProfileJson.TryReadPage(json, out var page), Is.True);
            Assert.That(page.total, Is.Zero);
            Assert.That(page.data, Is.Empty);
        }

        [TestCase("")]
        [TestCase("{}")]
        [TestCase("{\"data\":[],\"total\":-1,\"limit\":12,\"offset\":0}")]
        [TestCase("{\"data\":[{\"id\":\"not-a-uuid\",\"display_name\":\"A\"}],\"total\":1,\"limit\":12,\"offset\":0}")]
        public void MalformedPages_AreRejectedWithoutLocalFallback(string json)
        {
            Assert.That(CloudProfileJson.TryReadPage(json, out _), Is.False);
        }

        [Test]
        public void DetailParsesTextItemsAndRejectsMissingBody()
        {
            const string body =
                "{\"data\":{\"id\":\"11111111-1111-4111-8111-111111111111\"," +
                "\"display_name\":\"김유리\",\"email\":\"demo@example.invalid\"," +
                "\"items\":[{\"item_type\":\"SKILL\",\"title\":\"Unity\",\"sort_order\":0}]}}";
            Assert.That(CloudProfileJson.TryReadDetail(body, out var detail), Is.True);
            Assert.That(detail.email, Is.EqualTo("demo@example.invalid"));
            Assert.That(detail.items[0].title, Is.EqualTo("Unity"));
            Assert.That(CloudProfileJson.TryReadDetail("{\"data\":null}", out _), Is.False);
        }

        [Test]
        public void CloudModeDoesNotGrantReadAccessWithoutAuthentication()
        {
            Assert.That(CloudProfileJson.CanReadCloudProfiles(ProfileDataMode.Cloud, false), Is.False);
            Assert.That(CloudProfileJson.CanReadCloudProfiles(ProfileDataMode.Cloud, true), Is.True);
            Assert.That(CloudProfileJson.CanReadCloudProfiles(ProfileDataMode.Local, true), Is.False);
        }

        [Test]
        public void CloudRequestsAreDisabledWhenLocalModeIsChosen()
        {
            CloudConnectionRuntime.BeginFromWelcome(false);
            Assert.That(CloudProfileApi.PageSize, Is.EqualTo(12));
            Assert.That(CloudProfileApi.CanRequest, Is.False,
                "Local mode must never send administrator or device requests.");
            Assert.That(CloudModeSession.AccessRole, Is.EqualTo(CloudAccessRole.None),
                "Local mode always clears any previously granted administrator access.");
            Assert.That(CloudProfileApi.IsAdministrator, Is.False);
        }
    }
}
