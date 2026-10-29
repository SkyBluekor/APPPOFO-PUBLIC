#if UNITY_ANDROID || UNITY_EDITOR || UNITY_STANDALONE_WIN
using System;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    // Small self-contained code input added to cloud list at runtime on Android.
    // A linked device is remembered by its AndroidKeyStore identity, not the code.
    public sealed class CloudProfileConnectPanel : MonoBehaviour
    {
        private GameObject root;
        private InputField codeInput;
        private Text feedback;
        private Action<string> onConnect;
        private GameObject fields;

        public void Initialize(Transform parent, Font font, Action<string> connect)
        {
            if (root!=null) return;
            onConnect=connect;
            var chosenFont=font!=null?font:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root=new GameObject("ExistingCloudProfileLink",typeof(RectTransform),
                typeof(VerticalLayoutGroup),typeof(LayoutElement));
            root.transform.SetParent(parent,false);
            root.transform.SetSiblingIndex(1); // below the search toolbar
            var layout=root.GetComponent<VerticalLayoutGroup>();
            layout.padding=new RectOffset(20,20,12,12);
            layout.spacing=6;
            layout.childControlWidth=true;
            layout.childControlHeight=true;
            layout.childForceExpandHeight=false;
            root.GetComponent<LayoutElement>().preferredHeight=92;

            var disclosure = new GameObject("ShowProfileLink",typeof(RectTransform),typeof(Image),typeof(Button),typeof(LayoutElement));
            disclosure.transform.SetParent(root.transform,false);
            disclosure.GetComponent<LayoutElement>().preferredHeight=68;
            var theme=AppPofo.Presentation.Common.CloudPanelTheme.Current;
            if(theme!=null)theme.StyleButton(disclosure.GetComponent<Button>(),false);
            var disclosureLabel=Label(disclosure.transform,chosenFont,"다른 기기의 프로필 가져오기  ›",23,68);
            var disclosureRect=(RectTransform)disclosureLabel.transform;
            disclosureRect.anchorMin=Vector2.zero;disclosureRect.anchorMax=Vector2.one;
            disclosureRect.offsetMin=disclosureRect.offsetMax=Vector2.zero;
            fields=new GameObject("LinkFields",typeof(RectTransform),typeof(VerticalLayoutGroup),typeof(LayoutElement));
            fields.transform.SetParent(root.transform,false);
            var fieldLayout=fields.GetComponent<VerticalLayoutGroup>();
            fieldLayout.spacing=10;fieldLayout.childControlWidth=fieldLayout.childControlHeight=true;
            fieldLayout.childForceExpandHeight=false;
            fields.GetComponent<LayoutElement>().preferredHeight=186;
            disclosure.GetComponent<Button>().onClick.AddListener(()=>
            {
                bool expanded=!fields.activeSelf;
                fields.SetActive(expanded);
                root.GetComponent<LayoutElement>().preferredHeight=expanded?288:92;
                disclosureLabel.text=expanded?"프로필 가져오기 닫기  ‹":"다른 기기의 프로필 가져오기  ›";
                if(!expanded)codeInput.text="";
            });
            var inputGo=new GameObject("ProfileLinkCode",typeof(RectTransform),
                typeof(Image),typeof(InputField),typeof(LayoutElement));
            inputGo.transform.SetParent(fields.transform,false);
            inputGo.GetComponent<Image>().color=new Color(.95f,.95f,1f);
            inputGo.GetComponent<LayoutElement>().preferredHeight=66;
            codeInput=inputGo.GetComponent<InputField>();
            codeInput.contentType=InputField.ContentType.Password;
            codeInput.lineType=InputField.LineType.SingleLine;
            var fieldText=Label(inputGo.transform,chosenFont,"",21,66);
            fieldText.alignment=TextAnchor.MiddleLeft;
            fieldText.color=new Color(.2f,.17f,.25f);
            var rect=(RectTransform)fieldText.transform;
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=new Vector2(12,0);rect.offsetMax=new Vector2(-12,0);
            codeInput.textComponent=fieldText;

            var btn=new GameObject("ConnectProfile",typeof(RectTransform),
                typeof(Image),typeof(Button),typeof(LayoutElement));
            btn.transform.SetParent(fields.transform,false);
            if(theme!=null)theme.StyleButton(btn.GetComponent<Button>(),true);
            btn.GetComponent<LayoutElement>().preferredHeight=60;
            btn.GetComponent<Button>().onClick.AddListener(Submit);
            var btnText=Label(btn.transform,chosenFont,"연결하기",22,60);
            btnText.color=Color.white;
            var br=(RectTransform)btnText.transform;
            br.anchorMin=Vector2.zero;br.anchorMax=Vector2.one;
            br.offsetMin=br.offsetMax=Vector2.zero;
            feedback=Label(fields.transform,chosenFont,"",18,40);
            fields.SetActive(false);
        }

        private void Submit()
        {
            if(codeInput==null||onConnect==null)return;
            string code=codeInput.text.Trim();
            codeInput.text="";
            if(!AppPofo.Presentation.Common.CloudProfileLinkApi.ValidCode(code))
            {
                ShowResult("코드 형식을 확인해 주세요. 전체 코드를 붙여넣어야 합니다.");
                return;
            }
            ShowResult("연결 코드를 확인하고 있습니다...");
            onConnect(code);
        }

        public void ShowResult(string value)
        {
            if(feedback!=null)feedback.text=value??"";
        }

        public void SetVisible(bool visible)
        {
            if(root!=null)root.SetActive(visible);
        }

        private static Text Label(Transform parent,Font font,string value,int size,float height)
        {
            var go=new GameObject("Label",typeof(RectTransform),typeof(Text),typeof(LayoutElement));
            go.transform.SetParent(parent,false);
            go.GetComponent<LayoutElement>().preferredHeight=height;
            var text=go.GetComponent<Text>();
            text.font=font;text.fontSize=size;text.text=value;
            text.alignment=TextAnchor.MiddleCenter;
            text.color=new Color(.35f,.3f,.51f);
            text.supportRichText=false;
            return text;
        }
    }
}
#endif
