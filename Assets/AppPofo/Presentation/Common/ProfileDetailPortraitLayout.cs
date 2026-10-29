using System;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ProfileDetailPortraitLayout : MonoBehaviour
    {
        [Serializable] private sealed class SummaryCard
        {
            public RectTransform card, label, value;
            public Vector2 authoredSize, valueSize;
            public float valueLeft, valueTop, labelWidth;
        }
        [Serializable] private sealed class DialogGeometry
        {
            public RectTransform dialog, header, footer;
            public ScrollRect body;
            public Vector2 authoredSize;
            public float bodyTop, footerHeight, minimumBodyHeight, buttonWidth;
            public bool recommendedSkills, projectReview;
        }
        [SerializeField] private ResponsiveUIContext context;
        [SerializeField] private RectTransform centeredContent, header, actions;
        [SerializeField] private ScrollRect detailScroll;
        [SerializeField] private SummaryCard[] summaryCards;
        [SerializeField] private RectTransform[] sections;
        [SerializeField] private GridLayoutGroup skillGrid, recommendedGrid;
        [SerializeField] private DialogGeometry[] dialogs;
        private int lastState;
        private RectTransform supplementarySection;
        public void SetSupplementarySection(RectTransform section)
        {
            supplementarySection = section;
            ApplyLayout();
        }

        private void OnEnable()
        {
            if(context == null) return;
            context.Changed += ApplyLayout;
            context.Refresh(); ApplyLayout();
        }
        private void OnDisable() { if(context != null) context.Changed -= ApplyLayout; }
        private void LateUpdate() { if(context != null && State() != lastState) ApplyLayout(); }

        public void ApplyLayout()
        {
            if(!isActiveAndEnabled || context == null || centeredContent == null) return;
            Vector2 available = context.AvailableSize;
            // Keep rows inside the authored portrait frame when a tablet offers a wider aspect.
            float width = Mathf.Min(1080,available.x,available.y*1080/1920);
            centeredContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
            header.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
            float actionBottom = Mathf.Lerp(24,250,Mathf.Clamp01((available.y-1100)/820));
            actions.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,Mathf.Min(854,width-80));
            actions.anchoredPosition = new Vector2(-30,actionBottom);
            detailScroll.GetComponent<RectTransform>().offsetMin = new Vector2(40,actionBottom+143.1119f);
            detailScroll.GetComponent<RectTransform>().offsetMax = new Vector2(-40,-190);
            float spacing = Mathf.Min(220,actions.rect.width*220/854);
            for(int i=0;i<actions.childCount;i++)
            {
                var button=(RectTransform)actions.GetChild(i);
                button.anchoredPosition=new Vector2((i-1)*spacing,65);
                // A landscape viewport keeps the portrait frame narrow. Scale the
                // complete icon/caption together so the authored labels remain legible.
                float scale=width<660?Mathf.Min(1,spacing/220):1;
                button.localScale=Vector3.one*scale;
                var label=button.GetComponentInChildren<Text>(true);
                if(label != null) label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    width<660?(i==1?110:70):Mathf.Min(i==1?110:70,Mathf.Max(24,spacing-82)));
            }

            foreach(var row in summaryCards)
            {
                float rowWidth=Mathf.Min(row.authoredSize.x,Mathf.Max(1,detailScroll.content.rect.width*row.authoredSize.x/883));
                row.card.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,rowWidth);
                row.label.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,Mathf.Min(row.labelWidth,Mathf.Max(1,rowWidth-row.valueLeft-24)));
                row.value.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    Mathf.Max(1,row.valueSize.x-(row.authoredSize.x-rowWidth)));
                float textHeight=Mathf.Max(row.valueSize.y,row.value.GetComponent<Text>().preferredHeight);
                row.value.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,textHeight);
                row.value.anchoredPosition=new Vector2(row.valueLeft,-row.valueTop-textHeight/2);
                row.card.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,row.authoredSize.y+textHeight-row.valueSize.y);
            }
            for(int i=0;i<sections.Length;i++)
            {
                sections[i].SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    Mathf.Min(i==0?789.76f:783.4893f,Mathf.Max(1,detailScroll.content.rect.width*(i==0?789.76f:783.4893f)/883)));
            }
            if (supplementarySection != null)
                supplementarySection.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sections[2].rect.width);
            FitGrid(skillGrid,380,10,16,sections[0].rect.width-40);
            foreach(var section in sections) LayoutRebuilder.ForceRebuildLayoutImmediate(section);
            LayoutRebuilder.ForceRebuildLayoutImmediate(detailScroll.content);

            foreach(var geometry in dialogs)
            {
                var dialog=geometry.dialog;
                float dialogWidth=Mathf.Min(geometry.authoredSize.x,Mathf.Max(1,available.x-48));
                float dialogHeight=Mathf.Min(geometry.authoredSize.y,Mathf.Max(1,available.y-48));
                dialog.sizeDelta=new Vector2(dialogWidth,dialogHeight);
                // Preserve the deletion dialog's original reference-portrait center.
                dialog.anchoredPosition=new Vector2(0,geometry.authoredSize.x==880?
                    Mathf.Min(26.55585f,Mathf.Max(0,(available.y-dialogHeight)/2)):0);
                geometry.header.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,dialogWidth);
                geometry.footer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,dialogWidth);
                foreach(var button in geometry.footer.GetComponentsInChildren<Button>(true))
                    ((RectTransform)button.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                        geometry.buttonWidth*dialogWidth/geometry.authoredSize.x);
                var viewport=geometry.body.viewport;
                var bodyRect=(RectTransform)geometry.body.transform;
                bodyRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    geometry.recommendedSkills?Mathf.Max(1,dialogWidth-100):dialogWidth);
                float bodyHeight=Mathf.Max(1,dialogHeight-geometry.bodyTop-geometry.footerHeight);
                bodyRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    geometry.recommendedSkills?Mathf.Min(430,bodyHeight):bodyHeight);
                float minimum=geometry.minimumBodyHeight;
                if(geometry.projectReview)
                {
                    float top=0;
                    float[] originalHeights={70,290,60};
                    for(int i=0;i<geometry.body.content.childCount;i++)
                    {
                        var text=geometry.body.content.GetChild(i).GetComponent<Text>();
                        float height=Mathf.Max(originalHeights[i],text.preferredHeight);
                        text.rectTransform.anchoredPosition=new Vector2(0,-top-height/2);
                        text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
                        top+=height;
                    }
                    minimum=top;
                }
                if(geometry.recommendedSkills) FitGrid(recommendedGrid,352,16,12);
                else geometry.body.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(minimum,viewport.rect.height));
                geometry.body.vertical=geometry.recommendedSkills || minimum>viewport.rect.height+.1f;
                var mask=viewport.GetComponent<RectMask2D>();
                if(mask != null) mask.enabled=geometry.body.vertical;
                if(!geometry.body.vertical) geometry.body.verticalNormalizedPosition=1;
            }
            lastState=State();
        }

        private static void FitGrid(GridLayoutGroup grid,float authoredWidth,float horizontalSpacing,float verticalSpacing,float availableWidth=-1)
        {
            if(grid == null) return;
            float width=(availableWidth>=0?availableWidth:((RectTransform)grid.transform).rect.width)-grid.padding.horizontal;
            // Wrap at the existing chip's usable width; retain the authored two-column limit.
            int columns=Mathf.Clamp(Mathf.FloorToInt((width+horizontalSpacing)/(320+horizontalSpacing)),1,2);
            grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=columns;
            grid.cellSize=new Vector2(Mathf.Min(authoredWidth,Mathf.Max(1,(width-(columns-1)*horizontalSpacing)/columns)),84);
            int count=0;foreach(Transform child in grid.transform) if(child.gameObject.activeSelf) count++;
            int rows=Mathf.CeilToInt(count/(float)columns);
            float height=grid.padding.vertical+rows*84+Mathf.Max(0,rows-1)*verticalSpacing;
            var element=grid.GetComponent<LayoutElement>();
            if(element != null) element.minHeight=element.preferredHeight=height;
            ((RectTransform)grid.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)grid.transform);
        }
        private int State()
        {
            unchecked
            {
                int value=detailScroll.content.rect.width.GetHashCode();
                foreach(var row in summaryCards) value=value*31+row.value.GetComponent<Text>().text.GetHashCode();
                foreach(var section in sections) value=value*31+section.GetChild(section.childCount-1).childCount;
                foreach(var geometry in dialogs)
                {
                    value=value*31+geometry.dialog.gameObject.activeInHierarchy.GetHashCode();
                    if(geometry.projectReview) foreach(Transform child in geometry.body.content)
                        value=value*31+child.GetComponent<Text>().text.GetHashCode();
                }
                value=value*31+recommendedGrid.transform.childCount;
                return value;
            }
        }
    }
}
