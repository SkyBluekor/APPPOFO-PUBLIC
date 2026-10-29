using System;
using AppPofo.Presentation.FaceRecognition;
using NUnit.Framework;
using UnityEngine;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudFaceMatchCodecTests
    {
        [Serializable]
        private sealed class RequestData
        {
            public string request_id;
            public string model_version;
            public string embedding_b64url;
        }

        private const string ValidId = "11111111-1111-4111-8111-111111111111";

        [Test]
        public void UnitVector_SendsExactlyOneNormalized128DQuery_NotThreePoses()
        {
            var data = new float[128];
            data[0] = 1f;
            Assert.That(CloudFaceMatchCodec.TryCreatePayload(data, out string json), Is.True);
            var wire = JsonUtility.FromJson<RequestData>(json);
            Assert.That(Guid.TryParse(wire.request_id, out _), Is.True);
            Assert.That(wire.model_version, Is.EqualTo("sface_2021dec"));
            string base64 = wire.embedding_b64url.Replace('-', '+').Replace('_', '/');
            byte[] bytes = Convert.FromBase64String(base64 + new string('=', (4 - base64.Length % 4) % 4));
            Assert.That(bytes.Length, Is.EqualTo(128 * 4));
            Assert.That(BitConverter.ToSingle(bytes, 0), Is.EqualTo(1f));
        }

        [Test]
        public void InvalidVector_CannotCreateNetworkPayload()
        {
            Assert.That(CloudFaceMatchCodec.TryCreatePayload(null, out _), Is.False);
            Assert.That(CloudFaceMatchCodec.TryCreatePayload(new float[127], out _), Is.False);
            Assert.That(CloudFaceMatchCodec.TryCreatePayload(new float[128], out _), Is.False);
            var invalid = new float[128]; invalid[0] = float.NaN;
            Assert.That(CloudFaceMatchCodec.TryCreatePayload(invalid, out _), Is.False);
            invalid[0] = float.PositiveInfinity;
            Assert.That(CloudFaceMatchCodec.TryCreatePayload(invalid, out _), Is.False);
            invalid[0] = .5f;
            Assert.That(CloudFaceMatchCodec.TryCreatePayload(invalid, out _), Is.False);
        }

        [Test]
        public void PublicMatch_OnlyAcceptsUuidNameHeadlineAndKnownTestDecisionVersion()
        {
            const string good = "{\"matched\":true,\"profile\":{\"id\":\"" + ValidId +
                "\",\"display_name\":\"가상 사용자\",\"headline\":\"개발자\"}," +
                "\"decision_version\":\"local-synthetic-v1\"}";
            Assert.That(CloudFaceMatchCodec.TryReadResponse(good, out var response), Is.True);
            Assert.That(response.profile.id, Is.EqualTo(ValidId));
            Assert.That(response.profile.display_name, Is.EqualTo("가상 사용자"));
            Assert.That(response.profile.headline, Is.EqualTo("개발자"));
            Assert.That(CloudFaceMatchCodec.TryReadResponse("{\"matched\":false}",
                out var miss), Is.True);
            Assert.That(miss.profile, Is.Null);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(
                good.Replace(ValidId, "123"), out _), Is.False);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(
                good.Replace("local-synthetic-v1", "unknown"), out _), Is.False);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(
                "{\"matched\":true}", out _), Is.False);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(
                "{\"matched\":false,\"profile\":{\"id\":\"" + ValidId + "\"}}",
                out _), Is.False);
        }

        [Test]
        public void NoMatch_WireWithoutProfile_MustNotInventALocalOrCloudCard()
        {
            // JsonUtility can create empty inline [Serializable] objects for
            // missing JSON properties. The wire protocol omits 'profile' when
            // there is no match; deserialization must normalize it to null.
            const string noMatch = "{\"matched\":false}";
            Assert.That(CloudFaceMatchCodec.TryReadResponse(noMatch, out var result),
                Is.True);
            Assert.That(result.matched, Is.False);
            Assert.That(result.profile, Is.Null);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(
                "{ \"matched\" : false }", out var spaced), Is.True);
            Assert.That(spaced.profile, Is.Null);

            // A failed recognition response must not be allowed to carry a
            // stale profile, even if Unity deserializes it as a default object.
            Assert.That(CloudFaceMatchCodec.TryReadResponse(
                "{\"matched\":false,\"profile\":{}}", out _), Is.False);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(
                "{\"matched\":false,\"profile\":null}", out _), Is.False);
        }

        [Test]
        public void EditorLocalMatchRequiresExplicitApprovalAndDeviceSession()
        {
            // CI should never set local biometric opt-in variables. If a developer
            // deliberately opted in to the test, leave the environment untouched.
            bool optedIn = Environment.GetEnvironmentVariable(
                "APPPOFO_FACE_MATCH_LOCAL_EDITOR_ENABLED") == "enabled";
            if (optedIn) Assert.Ignore("Developer opted in to local face scan.");
            Assert.That(CloudFaceMatchApi.CanQuery, Is.False);
        }

        [TestCase(6486)] // Real incident: 8,648 base64 characters plus metadata.
        [TestCase(24576)] // Maximum permitted display thumbnail.
        public void MatchWithDisplayPhotoLargerThanLegacy4KiBLimitIsAccepted(int bytes)
        {
            string photo = Convert.ToBase64String(new byte[bytes]);
            string json = JsonUtility.ToJson(new CloudFaceMatchResponse {
                matched = true, decision_version = "production-v1",
                profile = new CloudFacePublicCard { id = ValidId, display_name = "Synthetic photo match",
                    headline = "Role", avatar_base64 = photo, business_card_theme = "AURORA" }
            });
            Assert.That(json.Length, Is.GreaterThan(4096));
            Assert.That(CloudFaceMatchCodec.TryReadResponse(json, out var result, "production-v1"), Is.True);
            Assert.That(result.matched, Is.True); Assert.That(result.profile.avatar_base64, Is.EqualTo(photo));
        }
        [Test]
        public void OversizedDisplayPhotoAndResponseEnvelopeAreStillRejected()
        {
            string json = JsonUtility.ToJson(new CloudFaceMatchResponse {
                matched = true, decision_version = "production-v1",
                profile = new CloudFacePublicCard { id = ValidId, display_name = "Synthetic oversized photo",
                    avatar_base64 = Convert.ToBase64String(new byte[24579]) }
            });
            Assert.That(CloudFaceMatchCodec.TryReadResponse(json, out _, "production-v1"), Is.False);
            Assert.That(CloudFaceMatchCodec.TryReadResponse(new string(' ',4096+32768)+"{}", out _, "production-v1"), Is.False);
        }
    }
}
