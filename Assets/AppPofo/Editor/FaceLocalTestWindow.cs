#if UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Editor
{
    // No credentials in EditorPrefs, scene assets, Library files or source code.
    // Environment variables are scoped to this Unity Editor process only.
    public sealed class FaceLocalTestWindow : EditorWindow
    {
        [NonSerialized] private string localToken = "";
        private string localPolicy = "local-face-test-v1";
        private bool consentToLocalCapture;
        private string localProfileUuid = "";
        private bool enrollmentStorageConsent;
        private bool enrollmentSubjectPresent;
        private bool enrollmentDiscoverabilityConsent;
        private string localSettingsMessage = "";
        private static GameObject localResultPanel;

        [InitializeOnLoadMethod]
        private static void RegisterSessionCleanup()
        {
            // Credentials are process-only and must not survive Play Mode.
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ClearLocalTestCredentials;
            AssemblyReloadEvents.beforeAssemblyReload += ClearLocalTestCredentials;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                ClearLocalTestCredentials();
                localResultPanel = null; // Play Mode objects are destroyed by Unity.
            }
        }

        private static void ClearLocalTestCredentials()
        {
            foreach (string name in new[]
            {
                "APPPOFO_FACE_LOCAL_ADMIN_TOKEN",
                "APPPOFO_FACE_LOCAL_POLICY_VERSION",
                "APPPOFO_FACE_LOCAL_CLIENT_ENABLED",
                "APPPOFO_FACE_LOCAL_CAPTURE_TEST_APPROVED",
                "APPPOFO_FACE_LOCAL_DISCOVERABLE_APPROVED"
            })
                Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        }

        [MenuItem("Tools/AppPoFo/Local Face Enrollment Test")]
        public static void ShowWindow()
        {
            GetWindow<FaceLocalTestWindow>("Local Face Test");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("AppPoFo - Local Face Enrollment", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Only http://127.0.0.1:8787. No production Worker/D1 writes. " +
                "The local Worker and 0004 migration must be running separately. " +
                "Real biometric capture requires explicit participant consent and a local-only test.",
                MessageType.Warning);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Status",
                ReadRuntimeLocalCaptureEnabled() ? "Enabled (this Editor only)" :
                    "Disabled (camera-only test)");
            localToken = EditorGUILayout.PasswordField("Local admin token", localToken);
            using (new EditorGUI.DisabledScope(!UnityEngine.Application.isPlaying))
            {
                if (GUILayout.Button("Load LOCAL .dev.vars token (no clipboard)"))
                    LoadLocalSettings();
            }
            if (!string.IsNullOrEmpty(localSettingsMessage))
                EditorGUILayout.HelpBox(localSettingsMessage, MessageType.Info);
            localPolicy = EditorGUILayout.TextField("Local policy version", localPolicy);
            consentToLocalCapture = EditorGUILayout.ToggleLeft(
                "The person being filmed has read the local storage notice and explicitly " +
                "consents to this local-only biometric test.", consentToLocalCapture);
            EditorGUILayout.Space(6);

            if (!UnityEngine.Application.isPlaying)
                EditorGUILayout.HelpBox(
                    "Enter Unity Play Mode first. Local upload authorization is available " +
                    "only during this Play session and is cleared when Play stops.",
                    MessageType.Info);
            using (new EditorGUI.DisabledScope(!UnityEngine.Application.isPlaying ||
                !consentToLocalCapture || string.IsNullOrWhiteSpace(localPolicy) ||
                localToken.Length < 32))
            {
                if (GUILayout.Button("Enable Local Face Upload (Editor only)"))
                {
                    Environment.SetEnvironmentVariable("APPPOFO_FACE_LOCAL_ADMIN_TOKEN",
                        localToken, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("APPPOFO_FACE_LOCAL_POLICY_VERSION",
                        localPolicy, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("APPPOFO_FACE_LOCAL_CLIENT_ENABLED",
                        "enabled", EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("APPPOFO_FACE_LOCAL_CAPTURE_TEST_APPROVED",
                        "YES_LOCAL_BIOMETRIC_TEST", EnvironmentVariableTarget.Process);
                    localToken = "";
                    consentToLocalCapture = false;
                    GUI.FocusControl(null);
                }
            }
            if (GUILayout.Button("Disable and Clear Local Test Credentials"))
            {
                ClearLocalTestCredentials();
                localToken = "";
                localSettingsMessage = "";
                consentToLocalCapture = false;
                enrollmentStorageConsent = false;
                enrollmentSubjectPresent = false;
                enrollmentDiscoverabilityConsent = false;
            }
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Direct Local Enrollment (Editor Play Mode)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Enter a UUID already seeded in the LOCAL D1 database. " +
                "This bypasses production profile creation; ONLY the hardcoded loopback " +
                "face endpoint (127.0.0.1:8787) will be used. " +
                "Do not enter a person's production profile UUID.",
                MessageType.Info);
            localProfileUuid = EditorGUILayout.TextField("Local profile UUID", localProfileUuid);
            enrollmentStorageConsent = EditorGUILayout.ToggleLeft(
                "I agree to store the three face embeddings in LOCAL encrypted D1 for this test.",
                enrollmentStorageConsent);
            enrollmentSubjectPresent = EditorGUILayout.ToggleLeft(
                "The person being filmed is present and has explicitly consented to " +
                "this biometric collection after reading the local-storage notice.",
                enrollmentSubjectPresent);
            bool scanReady = IsLocalMatchingConfigured();
            enrollmentDiscoverabilityConsent = EditorGUILayout.ToggleLeft(
                "I explicitly permit my encrypted face template to be SEARCHABLE " +
                "by authenticated devices in this LOCAL test database.",
                enrollmentDiscoverabilityConsent);
            EditorGUILayout.HelpBox(
                "Default is OFF. Enabling local discoverability is separate from " +
                "face storage consent. It makes this face eligible for face search " +
                "by other signed local test devices until deleted or withdrawn. " +
                "Never use another person's face without their specific permission.",
                MessageType.Warning);
            if (enrollmentDiscoverabilityConsent && !scanReady)
                EditorGUILayout.HelpBox(
                    "Restart the localhost Worker with -EnableFaceMatchLocalTest " +
                    "before consenting to local discoverability.",
                    MessageType.Warning);
            bool validUuid = Guid.TryParse(localProfileUuid, out Guid parsed) &&
                parsed != Guid.Empty;
            bool ready = UnityEngine.Application.isPlaying &&
                ReadRuntimeLocalCaptureEnabled() && validUuid &&
                enrollmentStorageConsent && enrollmentSubjectPresent &&
                (!enrollmentDiscoverabilityConsent || scanReady);
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("Open LOCAL Face Registration (camera upload)"))
                {
                    if (enrollmentDiscoverabilityConsent)
                        OpenLocalDiscoverableEnrollment(parsed.ToString("D"),
                            enrollmentStorageConsent, enrollmentSubjectPresent, true);
                    else
                        OpenLocalEnrollment(parsed.ToString("D"), enrollmentStorageConsent,
                            enrollmentSubjectPresent);
                    // Consent never carries over to another camera opening.
                    enrollmentStorageConsent = false;
                    enrollmentSubjectPresent = false;
                    enrollmentDiscoverabilityConsent = false;
                }
            }
            if (UnityEngine.Application.isPlaying && !ReadRuntimeLocalCaptureEnabled())
                EditorGUILayout.HelpBox(
                    "Enable Local Face Upload above before opening the camera. " +
                    "The local Worker must be running on 127.0.0.1:8787.",
                    MessageType.Info);
            EditorGUILayout.HelpBox(
                "Stopping Play Mode clears the local token and authorization. " +
                "This screen does NOT delete previously stored local face data; " +
                "delete it explicitly after testing.",
                MessageType.Warning);

            EditorGUILayout.HelpBox(
                "Stop local test after use and delete the local D1 face records. " +
                "Do not enable this for real participants without an approved consent notice.",
                MessageType.Info);
        }

        private void LoadLocalSettings()
        {
            // Read from this project's local-only secrets file. Do not log,
            // persist, copy to the clipboard or serialize the token.
            localToken = "";
            localSettingsMessage = "";
            try
            {
                string projectRoot = Directory.GetParent(
                    UnityEngine.Application.dataPath).FullName;
                string file = Path.Combine(projectRoot, "cloudflare",
                    "apppofo-api", ".dev.vars");
                if (!File.Exists(file))
                    throw new InvalidOperationException("Missing local .dev.vars file.");
                string token = ReadLocalVariable(file, "APPPOFO_API_TOKEN");
                string mode = ReadLocalVariable(file, "APPPOFO_FACE_LOCAL_TEST_MODE");
                string policy = ReadLocalVariable(file, "APPPOFO_FACE_POLICY_VERSION");
                if (string.IsNullOrEmpty(token) || token.Length < 32 ||
                    mode != "enabled" || string.IsNullOrWhiteSpace(policy))
                    throw new InvalidOperationException("Incomplete local face settings.");
                localToken = token;
                localPolicy = policy;
                localSettingsMessage = "Local token and policy loaded into this window only. " +
                    "The token is not displayed or placed on the clipboard.";
            }
            catch (Exception)
            {
                localToken = "";
                localSettingsMessage = "Could not load valid local settings. " +
                    "Check cloudflare/apppofo-api/.dev.vars on this PC.";
            }
        }

        private static string ReadLocalVariable(string file, string name)
        {
            string result = null;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.TrimStart('\uFEFF', ' ', '\t');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                int equals = line.IndexOf('=');
                if (equals < 0 || line.Substring(0, equals).Trim() != name) continue;
                if (result != null)
                    throw new InvalidOperationException("Duplicate local setting.");
                string value = line.Substring(equals + 1).Trim();
                if (value.StartsWith("\"", StringComparison.Ordinal) ||
                    value.StartsWith("'", StringComparison.Ordinal))
                {
                    if (value.Length < 2 || value[value.Length - 1] != value[0])
                        throw new InvalidOperationException("Invalid local setting.");
                    value = value.Substring(1, value.Length - 2);
                }
                else
                {
                    int comment = value.IndexOf(" #", StringComparison.Ordinal);
                    if (comment >= 0) value = value.Substring(0, comment).TrimEnd();
                }
                result = value;
            }
            return result;
        }

        private static bool IsLocalMatchingConfigured()
        {
            try
            {
                string projectRoot = Directory.GetParent(
                    UnityEngine.Application.dataPath).FullName;
                string varsPath = Path.Combine(projectRoot, "cloudflare",
                    "apppofo-api", ".dev.vars");
                return File.Exists(varsPath) &&
                    ReadLocalVariable(varsPath, "APPPOFO_FACE_LOCAL_TEST_MODE") == "enabled" &&
                    ReadLocalVariable(varsPath, "APPPOFO_FACE_MATCH_LOCAL_TEST_MODE") == "enabled" &&
                    !string.IsNullOrWhiteSpace(ReadLocalVariable(
                        varsPath, "APPPOFO_FACE_SCAN_POLICY_VERSION"));
            }
            catch (Exception) { return false; }
        }

        // Kept for compatibility with the existing PlayMode bridge contract.
        // All previous callers still create discoverable=false registrations.
        private static void OpenLocalEnrollment(string cloudId,
            bool storageConsent, bool subjectPresent)
        {
            OpenLocalEnrollmentCore(cloudId, storageConsent, subjectPresent, false);
        }

        private static void OpenLocalDiscoverableEnrollment(string cloudId,
            bool storageConsent, bool subjectPresent, bool discoverableConsent)
        {
            if (!discoverableConsent || !storageConsent || !subjectPresent ||
                !IsLocalMatchingConfigured())
            {
                Debug.LogWarning("[FaceLocalTest] Local discoverability is not authorized.");
                return;
            }
            Environment.SetEnvironmentVariable("APPPOFO_FACE_LOCAL_DISCOVERABLE_APPROVED",
                "YES_LOCAL_DISCOVERABLE_TEST", EnvironmentVariableTarget.Process);
            try
            {
                OpenLocalEnrollmentCore(cloudId, storageConsent, subjectPresent, true);
            }
            finally
            {
                Environment.SetEnvironmentVariable("APPPOFO_FACE_LOCAL_DISCOVERABLE_APPROVED",
                    null, EnvironmentVariableTarget.Process);
            }
        }

        private static void OpenLocalEnrollmentCore(string cloudId,
            bool storageConsent, bool subjectPresent, bool publicRecognitionConsent)
        {
            if (!UnityEngine.Application.isPlaying || !ReadRuntimeLocalCaptureEnabled() ||
                !storageConsent || !subjectPresent ||
                !Guid.TryParse(cloudId, out Guid parsed) || parsed == Guid.Empty)
            {
                Debug.LogWarning("[FaceLocalTest] Local enrollment is not authorized.");
                return;
            }
            Type screenType = PresentationType(
                "AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen");
            if (screenType == null || !typeof(Component).IsAssignableFrom(screenType))
            {
                Debug.LogError("[FaceLocalTest] FaceRegistrationScreen runtime type missing.");
                return;
            }
            var component = UnityEngine.Object.FindFirstObjectByType(screenType,
                FindObjectsInactive.Include) as Component;
            if (component == null || !component.gameObject.scene.IsValid())
            {
                Debug.LogError("[FaceLocalTest] Open the Bootstrap scene in Play Mode.");
                return;
            }
            if (component.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[FaceLocalTest] Face registration is already open.");
                return;
            }

            FieldInfo originField = screenType.GetField("returnScreenRoot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            GameObject origin = originField?.GetValue(component) as GameObject;
            if (origin == null || !origin.scene.IsValid() ||
                origin == component.gameObject ||
                component.transform.IsChildOf(origin.transform))
            {
                Debug.LogError("[FaceLocalTest] Registration return screen is not configured safely.");
                return;
            }
            MethodInfo open = screenType.GetMethod("ShowCloud", BindingFlags.Public |
                BindingFlags.Instance, null,
                new[] { typeof(string), typeof(GameObject), typeof(Action<string, bool>),
                    typeof(bool), typeof(bool) }, null);
            if (open == null)
            {
                Debug.LogError("[FaceLocalTest] Cloud enrollment entry point not found.");
                return;
            }
            // Do NOT return to the unrelated local ProfileDetailScreen.
            // That screen may still display a previous profile (e.g. 홍길동)
            // and would misleadingly appear to be the cloud face target.
            GameObject resultPanel = CreateLocalResultPanel(component, parsed.ToString("D"));
            if (resultPanel == null)
            {
                Debug.LogError("[FaceLocalTest] Could not create a neutral test result screen.");
                return;
            }
            try
            {
                Action<string, bool> onReturned = (returnedId, completed) =>
                {
                    Text label = resultPanel != null ?
                        resultPanel.GetComponentInChildren<Text>(true) : null;
                    if (label != null)
                        label.text = "로컬 얼굴 등록 테스트\n\n" +
                            (completed
                                ? "카메라 등록 완료. 서버 등록 상태를 별도로 조회해 확인하세요."
                                : "촬영이 종료됐습니다. 저장 완료 여부를 확인하세요.") +
                            "\n\n테스트 프로필 UUID:\n" + returnedId +
                            "\n\n이 화면은 일반 프로필 상세화면이 아닙니다." +
                            "\n기존 명함/프로필과 연결하지 않았습니다." +
                            "\n\n테스트가 끝나면 로컬 얼굴정보를 삭제하고 Play를 종료하세요.";
                    ClearLocalTestCredentials();
                };
                // The local face-search option is independent and defaults OFF.
                // The actual save still goes through CloudFaceLocalTestFactory,
                // which only constructs the hardcoded loopback transport.
                open.Invoke(component, new object[] { parsed.ToString("D"),
                    resultPanel, onReturned, publicRecognitionConsent, subjectPresent });
                Debug.Log("[FaceLocalTest] Local-only enrollment screen opened.");
            }
            catch (TargetInvocationException exception)
            {
                if (resultPanel != null)
                    UnityEngine.Object.Destroy(resultPanel);
                Debug.LogError("[FaceLocalTest] Could not open local enrollment: " +
                    (exception.InnerException?.GetType().Name ?? "InvocationError"));
            }
        }

        private static GameObject CreateLocalResultPanel(Component faceScreen, string uuid)
        {
            Canvas canvas = faceScreen.GetComponentInParent<Canvas>(true);
            Text sourceText = faceScreen.GetComponentInChildren<Text>(true);
            if (canvas == null || sourceText == null || sourceText.font == null)
                return null;

            // Destroy the previous result from this Play session if it exists.
            if (localResultPanel != null)
                UnityEngine.Object.Destroy(localResultPanel);

            var panel = new GameObject("LocalFaceTestResult",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(canvas.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.055f, 0.10f, 0.13f, 1f);

            var message = new GameObject("LocalFaceTestStatus",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            message.transform.SetParent(panel.transform, false);
            var messageRect = (RectTransform)message.transform;
            messageRect.anchorMin = new Vector2(0.08f, 0.08f);
            messageRect.anchorMax = new Vector2(0.92f, 0.92f);
            messageRect.offsetMin = Vector2.zero;
            messageRect.offsetMax = Vector2.zero;

            Text label = message.GetComponent<Text>();
            label.font = sourceText.font;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 30;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 15;
            label.resizeTextMaxSize = 30;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = "로컬 얼굴 등록 테스트\n\n처리 결과를 기다리는 중입니다." +
                "\n\n테스트 프로필 UUID:\n" + uuid;

            // ShowCloud deactivates this panel until its Hide() callback.
            panel.SetActive(false);
            localResultPanel = panel;
            return panel;
        }

        // Presentation lives in a named runtime assembly with autoReferenced=false.
        // Predefined Editor scripts cannot reference it directly. Read the two
        // public values by reflection without changing the project's asmdefs.
        private static Type PresentationType(string name)
        {
            return Type.GetType(name + ", AppPofo.presentation", false);
        }

        private static bool ReadRuntimeLocalCaptureEnabled()
        {
            Type factory = PresentationType(
                "AppPofo.Presentation.FaceRegistration.CloudFaceLocalTestFactory");
            PropertyInfo property = factory?.GetProperty("IsLocalCaptureEnabled",
                BindingFlags.Public | BindingFlags.Static);
            return property != null && property.GetValue(null) is bool enabled && enabled;
        }


    }
}
#endif
