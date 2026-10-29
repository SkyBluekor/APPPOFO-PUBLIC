using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace AppPofo.Presentation.Common
{
    // Read-only cloud boundary shared by one Unity application.
    // Administrators use the separately validated PC token. Regular Windows and
    // Android devices use signed sessions and only their linked private profiles.
    public static class CloudProfileApi
    {
        private static string ProfilesUrl => CloudServerConfiguration.BaseUrl + "/api/me/profiles";
        public const int PageSize = 12;
        public static string AdminProfilesUrl => CloudServerConfiguration.BaseUrl + "/api/profiles";

        public static bool CanRequest
        {
            get
            {
                if (CloudModeSession.Mode != ProfileDataMode.Cloud ||
                    CloudModeSession.Status != CloudConnectionStatus.ServerReachable)
                    return false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return IsAdministrator ? DesktopCloudAdminToken.HasToken : CloudDeviceAuthenticator.HasUsableToken;
#else
                return CloudDeviceAuthenticator.HasUsableToken;
#endif
            }
        }

        public static bool IsAdministrator =>
            CloudModeSession.AccessRole == CloudAccessRole.Administrator;

        internal static string CloudProfilesEndpoint
        {
            get
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return IsAdministrator ? AdminProfilesUrl : ProfilesUrl;
#else
                return ProfilesUrl;
#endif
            }
        }

        internal static bool TryGetAuthHeader(out string header)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (IsAdministrator)
            {
                if (!DesktopCloudAdminToken.TryReadToken(out var token)) { header = null; return false; }
                header = "Bearer " + token;
                return true;
            }
#endif
            header = "Bearer " + CloudDeviceAuthenticator.AccessToken;
            return CloudDeviceAuthenticator.HasUsableToken;
        }

        public static IEnumerator FetchPage(int pageIndex, Action<CloudProfilePage, string> done)
        {
            if (!CanRequest)
            {
                done(null, "서버 프로필 조회 권한이 없습니다.");
                yield break;
            }
            if (pageIndex < 0 || pageIndex > 8333)
            {
                done(null, "요청한 페이지가 올바르지 않습니다.");
                yield break;
            }
            if (!TryGetAuthHeader(out var authHeader))
            {
                done(null, "관리자 인증 파일을 읽을 수 없습니다.");
                yield break;
            }
            string url = CloudProfilesEndpoint + "?limit=" + PageSize + "&offset=" +
                         (pageIndex * PageSize);
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", authHeader);
                request.redirectLimit = 0;
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success ||
                    request.responseCode != 200)
                {
                    done(null, DescribeError(request));
                    yield break;
                }
                if (!CloudProfileJson.TryReadPage(request.downloadHandler.text, out var page))
                {
                    done(null, "서버에서 받은 목록 데이터를 읽을 수 없습니다.");
                    yield break;
                }
                done(page, null);
            }
        }

        public static IEnumerator FetchDetail(string id,
            Action<CloudProfileDetail, string> done)
        {
            if (!CanRequest)
            {
                done(null, "기기 인증이 완료되지 않았습니다.");
                yield break;
            }
            if (!Guid.TryParse(id, out _))
            {
                done(null, "프로필 ID가 올바르지 않습니다.");
                yield break;
            }
            if (!TryGetAuthHeader(out var authHeader))
            {
                done(null, "관리자 인증 파일을 읽을 수 없습니다.");
                yield break;
            }
            using (var request = UnityWebRequest.Get(CloudProfilesEndpoint + "/" +
                UnityWebRequest.EscapeURL(id)))
            {
                request.SetRequestHeader("Authorization", authHeader);
                request.redirectLimit = 0;
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success ||
                    request.responseCode != 200)
                {
                    done(null, DescribeError(request));
                    yield break;
                }
                if (!CloudProfileJson.TryReadDetail(request.downloadHandler.text,
                    out var detail) || !string.Equals(detail.id, id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    done(null, "서버에서 받은 프로필이 올바르지 않습니다.");
                    yield break;
                }
                done(detail, null);
            }
        }

        internal static string DescribeError(UnityWebRequest request)
        {
            if (request.responseCode == 401) return "서버 인증이 유효하지 않습니다. 다시 연결해 주세요.";
            if (request.responseCode == 403) return "이 작업에 필요한 권한이 없습니다.";
            if (request.responseCode == 400) return "입력값이나 연결 코드를 확인해 주세요.";
            if (request.responseCode == 409) return "다른 기기에서 정보가 변경됐습니다. 새로고침 후 다시 시도해 주세요.";
            if (request.responseCode == 404) return "서버에서 해당 프로필을 찾지 못했습니다.";
            if (request.responseCode == 429) return "요청이 많습니다. 잠시 뒤 다시 시도해 주세요.";
            if (request.responseCode >= 500) return "서버가 응답하지 않습니다. 다시 시도해 주세요.";
            return "인터넷 연결을 확인한 뒤 다시 시도해 주세요.";
        }
    }
}
