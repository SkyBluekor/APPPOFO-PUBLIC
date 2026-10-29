using System;
using System.Collections;
using AppPofo.Presentation.FaceRegistration;
using UnityEngine;

namespace AppPofo.Presentation.Common
{
    // Display thumbnail only. Neither originals nor biometric templates are uploaded here.
    public static class CloudProfileAvatar
    {
        public const int MaxImageBytes = 24576;
        public const int MaxBase64Characters = 32768;
        [Serializable] private sealed class Payload { public string image_base64; public bool only_if_missing; }
        public static byte[] Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) || encoded.Length > MaxBase64Characters) return null;
            try { return Convert.FromBase64String(encoded); } catch (FormatException) { return null; }
        }
        public static byte[] Thumbnail(byte[] image)
        {
            Texture2D source = null, thumbnail = null;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (image == null || !source.LoadImage(image)) return null;
                float scale = Mathf.Min(1f, 192f / Mathf.Max(source.width, source.height));
                int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
                var input = source.GetPixels32(); var output = new Color32[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        output[y * width + x] = input[Mathf.Min(source.height - 1, y * source.height / height) * source.width + Mathf.Min(source.width - 1, x * source.width / width)];
                thumbnail = new Texture2D(width, height, TextureFormat.RGB24, false);
                thumbnail.SetPixels32(output); thumbnail.Apply();
                byte[] jpeg = thumbnail.EncodeToJPG(70);
                if (jpeg.Length > MaxImageBytes) jpeg = thumbnail.EncodeToJPG(40);
                return jpeg.Length <= MaxImageBytes ? jpeg : null;
            }
            finally
            {
                if (source != null) UnityEngine.Object.Destroy(source);
                if (thumbnail != null) UnityEngine.Object.Destroy(thumbnail);
            }
        }
        public static IEnumerator Save(string id, byte[] image, bool onlyIfMissing, Action<string> completed)
        {
            if (!Guid.TryParse(id, out _) || image == null || image.Length > MaxImageBytes)
            { completed?.Invoke("프로필 사진을 준비하지 못했습니다."); yield break; }
            string path = (CloudProfileApi.IsAdministrator ? "/api/profiles/" : "/api/me/profiles/") + id + "/avatar";
            yield return CloudFaceRuntime.Send(path, "PUT", JsonUtility.ToJson(new Payload {
                image_base64 = Convert.ToBase64String(image), only_if_missing = onlyIfMissing
            }), false, (result, _) => completed?.Invoke(result.Status == CloudFaceTransportStatus.Success ? null :
                "프로필 사진을 저장하지 못했습니다. 연결을 확인하고 명함에서 다시 지정해 주세요."));
        }
    }
}
