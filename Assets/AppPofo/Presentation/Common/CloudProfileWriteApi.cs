using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AppPofo.Presentation.Common
{
    // Transport layer only. UI wiring is a separate step: no local action
    // may accidentally create, overwrite or delete a server profile.
    public static class CloudProfileWriteApi
    {
        public static IEnumerator Create(CloudProfileWritePayload payload,
            Action<string, string> completed) =>
            Send("POST", null, payload, completed, Guid.NewGuid().ToString("D"));

        public static IEnumerator Create(CloudProfileWritePayload payload, string requestId,
            Action<string, string> completed) => Send("POST", null, payload, completed, requestId);

        // Full replacement: load existing items before editing.
        public static IEnumerator Replace(string cloudId, CloudProfileWritePayload payload,
            Action<string, string> completed) =>
            Send("PUT", cloudId, payload, completed);

        public static IEnumerator Delete(string cloudId, Action<string, string> completed) =>
            Send("DELETE", cloudId, null, completed);

        private static IEnumerator Send(string method, string cloudId,
            CloudProfileWritePayload payload, Action<string, string> completed, string requestId = null)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            if (!CloudProfileApi.CanRequest)
            {
                completed(null, "서버 프로필 변경 권한이 없습니다.");
                yield break;
            }
            if (method != "POST" && !Guid.TryParse(cloudId, out _))
            {
                completed(null, "서버 프로필 ID가 올바르지 않습니다.");
                yield break;
            }
            string body = null;
            if (method != "DELETE" &&
                !CloudProfileWritePayload.TryToJson(payload, out body, out string error))
            {
                completed(null, error);
                yield break;
            }
            if (!CloudProfileApi.TryGetAuthHeader(out string authorization))
            {
                completed(null, "인증을 다시 진행해 주세요.");
                yield break;
            }
            string endpoint = CloudProfileApi.CloudProfilesEndpoint +
                (method == "POST" ? "" : "/" + UnityWebRequest.EscapeURL(cloudId));
            using (var request = new UnityWebRequest(endpoint, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (method != "DELETE")
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("Authorization", authorization);
                if (method == "POST") request.SetRequestHeader("Idempotency-Key", requestId);
                request.redirectLimit = 0;
                request.timeout = 8;
                yield return request.SendWebRequest();
                long expected = method == "POST" ? 201 : method == "PUT" ? 200 : 204;
                if (request.result != UnityWebRequest.Result.Success ||
                    request.responseCode != expected)
                {
                    completed(null, CloudProfileApi.DescribeError(request));
                    yield break;
                }
                if (method == "DELETE")
                {
                    completed(cloudId, null);
                    yield break;
                }
                if (!CloudProfileWriteJson.TryReadAck(request.downloadHandler.text,
                    out string returnedId) || (method == "PUT" &&
                    !string.Equals(cloudId, returnedId, StringComparison.OrdinalIgnoreCase)))
                {
                    completed(null, "서버 응답이 올바르지 않습니다. 중복 저장하지 말고 목록을 확인해 주세요.");
                    yield break;
                }
                completed(returnedId, null);
            }
        }
    }
}
