using System;
using System.Collections;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Components;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    // A read-only snapshot above the camera. It never opens owner editing routes.
    public sealed class ProfileSummaryPanel : MonoBehaviour
    {
        private RectTransform safe, sheet, identity, bioBox, skillsBox, projectBox, linksBox;
        private RectTransform brandLogo;
        private Text heading, bio, projectTitle, projectDescription, emptyLinks;
        private Button close, back, projectLink;
        private Text bioHeading, skillsHeading, projectHeading, linksHeading;
        private readonly Text[] skillLabels = new Text[5];
        private readonly Button[] linkButtons = new Button[2];
        private readonly Text[] linkLabels = new Text[2];
        private CloudPanelTheme theme;
        private BusinessCardView headerPrefab, header;
        private Action<bool> visibility;
        private Action<BusinessCardView> styleIdentity;
        private Sprite panelArtwork;
        private ProfileDataMode openedMode;
        private ProfileSummaryData current;
        private bool open, cloud;
        private int generation;
        private string cloudId;
        private Vector2 lastFitSize;
        public bool IsOpen => open && gameObject.activeInHierarchy;
        public ProfileSummaryData Current => current;

        public static ProfileSummaryPanel Create(Transform context, BusinessCardView cardPrefab, Action<bool> changed, Action<BusinessCardView> style = null)
        {
            var canvas = context.GetComponentInParent<Canvas>();
            if (canvas == null || cardPrefab == null) return null;
            var go = new GameObject("RecognizedProfileSummary", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = canvas.gameObject.layer; go.transform.SetParent(canvas.rootCanvas.transform, false);
            Stretch((RectTransform)go.transform);
            go.GetComponent<Image>().color = new Color(.07f, .05f, .13f, .7f);
            var panel = go.AddComponent<ProfileSummaryPanel>();
            panel.headerPrefab = cardPrefab; panel.visibility = changed; panel.theme = CloudPanelTheme.Current;
            panel.styleIdentity = style;
            panel.Build(); go.SetActive(false); return panel;
        }
        public void ShowLocal(ProfileSummaryData data)
        {
            if (data?.LocalProfile == null) return;
            Open(false); Bind(data);
        }
        public void ShowCloud(string id)
        {
            if (!Guid.TryParse(id, out _) || !CloudFaceRuntime.CanScan) return;
            Open(true); cloudId = id;
            SetMessage("프로필을 불러오는 중...", "");
            StartCoroutine(Load(id, generation));
        }
        private void Open(bool remote)
        {
            generation++; StopAllCoroutines(); current = null; cloud = remote; cloudId = null;
            openedMode = CloudModeSession.Mode; open = true;
            lastFitSize = Vector2.zero;
            gameObject.SetActive(true); transform.SetAsLastSibling(); visibility?.Invoke(true); Fit();
        }
        private IEnumerator Load(string id, int version)
        {
            ProfileSummaryData data = null; string error = null;
            yield return CloudProfileSummaryApi.Fetch(id, (value, message) => { data = value; error = message; });
            if (!IsOpen || version != generation || id != cloudId) yield break;
            if (data != null) Bind(data); else SetMessage("프로필을 확인할 수 없습니다", error);
        }
        private void SetMessage(string title, string message)
        {
            ClearHeader(); heading.text = title; bio.text = message ?? "";
            bioHeading.text = "안내"; skillsHeading.text = "주요 기술";
            projectTitle.text = ""; projectDescription.text = ""; projectLink.gameObject.SetActive(false);
            foreach (var label in skillLabels) label.transform.parent.gameObject.SetActive(false);
            foreach (var button in linkButtons) button.gameObject.SetActive(false);
            emptyLinks.gameObject.SetActive(false);
        }
        private void Bind(ProfileSummaryData data)
        {
            current = data; heading.text = "프로필 한눈에"; bioHeading.text = "소개";
            ClearHeader(); header = Instantiate(headerPrefab, identity, false);
            header.name = "SummaryIdentityCard";
            styleIdentity?.Invoke(header);
            foreach (var button in header.GetComponentsInChildren<Button>(true)) button.enabled = false;
            foreach (var graphic in header.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            if (data.LocalProfile != null) header.Bind(data.LocalProfile, data.LocalAvatar);
            else header.BindCloudPublic(data.display_name, data.headline, data.avatar_base64);
            header.HideSkillsForSummary();
            header.gameObject.SetActive(true);
            bio.text = string.IsNullOrWhiteSpace(data.bio) ? "등록된 소개가 없습니다." : ProfileSummaryData.Short(data.bio, 100);
            for (int i = 0; i < skillLabels.Length; i++)
            {
                bool exists = data.skills != null && i < data.skills.Length;
                skillLabels[i].transform.parent.gameObject.SetActive(exists);
                skillLabels[i].text = exists ? ProfileSummaryData.Short(data.skills[i], 20) : "";
            }
            skillsHeading.text = data.skills != null && data.skills.Length > 0 ? "주요 기술" : "주요 기술 · 공개된 정보 없음";
            bool hasProject = !string.IsNullOrWhiteSpace(data.project?.title);
            projectTitle.text = hasProject ? ProfileSummaryData.Short(data.project.title, 55) : "공개된 프로젝트가 없습니다.";
            projectDescription.text = hasProject ? ProfileSummaryData.Short(data.project.description, 100) : "";
            BindLink(projectLink, hasProject ? data.project.url : null);
            for (int i = 0; i < linkButtons.Length; i++)
            {
                var link = data.links != null && i < data.links.Length ? data.links[i] : null;
                linkLabels[i].text = link == null ? "" : ProfileSummaryData.Short(link.title, 24);
                BindLink(linkButtons[i], link?.url);
            }
            emptyLinks.gameObject.SetActive(data.links == null || data.links.Length == 0);
            lastFitSize = Vector2.zero; Fit();
        }
        private static void BindLink(Button button, string url)
        {
            button.onClick.RemoveAllListeners(); bool valid = ProfileSummaryData.WebLink(url);
            button.gameObject.SetActive(valid);
            if (valid) button.onClick.AddListener(() => { if (ProfileSummaryData.WebLink(url)) UnityEngine.Application.OpenURL(url); });
        }
        private void ClearHeader()
        {
            if (header != null) { header.gameObject.SetActive(false); Destroy(header.gameObject); header = null; }
        }
        public void Close()
        {
            if (!open) return;
            open = false; generation++; StopAllCoroutines(); current = null; cloudId = null;
            ClearHeader(); gameObject.SetActive(false); visibility?.Invoke(false);
        }
        private void OnDisable() { if (open) Close(); }
        private void Update()
        {
            if (!IsOpen) return;
            if (CloudModeSession.Mode != openedMode || cloud && !CloudFaceRuntime.CanScan) { Close(); return; }
            if (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) Close();
        }
        private void LateUpdate() { if (IsOpen) Fit(); }
        private void OnApplicationPause(bool paused) { if (paused) Close(); }
        private void OnDestroy() { if (panelArtwork != null) Destroy(panelArtwork); }
        private void Build()
        {
            safe = Rect("SafeAreaRoot", transform); Stretch(safe); safe.gameObject.AddComponent<SafeAreaRoot>();
            sheet = Surface("SummarySheet", safe, PanelArtwork());
            if (theme != null && theme.brandLogo != null)
            {
                brandLogo = Rect("AppPofoLogo", sheet);
                var image = brandLogo.gameObject.AddComponent<Image>();
                image.sprite = theme.brandLogo; image.preserveAspect = true; image.raycastTarget = false;
            }
            heading = Label("Heading", sheet, "프로필 한눈에", 38, true);
            heading.alignment = TextAnchor.MiddleLeft;
            close = ActionButton("CloseSummary", sheet, "×", Close, false);
            identity = Rect("Identity", sheet);
            bioHeading = Label("BioHeading", sheet, "소개", 32, true);
            bioBox = Surface("BioCard", sheet, theme != null ? theme.summaryInfo : null); bio = Label("Bio", bioBox, "", 28);
            skillsHeading = Label("SkillsHeading", sheet, "주요 기술", 32, true);
            skillsBox = Rect("SkillChips", sheet);
            for (int i = 0; i < skillLabels.Length; i++)
            {
                var chip = Surface("Skill" + i, skillsBox, theme != null ? theme.summarySkill : null);
                skillLabels[i] = Label("Label", chip, "", 24);
                skillLabels[i].alignment = TextAnchor.MiddleCenter;
                skillLabels[i].resizeTextForBestFit = true; skillLabels[i].resizeTextMinSize = 18; skillLabels[i].resizeTextMaxSize = 24;
            }
            projectHeading = Label("ProjectHeading", sheet, "대표 프로젝트", 32, true);
            projectBox = Surface("ProjectCard", sheet, theme != null ? theme.summaryProject : null);
            projectTitle = Label("ProjectTitle", projectBox, "", 32, true);
            projectTitle.resizeTextForBestFit = true; projectTitle.resizeTextMinSize = 24; projectTitle.resizeTextMaxSize = 32;
            projectDescription = Label("ProjectDescription", projectBox, "", 24);
            projectLink = ActionButton("ProjectLink", projectBox, "프로젝트 보기", null, false);
            linksHeading = Label("LinksHeading", sheet, "링크", 32, true);
            linksBox = Rect("Links", sheet);
            for (int i = 0; i < linkButtons.Length; i++)
            {
                linkButtons[i] = ActionButton("Link" + i, linksBox, "", null, false);
                linkLabels[i] = linkButtons[i].GetComponentInChildren<Text>();
            }
            emptyLinks = Label("EmptyLinks", linksBox, "공개된 링크가 없습니다.", 26);
            back = ActionButton("ReturnToCamera", sheet, "카메라로 돌아가기", Close, true);
        }
        private void Fit()
        {
            if (safe == null || sheet == null || safe.rect.width <= 0 || safe.rect.height <= 0) return;
            if (lastFitSize == safe.rect.size) return;
            lastFitSize = safe.rect.size;
            bool wide = safe.rect.width > safe.rect.height * 1.2f;
            float width = wide ? 1540 : 1000, height = wide ? 820 : 1700;
            sheet.anchorMin = sheet.anchorMax = sheet.pivot = new Vector2(.5f, .5f);
            sheet.sizeDelta = new Vector2(width, height); sheet.anchoredPosition = Vector2.zero;
            sheet.localScale = Vector3.one * Mathf.Min((safe.rect.width - 40) / width, (safe.rect.height - 40) / height);
            if (brandLogo != null) Place(brandLogo, 38, 32, 72, 72);
            float headingLeft = brandLogo != null ? 130 : 38;
            Place(heading.rectTransform, headingLeft, 28, width - headingLeft - 132, 80);
            Place((RectTransform)close.transform, width - 110, 28, 72, 72);
            Place(identity, 36, 136, wide ? 660 : 928, wide ? 265 : 330);
            float column = wide ? 660 : 928;
            Place(bioHeading.rectTransform, 38, wide ? 420 : 500, column, 52);
            Place(bioBox, 36, wide ? 478 : 562, column, wide ? 120 : 148); Inset(bio.rectTransform, 26, 14);
            Place(skillsHeading.rectTransform, 38, wide ? 628 : 744, column, 52);
            Place(skillsBox, 36, wide ? 690 : 806, column, 70);
            int count = current?.skills == null ? 5 : Mathf.Clamp(current.skills.Length, 1, 5);
            float chipWidth = Mathf.Min(180, (column - (count - 1) * 12) / count);
            for (int i = 0; i < skillLabels.Length; i++)
            {
                Place((RectTransform)skillLabels[i].transform.parent, i * (chipWidth + 12), 0, chipWidth, 70);
                Inset(skillLabels[i].rectTransform, 8, 5);
            }
            float right = wide ? 740 : 36, rightWidth = wide ? 764 : 928;
            Place(projectHeading.rectTransform, right, wide ? 136 : 916, rightWidth, 52);
            Place(projectBox, right, wide ? 198 : 978, rightWidth, 250);
            Place(projectTitle.rectTransform, 26, 18, rightWidth - 52, 65);
            Place(projectDescription.rectTransform, 26, 90, rightWidth - 52, 88);
            Place((RectTransform)projectLink.transform, rightWidth - 265, 180, 238, 52);
            Place(linksHeading.rectTransform, right, wide ? 478 : 1260, rightWidth, 52);
            Place(linksBox, right, wide ? 540 : 1322, rightWidth, 96);
            for (int i = 0; i < linkButtons.Length; i++) Place((RectTransform)linkButtons[i].transform, i * (rightWidth + 20) / 2, 0, (rightWidth - 20) / 2, 96);
            Inset(emptyLinks.rectTransform, 12, 8);
            Place((RectTransform)back.transform, wide ? right : 36, wide ? 674 : 1528, rightWidth, 110);
            if (header != null)
            {
                var rect = (RectTransform)header.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(900, 360);
                rect.localScale = Vector3.one * Mathf.Min(identity.rect.width / 900, identity.rect.height / 360);
            }
        }
        private RectTransform Surface(string name, Transform parent, Sprite sprite = null)
        {
            var rect = Rect(name, parent); var image = rect.gameObject.AddComponent<Image>();
            if (theme != null) theme.Surface(image, sprite != null ? sprite : theme.background);
            image.raycastTarget = false; return rect;
        }
        private Sprite PanelArtwork()
        {
            if (theme == null || theme.summaryPanel == null) return null;
            var source = theme.summaryPanel;
            var rect = source.rect;
            // Reuse the authored panel texture, trimming its transparent export gutter.
            // Slicing keeps its existing frame proportional in both orientations.
            rect = new UnityEngine.Rect(rect.x + rect.width * .075f, rect.y + rect.height * .06f, rect.width * .85f, rect.height * .88f);
            float border = Mathf.Min(rect.width, rect.height) * .08f;
            panelArtwork = Sprite.Create(source.texture, rect, new Vector2(.5f, .5f), source.pixelsPerUnit, 0,
                SpriteMeshType.FullRect, Vector4.one * border);
            panelArtwork.name = "ProfileDetailPanelSummary";
            return panelArtwork;
        }
        private Text Label(string name, Transform parent, string text, int size, bool bold = false)
        {
            var rect = Rect(name, parent); var label = rect.gameObject.AddComponent<Text>();
            label.font = theme != null ? bold ? theme.boldFont : theme.regularFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size; label.text = text; label.color = new Color(.12f, .15f, .27f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = false; label.raycastTarget = false; return label;
        }
        private Button ActionButton(string name, Transform parent, string caption, Action action, bool primary)
        {
            var rect = Rect(name, parent); rect.gameObject.AddComponent<Image>(); var button = rect.gameObject.AddComponent<Button>();
            if (theme != null) theme.StyleButton(button, primary);
            button.navigation = new Navigation {mode = Navigation.Mode.None};
            var label = Label("Label", rect, caption, 28, true); label.alignment = TextAnchor.MiddleCenter;
            if (primary) label.color = Color.white; Inset(label.rectTransform, 12, 6);
            if (action != null) button.onClick.AddListener(() => action()); return button;
        }
        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Inset(RectTransform rect, float x, float y) { Stretch(rect); rect.offsetMin = new Vector2(x, y); rect.offsetMax = new Vector2(-x, -y); }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
    }
}
