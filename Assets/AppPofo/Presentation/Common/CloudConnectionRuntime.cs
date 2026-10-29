using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace AppPofo.Presentation.Common
{
    // Not parented under Welcome: it keeps the non-blocking connection check
    // running after Start hides the welcome screen.
    public sealed class CloudConnectionRuntime : MonoBehaviour
    {
        private static CloudConnectionRuntime instance;
        private UnityWebRequest activeRequest;
        private Coroutine activeRoutine;
        private Coroutine renewRoutine;
        private float nextRefreshCheck;

        public static void BeginFromWelcome(bool serverEnabled)
        {
            var plan = CloudModeSession.Plan(serverEnabled);
            CloudDeviceAuthenticator.ClearToken();
            CloudFaceRuntime.Reset();
            CloudModeSession.SetStartedMode(plan);
            // The scene's serialized Start callbacks activate the list before
            // WelcomeCloudModeController applies the selected mode. Re-render now
            // so no SQLite cards remain visible in the cloud session.
            var list = UnityEngine.Object.FindAnyObjectByType<
                AppPofo.Presentation.Screens.ProfileListScreen>(FindObjectsInactive.Include);
            if (list != null && list.isActiveAndEnabled) list.Refresh();
            if (!plan.RequestConnectivityProbe)
            {
                if (instance != null) instance.StopCurrentRequest();
                return;
            }

            if (instance == null)
            {
                var go = new GameObject("AppPofoCloudConnectionRuntime");
                instance = go.AddComponent<CloudConnectionRuntime>();
                DontDestroyOnLoad(go);
            }

            instance.StopCurrentRequest();
            instance.activeRoutine = instance.StartCoroutine(instance.Probe());
        }

        public static bool SubmitAdministratorCode(string code)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (CloudModeSession.Mode != ProfileDataMode.Cloud ||
                !DesktopCloudAdminToken.Propose(code)) return false;
            BeginFromWelcome(true);
            return true;
#else
            return false;
#endif
        }

        public static void SignOutAdministrator()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            DesktopCloudAdminToken.Forget();
            if (CloudModeSession.Mode == ProfileDataMode.Cloud) BeginFromWelcome(true);
#endif
        }

        public static void RetryConnection()
        {
            if (CloudModeSession.Mode == ProfileDataMode.Cloud)
                BeginFromWelcome(true);
        }

        private IEnumerator Probe()
        {
            using (var request = UnityWebRequest.Get(CloudModeSession.HealthUrl))
            {
                activeRequest = request;
                request.timeout = 5;
                yield return request.SendWebRequest();
                if (activeRequest != request)
                    yield break; // Cancelled by a new start/mode change.

                bool ok = request.result == UnityWebRequest.Result.Success &&
                          request.responseCode == 200 &&
                          request.downloadHandler.text.Contains("\"ok\"");
                activeRequest = null;
                bool admin = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                // A file's presence is not permission: verify the bearer token with
                // the existing administrator API before assigning the admin role.
                if (ok && DesktopCloudAdminToken.TryReadToken(out string token))
                {
                    using (var auth = UnityWebRequest.Get(
                        CloudProfileApi.AdminProfilesUrl + "?limit=1&offset=0"))
                    {
                        auth.SetRequestHeader("Authorization", "Bearer " + token);
                        auth.redirectLimit = 0;
                        auth.timeout = 8;
                        activeRequest = auth;
                        yield return auth.SendWebRequest();
                        if (activeRequest != auth) yield break;
                        admin = auth.result == UnityWebRequest.Result.Success &&
                                auth.responseCode == 200;
                        activeRequest = null;
                        if (admin)
                        {
                            if (!DesktopCloudAdminToken.RememberValidated())
                                Debug.LogWarning("[AppPofo Cloud] Administrator authentication works but will need re-entry after restart.");
                        }
                        else if (auth.responseCode == 401)
                        {
                            DesktopCloudAdminToken.Forget();
                            Debug.LogWarning("[AppPofo Cloud] Saved administrator code was rejected. Enter it again.");
                        }
                    }
                }
#endif
                if (ok && CloudDeviceAuthenticator.Supported)
                {
                    // S5-04B: Server-reachable is not an authenticated cloud session.
                    // AndroidKeyStore proof is required before saying "connected".
                    bool authenticated = false;
                    yield return CloudDeviceAuthenticator.Authenticate(success => authenticated = success);
                    ok = admin || (authenticated && CloudDeviceAuthenticator.HasUsableToken);
                }
                if (CloudModeSession.Mode != ProfileDataMode.Cloud)
                {
                    CloudDeviceAuthenticator.ClearToken();
                    yield break;
                }
                CloudModeSession.SetProbeResult(ok,
                    admin ? CloudAccessRole.Administrator : CloudAccessRole.User);
                if (ok && CloudServerConfiguration.RemoteFacesEnabled)
                    yield return CloudFaceRuntime.LoadConfiguration();
                var activeList = UnityEngine.Object.FindAnyObjectByType<
                    AppPofo.Presentation.Screens.ProfileListScreen>(FindObjectsInactive.Include);
                if (activeList != null) activeList.RefreshRecognitionCameraAvailability();
                if (ok && admin)
                    Debug.Log("[AppPofo Cloud] Desktop administrator authenticated.");
                else if (ok && CloudDeviceAuthenticator.Supported)
                    Debug.Log("[AppPofo Cloud] Device authenticated. Private cloud session ready.");
                else if (ok)
                    Debug.Log("[AppPofo Cloud] Regular desktop mode; user cloud identity not authenticated.");
                else
                    Debug.LogWarning("[AppPofo Cloud] Server or device authentication unavailable; local data is unchanged.");
                activeRequest = null;
                activeRoutine = null;
            }
        }

        // Signed device sessions last 30 minutes. A remembered device must not ask
        // the user for a code each time an access token expires.
        private void Update()
        {
            if (!CloudDeviceAuthenticator.Supported ||
                CloudModeSession.Mode != ProfileDataMode.Cloud ||
                CloudModeSession.Status != CloudConnectionStatus.ServerReachable ||
                activeRoutine != null || renewRoutine != null ||
                Time.unscaledTime < nextRefreshCheck) return;
            nextRefreshCheck = Time.unscaledTime + 15f;
            if (CloudDeviceAuthenticator.ExpiresAtMs -
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < 120000)
                renewRoutine = StartCoroutine(RenewDeviceSession());
        }

        private IEnumerator RenewDeviceSession()
        {
            bool ok = false;
            yield return CloudDeviceAuthenticator.Authenticate(success => ok = success);
            renewRoutine = null;
            if (CloudModeSession.Mode != ProfileDataMode.Cloud)
            {
                CloudDeviceAuthenticator.ClearToken();
                yield break;
            }
            if (!ok || !CloudDeviceAuthenticator.HasUsableToken)
            {
                CloudModeSession.SetProbeResult(false, CloudAccessRole.None);
                var list = UnityEngine.Object.FindAnyObjectByType<
                    AppPofo.Presentation.Screens.ProfileListScreen>(FindObjectsInactive.Include);
                if (list != null && list.isActiveAndEnabled) list.Refresh();
            }
        }

        private void StopCurrentRequest()
        {
            if (renewRoutine != null)
            {
                StopCoroutine(renewRoutine);
                renewRoutine = null;
            }
            if (activeRequest != null)
            {
                activeRequest.Abort();
                activeRequest = null;
            }
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }
        }

        private void OnDestroy()
        {
            StopCurrentRequest();
            if (instance == this) instance = null;
        }
    }
}
