using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AppPofo.Presentation.FaceRegistration
{
    // Loopback-only development transport. No deployed APK/Windows Player can
    // enable this via a copied environment variable or a production admin token.
    public static class CloudFaceLocalTestFactory
    {
        public static IFaceEnrollmentWriter CreateOrPending(string cloudProfileId,
            bool publicRecognitionConsent, bool subjectPresent)
        {
#if UNITY_EDITOR_WIN
            // A discoverable face requires fresh, separate local-search consent.
            // The default private enrollment path does not need this flag.
            if (UnityEngine.Application.isEditor && subjectPresent &&
                (!publicRecognitionConsent ||
                    Environment.GetEnvironmentVariable(
                        "APPPOFO_FACE_LOCAL_DISCOVERABLE_APPROVED") ==
                        "YES_LOCAL_DISCOVERABLE_TEST") &&
                Environment.GetEnvironmentVariable("APPPOFO_FACE_LOCAL_CAPTURE_TEST_APPROVED") ==
                    "YES_LOCAL_BIOMETRIC_TEST" &&
                Environment.GetEnvironmentVariable("APPPOFO_FACE_LOCAL_CLIENT_ENABLED") == "enabled")
            {
                string token = Environment.GetEnvironmentVariable("APPPOFO_FACE_LOCAL_ADMIN_TOKEN");
                string policy = Environment.GetEnvironmentVariable("APPPOFO_FACE_LOCAL_POLICY_VERSION");
                if (!string.IsNullOrWhiteSpace(policy) && !string.IsNullOrEmpty(token) &&
                    token.Length >= 32)
                    return new CloudFaceEnrollmentWriter(cloudProfileId, policy,
                        publicRecognitionConsent, subjectPresent,
                        new CloudFaceLoopbackTransport(token));
            }
#endif
            // Safe default in Editor, Android, Windows Player and CI.
            return new PendingCloudFaceEnrollmentWriter(cloudProfileId);
        }

        public static bool IsLocalCaptureEnabled
        {
            get
            {
#if UNITY_EDITOR_WIN
                return UnityEngine.Application.isEditor &&
                    Environment.GetEnvironmentVariable("APPPOFO_FACE_LOCAL_CAPTURE_TEST_APPROVED") ==
                        "YES_LOCAL_BIOMETRIC_TEST" &&
                    Environment.GetEnvironmentVariable("APPPOFO_FACE_LOCAL_CLIENT_ENABLED") ==
                        "enabled" &&
                    !string.IsNullOrWhiteSpace(
                        Environment.GetEnvironmentVariable("APPPOFO_FACE_LOCAL_POLICY_VERSION")) &&
                    (Environment.GetEnvironmentVariable("APPPOFO_FACE_LOCAL_ADMIN_TOKEN")?.Length ?? 0)
                        >= 32;
#else
                return false;
#endif
            }
        }
    }

#if UNITY_EDITOR_WIN
    internal sealed class CloudFaceLoopbackTransport : ICloudFaceEnrollmentTransport
    {
        private const string LocalEndpoint = "http://127.0.0.1:8787/api/profiles/";
        private readonly string adminBearer;

        internal CloudFaceLoopbackTransport(string adminToken)
        {
            if (string.IsNullOrEmpty(adminToken) || adminToken.Length < 32)
                throw new ArgumentException("개발 서버 전용 관리자 토큰이 필요합니다.");
            adminBearer = "Bearer " + adminToken;
        }

        public IEnumerator GetStatus(string profileId, string requestId,
            Action<CloudFaceTransportResult, CloudFaceStatusData> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            string path = FacePath(profileId) +
                (string.IsNullOrEmpty(requestId) ? "" :
                    "?request_id=" + UnityWebRequest.EscapeURL(requestId));
            using (var request = UnityWebRequest.Get(path))
            {
                SetHeaders(request);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success ||
                    request.responseCode != 200)
                {
                    completed(Classify(request, false), null);
                    yield break;
                }
                CloudFaceStatusEnvelope envelope = null;
                try { envelope = JsonUtility.FromJson<CloudFaceStatusEnvelope>(
                    request.downloadHandler.text); }
                catch (ArgumentException) { }
                if (envelope?.data == null)
                    completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Unknown), null);
                else
                    completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Success),
                        envelope.data);
            }
        }

        public IEnumerator Enroll(string profileId, string json,
            Action<CloudFaceTransportResult, CloudFaceAckData> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            using (var request = new UnityWebRequest(FacePath(profileId), "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                SetHeaders(request);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success ||
                    (request.responseCode != 200 && request.responseCode != 201))
                {
                    completed(Classify(request, true), null);
                    yield break;
                }
                CloudFaceAckEnvelope envelope = null;
                try { envelope = JsonUtility.FromJson<CloudFaceAckEnvelope>(
                    request.downloadHandler.text); }
                catch (ArgumentException) { }
                if (envelope?.data == null)
                    completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Unknown), null);
                else
                    completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Success),
                        envelope.data);
            }
        }

        private void SetHeaders(UnityWebRequest request)
        {
            request.SetRequestHeader("Authorization", adminBearer);
            request.redirectLimit = 0;
            request.timeout = 8;
        }

        private static string FacePath(string profileId)
        {
            if (!Guid.TryParse(profileId, out Guid parsed))
                throw new ArgumentException("서버 프로필 ID가 올바르지 않습니다.");
            return LocalEndpoint + parsed.ToString("D") + "/face";
        }

        private static CloudFaceTransportResult Classify(UnityWebRequest request,
            bool wasUpload)
        {
            if (request.responseCode == 401 || request.responseCode == 403 ||
                request.responseCode == 404 || request.responseCode == 409 ||
                request.responseCode == 400 || request.responseCode == 422 ||
                request.responseCode == 413 || request.responseCode == 415 ||
                request.responseCode == 429)
                return new CloudFaceTransportResult(CloudFaceTransportStatus.Rejected);
            if (request.responseCode == 503)
                return new CloudFaceTransportResult(CloudFaceTransportStatus.Unavailable);
            // Once an upload has been dispatched, a missing reply, 5xx or timeout
            // cannot establish whether the server committed the request.
            return new CloudFaceTransportResult(
                wasUpload ? CloudFaceTransportStatus.Unknown :
                    CloudFaceTransportStatus.Unavailable);
        }
    }
#endif
}
