#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using AppPofo.Presentation.Common;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    // Ctrl+Shift+A opens authentication on Windows. Discovering the shortcut
    // grants no privileges: the server must still authorize every request.
    public sealed class CloudAdminLoginPanel : MonoBehaviour
    {
        private GameObject trigger;
        private GameObject dialog;
        private GameObject loginGroup;
        private GameObject signOutGroup;
        private InputField codeField;
        private Text status;
        private Text title;
        private Text description;
        private Button submitButton;
        private RectTransform sheetRect;
        private RectTransform safeAreaRect;
        private GameObject previousSelection;
        private Sprite roundedSurface;
        private Texture2D surfaceTexture;
        private CloudConnectionStatus displayedStatus;
        private CloudAccessRole displayedRole;
        private ProfileDataMode displayedMode;
        private bool awaitingVerification;
        private bool dialogOpen;

        public bool IsDialogOpen => dialogOpen;

        public void Initialize(Transform safeArea, Transform topBar, Font font)
        {
            if (trigger != null) return;
            var chosenFont = font != null ? font :
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            safeAreaRect = safeArea as RectTransform;
            roundedSurface = MakeRoundedSurface();

            trigger = new GameObject("CloudAdministrationMenu",
                typeof(RectTransform), typeof(Image), typeof(Button));
            trigger.transform.SetParent(topBar, false);
            trigger.layer = topBar.gameObject.layer;
            var triggerRect = (RectTransform)trigger.transform;
            triggerRect.anchorMin = triggerRect.anchorMax = new Vector2(1f, .5f);
            triggerRect.pivot = new Vector2(1f, .5f);
            triggerRect.sizeDelta = new Vector2(124f, 64f);
            triggerRect.anchoredPosition = new Vector2(-136f, 0);
            StyleSurface(trigger.GetComponent<Image>(), new Color(.91f, .88f, .98f));
            trigger.SetActive(false);
            trigger.GetComponent<Button>().onClick.AddListener(Open);
            var triggerLabel = MakeText(trigger.transform, chosenFont,
                "관리자", 25, TextAnchor.MiddleCenter);
            Stretch((RectTransform)triggerLabel.transform, 0, 0, 0, 0);
            triggerLabel.color = new Color(.39f, .34f, .56f);

            // A modal is only created once and stays inactive until explicitly opened.
            dialog = new GameObject("CloudAdministrationDialog",
                typeof(RectTransform), typeof(Image), typeof(Button));
            dialog.transform.SetParent(safeArea, false);
            dialog.layer = safeArea.gameObject.layer;
            Stretch((RectTransform)dialog.transform, 0, 0, 0, 0);
            dialog.GetComponent<Image>().color = new Color(.13f, .10f, .24f, .48f);
            dialog.GetComponent<Button>().onClick.AddListener(Close);
            dialog.GetComponent<Button>().transition = Selectable.Transition.None;
            dialog.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };

            var sheet = new GameObject("AdminLoginSheet",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(Shadow),
                typeof(VerticalLayoutGroup));
            sheet.transform.SetParent(dialog.transform, false);
            sheet.layer = dialog.layer;
            sheetRect = (RectTransform)sheet.transform;
            sheetRect.anchorMin = sheetRect.anchorMax = new Vector2(.5f, .5f);
            sheetRect.pivot = new Vector2(.5f, .5f);
            sheetRect.anchoredPosition = Vector2.zero;
            // Scales with reference canvas, with space around it on portrait screens.
            sheetRect.sizeDelta = new Vector2(760f, 760f);
            StyleSurface(sheet.GetComponent<Image>(), new Color(.97f, .95f, 1f));
            // Consume clicks on empty card space instead of closing the backdrop.
            sheet.GetComponent<Button>().transition = Selectable.Transition.None;
            sheet.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
            var shadow = sheet.GetComponent<Shadow>();
            shadow.effectColor = new Color(.27f, .16f, .47f, .18f);
            shadow.effectDistance = new Vector2(0, -8f);
            var sheetLayout = sheet.GetComponent<VerticalLayoutGroup>();
            sheetLayout.padding = new RectOffset(42, 42, 36, 36);
            sheetLayout.spacing = 18;
            sheetLayout.childControlWidth = true;
            sheetLayout.childControlHeight = true;
            sheetLayout.childForceExpandHeight = false;
            sheetLayout.childForceExpandWidth = true;

            title = MakeText(sheet.transform, chosenFont, "관리자 인증",
                36, TextAnchor.MiddleLeft, 57);
            title.fontStyle = FontStyle.Bold;
            description = MakeText(sheet.transform, chosenFont,
                "인증 코드를 입력해 관리자 모드에 접속하세요.",
                23, TextAnchor.MiddleLeft, 58);

            loginGroup = new GameObject("AdministratorCredentials",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            loginGroup.transform.SetParent(sheet.transform, false);
            var loginLayout = loginGroup.GetComponent<VerticalLayoutGroup>();
            loginLayout.spacing = 12;
            loginLayout.childControlWidth = true;
            loginLayout.childControlHeight = true;
            loginLayout.childForceExpandHeight = false;
            loginLayout.childForceExpandWidth = true;
            loginGroup.GetComponent<LayoutElement>().preferredHeight = 224;

            MakeText(loginGroup.transform, chosenFont, "관리자 인증 코드",
                23, TextAnchor.MiddleLeft, 42);
            var fieldObject = new GameObject("AdministratorCode",
                typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            fieldObject.transform.SetParent(loginGroup.transform, false);
            fieldObject.layer = dialog.layer;
            StyleSurface(fieldObject.GetComponent<Image>(), new Color(.91f, .88f, .97f));
            fieldObject.GetComponent<LayoutElement>().preferredHeight = 78;
            codeField = fieldObject.GetComponent<InputField>();
            codeField.contentType = InputField.ContentType.Password;
            codeField.lineType = InputField.LineType.SingleLine;
            codeField.characterLimit = 4096;
            var fieldText = MakeText(fieldObject.transform, chosenFont,
                "", 24, TextAnchor.MiddleLeft);
            fieldText.color = new Color(.22f, .18f, .32f);
            Stretch((RectTransform)fieldText.transform, 22, 0, 22, 0);
            codeField.textComponent = fieldText;
            codeField.targetGraphic = fieldObject.GetComponent<Image>();
            var placeholder = MakeText(fieldObject.transform, chosenFont,
                "인증 코드 붙여넣기", 24, TextAnchor.MiddleLeft);
            placeholder.color = new Color(.53f, .47f, .66f);
            Stretch((RectTransform)placeholder.transform, 22, 0, 22, 0);
            codeField.placeholder = placeholder;
            codeField.onEndEdit.AddListener(SubmitOnEnter);
            submitButton = MakeButton(loginGroup.transform, chosenFont, "접속", SubmitCode, true);

            signOutGroup = new GameObject("AdministratorActions",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            signOutGroup.transform.SetParent(sheet.transform, false);
            var signOutLayout = signOutGroup.GetComponent<VerticalLayoutGroup>();
            signOutLayout.childControlHeight = true;
            signOutLayout.childControlWidth = true;
            signOutLayout.childForceExpandHeight = false;
            signOutGroup.GetComponent<LayoutElement>().preferredHeight = 78;
            MakeButton(signOutGroup.transform, chosenFont, "이 기기에서 관리자 로그아웃",
                SignOut, true);

            status = MakeText(sheet.transform, chosenFont, "",
                22, TextAnchor.MiddleLeft, 90);
            MakeButton(sheet.transform, chosenFont, "닫기", Close, false);

            dialog.SetActive(false);
            Refresh();
        }

        private void Open()
        {
            if (dialog == null || dialogOpen || CloudModeSession.Mode != ProfileDataMode.Cloud) return;
            previousSelection = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
            dialogOpen = true;
            dialog.SetActive(true);
            dialog.transform.SetAsLastSibling();
            Refresh();
            FitSheet();
            if (loginGroup.activeSelf)
            {
                codeField.Select();
                codeField.ActivateInputField();
            }
        }

        public void Close()
        {
            bool wasOpen = dialogOpen;
            dialogOpen = false;
            if (codeField != null) codeField.text = "";
            if (dialog != null) dialog.SetActive(false);
            if (wasOpen && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection != null &&
                    previousSelection.activeInHierarchy ? previousSelection : null);
            previousSelection = null;
        }

        private void Update()
        {
            if (dialog == null) return;
            if (displayedMode != CloudModeSession.Mode || displayedStatus != CloudModeSession.Status ||
                displayedRole != CloudModeSession.AccessRole) Refresh();
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            HandleKeyboard(
                keyboard.ctrlKey.isPressed,
                keyboard.shiftKey.isPressed,
                keyboard.aKey.wasPressedThisFrame,
                keyboard.escapeKey.wasPressedThisFrame,
                keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
        }

        // Keeping key interpretation separate from InputSystem's frame clock
        // allows EditMode tests to exercise the actual UI actions deterministically.
        private void HandleKeyboard(bool controlHeld, bool shiftHeld, bool aJustPressed,
            bool escapeJustPressed, bool enterJustPressed)
        {
            if (dialogOpen && escapeJustPressed)
            {
                Close();
                return;
            }
            if (!dialogOpen && controlHeld && shiftHeld && aJustPressed) Open();
            else if (dialogOpen && codeField.isFocused && submitButton.interactable &&
                enterJustPressed) SubmitCode();
        }

        private void LateUpdate()
        {
            if (dialogOpen) FitSheet();
        }

        private void FitSheet()
        {
            if (safeAreaRect == null || sheetRect == null) return;
            float scale = Mathf.Min(1f, Mathf.Max(1f, safeAreaRect.rect.width - 64f) / 760f,
                Mathf.Max(1f, safeAreaRect.rect.height - 64f) / 760f);
            sheetRect.localScale = Vector3.one * scale;
        }

        private void OnDisable() => Close();

        private void OnDestroy()
        {
            Release(dialog);
            Release(trigger);
            Release(roundedSurface);
            Release(surfaceTexture);
        }

        private static void Release(Object owned)
        {
            if (owned == null) return;
            if (UnityEngine.Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }

        private void SubmitCode()
        {
            if (codeField == null || !dialogOpen || !submitButton.interactable) return;
            string proposedCode = codeField.text;
            codeField.text = "";
            if (!CloudConnectionRuntime.SubmitAdministratorCode(proposedCode))
            {
                status.text = "코드 형식을 확인해 주세요. 최소 32자 이상이어야 합니다.";
                codeField.ActivateInputField();
                return;
            }
            awaitingVerification = true;
            submitButton.interactable = false;
            status.text = "서버에서 관리자 권한을 확인하고 있습니다...";
        }

        private void SubmitOnEnter(string unused)
        {
            // InputField can finish editing before this component's Update runs.
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame)) SubmitCode();
        }

        private void SignOut()
        {
            CloudConnectionRuntime.SignOutAdministrator();
            Close();
        }

        public void Refresh()
        {
            if (trigger == null || dialog == null) return;
            bool cloudMode = CloudModeSession.Mode == ProfileDataMode.Cloud;
            displayedMode = CloudModeSession.Mode;
            displayedStatus = CloudModeSession.Status;
            displayedRole = CloudModeSession.AccessRole;
            bool administrator = cloudMode &&
                                 CloudModeSession.Status == CloudConnectionStatus.ServerReachable &&
                                 CloudModeSession.AccessRole == CloudAccessRole.Administrator &&
                                 CloudProfileApi.CanRequest;
            trigger.SetActive(administrator);
            if (!cloudMode)
            {
                Close();
                return;
            }
            // Never reveal a credential field just because server mode was selected.
            dialog.SetActive(dialogOpen);
            if (!dialogOpen) return;
            loginGroup.SetActive(!administrator);
            signOutGroup.SetActive(administrator);
            title.text = administrator ? "관리자 모드" : "관리자 인증";
            description.text = administrator ? "서버 관리 권한으로 접속 중입니다." :
                "인증 코드를 입력해 관리자 모드에 접속하세요.";
            submitButton.interactable = !administrator &&
                CloudModeSession.Status != CloudConnectionStatus.Connecting;
            if (administrator)
            {
                awaitingVerification = false;
                status.text = "관리자 권한 확인 완료. 인증정보는 Windows 계정에 보호 저장됩니다.";
            }
            else if (CloudModeSession.Status == CloudConnectionStatus.Connecting)
                status.text = "서버 연결 및 관리자 권한을 확인하고 있습니다...";
            else if (CloudModeSession.Status == CloudConnectionStatus.Offline)
                status.text = "서버에 연결하지 못했습니다. 연결을 확인하고 다시 접속해 주세요.";
            else if (awaitingVerification)
            {
                awaitingVerification = false;
                status.text = "인증 코드를 확인해 주세요. 관리자 인증에 실패했습니다.";
            }
            else
                status.text = "인증된 기기는 다음 실행부터 자동으로 접속합니다.";
        }

        private static Text MakeText(Transform parent, Font font, string value,
            int fontSize, TextAnchor alignment, float height = 0)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(.34f, .29f, .50f);
            text.supportRichText = false;
            text.raycastTarget = false;
            text.text = value;
            if (height > 0)
            {
                var element = go.AddComponent<LayoutElement>();
                element.preferredHeight = height;
            }
            return text;
        }

        private Button MakeButton(Transform parent, Font font, string caption,
            UnityAction action, bool emphasized)
        {
            var go = new GameObject("AdminAction", typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            go.GetComponent<LayoutElement>().preferredHeight = 78;
            StyleSurface(go.GetComponent<Image>(), emphasized ?
                new Color(.46f, .35f, .85f) : new Color(.91f, .88f, .97f));
            var button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            button.onClick.AddListener(action);
            var label = MakeText(go.transform, font, caption, 23, TextAnchor.MiddleCenter);
            label.color = emphasized ? Color.white : new Color(.31f, .27f, .47f);
            Stretch((RectTransform)label.transform, 0, 0, 0, 0);
            return button;
        }

        private void StyleSurface(Image graphic, Color color)
        {
            graphic.sprite = roundedSurface;
            graphic.type = Image.Type.Sliced;
            graphic.color = color;
        }

        private Sprite MakeRoundedSurface()
        {
            const int size = 72;
            const float radius = 22f;
            surfaceTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "AdminRoundedSurface", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x + .5f - size / 2f) - (size / 2f - radius);
                float dy = Mathf.Abs(y + .5f - size / 2f) - (size / 2f - radius);
                float distance = Mathf.Min(Mathf.Max(dx, dy), 0f) +
                    new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude - radius;
                pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(.5f - distance));
            }
            surfaceTexture.SetPixels(pixels);
            surfaceTexture.Apply(false, false);
            return Sprite.Create(surfaceTexture, new Rect(0, 0, size, size),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
        }

        private static void Stretch(RectTransform rect,
            float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
#endif
