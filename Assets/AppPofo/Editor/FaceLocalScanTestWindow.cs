#if UNITY_EDITOR_WIN
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AppPofo.Editor
{
    // No credentials are kept in Unity scenes, PlayerPrefs, source or EditorPrefs.
    // This test window opts in to ONE running local Windows Editor session.
    public sealed class FaceLocalScanTestWindow : EditorWindow
    {
        [Serializable]
        private sealed class LocalSession
        {
            public string device_id;
            public string access_token;
            public long expires_at;
            public string scanner_profile_id;
            public string policy_version;
        }

        private bool approvedLocalCapture;
        private bool approvedScanning;
        private string message = "검색 테스트는 기본적으로 비활성화되어 있습니다.";

        [MenuItem("Tools/AppPoFo/Local Face Scan Test")]
        public static void Open()
        {
            GetWindow<FaceLocalScanTestWindow>("Local Face Scan Test");
        }

        private static string SessionPath()
        {
            string project = Path.GetFullPath(Path.Combine(
                UnityEngine.Application.dataPath, ".."));
            return Path.Combine(project, "cloudflare", "apppofo-api",
                ".dev.vars.scan-session");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("S5-09-B 서버 얼굴 검색 (localhost 전용)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "로컬 Worker 실행 → 개발용 서명 기기 세션 생성 → " +
                "이 창에서 테스트 승인 순서로 진행하세요. " +
                "관리자 토큰을 사용하지 않으며 운영 서버에는 전송되지 않습니다.",
                MessageType.Info);
            EditorGUILayout.SelectableLabel(
                @"node .\tools\face_scan_local_session.mjs --confirm-local-scan-consent",
                EditorStyles.textField, GUILayout.Height(22));
            bool diagnostics = Environment.GetEnvironmentVariable(
                "APPPOFO_FACE_SCAN_DIAGNOSTICS") == "enabled";
            bool requestedDiagnostics = EditorGUILayout.ToggleLeft(
                "개발용 단계 진단 (Console, 개인정보 제외, 기본 OFF)", diagnostics);
            if (requestedDiagnostics != diagnostics)
                Environment.SetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS",
                    requestedDiagnostics ? "enabled" : null);
            Type api = Type.GetType(
                "AppPofo.Presentation.FaceRecognition.CloudFaceMatchApi, AppPofo.presentation", false);
            bool canQuery = api?.GetProperty("CanQuery")?.GetValue(null) is bool allowed && allowed;
            EditorGUILayout.LabelField("현재 CanQuery", canQuery ? "YES" : "NO");
            EditorGUILayout.LabelField("검색 준비 상태",
                api?.GetProperty("QueryReadiness")?.GetValue(null) as string ?? "RuntimeUnavailable");
            approvedLocalCapture = EditorGUILayout.ToggleLeft(
                "이번 테스트의 카메라 촬영·로컬 생체정보 처리를 당사자가 명시적으로 승인함",
                approvedLocalCapture);
            approvedScanning = EditorGUILayout.ToggleLeft(
                "127.0.0.1:8787 로컬 서버에 얼굴 특징 벡터를 검색 요청하는 것에 동의함",
                approvedScanning);

            if (GUILayout.Button("기기 세션 불러오기 및 로컬 검색 허용"))
            {
                if (!approvedLocalCapture || !approvedScanning)
                {
                    message = "두 가지 동의를 모두 확인해야 활성화할 수 있습니다.";
                    Repaint();
                }
                else TryActivate();
            }
            if (GUILayout.Button("검색 즉시 중지 · 세션 메모리 해제"))
            {
                Disable();
                message = "이 Unity Editor의 얼굴 검색을 중지했습니다.";
            }
            EditorGUILayout.HelpBox(message, MessageType.None);
            EditorGUILayout.HelpBox(
                "서명 세션은 약 30분 뒤 만료됩니다. 테스트가 끝나면 중지하고 " +
                "로컬 얼굴 등록정보가 남았다면 별도 삭제 절차로 정리하세요. " +
                "이 창을 닫으면 자동으로 현재 Editor 검색 권한을 해제합니다.",
                MessageType.Warning);
        }

        private void TryActivate()
        {
            Disable();
            string path = SessionPath();
            if (!File.Exists(path))
            {
                message = "로컬 세션 파일이 없습니다. 표시된 node 명령을 먼저 실행하세요.";
                return;
            }
            try
            {
                LocalSession session = JsonUtility.FromJson<LocalSession>(
                    File.ReadAllText(path));
                if (session == null || string.IsNullOrEmpty(session.access_token) ||
                    session.access_token.Length != 50 ||
                    !session.access_token.StartsWith("appdev_", StringComparison.Ordinal) ||
                    !Guid.TryParse(session.device_id, out _) ||
                    session.expires_at <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000 ||
                    session.policy_version != "local-face-scan-test-v1")
                {
                    message = "서명 세션이 유효하지 않거나 만료됐습니다. 다시 생성하세요.";
                    return;
                }
                // Process-only. Never persist a bearer in Unity project or PlayerPrefs.
                Environment.SetEnvironmentVariable(
                    "APPPOFO_FACE_LOCAL_DEVICE_TOKEN", session.access_token);
                Environment.SetEnvironmentVariable(
                    "APPPOFO_FACE_MATCH_LOCAL_EDITOR_ENABLED", "enabled");
                Environment.SetEnvironmentVariable(
                    "APPPOFO_FACE_MATCH_LOCAL_EDITOR_APPROVED", "YES_LOCAL_FACE_QUERY");
                RefreshCameraButton();
                message = "이 Unity Editor에서만 로컬 서버 얼굴 검색을 허용했습니다.";
            }
            catch (Exception)
            {
                Disable();
                message = "세션 파일을 안전하게 읽지 못했습니다. 다시 생성하세요.";
            }
        }

        private static void RefreshCameraButton()
        {
            // AppPofo.presentation has autoReferenced=false. Unity's predefined
            // Editor assembly cannot directly compile against its runtime types.
            // Follow FaceLocalTestWindow's established reflection boundary.
            Type type = Type.GetType(
                "AppPofo.Presentation.Screens.ProfileListScreen, AppPofo.presentation",
                false);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                return;
            Component list = UnityEngine.Object.FindFirstObjectByType(
                type, FindObjectsInactive.Include) as Component;
            if (list == null || !list.gameObject.scene.IsValid())
                return;
            var refresh = type.GetMethod("RefreshRecognitionCameraAvailability",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);
            if (refresh == null || refresh.GetParameters().Length != 0)
                return;
            refresh.Invoke(list, null);
        }

        private static void Disable()
        {
            if (Environment.GetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS") == "enabled")
                Debug.Log("[LocalFaceScan] stage=PermissionRevoked");
            Environment.SetEnvironmentVariable("APPPOFO_FACE_MATCH_LOCAL_EDITOR_ENABLED", null);
            Environment.SetEnvironmentVariable("APPPOFO_FACE_MATCH_LOCAL_EDITOR_APPROVED", null);
            Environment.SetEnvironmentVariable("APPPOFO_FACE_LOCAL_DEVICE_TOKEN", null);
            RefreshCameraButton();
        }

        private void OnDisable()
        {
            Disable();
            Environment.SetEnvironmentVariable("APPPOFO_FACE_SCAN_DIAGNOSTICS", null);
        }
    }
}
#endif
