using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AppPofo.Presentation.Common
{
    // Only the owner or admin can issue a permanent profile connection code.
    // The code is shown once and must never be stored in PlayerPrefs.
    public static class CloudProfileLinkApi
    {
        private static string ConnectUrl => CloudServerConfiguration.BaseUrl + "/api/me/profile-links";

        [Serializable] private sealed class CodeBody { public string code; }
        [Serializable] private sealed class Envelope { public CodeData data; }
        [Serializable] private sealed class CodeData
        {
            public string profile_id;
            public string link_code;
        }

        public static bool ValidCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 40 ||
                !code.StartsWith("applink_", StringComparison.Ordinal)) return false;
            foreach (char c in code.Substring(8))
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                      (c >= '0' && c <= '9') || c == '-' || c == '_'))
                    return false;
            return true;
        }

        public static bool TryReadCode(string json, string expectedId, out string code)
        {
            code = null;
            try
            {
                var response = JsonUtility.FromJson<Envelope>(json);
                if (response?.data == null || !ValidCode(response.data.link_code) ||
                    !string.Equals(expectedId, response.data.profile_id,
                        StringComparison.OrdinalIgnoreCase)) return false;
                code = response.data.link_code;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool TryReadConnectedId(string json, out string id)
        {
            id = null;
            try
            {
                var response = JsonUtility.FromJson<Envelope>(json);
                if (response?.data == null ||
                    !Guid.TryParse(response.data.profile_id, out _)) return false;
                id = response.data.profile_id;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static IEnumerator Issue(string profileId, Action<string, string> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            if (!CloudProfileApi.CanRequest || !Guid.TryParse(profileId, out _) ||
                !CloudProfileApi.TryGetAuthHeader(out string authorization))
            {
                completed(null, "프로필 관리 권한을 확인해 주세요.");
                yield break;
            }
            string url = CloudProfileApi.CloudProfilesEndpoint + "/" +
                UnityWebRequest.EscapeURL(profileId) + "/link-code";
            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", authorization);
                request.redirectLimit = 0;
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                {
                    completed(null, CloudProfileApi.DescribeError(request));
                    yield break;
                }
                if (!TryReadCode(request.downloadHandler.text, profileId, out string code))
                {
                    completed(null, "연결 코드 응답이 올바르지 않습니다.");
                    yield break;
                }
                completed(code, null);
            }
        }

        public static IEnumerator Connect(string code, Action<string, string> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            if (!ValidCode(code))
            {
                completed(null, "연결 코드 40자리를 확인해 주세요.");
                yield break;
            }
            if (!CloudProfileApi.CanRequest || CloudProfileApi.IsAdministrator ||
                !CloudDeviceAuthenticator.HasUsableToken)
            {
                completed(null, "일반 사용자 기기 인증이 필요합니다.");
                yield break;
            }
            var payload = JsonUtility.ToJson(new CodeBody { code = code });
            using (var request = new UnityWebRequest(ConnectUrl, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization",
                    "Bearer " + CloudDeviceAuthenticator.AccessToken);
                request.redirectLimit = 0;
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                {
                    completed(null, CloudProfileApi.DescribeError(request));
                    yield break;
                }
                if (!TryReadConnectedId(request.downloadHandler.text, out string id))
                {
                    completed(null, "연결 결과를 확인할 수 없습니다.");
                    yield break;
                }
                completed(id, null);
            }
        }
    }
}
