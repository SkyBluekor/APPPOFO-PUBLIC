using System;
using System.Collections;
using AppPofo.Presentation.FaceRegistration.Embedding;

namespace AppPofo.Presentation.FaceRegistration
{
    // CAS/idempotency contract shared by explicit local tests and staging HTTPS.
    // Remote enrollment is gated separately; production biometrics remain disabled.
    public enum CloudFaceTransportStatus { Success, Rejected, Unknown, Unavailable }

    public sealed class CloudFaceTransportResult
    {
        public CloudFaceTransportStatus Status { get; }
        public string Message { get; }

        public CloudFaceTransportResult(CloudFaceTransportStatus status, string message = null)
        {
            Status = status;
            Message = message ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class CloudFaceStatusData
    {
        public bool registered;
        public int revision;
        public int purge_epoch;
        public int consent_generation;
        public string model_version;
        public bool storage_consent;
        public bool discoverable;
        public string request_status;
        public int request_revision;
    }

    [Serializable]
    public sealed class CloudFaceAckData
    {
        public bool registered;
        public int revision;
        public string model_version;
        public string request_id;
    }

    [Serializable]
    internal sealed class CloudFaceStatusEnvelope { public CloudFaceStatusData data; }

    [Serializable]
    internal sealed class CloudFaceAckEnvelope { public CloudFaceAckData data; }

    [Serializable]
    internal sealed class CloudFaceConsentPayload
    {
        public string policy_version;
        public bool storage_opt_in;
        public bool discoverable_opt_in;
        public long confirmed_at;
        public bool subject_present;
    }

    [Serializable]
    internal sealed class CloudFaceEnrollmentPayload
    {
        public string request_id;
        public int expected_revision;
        public int purge_epoch;
        public string model_version;
        public string embeddings_b64url;
        public CloudFaceConsentPayload consent;
    }

    public interface ICloudFaceEnrollmentTransport
    {
        IEnumerator GetStatus(string profileId, string requestId,
            Action<CloudFaceTransportResult, CloudFaceStatusData> completed);

        IEnumerator Enroll(string profileId, string json,
            Action<CloudFaceTransportResult, CloudFaceAckData> completed);
    }

    // The writer can be unit-tested with a fake transport. It never creates a
    // local profile or saves a biometric in SQLite as a network fallback.
    public sealed class CloudFaceEnrollmentWriter : IFaceEnrollmentWriter
    {
        private readonly string cloudProfileId;
        private readonly string policyVersion;
        private readonly bool discoverable;
        private readonly bool subjectPresent;
        private readonly ICloudFaceEnrollmentTransport transport;

        public CloudFaceEnrollmentWriter(string cloudProfileId, string policyVersion,
            bool discoverable, bool subjectPresent, ICloudFaceEnrollmentTransport transport)
        {
            if (!Guid.TryParse(cloudProfileId, out _))
                throw new ArgumentException("올바른 클라우드 UUID가 필요합니다.", nameof(cloudProfileId));
            if (string.IsNullOrWhiteSpace(policyVersion))
                throw new ArgumentException("서버의 동의 정책 버전이 필요합니다.", nameof(policyVersion));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.cloudProfileId = cloudProfileId;
            this.policyVersion = policyVersion;
            this.discoverable = discoverable;
            this.subjectPresent = subjectPresent;
        }

        public IEnumerator Save(FaceEmbeddingSet embeddings,
            Action<FaceEnrollmentSaveResult> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            if (embeddings == null || embeddings.ModelVersion != SFaceRecognizer.ModelVersion)
            {
                completed(new FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus.Failed,
                    "지원하지 않는 얼굴 모델입니다. 얼굴정보는 전송하지 않았습니다."));
                yield break;
            }

            CloudFaceTransportResult stateResult = null;
            CloudFaceStatusData state = null;
            yield return transport.GetStatus(cloudProfileId, null, (result, data) =>
            {
                stateResult = result;
                state = data;
            });
            if (stateResult == null || stateResult.Status != CloudFaceTransportStatus.Success ||
                state == null || state.revision < 0 || state.purge_epoch < 0)
            {
                completed(new FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus.Unavailable,
                    "서버에서 기존 얼굴 등록 상태를 확인할 수 없습니다. 전송하지 않았습니다."));
                yield break;
            }

            // Reuse the same request ID for receipt lookup; never auto-resend
            // the sensitive POST when the server's result is unknown.
            string requestId = Guid.NewGuid().ToString("D");
            var payload = new CloudFaceEnrollmentPayload
            {
                request_id = requestId,
                expected_revision = state.revision,
                purge_epoch = state.purge_epoch,
                model_version = embeddings.ModelVersion,
                embeddings_b64url = FaceEmbeddingTransportCodec.ToBase64Url(embeddings),
                consent = new CloudFaceConsentPayload
                {
                    policy_version = policyVersion,
                    storage_opt_in = true,
                    discoverable_opt_in = discoverable,
                    confirmed_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    subject_present = subjectPresent
                }
            };
            string json = UnityEngine.JsonUtility.ToJson(payload);
            CloudFaceTransportResult sendResult = null;
            CloudFaceAckData ack = null;
            yield return transport.Enroll(cloudProfileId, json, (result, data) =>
            {
                sendResult = result;
                ack = data;
            });

            if (sendResult != null && sendResult.Status == CloudFaceTransportStatus.Success)
            {
                if (ack != null && ack.registered && ack.revision == state.revision + 1 &&
                    string.Equals(ack.request_id, requestId, StringComparison.OrdinalIgnoreCase) &&
                    ack.model_version == embeddings.ModelVersion)
                {
                    completed(new FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus.Success,
                        "서버 얼굴 등록이 완료됐습니다."));
                    yield break;
                }
                // A malformed acknowledgement cannot establish success.
                sendResult = new CloudFaceTransportResult(CloudFaceTransportStatus.Unknown);
            }

            if (sendResult != null && sendResult.Status == CloudFaceTransportStatus.Rejected)
            {
                completed(new FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus.Failed,
                    "서버에서 등록을 거절했습니다. 권한·동의·등록 상태를 확인해 주세요."));
                yield break;
            }
            if (sendResult != null && sendResult.Status == CloudFaceTransportStatus.Unavailable)
            {
                completed(new FaceEnrollmentSaveResult(FaceEnrollmentSaveStatus.Unavailable,
                    "서버가 준비되지 않았습니다. 얼굴정보 저장을 확인할 수 없습니다."));
                yield break;
            }

            CloudFaceTransportResult receiptResult = null;
            CloudFaceStatusData receipt = null;
            yield return transport.GetStatus(cloudProfileId, requestId, (result, data) =>
            {
                receiptResult = result;
                receipt = data;
            });
            bool confirmed = receiptResult != null &&
                receiptResult.Status == CloudFaceTransportStatus.Success &&
                receipt != null && receipt.registered && receipt.revision == state.revision + 1 &&
                receipt.request_status == "COMMITTED" &&
                receipt.request_revision == receipt.revision &&
                receipt.model_version == embeddings.ModelVersion;
            completed(new FaceEnrollmentSaveResult(
                confirmed ? FaceEnrollmentSaveStatus.Success : FaceEnrollmentSaveStatus.Unknown,
                confirmed ? "서버 저장 기록을 조회해 얼굴 등록을 확인했습니다." :
                    "서버 저장 결과를 확인하지 못했습니다. 중복 등록하지 말고 상태를 다시 확인해 주세요."));
        }
    }
}
