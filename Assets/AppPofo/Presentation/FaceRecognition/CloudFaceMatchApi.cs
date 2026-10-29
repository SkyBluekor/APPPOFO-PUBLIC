using System;
using System.Collections;
using System.Text;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.FaceRegistration.Embedding;
using UnityEngine;
using UnityEngine.Networking;

namespace AppPofo.Presentation.FaceRecognition
{
    // Remote queries use signed device sessions and explicit scan consent.
    // Legacy loopback tests remain Windows Editor opt-in only; no production faces.
    public enum CloudFaceMatchStatus { Matched, NoMatch, NotReady, Rejected, Unavailable }

    [Serializable]
    public sealed class CloudFacePublicCard
    {
        public string id;
        public string display_name;
        public string headline;
        public string avatar_base64;
        public string business_card_theme;
    }

    [Serializable]
    public sealed class CloudFaceMatchResponse
    {
        public bool matched;
        public CloudFacePublicCard profile;
        public string decision_version;
    }

    [Serializable]
    internal sealed class CloudFaceMatchPayload
    {
        public string request_id;
        public string model_version;
        public string embedding_b64url;
    }

    public static class CloudFaceMatchCodec
    {
        public const int Dimension = 128;
        public const string TestDecisionVersion = "local-synthetic-v1";

        public static bool TryCreatePayload(float[] vector, out string json)
        {
            json = null;
            if (vector == null || vector.Length != Dimension) return false;
            double norm = 0d;
            byte[] bytes = new byte[Dimension * sizeof(float)];
            for (int i = 0; i < vector.Length; i++)
            {
                float value = vector[i];
                if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 1f)
                    return false;
                norm += (double)value * value;
                byte[] packed = BitConverter.GetBytes(value);
                if (!BitConverter.IsLittleEndian) Array.Reverse(packed);
                Buffer.BlockCopy(packed, 0, bytes, i * sizeof(float), sizeof(float));
            }
            if (Math.Abs(Math.Sqrt(norm) - 1d) > .001d) return false;
            try
            {
                json = JsonUtility.ToJson(new CloudFaceMatchPayload
                {
                    request_id = Guid.NewGuid().ToString("D"),
                    model_version = SFaceRecognizer.ModelVersion,
                    embedding_b64url = Convert.ToBase64String(bytes)
                        .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                });
                return true;
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        public static bool TryReadResponse(string json, out CloudFaceMatchResponse response,
            string expectedDecisionVersion = TestDecisionVersion)
        {
            response = null;
            // Allow the bounded display thumbnail as well as the existing metadata budget.
            if (string.IsNullOrEmpty(json) || json.Length > 4096 + CloudProfileAvatar.MaxBase64Characters) return false;
            try
            {
                var parsed = JsonUtility.FromJson<CloudFaceMatchResponse>(json);
                if (parsed == null || !json.Contains("\"matched\"")) return false;
                if (parsed.matched)
                {
                    if (parsed.profile == null ||
                        !Guid.TryParse(parsed.profile.id, out _) ||
                        string.IsNullOrWhiteSpace(parsed.profile.display_name) ||
                        parsed.profile.display_name.Length > 100 ||
                        (parsed.profile.headline != null && parsed.profile.headline.Length > 200) ||
                        (parsed.profile.avatar_base64 != null && parsed.profile.avatar_base64.Length > CloudProfileAvatar.MaxBase64Characters) ||
                        parsed.decision_version != expectedDecisionVersion) return false;
                }
                else
                {
                    // Unity JsonUtility may instantiate an inline [Serializable]
                    // class even when its key is absent from the JSON payload.
                    // Judge the actual wire key, not the deserialized reference.
                    // A NO_MATCH result must never carry any profile data.
                    if (json.Contains("\"profile\"")) return false;
                    parsed.profile = null;
                }
                response = parsed;
                return true;
            }
            catch (ArgumentException) { return false; }
        }
    }

    public static class CloudFaceMatchApi
    {
        private const string LocalMatchUrl = "http://127.0.0.1:8787/api/me/face/match";
        private static readonly CloudFaceRequestBudget requestBudget = new CloudFaceRequestBudget();

        // Explicit consent is handled by the authenticated Worker scan-consent API,
        // NOT silently granted here. The token must be a local signed-device session.
        public static bool CanQuery => QueryReadiness == "Ready";

        // Fixed diagnostic reasons only; never return any credential value.
        public static string QueryReadiness
        {
            get
            {
                if (CloudFaceRuntime.CanScan) return "Ready";
                if (CloudServerConfiguration.IsStaging)
                    return CloudFaceRuntime.Available ? "ScanConsentRequired" : "StagingUnavailable";
                if (CloudServerConfiguration.IsProduction && CloudServerConfiguration.RemoteFacesEnabled)
                    return CloudFaceRuntime.Available ? "ScanConsentRequired" : "ProductionUnavailable";
#if UNITY_EDITOR_WIN
                if (!UnityEngine.Application.isEditor) return "EditorRequired";
                if (CloudModeSession.Mode != ProfileDataMode.Cloud) return "CloudModeRequired";
                if (Environment.GetEnvironmentVariable("APPPOFO_FACE_MATCH_LOCAL_EDITOR_ENABLED") != "enabled")
                    return "LocalPermissionDisabled";
                if (Environment.GetEnvironmentVariable("APPPOFO_FACE_MATCH_LOCAL_EDITOR_APPROVED") != "YES_LOCAL_FACE_QUERY")
                    return "LocalConsentRequired";
                return TryQueryBearer(out _) ? "Ready" : "DeviceSessionInvalid";
#else
                return "WindowsEditorRequired";
#endif
            }
        }

        private static bool TryQueryBearer(out string bearer)
        {
            bearer = null;
            if (CloudFaceRuntime.CanScan)
            {
                bearer = "Bearer " + CloudDeviceAuthenticator.AccessToken;
                return true;
            }
#if UNITY_EDITOR_WIN
            string token = Environment.GetEnvironmentVariable("APPPOFO_FACE_LOCAL_DEVICE_TOKEN");
            if (string.IsNullOrEmpty(token) || token.Length != 50 ||
                !token.StartsWith("appdev_", StringComparison.Ordinal)) return false;
            for (int i = 7; i < token.Length; i++)
            {
                char c = token[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '-' && c != '_') return false;
            }
            bearer = "Bearer " + token;
            return true;
#else
            return false;
#endif
        }

        public static IEnumerator Match(float[] vector,
            Action<CloudFaceMatchStatus, CloudFacePublicCard> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            if (!CanQuery || !TryQueryBearer(out string bearer))
            {
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.NOT_READY);
                completed(CloudFaceMatchStatus.NotReady, null);
                yield break;
            }
            if (!CloudFaceMatchCodec.TryCreatePayload(vector, out string json))
            {
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.PayloadRejected);
                completed(CloudFaceMatchStatus.NotReady, null);
                yield break;
            }
            bool remote = CloudFaceRuntime.CanScan;
            string endpoint = remote ? CloudServerConfiguration.BaseUrl + "/api/me/face/match" : LocalMatchUrl;
            double dispatchAt = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
            if (!requestBudget.TryReserve(dispatchAt))
            {
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.REJECTED);
                completed(CloudFaceMatchStatus.Rejected, null);
                yield break;
            }
            using (var request = new UnityWebRequest(endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", bearer);
                request.redirectLimit = 0;
                request.timeout = 4;
                double startedAt = Time.realtimeSinceStartupAsDouble;
                // SendWebRequest can throw before any HTTP request (e.g. transport policy).
                UnityWebRequestAsyncOperation operation = null;
                try { operation = request.SendWebRequest(); }
                catch (Exception) { /* Report fixed metadata below, never exception text. */ }
                if (operation == null)
                {
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.UNAVAILABLE);
                    if (remote) CloudFaceRuntime.StopScanning();
                    completed(CloudFaceMatchStatus.Unavailable, null);
                    yield break;
                }
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.RequestSent);
                yield return operation;
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.HttpCompleted,
                    durationMs: (Time.realtimeSinceStartupAsDouble - startedAt) * 1000d,
                    httpStatus: request.responseCode);

                if (!CanQuery || !TryQueryBearer(out string current) ||
                    current != bearer || (remote && !CloudFaceRuntime.CanScan))
                {
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.NOT_READY);
                    completed(CloudFaceMatchStatus.NotReady, null);
                    yield break;
                }
                if (request.responseCode == 401 || request.responseCode == 403 ||
                    request.responseCode == 429 || request.responseCode == 400 ||
                    request.responseCode == 404 || request.responseCode == 413 ||
                    request.responseCode == 415 || request.responseCode == 422)
                {
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.REJECTED,
                        httpStatus: request.responseCode);
                    if (remote) CloudFaceRuntime.StopScanning();
                    completed(CloudFaceMatchStatus.Rejected, null);
                    yield break;
                }
                if (request.result != UnityWebRequest.Result.Success ||
                    request.responseCode != 200)
                {
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.UNAVAILABLE);
                    if (remote) CloudFaceRuntime.StopScanning();
                    completed(CloudFaceMatchStatus.Unavailable, null);
                    yield break;
                }
                if (!CloudFaceMatchCodec.TryReadResponse(
                    request.downloadHandler.text, out var response, remote ?
                        CloudFaceRuntime.Config.decision_version : CloudFaceMatchCodec.TestDecisionVersion))
                {
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.ResponseInvalid);
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.UNAVAILABLE);
                    if (remote) CloudFaceRuntime.StopScanning();
                    completed(CloudFaceMatchStatus.Unavailable, null);
                    yield break;
                }
                CloudFaceScanDiagnostics.Record(response.matched ? CloudFaceScanStage.MATCH :
                    CloudFaceScanStage.NO_MATCH);
                completed(response.matched ? CloudFaceMatchStatus.Matched :
                    CloudFaceMatchStatus.NoMatch, response.profile);
            }
        }
    }
}
