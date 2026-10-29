using System;
using System.Collections;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.FaceRegistration;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    // Uses the current screen's canvas; no scenes/prefabs or biometric data are copied.
    public sealed class CloudFaceConsentPanel : MonoBehaviour
    {
        private GameObject modal;
        private string selectedId;
        private int generation;
        private bool busy;
        private Text status;
        private Font inheritedFont;
        private RectTransform sheet;
        private CloudPanelTheme theme;
        private bool faceAlreadyRegistered;

        private Transform Open(string name, string description)
        {
            Close();
            theme = CloudPanelTheme.Current;
            modal = new GameObject(name, typeof(RectTransform), typeof(Image));
            modal.layer = gameObject.layer;
            modal.transform.SetParent(transform.Find("SafeAreaRoot") ?? transform, false);
            var rect = (RectTransform)modal.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            modal.GetComponent<Image>().color = new Color(.12f,.08f,.2f,.8f);
            var surface = new GameObject("ConsentSurface", typeof(RectTransform), typeof(Image));
            surface.layer = gameObject.layer; surface.transform.SetParent(modal.transform, false);
            sheet = (RectTransform)surface.transform;
            sheet.anchorMin = sheet.anchorMax = new Vector2(.5f,.5f);
            sheet.sizeDelta = new Vector2(860, 1120);
            if (theme != null) theme.Surface(surface.GetComponent<Image>(), theme.background);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.layer = gameObject.layer; viewport.transform.SetParent(surface.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(40,40); viewportRect.offsetMax = new Vector2(-40,-40);
            viewport.GetComponent<Image>().color = Color.clear;
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.layer = gameObject.layer; content.transform.SetParent(viewport.transform, false);
            var area = (RectTransform)content.transform;
            area.anchorMin = new Vector2(0,1); area.anchorMax = Vector2.one;
            area.pivot = new Vector2(.5f,1); area.sizeDelta = Vector2.zero;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect; scroll.content = area; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16,16,24,24); layout.spacing = 24;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var heading = Label(content.transform, "Heading", name == "CloudScanConsent" ? "카메라로 명함 보기" : "얼굴 등록 관리", 78);
            heading.fontSize = 36;
            if (theme != null) heading.font = theme.boldFont;
            Label(content.transform, "Guide", name == "CloudScanConsent"
                ? "카메라에 얼굴을 비추면 등록된 명함이 나타납니다."
                : "내 명함에 사용할 얼굴 정보를 관리하세요.", 80);
            status = Label(content.transform, "Status", description, 150);
            FitSheet();
            return content.transform;
        }
        private void LateUpdate() => FitSheet();
        private void FitSheet()
        {
            if (sheet == null || modal == null) return;
            var bounds = ((RectTransform)modal.transform).rect;
            sheet.localScale = Vector3.one * Mathf.Min(1f,
                Mathf.Max(1, bounds.width - 32) / 860f, Mathf.Max(1, bounds.height - 32) / 1120f);
        }
        private Text Label(Transform parent, string name, string caption, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.layer = parent.gameObject.layer; go.transform.SetParent(parent,false);
            var text = go.GetComponent<Text>();
            if (inheritedFont == null)
                foreach (var reference in GetComponentsInChildren<Text>(true))
                    if (reference != text && reference.font != null) { inheritedFont = reference.font; break; }
            text.font = theme != null ? theme.regularFont : inheritedFont != null ? inheritedFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 26;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = new Color(.22f,.17f,.35f); text.supportRichText = false;
            text.text = caption; text.raycastTarget = false;
            go.GetComponent<LayoutElement>().minHeight = height;
            return text;
        }
        private Button Button(Transform parent, string name, string caption, Action action)
        {
            if (theme == null) theme = CloudPanelTheme.Current;
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.layer = parent.gameObject.layer; go.transform.SetParent(parent,false);
            go.GetComponent<LayoutElement>().preferredHeight = 86;
            bool primary = name == "ApproveScan" || name == "EnrollFace";
            var label = Label(go.transform,"Label",caption,86);
            label.color = primary ? Color.white : new Color(.30f,.25f,.47f);
            label.alignment = TextAnchor.MiddleCenter;
            var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var button = go.GetComponent<Button>();
            if (theme != null) theme.StyleButton(button, primary);
            button.onClick.AddListener(()=>action());
            return button;
        }
        private Toggle Consent(Transform parent, string name, string caption)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup), typeof(Toggle));
            row.layer = parent.gameObject.layer; row.transform.SetParent(parent,false);
            if (theme != null) theme.Surface(row.GetComponent<Image>(),theme.card);
            row.GetComponent<LayoutElement>().minHeight = row.GetComponent<LayoutElement>().preferredHeight = 116;
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24,24,20,20); layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var toggle = row.GetComponent<Toggle>();
            var box = new GameObject("Check",typeof(RectTransform),typeof(Image));
            box.layer=parent.gameObject.layer; box.transform.SetParent(row.transform,false);
            var boxLayout=box.AddComponent<LayoutElement>();
            boxLayout.minWidth=boxLayout.preferredWidth=36;boxLayout.minHeight=boxLayout.preferredHeight=36;
            var rect=(RectTransform)box.transform;
            rect.anchorMin=rect.anchorMax=new Vector2(0,.5f); rect.anchoredPosition=new Vector2(14,0); rect.sizeDelta=new Vector2(24,24);
            box.GetComponent<Image>().color=new Color(.82f,.77f,.94f);
            var mark=new GameObject("Checked",typeof(RectTransform),typeof(Image));
            mark.layer=parent.gameObject.layer;mark.transform.SetParent(box.transform,false);
            var inner=(RectTransform)mark.transform;inner.anchorMin=Vector2.zero;inner.anchorMax=Vector2.one;
            inner.offsetMin=new Vector2(4,4);inner.offsetMax=new Vector2(-4,-4);
            mark.GetComponent<Image>().color=new Color(.48f,.35f,.84f);
            mark.GetComponent<Image>().raycastTarget=false;
            toggle.graphic=mark.GetComponent<Image>();toggle.targetGraphic=box.GetComponent<Image>();
            var text = Label(row.transform,"Description",caption,76);
            text.alignment=TextAnchor.MiddleLeft;
            text.GetComponent<LayoutElement>().flexibleWidth=1;
            toggle.isOn=false;
            return toggle;
        }
        public void ResumeScan(Action approved)
        {
            if (!busy) StartCoroutine(ResumeSavedScan(approved));
        }
        private IEnumerator ResumeSavedScan(Action approved)
        {
            busy = true; string error = null; int version = generation;
            yield return CloudFaceRuntime.LoadScanConsent(e => error = e);
            busy = false;
            if (!isActiveAndEnabled || version != generation) yield break;
            if (error != null)
            {
                var parent = Open("CloudScanStatusError", error);
                Button(parent, "CloseScanStatusError", "닫기", Close);
            }
            else if (CloudFaceRuntime.CanScan) approved();
            else ShowScan(approved);
        }
        public void ShowScan(Action approved)
        {
            var parent = Open("CloudScanConsent", "얼굴 특징값을 서버로 보내 명함을 찾습니다. 검색 카메라의 사진·영상은 전송하지 않습니다. 공개 인식에 동의한 사람의 명함과 요약 프로필(소개·기술·대표 프로젝트·링크)을 볼 수 있습니다. 이 기기의 동의는 저장되며, 언제든 검색 이용을 중지하고 철회할 수 있습니다.");
            var capture=Consent(parent,"CaptureConsent","이 기기의 명함 검색 카메라 촬영과 얼굴 특징값 처리에 동의합니다.");
            var scan=Consent(parent,"ScanConsent","서버에 얼굴 특징값을 보내 명함을 찾는 데 동의합니다.");
            Button(parent,"ApproveScan","동의하고 카메라 열기",()=>
            {
                if (busy) return;
                if (!capture.isOn || !scan.isOn) { status.text="촬영과 서버 검색에 각각 동의해야 시작할 수 있습니다."; return; }
                StartCoroutine(ApproveScan(approved));
            });
            Button(parent,"CancelScan","취소",Close);
        }
        private IEnumerator ApproveScan(Action approved)
        {
            busy=true; int version=generation; string error=null;
            yield return CloudFaceRuntime.SetScanConsent(true,true,e=>error=e);
            busy=false;
            if (version!=generation || modal==null) { CloudFaceRuntime.StopScanning(); yield break; }
            if (error!=null) { status.text=error; yield break; }
            Close(); approved();
        }
        public void AddScanStopControl()
        {
            Transform parent=transform.Find("SafeAreaRoot")??transform;
            if (parent.Find("StopCloudFaceScan")!=null) return;
            var button=Button(parent,"StopCloudFaceScan","검색 이용 중지·동의 철회",()=>StartCoroutine(WithdrawScan()));
            var rect=(RectTransform)button.transform;
            rect.anchorMin=rect.anchorMax=new Vector2(.5f,0); rect.pivot=new Vector2(.5f,0);
            rect.anchoredPosition=new Vector2(0,24); rect.sizeDelta=new Vector2(440,64);
        }
        private IEnumerator WithdrawScan()
        {
            string error=null;
            yield return CloudFaceRuntime.RevokeScanConsent(e=>error=e);
            var parent=Open("CloudScanStopped",error??"검색을 중지하고 서버 이용 동의를 철회했습니다.");
            Button(parent,"CloseScanStopped","닫기",Close);
        }
        public void ShowManagement(string id, Action<string> enroll)
        {
            selectedId=id;
            var parent=Open("CloudFaceManagement","서버 얼굴 등록 상태를 확인하는 중...");
            bool replaceConfirmed=false, deleteConfirmed=false;
            var register=Button(parent,"EnrollFace","얼굴 등록·재등록",()=>
            {
                if (busy) return;
                if (faceAlreadyRegistered && !replaceConfirmed) { replaceConfirmed=true;status.text="얼굴을 다시 등록할까요? 새 등록이 완료될 때까지 기존 얼굴정보는 유지됩니다. ‘얼굴 다시 등록’을 한 번 더 누르면 진행합니다.";return; }
                Close();enroll(id);
            });
            var withdraw=Button(parent,"WithdrawDiscovery","공개 인식 중지",()=>
            { if (!busy) StartCoroutine(Remove(id,true)); });
            var delete=Button(parent,"DeleteFace","얼굴정보 삭제·저장 동의 철회",()=>
            {
                if (busy) return;
                if (!deleteConfirmed) { deleteConfirmed=true;status.text="얼굴정보를 삭제하고 저장·공개 인식 동의를 철회하시겠습니까? 텍스트 프로필은 유지됩니다. 같은 버튼을 눌러 삭제를 확인하세요.";return; }
                StartCoroutine(Remove(id,false));
            });
            register.interactable=withdraw.interactable=delete.interactable=false;
            Button(parent,"RefreshFace","상태 새로고침",()=> { if(!busy) StartCoroutine(Status(id,register,withdraw,delete)); });
            Button(parent,"CloseFaceManagement","닫기",Close);
            StartCoroutine(Status(id,register,withdraw,delete));
        }
        private IEnumerator Status(string id, Button register, Button withdraw, Button delete)
        {
            busy=true; int version=generation; CloudFaceStatusData state=null;
            yield return new CloudFaceHttpsTransport().GetStatus(id,null,(_,value)=>state=value);
            busy=false;
            if (version!=generation || modal==null || selectedId!=id) yield break;
            status.text=state==null?"서버 얼굴 상태를 확인하지 못했습니다.":
                "얼굴 등록: "+(state.registered?"완료":"없음")+" · 공개 인식: "+(state.discoverable?"허용":"중지")+
                "\n얼굴 등록은 선택 사항이며 프로필은 별도로 유지됩니다.";
            faceAlreadyRegistered=state!=null&&state.registered;
            var label=register.GetComponentInChildren<Text>();
            if(label!=null)label.text=faceAlreadyRegistered?"얼굴 다시 등록":"얼굴 등록";
            register.interactable=state!=null;
            withdraw.interactable=state!=null&&state.discoverable;
            delete.interactable=state!=null&&state.registered;
        }
        private IEnumerator Remove(string id,bool publicOnly)
        {
            busy=true;int version=generation;string error=null;
            yield return new CloudFaceHttpsTransport().Remove(id,publicOnly,e=>error=e);
            busy=false;
            if (version==generation&&modal!=null) status.text=error??"변경한 서버 상태를 확인했습니다. 상태 새로고침으로 확인할 수 있습니다.";
        }
        public void Close()
        {
            generation++;
            if(modal!=null) {modal.SetActive(false);Destroy(modal);modal=null;}
        }
        private void OnDisable() => Close();
    }
}
