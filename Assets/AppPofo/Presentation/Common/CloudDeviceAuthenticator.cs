using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AppPofo.Presentation.Common
{
    /// <summary>
    /// Signed device login on Windows/Android. Only a public key, one-time signatures, and a short-lived
    /// session token cross the wire; never the non-exportable private key.
    /// Does not query, upload or mutate any local SQLite/face data.
    /// </summary>
    public static class CloudDeviceAuthenticator
    {
        private static string BaseUrl => CloudServerConfiguration.BaseUrl;
        private static string StoredDeviceId => CloudServerConfiguration.BaseUrl == CloudServerConfiguration.ProductionUrl
            ? "AppPofo.Cloud.DeviceId.v1" : "AppPofo.Cloud.DeviceId.v1." + CloudServerConfiguration.Scope;
        public static string DeviceId => PlayerPrefs.GetString(StoredDeviceId, "");
        public static bool Supported => UnityEngine.Application.platform == RuntimePlatform.Android ||
            UnityEngine.Application.platform == RuntimePlatform.WindowsEditor ||
            UnityEngine.Application.platform == RuntimePlatform.WindowsPlayer;
        public static string AccessToken { get; private set; }
        public static long ExpiresAtMs { get; private set; }

        [Serializable] private class PublicJwk { public string kty, crv, x, y; }
        [Serializable] private class RegisterChallenge { public string purpose = "register"; public PublicJwk public_key; }
        [Serializable] private class SessionChallenge { public string purpose = "session"; public string device_id; }
        [Serializable] private class ChallengeReply { public string challenge_id; public long expires_at; }
        [Serializable] private class Registration { public string challenge_id, signature; public PublicJwk public_key; }
        [Serializable] private class RegistrationReply { public string device_id; }
        [Serializable] private class SessionLogin { public string challenge_id, device_id, signature; }
        [Serializable] private class SessionReply { public string access_token, token_type; public long expires_at; }

        public static bool HasUsableToken =>
            !string.IsNullOrEmpty(AccessToken) &&
            ExpiresAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000;

        public static void ClearToken()
        {
            AccessToken = null;
            ExpiresAtMs = 0;
        }

        private static string Sign(string text)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            using (var key = new WindowsDeviceKey("AppPofo.Device.P256." + CloudServerConfiguration.Scope,
                string.IsNullOrEmpty(DeviceId))) return key.Sign(text);
#else
            using (var key = new AndroidJavaClass("com.apppofo.cloud.AppPofoDeviceKeyStore"))
                return key.CallStatic<string>("signMessageForScope",
                    BaseUrl == CloudServerConfiguration.ProductionUrl ? "" : CloudServerConfiguration.Scope, text);
#endif
        }

        private static PublicJwk PublicKey()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            using (var key = new WindowsDeviceKey("AppPofo.Device.P256." + CloudServerConfiguration.Scope,
                string.IsNullOrEmpty(DeviceId)))
            {
                byte[] xy = key.PublicCoordinates(), x = new byte[32], y = new byte[32];
                Buffer.BlockCopy(xy, 0, x, 0, 32); Buffer.BlockCopy(xy, 32, y, 0, 32);
                return new PublicJwk { kty = "EC", crv = "P-256",
                    x = WindowsDeviceKey.Base64Url(x), y = WindowsDeviceKey.Base64Url(y) };
            }
#else
            using (var key = new AndroidJavaClass("com.apppofo.cloud.AppPofoDeviceKeyStore"))
                return JsonUtility.FromJson<PublicJwk>(
                    key.CallStatic<string>("getPublicKeyJwkJsonForScope",
                        BaseUrl == CloudServerConfiguration.ProductionUrl ? "" : CloudServerConfiguration.Scope,
                        string.IsNullOrEmpty(DeviceId)));
#endif
        }

        private static string Fingerprint(PublicJwk key)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("AppPofo-P256-v1\n" + key.x + "\n" + key.y);
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(bytes))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Proof(string purpose, string challengeId, string subject)
        {
            return Sign("AppPofo-v1\n" + purpose + "\n" + challengeId + "\n" + subject);
        }

        private static IEnumerator Post(string path, string payload, Action<long, string> completed)
        {
            using (var request = new UnityWebRequest(BaseUrl + path, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 8;
                request.redirectLimit = 0;
                yield return request.SendWebRequest();
                long code = request.result == UnityWebRequest.Result.Success ?
                    request.responseCode : 0;
                completed(code, request.downloadHandler.text);
            }
        }

        private static T ReadReply<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        public static IEnumerator Authenticate(Action<bool> completed)
        {
            ClearToken();
            if (!Supported)
            {
                completed(false);
                yield break;
            }

            PublicJwk publicKey;
            try { publicKey = PublicKey(); }
            catch (Exception ex)
            {
                Debug.LogWarning("[AppPofo Cloud] Device keystore unavailable: " + ex.GetType().Name);
                completed(false);
                yield break;
            }
            if (publicKey == null || string.IsNullOrEmpty(publicKey.x) ||
                string.IsNullOrEmpty(publicKey.y))
            {
                completed(false);
                yield break;
            }

            string deviceId = PlayerPrefs.GetString(StoredDeviceId, "");
            if (string.IsNullOrEmpty(deviceId))
            {
                long regCode = 0;
                string regJson = null;
                yield return Post("/api/device/challenge",
                    JsonUtility.ToJson(new RegisterChallenge { public_key = publicKey }),
                    (code, body) => { regCode = code; regJson = body; });
                var challenge = regCode == 201 ? ReadReply<ChallengeReply>(regJson) : null;
                if (challenge == null || string.IsNullOrEmpty(challenge.challenge_id))
                {
                    completed(false);
                    yield break;
                }

                string signature;
                try { signature = Proof("register", challenge.challenge_id, Fingerprint(publicKey)); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AppPofo Cloud] Device signing unavailable: " + ex.GetType().Name);
                    completed(false);
                    yield break;
                }
                long createdCode = 0;
                string createdJson = null;
                yield return Post("/api/device/register",
                    JsonUtility.ToJson(new Registration {
                        challenge_id = challenge.challenge_id, public_key = publicKey,
                        signature = signature
                    }),
                    (code, body) => { createdCode = code; createdJson = body; });
                var registered = createdCode == 201 ? ReadReply<RegistrationReply>(createdJson) : null;
                if (registered == null || string.IsNullOrEmpty(registered.device_id))
                {
                    completed(false);
                    yield break;
                }
                deviceId = registered.device_id;
                PlayerPrefs.SetString(StoredDeviceId, deviceId);
                PlayerPrefs.Save();
            }

            long sessionCode = 0;
            string sessionJson = null;
            yield return Post("/api/device/challenge",
                JsonUtility.ToJson(new SessionChallenge { device_id = deviceId }),
                (code, body) => { sessionCode = code; sessionJson = body; });
            var sessionChallenge = sessionCode == 201 ? ReadReply<ChallengeReply>(sessionJson) : null;
            if (sessionChallenge == null || string.IsNullOrEmpty(sessionChallenge.challenge_id))
            {
                // Never silently replace an old identity if its local key is lost.
                completed(false);
                yield break;
            }

            string loginSignature;
            try { loginSignature = Proof("session", sessionChallenge.challenge_id, deviceId); }
            catch (Exception ex)
            {
                Debug.LogWarning("[AppPofo Cloud] Signing failed: " + ex.GetType().Name);
                completed(false);
                yield break;
            }
            long loginCode = 0;
            string loginJson = null;
            yield return Post("/api/device/session",
                JsonUtility.ToJson(new SessionLogin {
                    challenge_id = sessionChallenge.challenge_id,
                    device_id = deviceId, signature = loginSignature
                }),
                (code, body) => { loginCode = code; loginJson = body; });
            var response = loginCode == 200 ? ReadReply<SessionReply>(loginJson) : null;
            bool authorized = response != null &&
                !string.IsNullOrEmpty(response.access_token) &&
                response.access_token.StartsWith("appdev_", StringComparison.Ordinal) &&
                response.expires_at > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (authorized)
            {
                AccessToken = response.access_token;
                ExpiresAtMs = response.expires_at;
            }
            completed(authorized);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetToken() => ClearToken();
    }
}
