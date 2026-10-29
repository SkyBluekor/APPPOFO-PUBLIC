using System;
using System.Collections;
using UnityEngine;
using AppPofo.Presentation.FaceRegistration;

namespace AppPofo.Presentation.Common
{
    public static class CloudProfileSummaryApi
    {
        public static IEnumerator Fetch(string id, Action<ProfileSummaryData, string> completed)
        {
            if (!Guid.TryParse(id, out _) || !CloudFaceRuntime.CanScan)
            { completed(null, "검색 권한을 확인해 주세요."); yield break; }
            yield return CloudFaceRuntime.Send("/api/me/face/profiles/" + id + "/summary", "GET", null, true, (result, body) =>
            {
                if (result.Status == CloudFaceTransportStatus.Success && ProfileSummaryCodec.TryRead(body, id, out var data)) completed(data, null);
                else completed(null, "공개된 프로필을 불러오지 못했습니다. 연결과 공개 상태를 확인해 주세요.");
            });
        }
    }
}
