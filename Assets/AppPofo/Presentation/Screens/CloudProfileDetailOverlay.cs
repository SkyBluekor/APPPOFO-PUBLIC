using System;
using AppPofo.Presentation.Common;
using UnityEngine;

namespace AppPofo.Presentation.Screens
{
    // Compatibility presenter. The existing scene's ProfileDetailScreen owns all UI.
    public sealed class CloudProfileDetailOverlay : MonoBehaviour
    {
        private ProfileDetailScreen view;
        private CloudProfileDetail selectedDetail;
        private Action<string> requestLinkCode, requestFaceRegistration, requestDelete;
        private bool deleting;
        private Action<CloudProfileDetail> requestEdit;
        private string issuedCode;
        private bool issuingCode;
        private bool visible;
        private bool hadAuthorization;
        private CloudAccessRole openedRole;
        public int Version { get; private set; }
        public bool IsVisible => visible && view != null && view.gameObject.activeSelf;
        public ProfileDetailScreen View => view;
        public CloudProfileDetail SelectedDetail => selectedDetail;

        public void Initialize(ProfileDetailScreen sharedView) => view = sharedView;

        private void Update()
        {
            if (!IsVisible) return;
            if (CloudModeSession.Mode != ProfileDataMode.Cloud) { Hide(); return; }
            if (openedRole != CloudModeSession.AccessRole) { Hide(); return; }
            bool authorized = CloudProfileApi.CanRequest;
            if (hadAuthorization && !authorized)
            {
                issuedCode = null; issuingCode = false;
                view.SetCloudStatus("서버 연결 또는 인증이 만료됐습니다. 다시 연결해 주세요.");
            }
            hadAuthorization = authorized;
            RefreshPermissions();
        }

        private void RefreshPermissions() => view.RefreshCloudPermissions(
            CloudProfileApi.CanRequest && !deleting, selectedDetail != null && requestLinkCode != null && !issuingCode,
            issuedCode != null);

        public void ShowLoading()
        {
            Version++;
            ClearSelection(); visible = true;
            openedRole = CloudModeSession.AccessRole; hadAuthorization = CloudProfileApi.CanRequest;
            view.ShowCloudLoading(Hide);
        }

        public void ShowError(string error)
        {
            if (!IsVisible) return;
            ClearSelection();
            view.ShowCloudLoading(Hide, "프로필 조회 실패");
            view.SetCloudStatus(error ?? "프로필을 불러오지 못했습니다.");
        }

        public void ShowProfile(CloudProfileDetail detail, Action<string> onRequestCode,
            Action<string> onFaceRegistration = null, Action<CloudProfileDetail> onEdit = null,
            Action<string> onDelete = null)
        {
            if (!IsVisible) return;
            selectedDetail = detail; issuedCode = null; issuingCode = false;
            requestLinkCode = onRequestCode; requestFaceRegistration = onFaceRegistration; requestEdit = onEdit;
            requestDelete = onDelete;
            view.ShowCloudProfile(detail, onEdit != null ? RequestEdit : (Action)null,
                onFaceRegistration != null ? RequestFaceRegistration : (Action)null,
                onRequestCode != null ? RequestLinkCode : (Action)null, CopyLinkCode,
                onDelete != null ? RequestDelete : (Action)null);
        }

        private bool CanAct => IsVisible && selectedDetail != null && CloudProfileApi.CanRequest;
        private void RequestEdit() { if (CanAct) requestEdit?.Invoke(selectedDetail); }
        private void RequestFaceRegistration() { if (CanAct) requestFaceRegistration?.Invoke(selectedDetail.id); }
        private void RequestDelete()
        {
            if (!CanAct || deleting || requestDelete == null) return;
            deleting = true; RefreshPermissions();
            view.SetCloudStatus("프로필을 삭제하는 중...");
            requestDelete(selectedDetail.id);
        }
        public void ShowDeleteError(string error)
        {
            if (!IsVisible) return;
            deleting = false; view.CloseDeleteConfirm();
            view.SetCloudStatus(error); RefreshPermissions();
        }
        private void RequestLinkCode()
        {
            if (!CanAct || issuingCode || requestLinkCode == null) return;
            issuedCode = null; issuingCode = true; RefreshPermissions();
            view.SetCloudStatus("연결 코드를 발급하는 중... 기존 코드가 있다면 무효화됩니다.");
            requestLinkCode(selectedDetail.id);
        }
        public void ShowLinkCode(string code)
        {
            if (!CanAct || !CloudProfileLinkApi.ValidCode(code)) return;
            issuingCode = false; issuedCode = code; RefreshPermissions();
            view.SetCloudStatus("다른 기기에서 프로필을 연결할 때 사용하세요.\n" + code +
                "\n새 코드를 발급하면 이 코드는 무효화됩니다. 코드를 가진 사람은 프로필을 수정할 수 있으니 공유하지 마세요.");
        }
        public void ShowLinkCodeError(string error)
        {
            if (!IsVisible) return;
            issuingCode = false; issuedCode = null; RefreshPermissions();
            view.SetCloudStatus(error ?? "연결 코드를 발급하지 못했습니다.");
        }
        private void CopyLinkCode()
        {
            if (!CanAct || issuedCode == null) return;
            GUIUtility.systemCopyBuffer = issuedCode;
            view.SetCloudStatus("연결 코드 복사 완료");
        }
        private void ClearSelection()
        {
            selectedDetail = null; issuedCode = null; issuingCode = false;
            requestLinkCode = requestFaceRegistration = null; requestEdit = null;
            requestDelete = null; deleting = false;
        }
        public void Hide()
        {
            if (!visible) return;
            Version++; visible = false; ClearSelection();
            if (view != null) view.HideCloudPresentation();
        }
        private void OnDisable() => Hide();
    }
}
