using System;
using System.Collections;
using System.Collections.Generic;
using AppPofo.Application.Abstractions;
using AppPofo.Application.UseCases;
using AppPofo.Domain.Entities;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Components;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.FaceRecognition;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Screens
{
    public sealed class ProfileListScreen : MonoBehaviour
    {
        [SerializeField] private AppPofoBootstrap bootstrap;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private ProfileListItemView profileItemPrefab;
        [SerializeField] private GameObject emptyState;
        [SerializeField] private GameObject createProfileScreenRoot;
        [SerializeField] private ProfileDetailScreen profileDetailScreen;
        [SerializeField] private BusinessCardPanelController businessCardPanel;
        [SerializeField] private FaceRegistrationScreen faceRegistrationScreen;
        [SerializeField] private Sprite welcomeBackIcon;

        private const int PageSize = SearchProfilesUseCase.DefaultPageSize;
        private readonly List<GameObject> spawnedRows = new();
        private bool hasStarted;
        private int pageIndex;
        private int totalMatches;
        private ProfileSearchPanelView searchView;
        private Coroutine searchDelay;
        private Coroutine cloudLoad;
        private Coroutine cloudDetailLoad;
        private int cloudGeneration;
        private Text cloudMessage;
        private CloudProfileDetailOverlay cloudDetails;
        private bool? previousCloudMode;
        private Button welcomeBackButton;
        private Coroutine linkCodeRoutine;
        private Coroutine linkConnectRoutine;
#if UNITY_ANDROID || UNITY_EDITOR || UNITY_STANDALONE_WIN
        private CloudProfileConnectPanel connectPanel;
#endif
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private CloudAdminLoginPanel adminLoginPanel;
#endif

        public void OpenRecognitionCamera()
        {
            if (CloudModeSession.Mode == ProfileDataMode.Cloud &&
                !CloudFaceMatchApi.CanQuery)
            {
                if (CloudFaceRuntime.Available)
                {
                    var consent = GetComponent<CloudFaceConsentPanel>() ?? gameObject.AddComponent<CloudFaceConsentPanel>();
                    consent.ResumeScan(OpenRecognitionCamera);
                    return;
                }
                ShowCloudMessage("서버 얼굴 검색은 기본적으로 꺼져 있습니다. " +
                    "Windows Unity Editor의 로컬 얼굴 검색 테스트에서 동의하고 활성화해 주세요.");
                return;
            }
            if (faceRegistrationScreen == null)
            {
                Debug.LogError("FaceRegistrationScreen이 연결되지 않았습니다.", this);
                return;
            }
            faceRegistrationScreen.ShowRecognition(gameObject);
        }

        private void OnEnable()
        {
            EnsureWelcomeBackButton();
            if (businessCardPanel != null) { businessCardPanel.AvatarSaved += OnAvatarSaved; businessCardPanel.CloudAvatarSaved += OnCloudAvatarSaved; }
            if (hasStarted) Refresh();
        }

        private void OnDisable()
        {
            if (businessCardPanel != null) { businessCardPanel.AvatarSaved -= OnAvatarSaved; businessCardPanel.CloudAvatarSaved -= OnCloudAvatarSaved; }
            if (searchDelay != null)
            {
                StopCoroutine(searchDelay);
                searchDelay = null;
            }
            CancelCloudQueries();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (adminLoginPanel != null) adminLoginPanel.Close();
#endif
            if (cloudDetails != null) cloudDetails.Hide();
        }

        // Welcome and ProfileList are siblings in Bootstrap.unity. Navigate without
        // reloading the scene, clearing the SQLite database, or changing DPAPI credentials.
        private void EnsureWelcomeBackButton()
        {
            if (welcomeBackButton != null) return;
            Transform topBar = transform.Find("SafeAreaRoot/CenteredContent/TopBar");
            if (topBar == null) return;

            var go = new GameObject("BackToWelcomeButton",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(topBar, false);
            go.layer = topBar.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(128f, 120f);
            rect.anchoredPosition = new Vector2(64f, 0f);

            // A full-sized hit target surrounds the existing themed back icon.
            // Keep the navigation control separate from the logo and page title.
            var background = go.GetComponent<Image>();
            background.color = Color.clear;
            welcomeBackButton = go.GetComponent<Button>();
            welcomeBackButton.onClick.AddListener(ReturnToWelcome);

            var icon = new GameObject("BackIcon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(go.transform, false);
            icon.layer = go.layer;
            var iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f, .5f);
            iconRect.sizeDelta = new Vector2(88f, 88f);
            var graphic = icon.GetComponent<Image>();
            graphic.sprite = welcomeBackIcon;
            graphic.preserveAspect = true;
            graphic.raycastTarget = false;
            welcomeBackButton.targetGraphic = graphic;
        }

        public void ReturnToWelcome()
        {
            var controller = UnityEngine.Object.FindAnyObjectByType<
                WelcomeCloudModeController>(FindObjectsInactive.Include);
            if (controller == null || controller.transform.parent == null)
            {
                Debug.LogWarning("[AppPofo] Welcome 화면을 찾지 못했습니다.", this);
                return;
            }

            // OnDisable cancels list/profile requests and closes transient overlays.
            gameObject.SetActive(false);
            // Cancel in-flight cloud probes and ephemeral device sessions, not saved
            // administrator DPAPI credentials or the device's long-term key.
            CloudConnectionRuntime.BeginFromWelcome(false);
            controller.transform.parent.gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            if (welcomeBackButton != null)
                welcomeBackButton.onClick.RemoveListener(ReturnToWelcome);
        }

        private void OnAvatarSaved(long profileId)
        {
            if (CloudModeSession.Mode == ProfileDataMode.Local) Refresh();
        }

        private void Start()
        {
            hasStarted = true;
            Refresh();
        }

        public void Refresh()
        {
            if (bootstrap == null || contentRoot == null || profileItemPrefab == null)
                return;
            if (CloudModeSession.Mode == ProfileDataMode.Cloud)
            {
                RefreshCloud();
                return;
            }
            PrepareLocalPresentation();

            IReadOnlyList<Profile> profiles;
            bool showEmpty;
            if (bootstrap.SearchProfilesUseCase != null)
            {
                EnsureSearchPanel();
                ProfileSearchPage page;
                try
                {
                    page = QueryPage();
                    int lastPage = page.TotalCount == 0 ? 0 : (page.TotalCount - 1) / PageSize;
                    if (pageIndex > lastPage)
                    {
                        pageIndex = lastPage;
                        page = QueryPage();
                    }
                }
                catch (Exception error)
                {
                    Debug.LogError("[ProfileList] 프로필 검색에 실패했습니다: " + error, this);
                    if (searchView != null)
                        searchView.SetStatus("검색할 수 없습니다. 조건을 확인해 주세요.", false, false);
                    return;
                }

                profiles = page.Profiles;
                totalMatches = page.TotalCount;
                bool filtered = !string.IsNullOrWhiteSpace(searchView?.NameQuery) ||
                                !string.IsNullOrWhiteSpace(searchView?.SkillsQuery);
                showEmpty = totalMatches == 0 && !filtered;
                if (searchView != null) searchView.gameObject.SetActive(!showEmpty);
                UpdatePageStatus();
            }
            else
            {
                // Existing PlayMode fixtures may inject only the original GetProfilesUseCase.
                profiles = bootstrap.GetProfilesUseCase.Execute();
                totalMatches = profiles.Count;
                showEmpty = profiles.Count == 0;
            }

            ClearItems();
            if (emptyState != null) emptyState.SetActive(showEmpty);
            // The authored empty illustration occupies its own overlay above
            // the scroll list. Hide the entire overlay when cards or search
            // results are shown, otherwise it obscures the search controls.
            Transform emptyOverlay = transform.Find(
                "SafeAreaRoot/CenteredContent/Body/EmptyContent");
            if (emptyOverlay != null) emptyOverlay.gameObject.SetActive(showEmpty);

            foreach (Profile profile in profiles)
            {
                ProfileListItemView item = Instantiate(profileItemPrefab, contentRoot);
                item.Bind(profile, OpenProfileDetail, businessCardPanel.Show,
                    bootstrap.LoadAvatarUseCase);
                spawnedRows.Add(item.gameObject);
            }

            var scroll = contentRoot.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        // S5-04C: the source is chosen at Start, not by the contents of SQLite.
        private void RefreshCloud()
        {
            bool enteringCloud = previousCloudMode != true;
            previousCloudMode = true;
            CancelCloudQueries();
            if (enteringCloud) pageIndex = 0;
            EnsureSearchPanel();
            if (searchView != null)
            {
                searchView.SetCloudMode(true);
                searchView.gameObject.SetActive(true);
                searchView.SetStatus("서버 목록 · 연결 확인 중...", false, false);
            }
            SetLocalActionsEnabled(false);
            UpdateCloudCreateAction();
            RefreshRecognitionCameraAvailability();
            SetEmptyOverlay(false);
            ClearItems(); // Never leave local people visible in the server view.
            ShowCloudMessage(CloudProfileApi.IsAdministrator
                ? "관리자 권한으로 서버 프로필을 불러오는 중..."
                : "서버에서 내 프로필을 불러오는 중...");
            RefreshAdministratorLogin();
            RefreshConnectPanel();
            if (isActiveAndEnabled)
                cloudLoad = StartCoroutine(LoadCloudPage(++cloudGeneration));
        }

        private IEnumerator LoadCloudPage(int generation)
        {
            float started = Time.realtimeSinceStartup;
            while (CloudModeSession.Mode == ProfileDataMode.Cloud &&
                   CloudModeSession.Status == CloudConnectionStatus.Connecting &&
                   Time.realtimeSinceStartup - started < 40f)
                yield return null;

            if (generation != cloudGeneration || CloudModeSession.Mode != ProfileDataMode.Cloud)
                yield break;
            if (CloudModeSession.Status == CloudConnectionStatus.Connecting)
            {
                ShowCloudMessage("연결 시간이 초과되었습니다. 새로고침으로 다시 시도해 주세요.");
                UpdateCloudPageStatus(false);
                yield break;
            }
            RefreshAdministratorLogin();
            UpdateCloudCreateAction();
            if (CloudModeSession.Status == CloudConnectionStatus.Offline)
            {
                ShowCloudMessage("서버 연결 또는 기기 인증에 실패했습니다. 연결 상태를 확인해 주세요.");
                UpdateCloudPageStatus(false);
                yield break;
            }
            if (!CloudProfileApi.CanRequest)
            {
                ShowCloudMessage(UnityEngine.Application.platform == RuntimePlatform.Android
                    ? "서버 인증이 필요합니다. 서버 모드로 다시 시작해 주세요."
                    : "일반 PC 모드로 연결됐습니다. 개인 서버 프로필은 아직 Android 기기 인증으로만 조회할 수 있어요.");
                UpdateCloudPageStatus(false);
                yield break;
            }

            CloudProfilePage page = null;
            string error = null;
            yield return CloudProfileApi.FetchPage(pageIndex,
                (result, failure) => { page = result; error = failure; });
            if (generation != cloudGeneration || !isActiveAndEnabled ||
                CloudModeSession.Mode != ProfileDataMode.Cloud)
                yield break;
            if (page == null)
            {
                ShowCloudMessage(error ?? "서버 프로필을 불러오지 못했습니다.");
                UpdateCloudPageStatus(false);
                yield break;
            }
            totalMatches = page.total;
            int lastPage = totalMatches == 0 ? 0 : (totalMatches - 1) / CloudProfileApi.PageSize;
            if (pageIndex > lastPage)
            {
                pageIndex = lastPage;
                RefreshCloud();
                yield break;
            }
            ClearItems();
            foreach (CloudProfileSummary profile in page.data)
            {
                var item = Instantiate(profileItemPrefab, contentRoot);
                item.BindCloud(profile, OpenCloudProfileDetail, OpenCloudBusinessCard);
                spawnedRows.Add(item.gameObject);
            }
            if (page.total == 0)
                ShowCloudMessage(CloudProfileApi.IsAdministrator
                    ? "서버에 등록된 프로필이 없습니다."
                    : "서버에 저장된 내 프로필이 아직 없습니다.\n기존 기기 프로필은 자동 업로드되지 않아요.");
            else
                HideCloudMessage();
            UpdateCloudPageStatus(true);
            cloudLoad = null;
            var scroll = contentRoot.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private void UpdateCloudPageStatus(bool loaded)
        {
            if (searchView == null) return;
            if (!loaded)
            {
                searchView.SetStatus("서버 · 새로고침 가능", false, false);
                return;
            }
            int pages = Mathf.Max(1,
                (totalMatches + CloudProfileApi.PageSize - 1) / CloudProfileApi.PageSize);
            searchView.SetStatus((CloudProfileApi.IsAdministrator ? "관리자 · 전체 서버 프로필 · " : "내 서버 프로필 · ") + "총 " + totalMatches + "명 · " +
                (pageIndex + 1) + "/" + pages + " 페이지",
                pageIndex > 0, (pageIndex + 1) * CloudProfileApi.PageSize < totalMatches);
        }

        public void OpenCloudProfileDetail(string cloudId)
        {
            if (CloudModeSession.Mode != ProfileDataMode.Cloud) return;
            if (cloudDetailLoad != null)
                StopCoroutine(cloudDetailLoad);
            if (linkCodeRoutine != null)
            {
                StopCoroutine(linkCodeRoutine);
                linkCodeRoutine=null;
            }
            if (cloudDetails == null)
            {
                cloudDetails = gameObject.AddComponent<CloudProfileDetailOverlay>();
                cloudDetails.Initialize(profileDetailScreen);
            }
            cloudDetails.ShowLoading();
            cloudDetailLoad = StartCoroutine(LoadCloudDetail(cloudId, cloudGeneration, cloudDetails.Version));
        }

        private IEnumerator LoadCloudDetail(string id, int generation, int detailVersion)
        {
            CloudProfileDetail detail = null;
            string error = null;
            yield return CloudProfileApi.FetchDetail(id, (data, failure) =>
            {
                detail = data;
                error = failure;
            });
            if (!isActiveAndEnabled || generation != cloudGeneration ||
                CloudModeSession.Mode != ProfileDataMode.Cloud ||
                cloudDetails == null || !cloudDetails.IsVisible || cloudDetails.Version != detailVersion) yield break;
            if (detail == null)
                cloudDetails.ShowError(error ?? "프로필 정보를 불러올 수 없습니다.");
            else
                cloudDetails.ShowProfile(detail, BeginIssueLinkCode,
                    CloudFaceRuntime.Available || CloudFaceLocalTestFactory.IsLocalCaptureEnabled ? BeginCloudFaceEnrollment : null,
                    BeginCloudEdit, BeginCloudDelete);
            cloudDetailLoad = null;
        }

        private void BeginCloudDelete(string id) => StartCoroutine(DeleteCloudProfile(id));
        private IEnumerator DeleteCloudProfile(string id)
        {
            int generation = cloudGeneration;
            int version = cloudDetails.Version;
            string error = null;
            yield return CloudProfileWriteApi.Delete(id, (_, failure) => error = failure);
            if (generation != cloudGeneration || cloudDetails == null || !cloudDetails.IsVisible ||
                cloudDetails.Version != version || cloudDetails.SelectedDetail?.id != id) yield break;
            if (error != null) { cloudDetails.ShowDeleteError(error); yield break; }
            cloudDetails.Hide();
            if (businessCardPanel != null) businessCardPanel.CloseCloudProfile(id);
            RefreshCloud();
        }
        private void OnCloudAvatarSaved(string id) { if (CloudModeSession.Mode == ProfileDataMode.Cloud) RefreshCloud(); }
        private void OpenCloudBusinessCard(string id) => StartCoroutine(LoadCloudBusinessCard(id));
        private IEnumerator LoadCloudBusinessCard(string id)
        {
            int generation = cloudGeneration;
            CloudProfileDetail detail = null; string error = null;
            yield return CloudProfileApi.FetchDetail(id, (data, failure) => { detail = data; error = failure; });
            if (!isActiveAndEnabled || generation != cloudGeneration || !CloudProfileApi.CanRequest) yield break;
            if (detail == null) { ShowCloudMessage(error ?? "명함을 불러오지 못했습니다."); yield break; }
            if (businessCardPanel != null) businessCardPanel.ShowCloud(detail);
        }

        private void CancelCloudQueries()
        {
            cloudGeneration++;
            if (cloudLoad != null)
            {
                StopCoroutine(cloudLoad);
                cloudLoad = null;
            }
            if (cloudDetailLoad != null)
            {
                StopCoroutine(cloudDetailLoad);
                cloudDetailLoad = null;
            }
            if (linkCodeRoutine != null)
            {
                StopCoroutine(linkCodeRoutine);
                linkCodeRoutine=null;
            }
            if (linkConnectRoutine != null)
            {
                StopCoroutine(linkConnectRoutine);
                linkConnectRoutine=null;
            }
        }

        private void PrepareLocalPresentation()
        {
            CancelCloudQueries();
            previousCloudMode = false;
            if (searchView != null) searchView.SetCloudMode(false);
            HideCloudMessage();
            SetLocalActionsEnabled(true);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (adminLoginPanel != null) adminLoginPanel.Refresh();
#endif
#if UNITY_ANDROID || UNITY_EDITOR || UNITY_STANDALONE_WIN
            if (connectPanel != null) connectPanel.SetVisible(false);
#endif
            if (cloudDetails != null) cloudDetails.Hide();
        }

        private void RefreshAdministratorLogin()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (adminLoginPanel == null)
            {
                adminLoginPanel = gameObject.AddComponent<CloudAdminLoginPanel>();
                var reference = profileItemPrefab.GetComponentInChildren<Text>(true);
                var safeArea = transform.Find("SafeAreaRoot");
                var topBar = transform.Find("SafeAreaRoot/CenteredContent/TopBar");
                adminLoginPanel.Initialize(safeArea != null ? safeArea : transform,
                    topBar != null ? topBar : transform,
                    reference != null ? reference.font : null);
            }
            adminLoginPanel.Refresh();
#endif
        }

        private void BeginCloudEdit(CloudProfileDetail detail)
        {
            if (!CloudProfileApi.CanRequest || createProfileScreenRoot == null ||
                !createProfileScreenRoot.TryGetComponent<CreateProfileScreen>(out var creator)) return;
            creator.BeginCloudEdit(detail, OpenCloudProfileDetail);
            if (createProfileScreenRoot.activeSelf) { cloudDetails.Hide(); gameObject.SetActive(false); }
        }

        private void BeginCloudFaceEnrollment(string cloudId)
        {
            if (CloudFaceRuntime.Available)
            {
                var management = profileDetailScreen.GetComponent<CloudFaceConsentPanel>() ?? profileDetailScreen.gameObject.AddComponent<CloudFaceConsentPanel>();
                management.ShowManagement(cloudId, OpenCloudFaceEnrollment);
                return;
            }
            OpenCloudFaceEnrollment(cloudId);
        }

        private void OpenCloudFaceEnrollment(string cloudId)
        {
            if (CloudModeSession.Mode != ProfileDataMode.Cloud || !CloudProfileApi.CanRequest)
            {
                ShowCloudMessage("서버 연결 및 인증 상태를 확인해 주세요.");
                return;
            }
            if (createProfileScreenRoot == null ||
                !createProfileScreenRoot.TryGetComponent<CreateProfileScreen>(out var creator))
            {
                ShowCloudMessage("얼굴 등록 화면을 찾지 못했습니다.");
                return;
            }
            var detail = cloudDetails != null ? cloudDetails.SelectedDetail : null;
            if (cloudDetails != null) cloudDetails.Hide();
            creator.BeginCloudFaceRegistration(cloudId, detail);
            if (createProfileScreenRoot.activeSelf) gameObject.SetActive(false);
        }

        private void BeginIssueLinkCode(string profileId)
        {
            if (linkCodeRoutine != null) StopCoroutine(linkCodeRoutine);
            linkCodeRoutine=StartCoroutine(IssueLinkCode(profileId, cloudDetails.Version));
        }

        private IEnumerator IssueLinkCode(string id, int detailVersion)
        {
            string code=null,error=null;
            yield return CloudProfileLinkApi.Issue(id,(c,e)=>{code=c;error=e;});
            linkCodeRoutine=null;
            if (!isActiveAndEnabled || CloudModeSession.Mode != ProfileDataMode.Cloud ||
                cloudDetails == null || !cloudDetails.IsVisible || cloudDetails.Version != detailVersion) yield break;
            if(code!=null)cloudDetails.ShowLinkCode(code);
            else cloudDetails.ShowLinkCodeError(error);
        }

        private void RefreshConnectPanel()
        {
#if UNITY_ANDROID || UNITY_EDITOR || UNITY_STANDALONE_WIN
            // R2: Profile linking is outside the basic user flow.
            // Preserve the client/API implementation for the separate R5 cleanup.
            if (connectPanel != null) connectPanel.SetVisible(false);
#endif
        }

        private void BeginConnectCode(string code)
        {
            if (linkConnectRoutine!=null) StopCoroutine(linkConnectRoutine);
            linkConnectRoutine=StartCoroutine(ConnectCode(code));
        }

        private IEnumerator ConnectCode(string code)
        {
            string id=null,error=null;
            yield return CloudProfileLinkApi.Connect(code,(p,e)=>{id=p;error=e;});
            linkConnectRoutine=null;
            if (!isActiveAndEnabled || CloudModeSession.Mode!=ProfileDataMode.Cloud) yield break;
#if UNITY_ANDROID || UNITY_EDITOR || UNITY_STANDALONE_WIN
            if (connectPanel!=null)
                connectPanel.ShowResult(id!=null?"프로필 연결이 완료됐습니다.":error??"프로필 연결 실패");
#endif
            if (id!=null)RefreshCloud();
        }

        private void SetEmptyOverlay(bool visible)
        {
            if (emptyState != null) emptyState.SetActive(visible);
            var overlay = transform.Find("SafeAreaRoot/CenteredContent/Body/EmptyContent");
            if (overlay != null) overlay.gameObject.SetActive(visible);
        }

        private void UpdateCloudCreateAction()
        {
            // Both authenticated administrators and signed owners can create
            // cloud profiles. Camera consent is checked independently.
            bool canCreate = CloudModeSession.Mode == ProfileDataMode.Cloud &&
                CloudProfileApi.CanRequest;
            foreach (string path in new[]
            {
                "SafeAreaRoot/CenteredContent/TopBar/CreateProfileButton",
                "SafeAreaRoot/CenteredContent/Body/EmptyContent/Content/PrimaryAddButton"
            })
            {
                Transform target = transform.Find(path);
                if (target != null && target.TryGetComponent<Button>(out var button))
                    button.interactable = canCreate;
            }
        }

        // Called only on explicit editor permission changes; no remote requests.
        public void RefreshRecognitionCameraAvailability()
        {
            Transform target = transform.Find(
                "SafeAreaRoot/CenteredContent/BottomActions/CameraSearchButton");
            if (target != null && target.TryGetComponent<Button>(out var button))
                button.interactable = CloudModeSession.Mode == ProfileDataMode.Local ||
                    CloudFaceMatchApi.CanQuery || CloudFaceRuntime.Available;
        }

        private void SetLocalActionsEnabled(bool enabled)
        {
            foreach (string path in new[]
            {
                "SafeAreaRoot/CenteredContent/TopBar/CreateProfileButton",
                "SafeAreaRoot/CenteredContent/Body/EmptyContent/Content/PrimaryAddButton",
                "SafeAreaRoot/CenteredContent/BottomActions/CameraSearchButton"
            })
            {
                var target = transform.Find(path);
                if (target != null && target.TryGetComponent<Button>(out var button))
                    button.interactable = enabled;
            }
            RefreshRecognitionCameraAvailability();
        }

        private void EnsureCloudMessage()
        {
            if (cloudMessage != null) return;
            var go = new GameObject("CloudStateMessage",
                typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.layer = contentRoot.gameObject.layer;
            go.transform.SetParent(contentRoot, false);
            cloudMessage = go.GetComponent<Text>();
            var reference = searchView != null ?
                searchView.GetComponentInChildren<Text>(true) : null;
            cloudMessage.font = reference != null ? reference.font :
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            cloudMessage.fontSize = 25;
            cloudMessage.alignment = TextAnchor.MiddleCenter;
            cloudMessage.color = new Color(.37f, .29f, .57f, 1f);
            cloudMessage.supportRichText = false;
            cloudMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            cloudMessage.verticalOverflow = VerticalWrapMode.Overflow;
            cloudMessage.raycastTarget = false;
            var layout = go.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 115;
        }

        private void ShowCloudMessage(string text)
        {
            EnsureCloudMessage();
            cloudMessage.text = text;
            cloudMessage.gameObject.SetActive(true);
        }

        private void HideCloudMessage()
        {
            if (cloudMessage != null) cloudMessage.gameObject.SetActive(false);
        }

        private ProfileSearchPage QueryPage()
        {
            return bootstrap.SearchProfilesUseCase.Execute(
                searchView?.NameQuery, searchView?.SkillsQuery, pageIndex, PageSize);
        }

        private void UpdatePageStatus()
        {
            if (searchView == null) return;
            int pages = Mathf.Max(1, (totalMatches + PageSize - 1) / PageSize);
            string label = totalMatches == 0
                ? "조건에 맞는 프로필이 없어요"
                : "총 " + totalMatches + "명 · " + (pageIndex + 1) + "/" +
                  pages + " 페이지";
            searchView.SetStatus(label, pageIndex > 0, pageIndex + 1 < pages);
        }

        private void OnFilterChanged(string ignored)
        {
            if (CloudModeSession.Mode == ProfileDataMode.Cloud) return;
            pageIndex = 0;
            if (searchDelay != null) StopCoroutine(searchDelay);
            searchDelay = StartCoroutine(RefreshAfterTyping());
        }

        private IEnumerator RefreshAfterTyping()
        {
            yield return new WaitForSecondsRealtime(.25f);
            searchDelay = null;
            Refresh();
        }

        private void ResetFilters()
        {
            if (CloudModeSession.Mode == ProfileDataMode.Cloud)
            {
                if (CloudModeSession.Status == CloudConnectionStatus.Offline ||
                    (CloudModeSession.Status == CloudConnectionStatus.ServerReachable &&
                     !CloudProfileApi.CanRequest))
                    CloudConnectionRuntime.RetryConnection();
                else
                    Refresh();
                return;
            }
            if (searchView == null) return;
            searchView.ClearFiltersWithoutNotifying();
            pageIndex = 0;
            if (searchDelay != null)
            {
                StopCoroutine(searchDelay);
                searchDelay = null;
            }
            Refresh();
        }

        private void ChangePage(int delta)
        {
            int next = pageIndex + delta;
            if (next < 0 || (delta > 0 && next * PageSize >= totalMatches)) return;
            pageIndex = next;
            Refresh();
        }

        private void EnsureSearchPanel()
        {
            if (searchView != null) return;
            // This prefab holds the actual ConceptD sprites and Pretendard fonts.
            // ProfileListScreen no longer manufactures UIKit controls itself.
            var prefab = Resources.Load<ProfileSearchPanelView>("ProfileSearchPanel");
            if (prefab == null)
            {
                Debug.LogError("[ProfileList] ProfileSearchPanel prefab을 불러올 수 없습니다.", this);
                return;
            }
            searchView = Instantiate(prefab, contentRoot, false);
            searchView.name = "ProfileSearchPanel";
            searchView.transform.SetAsFirstSibling();
            searchView.Bind(OnFilterChanged, ResetFilters,
                () => ChangePage(-1), () => ChangePage(1));
        }

        private void ClearItems()
        {
            foreach (GameObject item in spawnedRows)
            {
                if (item == null) continue;
                item.SetActive(false);
                Destroy(item);
            }
            spawnedRows.Clear();
        }

        public void OpenCreateProfileScreen()
        {
            if (CloudModeSession.Mode == ProfileDataMode.Cloud &&
                !CloudProfileApi.CanRequest)
            {
                ShowCloudMessage("서버 프로필을 등록하려면 기기 인증이 필요합니다.");
                return;
            }
            if (createProfileScreenRoot == null ||
                !createProfileScreenRoot.TryGetComponent<CreateProfileScreen>(out var creator))
            {
                Debug.LogError("[ProfileList] CreateProfileScreen 연결이 필요합니다.", this);
                return;
            }
            creator.Begin();
            if (createProfileScreenRoot.activeSelf) gameObject.SetActive(false);
        }

        private void OpenProfileDetail(long profileId)
        {
            profileDetailScreen.Show(profileId);
            gameObject.SetActive(false);
        }
    }
}
