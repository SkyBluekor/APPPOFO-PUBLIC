using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 이 컴포넌트는 여러 입력 화면에서 공통으로 사용합니다.
//
// PC
// - Tab         : 다음 입력칸
// - Shift + Tab : 이전 입력칸
//
// Android
// - 한 줄 입력칸에서 키보드의 완료 동작이 발생하면 다음 입력칸으로 이동
// - Bio처럼 여러 줄 입력칸은 Enter 줄바꿈 유지
//
// CreateProfile, EditProfile, Skill, Project, Link 등
// InputField가 존재하는 여러 화면에서 같은 컴포넌트를 재사용할 수 있습니다.

namespace AppPofo.Presentation.Common
{
    public sealed class InputFieldFocusNavigator : MonoBehaviour
    {
        [Header("입력 순서")]
        [SerializeField]
        private InputField[] inputFields;

        [Header("마지막 입력 후 실행할 버튼 (선택)")]
        [SerializeField]
        private Button submitButton;

        [SerializeField]
        private bool submitOnLastField = false;


        private UnityAction<string>[] endEditHandlers;
        private bool eventsRegistered;
        private Coroutine pendingTabMove;

        // Reuse the same navigator when an onboarding step changes visible fields.
        public void Configure(InputField[] fields, Button submit)
        {
            CancelPendingTabMove();
            UnregisterEndEditEvents();
            inputFields = fields ?? System.Array.Empty<InputField>();
            submitButton = submit;
            if (isActiveAndEnabled) RegisterEndEditEvents();
        }


        private void OnEnable()
        {
            RegisterEndEditEvents();
        }


        private void Update()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            HandleTabNavigation();
#endif
        }


        private void OnDisable()
        {
            CancelPendingTabMove();
            UnregisterEndEditEvents();
        }


        // =========================================================
        // PC Tab 이동
        // =========================================================

        // Tab을 누르면 다음 입력칸,
        // Shift + Tab을 누르면 이전 입력칸으로 이동합니다.
        private void HandleTabNavigation()
        {
            if (pendingTabMove != null || !WasTabPressed())
                return;


            int currentIndex =
                FindCurrentInputFieldIndex();


            // 현재 선택된 InputField가 없다면 아무것도 하지 않습니다.
            if (currentIndex < 0)
                return;


            bool moveBackward =
                IsShiftPressed();


            if (moveBackward && currentIndex == 0) return;
            pendingTabMove = StartCoroutine(MoveAfterCompositionCommit(currentIndex, moveBackward));
        }

        private IEnumerator MoveAfterCompositionCommit(int currentIndex, bool moveBackward)
        {
            InputField source = inputFields[currentIndex];
            var system = EventSystem.current;
            if (source == null || system == null || system.currentSelectedGameObject != source.gameObject)
                yield break;
            bool hadComposition = CurrentComposition.Length > 0;
            // uGUI commits the pending composition into the source on deselect.
            // Keep the next field inactive until the subsequent native commit event has passed.
            system.SetSelectedGameObject(null);

            yield return null;
            while (hadComposition && CurrentComposition.Length > 0 &&
                system != null && system.currentSelectedGameObject == null)
                yield return null;
            // The OS commit must not become input for a newly selected field.
            if (hadComposition) yield return null;

            pendingTabMove = null;
            if (source == null || system == null || system != EventSystem.current ||
                system.currentSelectedGameObject != null) yield break;

            if (moveBackward) MoveToPreviousField(currentIndex);
            else MoveToNextField(currentIndex);
        }

        private static string CurrentComposition
        {
            get
            {
                var system = EventSystem.current;
                return system != null && system.currentInputModule != null
                    ? system.currentInputModule.input.compositionString : Input.compositionString;
            }
        }

        private void CancelPendingTabMove()
        {
            if (pendingTabMove != null) StopCoroutine(pendingTabMove);
            pendingTabMove = null;
        }


        // 다음 입력칸으로 이동합니다.
        private void MoveToNextField(int currentIndex)
        {
            int nextIndex =
                currentIndex + 1;


            // 마지막 입력칸이면 더 이상 앞으로 이동하지 않습니다.
            if (nextIndex >= inputFields.Length)
            {
                ClearFocus();

                return;
            }


            FocusInputField(nextIndex);
        }


        // 이전 입력칸으로 이동합니다.
        private void MoveToPreviousField(int currentIndex)
        {
            int previousIndex =
                currentIndex - 1;


            // 첫 번째 입력칸에서 Shift + Tab을 눌렀다면
            // 더 이상 이동하지 않습니다.
            if (previousIndex < 0)
                return;


            FocusInputField(previousIndex);
        }


        // =========================================================
        // 현재 InputField 확인
        // =========================================================

        // 현재 EventSystem에서 선택된 오브젝트가
        // inputFields 배열의 몇 번째인지 찾습니다.
        private int FindCurrentInputFieldIndex()
        {
            if (EventSystem.current == null)
                return -1;


            GameObject selectedObject =
                EventSystem.current.currentSelectedGameObject;


            if (selectedObject == null)
                return -1;


            for (int i = 0; i < inputFields.Length; i++)
            {
                InputField field =
                    inputFields[i];


                if (field == null)
                    continue;


                if (field.gameObject == selectedObject)
                {
                    return i;
                }
            }


            return -1;
        }


        // =========================================================
        // InputField 포커스 이동
        // =========================================================

        // 지정한 InputField를 선택하고
        // 실제 텍스트 입력 상태까지 활성화합니다.
        private void FocusInputField(int index)
        {
            if (inputFields == null)
                return;


            if (index < 0 ||
                index >= inputFields.Length)
            {
                return;
            }


            InputField nextField =
                inputFields[index];


            if (nextField == null)
                return;


            if (!nextField.interactable ||
                !nextField.gameObject.activeInHierarchy)
            {
                return;
            }


            nextField.Select();
            nextField.ActivateInputField();
        }


        // 입력 포커스와 키보드를 종료합니다.
        private void ClearFocus()
        {
            int currentIndex =
                FindCurrentInputFieldIndex();


            if (EventSystem.current != null)
            {
                // OnDeselect owns composition commit and deactivation. Do not
                // deactivate first and then commit the same IME sequence again.
                EventSystem.current.SetSelectedGameObject(null);
                return;
            }
            if (currentIndex >= 0 &&
                currentIndex < inputFields.Length)
            {
                InputField currentField =
                    inputFields[currentIndex];


                if (currentField != null)
                {
                    currentField.DeactivateInputField();
                }
            }


        }


        // =========================================================
        // Android 입력 완료 처리
        // =========================================================

        // 각 InputField의 onEndEdit 이벤트를 등록합니다.
        private void RegisterEndEditEvents()
        {
            if (eventsRegistered)
                return;


            if (inputFields == null ||
                inputFields.Length == 0)
            {
                return;
            }


            endEditHandlers =
                new UnityAction<string>[inputFields.Length];


            for (int i = 0; i < inputFields.Length; i++)
            {
                InputField field =
                    inputFields[i];


                if (field == null)
                    continue;


                int capturedIndex = i;


                UnityAction<string> handler =
                    _ => OnInputFieldEndEdit(capturedIndex);


                endEditHandlers[i] =
                    handler;


                field.onEndEdit.AddListener(
                    handler);
            }


            eventsRegistered = true;
        }


        // 화면이 비활성화될 때
        // 등록했던 이벤트를 제거합니다.
        private void UnregisterEndEditEvents()
        {
            if (!eventsRegistered)
                return;


            if (inputFields == null ||
                endEditHandlers == null)
            {
                eventsRegistered = false;

                return;
            }


            for (int i = 0; i < inputFields.Length; i++)
            {
                if (i >= endEditHandlers.Length)
                    break;


                InputField field =
                    inputFields[i];


                UnityAction<string> handler =
                    endEditHandlers[i];


                if (field == null ||
                    handler == null)
                {
                    continue;
                }


                field.onEndEdit.RemoveListener(
                    handler);
            }


            eventsRegistered = false;
            endEditHandlers = null;
        }


        // Android에서 한 줄 입력칸의 입력이 끝났을 때
        // 키보드 완료 상태인지 확인합니다.
        private void OnInputFieldEndEdit(int index)
        {
#if UNITY_ANDROID && !UNITY_EDITOR

            if (inputFields == null)
                return;


            if (index < 0 ||
                index >= inputFields.Length)
            {
                return;
            }


            InputField field =
                inputFields[index];


            if (field == null)
                return;


            // Android 뒤로가기로 키보드를 닫은 경우처럼
            // 입력 자체가 취소됐다면 다음으로 이동하지 않습니다.
            if (field.wasCanceled)
                return;


            // Bio처럼 여러 줄 입력칸은
            // Enter를 줄바꿈으로 사용해야 하므로
            // 자동으로 다음 칸으로 이동시키지 않습니다.
            if (field.lineType != InputField.LineType.SingleLine)
                return;


            TouchScreenKeyboard keyboard =
                field.touchScreenKeyboard;


            if (keyboard == null)
                return;


            // 키보드가 정상적인 완료(Done) 상태일 때만 처리합니다.
            //
            // 단순히 다른 곳을 터치해서 포커스가 빠진 경우에는
            // 다음 입력칸으로 강제 이동시키지 않습니다.
            if (keyboard.status != TouchScreenKeyboard.Status.Done)
                return;


            StartCoroutine(
                MoveAfterAndroidSubmit(index));

#endif
        }


        // Android 키보드가 닫힌 직후 바로 InputField를 활성화하면
        // 일부 기기에서 키보드 전환이 불안정할 수 있기 때문에
        // 한 프레임 기다린 뒤 다음 입력칸으로 이동합니다.
        private IEnumerator MoveAfterAndroidSubmit(
            int currentIndex)
        {
            yield return null;


            // 사용자가 그 사이에 다른 InputField나 버튼을 직접 눌렀다면
            // 자동 이동으로 사용자의 선택을 덮어쓰지 않습니다.
            if (EventSystem.current != null)
            {
                GameObject selectedObject =
                    EventSystem.current.currentSelectedGameObject;


                if (selectedObject != null &&
                    selectedObject != inputFields[currentIndex].gameObject)
                {
                    yield break;
                }
            }


            int nextIndex =
                currentIndex + 1;


            // 다음 InputField가 존재하면 이동합니다.
            if (nextIndex < inputFields.Length)
            {
                FocusInputField(nextIndex);

                yield break;
            }


            // 마지막 InputField라면 키보드와 포커스를 종료합니다.
            ClearFocus();


            // 필요한 패널에서만 활성화하면
            // 마지막 입력 완료와 동시에 저장/추가 버튼을 실행할 수 있습니다.
            if (submitOnLastField &&
                submitButton != null)
            {
                submitButton.onClick.Invoke();
            }
        }


        // =========================================================
        // PC 키 입력
        // =========================================================

        // PC에서 Tab 키가 이번 프레임에 눌렸는지 확인합니다.
        private bool WasTabPressed()
        {
            if (Keyboard.current == null)
                return false;


            return Keyboard.current.tabKey.wasPressedThisFrame;
        }


        // Shift + Tab 구분을 위해
        // 왼쪽 또는 오른쪽 Shift 키 상태를 확인합니다.
        private bool IsShiftPressed()
        {
            if (Keyboard.current == null)
                return false;


            return
                Keyboard.current.leftShiftKey.isPressed ||
                Keyboard.current.rightShiftKey.isPressed;
        }
    }
}
