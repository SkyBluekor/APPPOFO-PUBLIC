using System;
using System.Collections;
using AppPofo.Presentation.Common;
using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration
{
    public sealed class CloudFaceHttpsTransport : ICloudFaceEnrollmentTransport
    {
        private int consentGeneration = -1;
        private readonly CloudFaceStatusData approvedSnapshot;
        private readonly string approvedProfileId;
        public CloudFaceHttpsTransport(CloudFaceStatusData approvedSnapshot = null, string approvedProfileId = null)
        {
            if (approvedSnapshot == null) return;
            if (!Guid.TryParse(approvedProfileId, out _)) throw new ArgumentException("Approval target UUID required.");
            this.approvedProfileId = approvedProfileId;
            this.approvedSnapshot = new CloudFaceStatusData {
                revision=approvedSnapshot.revision, purge_epoch=approvedSnapshot.purge_epoch,
                consent_generation=approvedSnapshot.consent_generation, registered=approvedSnapshot.registered,
                storage_consent=approvedSnapshot.storage_consent, discoverable=approvedSnapshot.discoverable,
                model_version=approvedSnapshot.model_version };
            consentGeneration = approvedSnapshot.consent_generation;
        }
        [Serializable] private sealed class DeleteBody { public string request_id; public int expected_revision, purge_epoch; }
        [Serializable] private sealed class DiscoveryBody { public bool discoverable; public int expected_revision, purge_epoch; }
        private static string PathFor(string id)
        {
            if (!Guid.TryParse(id, out _)) throw new ArgumentException("Cloud UUID required.");
            return (CloudProfileApi.IsAdministrator ? "/api/profiles/" : "/api/me/profiles/") + id + "/face";
        }
        public IEnumerator GetStatus(string profileId, string requestId,
            Action<CloudFaceTransportResult, CloudFaceStatusData> completed)
        {
            // Freeze revision, purge epoch and consent generation when the subject
            // approves capture, not after a long three-pose collection finishes.
            if (requestId == null && approvedSnapshot != null)
            {
                bool sameTarget=string.Equals(profileId,approvedProfileId,StringComparison.OrdinalIgnoreCase);
                completed(new CloudFaceTransportResult(sameTarget ? CloudFaceTransportStatus.Success :
                    CloudFaceTransportStatus.Rejected), sameTarget ? approvedSnapshot : null);
                yield break;
            }
            string path = PathFor(profileId) + (requestId == null ? "" : "?request_id=" + Uri.EscapeDataString(requestId));
            yield return CloudFaceRuntime.Send(path, "GET", null, false, (result, body) =>
            {
                CloudFaceStatusData data = null;
                if (result.Status == CloudFaceTransportStatus.Success)
                    try { data = JsonUtility.FromJson<CloudFaceStatusEnvelope>(body)?.data; } catch (ArgumentException) { }
                if (data != null && approvedSnapshot == null) consentGeneration = data.consent_generation;
                completed(data != null ? result : new CloudFaceTransportResult(result.Status == CloudFaceTransportStatus.Success
                    ? CloudFaceTransportStatus.Unknown : result.Status), data);
            });
        }
        public IEnumerator Enroll(string profileId, string json,
            Action<CloudFaceTransportResult, CloudFaceAckData> completed)
        {
            if (approvedProfileId != null && !string.Equals(profileId, approvedProfileId, StringComparison.OrdinalIgnoreCase))
            { completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Rejected), null); yield break; }
            if (consentGeneration < 0)
            { completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Unavailable), null); yield break; }
            string generation = consentGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
            yield return CloudFaceRuntime.Send(PathFor(profileId), "POST", json, false, (result, body) =>
            {
                CloudFaceAckData data = null;
                if (result.Status == CloudFaceTransportStatus.Success)
                    try { data = JsonUtility.FromJson<CloudFaceAckEnvelope>(body)?.data; } catch (ArgumentException) { }
                completed(result, data);
            }, consentGeneration: generation);
        }
        public IEnumerator Remove(string id, bool publicRecognitionOnly, Action<string> completed)
        {
            CloudFaceStatusData state = null;
            yield return GetStatus(id, null, (_, value) => state = value);
            if (state == null) { completed("현재 얼굴 등록 상태를 확인하지 못했습니다."); yield break; }
            string json = publicRecognitionOnly ? JsonUtility.ToJson(new DiscoveryBody {
                expected_revision = state.revision, purge_epoch = state.purge_epoch, discoverable = false }) :
                JsonUtility.ToJson(new DeleteBody { request_id = Guid.NewGuid().ToString("D"),
                    expected_revision = state.revision, purge_epoch = state.purge_epoch });
            yield return CloudFaceRuntime.Send(PathFor(id), publicRecognitionOnly ? "PATCH" : "DELETE", json,
                false, (_, __) => { });
            CloudFaceStatusData verified = null;
            yield return GetStatus(id, null, (_, value) => verified = value);
            bool confirmed = verified != null && !verified.discoverable &&
                (publicRecognitionOnly || (!verified.registered &&
                    verified.revision == state.revision + 1 && verified.purge_epoch == state.purge_epoch));
            completed(confirmed ? null : "변경 결과를 확인하지 못했습니다. 상태를 새로 확인해 주세요.");
        }
    }
}
