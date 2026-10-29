using System;
using System.Collections;
using System.Text;
using AppPofo.Presentation.FaceRegistration;
using UnityEngine;
using UnityEngine.Networking;

namespace AppPofo.Presentation.Common
{
    public static class CloudFaceRuntime
    {
        [Serializable] public sealed class Configuration
        {
            public string enrollment_policy, scan_policy, model_version, decision_version, environment;
        }
        [Serializable] private sealed class ScanConsent { public bool consent = true; public string policy_version; }
        [Serializable] private sealed class DeviceAuthorization { public string device_id; }
        [Serializable] private sealed class SavedScanConsent { public bool approved; public string policy_version; }
        public static Configuration Config { get; private set; }
        public static bool ScanApproved { get; private set; }
        public static bool Available => CloudServerConfiguration.RemoteFacesEnabled &&
            CloudProfileApi.CanRequest && Config != null;
        public static bool CanScan => Available && ScanApproved && CloudDeviceAuthenticator.HasUsableToken;
        public static void Reset() { Config = null; ScanApproved = false; }
        public static void StopScanning() => ScanApproved = false;

        internal static bool AcceptsConfiguration(Configuration config) =>
            CloudServerConfiguration.RemoteFacesEnabled && config != null &&
            config.environment == CloudServerConfiguration.EnvironmentName && config.model_version == "sface_2021dec" &&
            config.decision_version == CloudServerConfiguration.FaceDecisionVersion &&
            !string.IsNullOrWhiteSpace(config.enrollment_policy) && config.enrollment_policy.Length <= 100 &&
            !string.IsNullOrWhiteSpace(config.scan_policy) && config.scan_policy.Length <= 100;

        public static IEnumerator LoadConfiguration()
        {
            Config = null;
            ScanApproved = false;
            if (!CloudServerConfiguration.RemoteFacesEnabled) yield break;
            yield return Send(CloudProfileApi.IsAdministrator ? "/api/face/config" : "/api/me/face/config",
                "GET", null, false, (result, body) =>
                {
                    if (result.Status != CloudFaceTransportStatus.Success) return;
                    Configuration config = null;
                    try { config = JsonUtility.FromJson<Configuration>(body); } catch (ArgumentException) { }
                    if (AcceptsConfiguration(config)) Config = config;
                });
            if (Config != null) yield return LoadScanConsent();
        }
        // The signed device's server grant persists across camera close and app restart.
        // Never infer consent from a local preference or grant it while reading status.
        public static IEnumerator LoadScanConsent(Action<string> completed = null)
        {
            ScanApproved = false;
            if (!Available || !CloudDeviceAuthenticator.HasUsableToken)
            { completed?.Invoke("서버 인증 상태를 확인해 주세요."); yield break; }
            string policy = Config.scan_policy;
            string error = "저장된 검색 동의를 확인하지 못했습니다. 다시 시도해 주세요.";
            yield return Send("/api/me/face/scan-consent", "GET", null, true, (result, body) =>
            {
                if (result.Status != CloudFaceTransportStatus.Success) return;
                if (RestoreScanConsent(body, policy)) error = null;
            });
            completed?.Invoke(error);
        }
        internal static bool RestoreScanConsent(string body, string requestedPolicy)
        {
            ScanApproved = false;
            SavedScanConsent state = null;
            try { state = JsonUtility.FromJson<SavedScanConsent>(body); } catch (ArgumentException) { }
            if (!Available || !CloudDeviceAuthenticator.HasUsableToken || state == null ||
                Config.scan_policy != requestedPolicy || state.policy_version != requestedPolicy) return false;
            ScanApproved = state.approved; return true;
        }
        public static IEnumerator SetScanConsent(bool captureConsent, bool scanConsent, Action<string> completed)
        {
            ScanApproved = false;
            if (!Available || !CloudDeviceAuthenticator.HasUsableToken || !captureConsent || !scanConsent)
            { completed("서버 인증과 촬영·검색 이용 동의를 확인해 주세요."); yield break; }
            // Admin approval authorizes this signed device, not the shared bearer as a scanner.
            if (CloudProfileApi.IsAdministrator)
            {
                bool authorized = false;
                yield return Send("/api/admin/face-scanners", "POST", JsonUtility.ToJson(
                    new DeviceAuthorization { device_id = CloudDeviceAuthenticator.DeviceId }), false,
                    (result, _) => authorized = result.Status == CloudFaceTransportStatus.Success);
                if (!authorized) { completed("이 기기의 서버 검색 권한을 승인하지 못했습니다."); yield break; }
            }
            string error = null;
            yield return Send("/api/me/face/scan-consent", "POST", JsonUtility.ToJson(
                new ScanConsent { policy_version = Config.scan_policy }), true,
                (result, _) =>
                {
                    ScanApproved = result.Status == CloudFaceTransportStatus.Success;
                    if (!ScanApproved) error = "검색 동의를 저장하지 못했습니다. 기기의 프로필 연결·권한을 확인해 주세요.";
                });
            completed(error);
        }
        public static IEnumerator RevokeScanConsent(Action<string> completed)
        {
            ScanApproved = false; // Immediate local stop, even while the network is unavailable.
            yield return Send("/api/me/face/scan-consent", "DELETE", null, true,
                (result, _) => completed(result.Status == CloudFaceTransportStatus.Success ? null :
                    "기기 검색은 중지됐지만 서버 동의 철회를 확인하지 못했습니다. 연결 후 다시 철회해 주세요."));
        }

        // No response body or exception text is logged. Redirects never receive credentials.
        public static IEnumerator Send(string path, string method, string json, bool deviceOnly,
            Action<CloudFaceTransportResult, string> completed, string consentGeneration = null)
        {
            string auth;
            if (!CloudServerConfiguration.RemoteFacesEnabled || CloudModeSession.Mode != ProfileDataMode.Cloud ||
                (deviceOnly ? !CloudDeviceAuthenticator.HasUsableToken : !CloudProfileApi.TryGetAuthHeader(out _)))
            { completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Unavailable), null); yield break; }
            if (deviceOnly) auth = "Bearer " + CloudDeviceAuthenticator.AccessToken;
            else CloudProfileApi.TryGetAuthHeader(out auth);
            string origin = CloudServerConfiguration.BaseUrl;
            using (var request = new UnityWebRequest(origin + path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (json != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("Authorization", auth);
                if (consentGeneration != null) request.SetRequestHeader("If-Face-Consent-Generation", consentGeneration);
                request.redirectLimit = 0;
                request.timeout = 8;
                UnityWebRequestAsyncOperation operation = null;
                try { operation = request.SendWebRequest(); } catch (Exception) { }
                if (operation == null)
                { completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Unavailable), null); yield break; }
                yield return operation;
                string current;
                if (deviceOnly) current = "Bearer " + CloudDeviceAuthenticator.AccessToken;
                else CloudProfileApi.TryGetAuthHeader(out current);
                if (CloudModeSession.Mode != ProfileDataMode.Cloud || origin != CloudServerConfiguration.BaseUrl || auth != current)
                { completed(new CloudFaceTransportResult(CloudFaceTransportStatus.Rejected), null); yield break; }
                long code = request.responseCode;
                var status = request.result == UnityWebRequest.Result.Success && (code == 200 || code == 201 || code == 204)
                    ? CloudFaceTransportStatus.Success : code >= 400 && code < 500
                    ? CloudFaceTransportStatus.Rejected : code == 503 ? CloudFaceTransportStatus.Unavailable : CloudFaceTransportStatus.Unknown;
                if (deviceOnly && (code == 401 || code == 403 || code == 429)) ScanApproved = false;
                completed(new CloudFaceTransportResult(status), status == CloudFaceTransportStatus.Success ? request.downloadHandler.text : null);
            }
        }
    }
}
