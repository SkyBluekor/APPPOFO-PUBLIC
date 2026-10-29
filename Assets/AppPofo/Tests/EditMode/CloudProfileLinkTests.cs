using AppPofo.Presentation.Common;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudProfileLinkTests
    {
        private const string Id="11111111-1111-4111-8111-111111111111";
        private const string Code="applink_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdef";

        [Test]
        public void CodeFormat_RequiresServerGeneratedLengthAndAlphabet()
        {
            Assert.That(CloudProfileLinkApi.ValidCode(Code), Is.True);
            Assert.That(CloudProfileLinkApi.ValidCode("123456"), Is.False);
            Assert.That(CloudProfileLinkApi.ValidCode("applink_" + new string(' ',32)), Is.False);
            Assert.That(CloudProfileLinkApi.ValidCode(Code + "x"), Is.False);
        }

        [Test]
        public void IssuedCode_RequiresMatchingUuidAndValidCode()
        {
            string body="{\"data\":{\"profile_id\":\""+Id+"\",\"link_code\":\""+Code+"\"}}";
            Assert.That(CloudProfileLinkApi.TryReadCode(body,Id,out var code), Is.True);
            Assert.That(code,Is.EqualTo(Code));
            Assert.That(CloudProfileLinkApi.TryReadCode(body,"22222222-2222-4222-8222-222222222222",out _),Is.False);
        }

        [Test]
        public void ConnectionResponse_RequiresCloudUuid()
        {
            Assert.That(CloudProfileLinkApi.TryReadConnectedId(
                "{\"data\":{\"profile_id\":\""+Id+"\"}}",out var id),Is.True);
            Assert.That(id,Is.EqualTo(Id));
            Assert.That(CloudProfileLinkApi.TryReadConnectedId(
                "{\"data\":{\"profile_id\":\"nope\"}}",out _),Is.False);
        }
    }
}
