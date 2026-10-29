using AppPofo.Presentation.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections;
using AppPofo.Domain.Entities;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.FaceRecognition.Matching;
using AppPofo.Presentation.FaceRegistration.Alignment;
using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.FaceRegistration.Embedding;
using OpenCvSharp;
using UnityEngine;
using Unity.InferenceEngine;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.Screens;
using AppPofo.Presentation.Components;
using UnityEngine.UI;
using Unity.Profiling;


namespace AppPofo.Presentation.FaceRecognition
{
    public sealed class FaceRecognitionCamera : MonoBehaviour
    {
        [SerializeField] private AppPofoBootstrap bootstrap;
        [SerializeField] private float recognitionThreshold = 0.40f;
        [SerializeField] ModelAsset faceDetectionModel;
        [SerializeField] ModelAsset faceRecognitionModel;
        [SerializeField] FaceRegistrationImageSourceAdapter imageSource;
        [SerializeField] private float recognitionInterval = 0.2f;
        [SerializeField] private float recognizedFaceCooldown = 2f;
        [SerializeField] private bool enableDebugLogs = false;
        [SerializeField] private BusinessCardPanelController businessCardPanelController;
        [SerializeField] private RectTransform previewRectTransform;
        [SerializeField] private RawImage previewRawImage;
        [SerializeField] private RectTransform faceOverlayRoot;
        [SerializeField] private RectTransform faceOverlayPrefab;
        private float nextRecognitionTime;
        private float nextRearDetectionTraceAt;
        private SentisFaceDetector faceDetector;
        private bool cameraReady;
        private bool previewActive;
        // Camera preview is displayed before loading expensive AI workers.
        private Coroutine modelInitializationRoutine;
        private bool recognitionModelsReady;
        private bool firstPreviewFrameObserved;
        private bool initialYuNetAttemptObserved;
        private bool initialYuNetResultObserved;
        private bool firstSFaceAttemptObserved;
        private bool firstSFaceResultObserved;
        private bool modelStartupFailed;
        private bool cameraStartupPaused;
        private double cameraStartupAt;
        private int cameraStartupAttempt;
        private static int processCameraStartupAttempts;
        private CameraPreviewOrientationEffect previewOrientationEffect;
        private UnityEngine.Rect previousPreviewUVRect;
        private readonly Dictionary<long, BusinessCardView> businessCardsByProfile = new Dictionary<long, BusinessCardView>();
        private readonly Vector3[] overlayCorners = new Vector3[4];
        private Coroutine recognitionRoutine;
        private int recognitionGeneration;
        private bool recognitionFaulted;
        private bool recognitionProcessing;
        private MultiFaceDetectionFrame recognitionFrame;
        private readonly List<FaceRecognitionItem> recognitionResults = new List<FaceRecognitionItem>();
        private readonly List<CloudRecognitionHit> cloudResults = new List<CloudRecognitionHit>();
        private CloudFaceRecognitionPresenter cloudPresenter;
        // Shared over camera entries: prevent rapid reopening from exceeding the local scan budget.
        private static readonly CloudFaceScanPacer cloudScanPacer = new CloudFaceScanPacer();
        private int cloudRoundRobin;
        private ProfileDataMode previewMode;
        private readonly List<long> cardLayoutIds = new List<long>();
        private readonly List<BusinessCardView> layoutCards = new List<BusinessCardView>();
        private readonly List<UnityEngine.Rect> layoutFaces = new List<UnityEngine.Rect>();
        private List<UnityEngine.Rect> cardLayoutBounds = new List<UnityEngine.Rect>();
        private UnityEngine.Rect cardLayoutArea;
        private bool cardLayoutDirty = true;
        private readonly Dictionary<long, float> lastVisibleTimeByProfile = new Dictionary<long, float>();
        private const float RecognitionUiGraceSeconds = .6f;
        private const double MaxAsyncResultAgeSeconds = 5d;
        private static readonly ProfilerMarker DetectionMarker = new ProfilerMarker("FaceRecognition.Detection");
        private static readonly ProfilerMarker SFaceMarker = new ProfilerMarker("FaceRecognition.SFace");
        private static readonly ProfilerMarker CardLayoutMarker = new ProfilerMarker("FaceRecognition.CardLayout");

        public float PreviewExposureGain => detectionController != null ?
            detectionController.PreviewExposureGain : 1f;
        private FaceDetectionController detectionController;
        private SFaceRecognizer sFaceRecognizer;
        private RecognitionInferencePolicy recognitionPolicy;
        private bool firstCardCreated;
        private readonly Dictionary<long, float> lastHandledTimeByProfile = new Dictionary<long, float>();
        private readonly Dictionary<long,RectTransform> faceOverlaysByProfile = new Dictionary<long, RectTransform>();
        private void TraceCameraStartup(string stage, double durationMs = -1d)
        {
#if APPPOFO_PERF_DIAGNOSTICS && (UNITY_EDITOR || DEVELOPMENT_BUILD)
            if (cameraStartupAt <= 0d) return;
            double elapsedMs = (Time.realtimeSinceStartupAsDouble - cameraStartupAt) * 1000d;
            Debug.Log("[PERF4] Camera startup attempt=" + cameraStartupAttempt +
                " stage=" + stage + " elapsedMs=" + elapsedMs.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) +
                (durationMs < 0d ? "" : " durationMs=" + durationMs.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture)), this);
#endif
        }

        private static void ReadRecognitionOverrides(out string yunet, out string sface)
        {
            yunet = null;
            sface = null;
#if UNITY_ANDROID && DEVELOPMENT_BUILD && !UNITY_EDITOR
            // Unlike PERF1 diagnostics, do not mutate the activity Intent.
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                yunet = intent.Call<string>("getStringExtra", "apppofo_recognition_yunet");
                sface = intent.Call<string>("getStringExtra", "apppofo_recognition_sface");
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("[S4-17] Recognition A/B extras unavailable: " +
                    error.GetType().Name);
            }
#endif
        }

        private void ConfigureRecognitionPolicy()
        {
            ReadRecognitionOverrides(out string yunet, out string sface);
            try
            {
                recognitionPolicy = RecognitionInferencePolicy.Resolve(
                    SystemInfo.deviceModel, yunet, sface, PerformanceDiagnostics.Configured,
                    YuNetExecutionPolicy.RecognitionDiagnostics,
                    PerformanceDiagnostics.UseCpuSFace,
                    PerformanceDiagnostics.SFaceLayersPerFrame);
            }
            catch (System.ArgumentException error)
            {
                // Invalid developer options must not prevent a normal camera session.
                Debug.LogWarning("[S4-17] " + error.Message +
                    " Restoring device or PERF baseline.", this);
                recognitionPolicy = RecognitionInferencePolicy.Resolve(
                    SystemInfo.deviceModel, benchmarkConfigured: PerformanceDiagnostics.Configured,
                    benchmarkYuNet: YuNetExecutionPolicy.RecognitionDiagnostics,
                    benchmarkCpuSFace: PerformanceDiagnostics.UseCpuSFace,
                    benchmarkSFaceLayers: PerformanceDiagnostics.SFaceLayersPerFrame);
            }
#if UNITY_ANDROID && DEVELOPMENT_BUILD
            Debug.Log("[S4-17] Recognition policy attempt=" + cameraStartupAttempt +
                " " + recognitionPolicy, this);
#endif
        }

        private IEnumerator InitializeRecognitionModelsAfterCamera()
        {
            // Wait for a real oriented preview frame; no GPU worker construction
            // may block the camera start request or first camera presentation.
            while (previewActive && !modelStartupFailed &&
                (!firstPreviewFrameObserved || cameraStartupPaused))
                yield return null;
            if (!previewActive || modelStartupFailed) yield break;

            // Leave a frame for the preview to reach the renderer.
            yield return null;
            while (previewActive && cameraStartupPaused) yield return null;
            if (!previewActive || modelStartupFailed) yield break;

            S415FrameGapProbe.SetStage("Recognition.WorkerInit");
            double workerStart = Time.realtimeSinceStartupAsDouble;
            TraceCameraStartup("yunet-worker-begin");
            bool workerFailed = false;
            try
            {
                faceDetector = new SentisFaceDetector(faceDetectionModel,
                    recognitionPolicy.YuNet);
                detectionController = new FaceDetectionController(faceDetector);
            }
            catch (System.Exception error)
            {
                modelStartupFailed = true;
                ReportAsyncFailure("YuNet worker startup", error);
                TraceCameraStartup("yunet-worker-error");
                workerFailed = true;
            }
            if (workerFailed) yield break;
            TraceCameraStartup("yunet-worker-ready",
                (Time.realtimeSinceStartupAsDouble - workerStart) * 1000d);
            CloudTrace(CloudFaceScanStage.YuNetModelReady, durationMs:
                (Time.realtimeSinceStartupAsDouble - workerStart) * 1000d);

            // Avoid constructing both workers on one rendered frame.
            yield return null;
            while (previewActive && cameraStartupPaused) yield return null;
            if (!previewActive || modelStartupFailed) yield break;

            workerStart = Time.realtimeSinceStartupAsDouble;
            TraceCameraStartup("sface-worker-begin");
            workerFailed = false;
            try
            {
                sFaceRecognizer = new SFaceRecognizer(faceRecognitionModel,
                    recognitionPolicy.CpuSFace ? BackendType.CPU : BackendType.GPUCompute);
            }
            catch (System.Exception error)
            {
                modelStartupFailed = true;
                ReportAsyncFailure("SFace worker startup", error);
                TraceCameraStartup("sface-worker-error");
                workerFailed = true;
            }
            if (workerFailed) yield break;
            TraceCameraStartup("sface-worker-ready",
                (Time.realtimeSinceStartupAsDouble - workerStart) * 1000d);
            CloudTrace(CloudFaceScanStage.SFaceModelReady, durationMs:
                (Time.realtimeSinceStartupAsDouble - workerStart) * 1000d);
            if (!previewActive || modelStartupFailed) yield break;

            faceDetector.DiagnosticPreprocessingOnly =
                PerformanceDiagnostics.IsScenario(PerfScenario.Preprocessing);
            recognitionModelsReady = true;
            CloudTrace(CloudFaceScanStage.ModelsReady, durationMs:
                (Time.realtimeSinceStartupAsDouble - cameraStartupAt) * 1000d);
            S415FrameGapProbe.SetStage("Recognition.Idle");
            // Preserve S3-C 90-second benchmark: exclude model/camera initialization.
            PerformanceDiagnostics.CameraReady();
            TraceCameraStartup("recognition-ready");
            modelInitializationRoutine = null;
        }

        private void StartPreview()
        {
            if (previewActive)
                return;

            if (faceDetectionModel == null)
            {
                Debug.LogError(
                    $"faceDetectionModel 이 연결되지 않았습니다.",
                    this
                );

                return;
            }

            if (faceRecognitionModel == null)
            {
                Debug.LogError
                ("faceRecognitionModel 이 연결되지 않았습니다.", this);

                return;
            }

            if (imageSource == null)
            {
                Debug.LogError("imageSource가 연결되지 않았습니다.", this);
                return;
            }

            if (bootstrap == null)
            {
                Debug.LogError("bootstrap이 연결되지 않았습니다.", this);
                return;
            }

            if (previewRawImage == null)
            {
                Debug.LogError("previewRawImage가 연결되지 않았습니다.", this);
                return;
            }

            if (faceOverlayRoot == null)
            {
                Debug.LogError("faceOverlayRoot가 연결되지 않았습니다.", this);
                return;
            }

            if (faceOverlayPrefab == null)
            {
                Debug.LogError("faceOverlayPrefab이 연결되지 않았습니다.", this);
                return;
            }

            if (businessCardPanelController == null)
            {
                Debug.LogError("businessCardPanelController가 연결되지 않았습니다.", this);
                return;
            }

            FaceRecognitionOverlayView overlayView = faceOverlayPrefab.GetComponent<FaceRecognitionOverlayView>();
            if (overlayView == null || !overlayView.HasRequiredReferences)
            {
                Debug.LogError("[FaceRecognition] faceOverlayPrefab의 View / Button 연결이 필요합니다.", this);
                return;
            }

            if (recognitionInterval <= 0f)
            {
                Debug.LogError("recognitionInterval은 0보다 커야 합니다.");
                return;
            }

            if (recognitionThreshold > 1f || recognitionThreshold < -1f)
            {
                Debug.LogError(
                    $"recognitionThreshold는 -1과 1사이어야 합니다. 현재 recognitionThreshold 값 {recognitionThreshold}", this);

                return;
            }

            recognitionGeneration++;
            recognitionFaulted = false;
            recognitionModelsReady = false;
            firstPreviewFrameObserved = false;
            initialYuNetAttemptObserved = false;
            initialYuNetResultObserved = false;
            firstSFaceAttemptObserved = false;
            firstSFaceResultObserved = false;
            modelStartupFailed = false;
            cameraStartupPaused = false;
            cameraReady = false;
            previewActive = true;
            previewMode = CloudModeSession.Mode;
            if (previewMode == ProfileDataMode.Cloud)
                cloudScanPacer.BeginSession(CloudScanClockSeconds());
            cameraStartupAt = Time.realtimeSinceStartupAsDouble;
            CloudTrace(CloudFaceScanStage.PreviewStarted);
            cameraStartupAttempt = ++processCameraStartupAttempts;
            firstCardCreated = false;
            ConfigureRecognitionPolicy();
            S415FrameGapProbe.SetStage("Recognition.CameraStart");
            TraceCameraStartup("view-enter");
            nextRecognitionTime = 0f;
            previousPreviewUVRect = previewRawImage.uvRect;
            previewRawImage.uvRect = new UnityEngine.Rect(0, 0, 1, 1);
            previewOrientationEffect = previewRawImage.GetComponent<CameraPreviewOrientationEffect>() ??
                previewRawImage.gameObject.AddComponent<CameraPreviewOrientationEffect>();
            previewOrientationEffect.SetAspectFill(true);
            if (CloudModeSession.Mode == ProfileDataMode.Cloud)
                cloudPresenter = new CloudFaceRecognitionPresenter(
                    faceOverlayRoot, faceOverlayPrefab, businessCardPanelController,
                    previewOrientationEffect);
            cloudRoundRobin = 0;

            // Previously both model Workers were created synchronously BEFORE
            // imageSource.StartCamera, delaying the first visible camera frame.
            TraceCameraStartup("camera-request");
            imageSource.StartCamera(OnCameraReady, OnCameraError);
            modelInitializationRoutine = StartCoroutine(InitializeRecognitionModelsAfterCamera());
        }

        private void StopPreview()
        {
            businessCardPanelController?.CloseRecognizedSummary();
            if (previewActive && previewMode == ProfileDataMode.Cloud)
            {
                CloudFaceScanDiagnostics.EndGate();
                CloudTrace(CloudFaceScanStage.PreviewStopped, durationMs:
                    (Time.realtimeSinceStartupAsDouble - cameraStartupAt) * 1000d);
            }
            if (previewActive) TraceCameraStartup("view-exit");
            S415FrameGapProbe.SetStage("Idle");
            if (modelInitializationRoutine != null)
            {
                StopCoroutine(modelInitializationRoutine);
                modelInitializationRoutine = null;
            }
            recognitionModelsReady = false;
            firstCardCreated = false;
            if (previewActive && previewRawImage != null)
                previewRawImage.uvRect = previousPreviewUVRect;
            if (previewOrientationEffect != null)
            {
                previewOrientationEffect.SetAspectFill(false);
                previewOrientationEffect.ClearOrientation();
            }
            recognitionGeneration++;
            PerformanceDiagnostics.StopSession();
            cameraReady = false;
            previewActive = false;
            StopRecognitionProcessing();
            cloudPresenter?.Clear();
            cloudPresenter = null;
            ClearBusinessCards();

            foreach (RectTransform overlay in faceOverlaysByProfile.Values)
            {
                if (overlay != null)
                    Destroy(overlay.gameObject);
            }
            faceOverlaysByProfile.Clear();
            lastHandledTimeByProfile.Clear();
            lastVisibleTimeByProfile.Clear();

            if (imageSource != null)
                imageSource.StopCamera();
            if(previewRawImage != null)
                previewRawImage.texture = null;
            faceDetector?.Dispose();
            faceDetector = null;

            detectionController = null;

            sFaceRecognizer?.Dispose();
            sFaceRecognizer = null;
        }

        private void OnEnable()
        {
            if (businessCardPanelController != null)
            {
                businessCardPanelController.SummaryVisibilityChanged -= OnSummaryVisibilityChanged;
                businessCardPanelController.SummaryVisibilityChanged += OnSummaryVisibilityChanged;
            }
            StartPreview();
        }
        private void OnDisable()
        {
            if (businessCardPanelController != null)
            {
                businessCardPanelController.SummaryVisibilityChanged -= OnSummaryVisibilityChanged;
                businessCardPanelController.CloseRecognizedSummary();
            }
            StopPreview();
        }

        private void OnSummaryVisibilityChanged(bool visible)
        {
            // Invalidate pre-modal async results and release only recognition work.
            // The live camera and scan pacing budget remain owned by this screen.
            recognitionGeneration++;
            StopRecognitionProcessing(); cloudPresenter?.Clear(); ClearBusinessCards();
        }

        private void OnApplicationPause(bool paused)
        {
            cameraStartupPaused = paused;
            if (!paused) return;
            // A resumed Android camera may have new dimensions/orientation.
            // Never apply a readback from a pre-pause frame.
            recognitionGeneration++;
            // Only a partially submitted GPU model invalidates this Worker on pause.
            // Preserve S2 pause/resume behavior when no sliced schedule is in flight.
            if ((sFaceRecognizer != null && sFaceRecognizer.IsScheduling) ||
                (detectionController != null && detectionController.IsScheduling))
                recognitionFaulted = true;
            StopRecognitionProcessing();
            cloudPresenter?.Clear();
        }

        private void LateUpdate()
        {
            if (businessCardPanelController != null && businessCardPanelController.IsSummaryOpen) return;
            if (previewActive && CloudModeSession.Mode != previewMode)
            {
                StopRecognitionProcessing();
                cloudPresenter?.Clear();
                ClearBusinessCards();
                return; // Exit and re-enter camera after any profile mode change.
            }
            // Permission withdrawal and expiry must clear cards even if the
            // camera stops producing frames or workers are no longer ready.
            if (previewMode == ProfileDataMode.Cloud)
            {
                if (!CloudFaceMatchApi.CanQuery)
                {
                    CloudGate(CloudFaceScanStage.QueryBlocked);
                    cloudPresenter?.Clear();
                }
                else
                {
                    cloudPresenter?.SetScanInFlight(recognitionProcessing);
                    cloudPresenter?.Tick(Time.unscaledTime);
                }
            }
            if (!cameraReady)
                return;

            if (!imageSource.TryGetPreviewFrame(
                out Texture texture,
                out CameraFrameOrientation orientation))
                return;

            if (previewRawImage.texture != texture) previewRawImage.texture = texture;
            previewOrientationEffect.SetOrientation(orientation);
            if (!firstPreviewFrameObserved)
            {
                firstPreviewFrameObserved = true;
                CloudTrace(CloudFaceScanStage.PreviewFrame, durationMs:
                    (Time.realtimeSinceStartupAsDouble - cameraStartupAt) * 1000d);
                TraceCameraStartup("first-preview-frame");
            }
            if (!recognitionModelsReady)
            {
                CloudGate(recognitionFaulted ? CloudFaceScanStage.PipelineFaulted : CloudFaceScanStage.ModelsPending);
                return;
            }
            if (CloudModeSession.Mode == ProfileDataMode.Cloud)
            {
                if (cloudPresenter == null)
                    cloudPresenter = new CloudFaceRecognitionPresenter(
                        faceOverlayRoot, faceOverlayPrefab, businessCardPanelController,
                        previewOrientationEffect);
                if (!CloudFaceMatchApi.CanQuery)
                {
                    CloudGate(CloudFaceScanStage.QueryBlocked);
                    cloudPresenter.Clear();
                    return; // Camera preview stays live, but no biometric upload.
                }
            }
            else if (cloudPresenter != null)
            {
                // Mode changes must never leave a cloud card in local mode.
                cloudPresenter.Clear();
            }
            if (PerformanceDiagnostics.IsScenario(PerfScenario.CameraOnly)) return;
            if (!PerformanceDiagnostics.SkipPresentation)
            {
                using (PerformanceDiagnostics.Measure(PerfMetric.CardUiMs))
                {
                    UpdateBusinessCardPositions();
                    SmoothSingleBusinessCard();
                }
            }
            if (recognitionProcessing)
            {
                CloudGate(CloudFaceScanStage.Busy);
                // Count only camera observations dropped by an in-flight inference.
                if (texture is WebCamTexture camera && camera.didUpdateThisFrame)
                    PerformanceDiagnostics.Record(PerfMetric.SkippedFrames, 1);
                return;
            }
            if (recognitionFaulted || Time.time < nextRecognitionTime)
            {
                if (recognitionFaulted) CloudGate(CloudFaceScanStage.PipelineFaulted);
                return;
            }

            // Reserve one cloud query opportunity per inference, at least 0.5s
            // apart, with a shared rolling cap of 120 starts per 60 seconds.
            if (CloudModeSession.Mode == ProfileDataMode.Cloud)
            {
                if (!cloudScanPacer.TryStartScan(CloudScanClockSeconds()))
                {
                    CloudGate(cloudScanPacer.IsBudgetBlocked ?
                        CloudFaceScanStage.WaitingForBudget : CloudFaceScanStage.WaitingForScan);
                    return;
                }
                CloudGate(CloudFaceScanStage.QueryAllowed);
                CloudTrace(CloudFaceScanStage.ScanStarted);
            }
            nextRecognitionTime = Time.time + recognitionInterval;

            ProcessRecognitionFrame(
                texture,
                orientation
            );
        }
        private void ProcessRecognitionFrame(
            Texture texture,
            CameraFrameOrientation orientation)
        {
            if (recognitionProcessing) return;
            recognitionProcessing = true;
            recognitionRoutine = StartCoroutine(ProcessRecognitionFrameRoutine(texture, orientation));
        }

        private void StopRecognitionProcessing()
        {
            if (recognitionRoutine != null) StopCoroutine(recognitionRoutine);
            recognitionRoutine = null;
            // The pending output belongs to its Worker. Abort before Worker disposal.
            detectionController?.CancelAsync();
            sFaceRecognizer?.CancelAsync();
            recognitionFrame?.Dispose();
            recognitionFrame = null;
            recognitionResults.Clear();
            cloudResults.Clear();
            recognitionProcessing = false;
            PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.Idle);
        }

        // Monotonic across Unity PlayMode/camera reopen; never based on wall-clock time.
        private static double CloudScanClockSeconds() =>
            (double)System.Diagnostics.Stopwatch.GetTimestamp() /
            System.Diagnostics.Stopwatch.Frequency;

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void CloudTrace(CloudFaceScanStage stage, int count = -1, double durationMs = -1d)
        {
            if (previewMode == ProfileDataMode.Cloud)
                CloudFaceScanDiagnostics.Record(stage, count, durationMs);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void CloudGate(CloudFaceScanStage stage)
        {
            if (previewMode == ProfileDataMode.Cloud) CloudFaceScanDiagnostics.Gate(stage);
        }

        private bool AsyncFrameInvalid(CameraFrameOrientation capturedOrientation, double capturedAt, int generation)
        {
            CloudFaceScanStage? reason = null;
            if (generation != recognitionGeneration) reason = CloudFaceScanStage.DiscardGeneration;
            else if (!previewActive || !cameraReady) reason = CloudFaceScanStage.DiscardCamera;
            else if (previewMode != CloudModeSession.Mode) reason = CloudFaceScanStage.DiscardMode;
            else if (Time.realtimeSinceStartupAsDouble - capturedAt > MaxAsyncResultAgeSeconds)
                reason = CloudFaceScanStage.DiscardAge;
            else if (previewOrientationEffect != null && previewOrientationEffect.HasOrientation &&
                !previewOrientationEffect.Orientation.Equals(capturedOrientation))
                reason = CloudFaceScanStage.DiscardOrientation;
            if (!reason.HasValue) return false;
            CloudTrace(reason.Value, durationMs:
                (Time.realtimeSinceStartupAsDouble - capturedAt) * 1000d);
            // Every rejected cloud frame must fail closed, including pre-HTTP
            // stale inference branches that previously left an old card visible.
            if (previewMode == ProfileDataMode.Cloud) cloudPresenter?.Clear();
            return true;
        }

        private void ReportAsyncFailure(string stage, System.Exception error)
        {
            CloudTrace(CloudFaceScanStage.InferenceFailed);
            if (previewMode == ProfileDataMode.Cloud) cloudPresenter?.Clear();
            recognitionFaulted = true; // Do not reuse a Worker after a failed/unfinished GPU readback.
            if (!initialYuNetResultObserved) TraceCameraStartup("first-inference-error");
            PerformanceDiagnostics.Record(PerfMetric.PipelineErrors, 1);
            // Cloud diagnostics accept fixed stages/numbers only. Exception text
            // and stacks may contain input or credentials from lower layers.
            if (previewMode == ProfileDataMode.Cloud) return;
            Debug.LogError("[FaceRecognition] Async pipeline stopped at " + stage +
                ": " + error.GetType().Name, this);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Exact message and original stack for diagnosis, not the user-facing UI.
            Debug.LogException(error, this);
#endif
        }

        private IEnumerator ProcessRecognitionFrameRoutine(Texture texture, CameraFrameOrientation orientation)
        {
            double capturedAt = Time.realtimeSinceStartupAsDouble;
            int generation = recognitionGeneration;
            float[] queryEmbedding = null;
            PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.Capture);
            S415FrameGapProbe.SetStage("Recognition.YuNet");
            try
            {
                // PERF1 Preprocessing intentionally keeps its original no-inference path.
                if (PerformanceDiagnostics.IsScenario(PerfScenario.Preprocessing))
                {
                    using (DetectionMarker.Auto())
                    using (PerformanceDiagnostics.Measure(PerfMetric.DetectionMs))
                        recognitionFrame = detectionController.ProcessMultiFrame(texture, orientation);
                }
                else
                {
                    if (!initialYuNetAttemptObserved)
                    {
                        initialYuNetAttemptObserved = true;
                        TraceCameraStartup("first-yunet-begin");
                    }
                    IEnumerator detection = detectionController.ProcessMultiFrameAsync(
                        texture, orientation, frame => recognitionFrame = frame,
                        rearFacing: imageSource != null &&
                            imageSource.CurrentFacing == CameraFacing.Rear);
                    bool detectionFailed = false;
                    try
                    {
                        // Manually advance this simple frame-polling enumerator to capture
                        // GPU failures without leaking the pending snapshot/Worker resources.
                        while (true)
                        {
                            bool more = false;
                            object yielded = null;
                            try
                            {
                                more = detection.MoveNext();
                                if (more) yielded = detection.Current;
                            }
                            catch (System.Exception error)
                            {
                                ReportAsyncFailure("YuNet execution", error);
                                detectionFailed = true;
                            }
                            if (detectionFailed || !more) break;
                            yield return yielded;
                        }
                    }
                    finally { (detection as System.IDisposable)?.Dispose(); }
                    if (detectionFailed) yield break;
                    PerformanceDiagnostics.Record(PerfMetric.DetectionMs,
                        (Time.realtimeSinceStartupAsDouble - capturedAt) * 1000d);
                }

                MultiFaceDetectionFrame frameResult = recognitionFrame;
                if (frameResult == null)
                {
                    CloudTrace(CloudFaceScanStage.DetectionMissing, durationMs:
                        (Time.realtimeSinceStartupAsDouble - capturedAt) * 1000d);
                    if (previewMode == ProfileDataMode.Cloud) cloudPresenter?.Clear();
                    yield break;
                }
                CloudTrace(CloudFaceScanStage.Detection, frameResult.Faces.Count,
                    (Time.realtimeSinceStartupAsDouble - capturedAt) * 1000d);
                if (frameResult.Faces.Count == 0) CloudTrace(CloudFaceScanStage.NoFace);
#if UNITY_ANDROID && DEVELOPMENT_BUILD
                if (imageSource != null && imageSource.CurrentFacing == CameraFacing.Rear &&
                    Time.unscaledTime >= nextRearDetectionTraceAt)
                {
                    nextRearDetectionTraceAt = Time.unscaledTime + 1f;
                    Debug.Log("[S4-14] Rear recognition faces=" + frameResult.Faces.Count +
                        " frame=" + Time.frameCount, this);
                }
#endif
                if (!initialYuNetResultObserved)
                {
                    initialYuNetResultObserved = true;
                    TraceCameraStartup("first-yunet-result-ready");
                }

                // Never overlay a result on a rotated/restarted preview.
                if (AsyncFrameInvalid(orientation, capturedAt, generation))
                {
                    PerformanceDiagnostics.Record(PerfMetric.StaleResults, 1);
                    yield break;
                }

                PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.Match);
                PerformanceDiagnostics.PipelinePass(frameResult.Faces.Count);
                if (PerformanceDiagnostics.IsScenario(PerfScenario.Preprocessing) ||
                    PerformanceDiagnostics.IsScenario(PerfScenario.YuNet)) yield break;

                if (CloudModeSession.Mode == ProfileDataMode.Cloud)
                    cloudPresenter?.ObserveDetections(frameResult.Faces,
                        frameResult.CanonicalWidth, frameResult.CanonicalHeight);

                if (frameResult.Faces.Count == 0)
                {
                    PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.Present);
                    if (CloudModeSession.Mode == ProfileDataMode.Cloud)
                        cloudPresenter?.Clear(); // No face: never show a previous person's card.
                    else
                        HandleRecognitionResults(System.Array.Empty<FaceRecognitionItem>(),
                            frameResult.CanonicalWidth, frameResult.CanonicalHeight);
                    yield break;
                }

                bool cloudMode = CloudModeSession.Mode == ProfileDataMode.Cloud;
                bool cloudNoMatchObserved = false;
                IReadOnlyList<FaceEmbedding> registeredEmbeddings = null;
                if (!cloudMode && !PerformanceDiagnostics.SkipMatching)
                {
                    using (PerformanceDiagnostics.Measure(PerfMetric.SQLiteEmbeddingsMs))
                        registeredEmbeddings = bootstrap.FaceEmbeddingRepository.GetByModel(SFaceRecognizer.ModelVersion);
                }

                if (!cloudMode && !PerformanceDiagnostics.SkipMatching &&
                    registeredEmbeddings.Count == 0)
                {
                    if (enableDebugLogs && !PerformanceDiagnostics.Active)
                        Debug.Log("[FaceRecognition] 등록된 임베딩이 하나도 없습니다.", this);
                    HandleRecognitionResults(System.Array.Empty<FaceRecognitionItem>(),
                        frameResult.CanonicalWidth, frameResult.CanonicalHeight);
                    yield break;
                }

                recognitionResults.Clear();
                cloudResults.Clear();
                // At most one query per cloud scan; rotate candidate faces so
                // the first detected face cannot starve all others.
                int cloudSelected = cloudMode ?
                    cloudRoundRobin++ % frameResult.Faces.Count : -1;
                int faceIndex = 0;
                foreach (FaceDetectionItem face in frameResult.Faces)
                {
                    if (cloudMode && faceIndex++ != cloudSelected) continue;
                    // Yield between faces, and poll GPU completion over many rendered frames.
                    yield return null;
                    if (AsyncFrameInvalid(orientation, capturedAt, generation))
                    {
                        PerformanceDiagnostics.Record(PerfMetric.StaleResults, 1);
                        yield break;
                    }

                    PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.SFacePrepare);
                    S415FrameGapProbe.SetStage("Recognition.SFace");
                    double faceStart = Time.realtimeSinceStartupAsDouble;
                    CloudTrace(CloudFaceScanStage.SFaceStarted);
                    bool faceFailed = false;
                    try
                    {
                        if (!firstSFaceAttemptObserved)
                        {
                            firstSFaceAttemptObserved = true;
                            TraceCameraStartup("first-sface-begin");
                        }
                        using (Mat alignedFace = FaceAligner.Align(frameResult.CanonicalMat, face.Landmarks))
                            sFaceRecognizer.BeginAsync(alignedFace, recognitionPolicy.SFaceLayersPerFrame);
                    }
                    catch (System.Exception error)
                    {
                        ReportAsyncFailure("SFace schedule", error);
                        faceFailed = true;
                    }
                    if (faceFailed) yield break;

                    if (sFaceRecognizer.IsScheduling)
                    {
                        // Frame slicing ONLY spreads model layer submissions; the GPU may still
                        // block rendering on some devices. Keep the original result pipeline.
                        PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.SFaceScheduling);
                        double scheduleStart = Time.realtimeSinceStartupAsDouble;
                        while (sFaceRecognizer.IsScheduling)
                        {
                            try { sFaceRecognizer.AdvanceAsyncSchedule(); }
                            catch (System.Exception error)
                            {
                                ReportAsyncFailure("SFace sliced schedule", error);
                                faceFailed = true;
                            }
                            if (faceFailed) break;
                            if (Time.realtimeSinceStartupAsDouble - scheduleStart > 15d)
                            {
                                ReportAsyncFailure("SFace schedule timeout",
                                    new System.TimeoutException("SFace sliced scheduling exceeded 15 seconds."));
                                faceFailed = true;
                                break;
                            }
                            if (sFaceRecognizer.IsScheduling) yield return null;
                        }
                        if (faceFailed) yield break;
                        PerformanceDiagnostics.Record(PerfMetric.SFaceScheduleWallMs,
                            (Time.realtimeSinceStartupAsDouble - scheduleStart) * 1000d);
                    }

                    PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.SFaceWaiting);
                    double sfaceWaitStart = Time.realtimeSinceStartupAsDouble;
                    while (true)
                    {
                        bool ready = false;
                        try { ready = sFaceRecognizer.IsAsyncComplete; }
                        catch (System.Exception error)
                        {
                            ReportAsyncFailure("SFace polling", error);
                            faceFailed = true;
                        }
                        if (faceFailed || ready) break;
                        if (Time.realtimeSinceStartupAsDouble - sfaceWaitStart > 15d)
                        {
                            ReportAsyncFailure("SFace polling timeout", new System.TimeoutException("SFace readback timed out."));
                            faceFailed = true;
                            break;
                        }
                        yield return null;
                    }
                    if (faceFailed) yield break;

                    PerformanceDiagnostics.Record(PerfMetric.SFaceWaitWallMs,
                        (Time.realtimeSinceStartupAsDouble - sfaceWaitStart) * 1000d);
                    PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.SFaceFinalize);
                    queryEmbedding = null;
                    try
                    {
                        queryEmbedding = sFaceRecognizer.CompleteAsync();
                        if (!firstSFaceResultObserved)
                        {
                            firstSFaceResultObserved = true;
                            TraceCameraStartup("first-sface-result-ready");
                        }
                    }
                    catch (System.Exception error)
                    {
                        ReportAsyncFailure("SFace finalize", error);
                        faceFailed = true;
                    }
                    if (faceFailed) yield break;
                    CloudTrace(CloudFaceScanStage.SFaceReady, durationMs:
                        (Time.realtimeSinceStartupAsDouble - faceStart) * 1000d);
                    PerformanceDiagnostics.Record(PerfMetric.SFaceMs,
                        (Time.realtimeSinceStartupAsDouble - faceStart) * 1000d);
                    if (AsyncFrameInvalid(orientation, capturedAt, generation))
                    {
                        PerformanceDiagnostics.Record(PerfMetric.StaleResults, 1);
                        yield break;
                    }

                    PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.Match);
                    if (PerformanceDiagnostics.SkipMatching) continue;
                    if (cloudMode)
                    {
                        CloudFaceMatchStatus status = CloudFaceMatchStatus.NotReady;
                        CloudFacePublicCard card = null;
                        yield return CloudFaceMatchApi.Match(queryEmbedding,
                            (value, matchedCard) => { status = value; card = matchedCard; });
                        if (queryEmbedding != null)
                            Array.Clear(queryEmbedding, 0, queryEmbedding.Length);
                        if (AsyncFrameInvalid(orientation, capturedAt, generation) ||
                            CloudModeSession.Mode != ProfileDataMode.Cloud ||
                            !CloudFaceMatchApi.CanQuery)
                        {
                            cloudPresenter?.Clear();
                            yield break;
                        }
                        if (status == CloudFaceMatchStatus.Matched && card != null)
                            cloudResults.Add(new CloudRecognitionHit
                            {
                                Card = card,
                                FaceBounds = face.Bounds,
                            });
                        else if (status == CloudFaceMatchStatus.NoMatch)
                        {
                            cloudNoMatchObserved = true;
                            cloudPresenter?.ObserveNoMatch(face.Bounds, frameResult.CanonicalMat.Cols,
                                frameResult.CanonicalMat.Rows, Time.unscaledTime);
                        }
                        else
                        {
                            cloudPresenter?.Clear();
                            yield break;
                        }
                        continue;
                    }
                    if (!FaceRecognitionMatcher.TryRecognize(
                        queryEmbedding, registeredEmbeddings, recognitionThreshold,
                        out FaceRecognitionResult result))
                    {
                        if (enableDebugLogs && !PerformanceDiagnostics.Active)
                            Debug.Log("[FaceRecognition] NO CANDIDATE", this);
                        continue;
                    }

                    recognitionResults.Add(new FaceRecognitionItem(face, result));
                    if (enableDebugLogs && !PerformanceDiagnostics.Active && !result.Matched)
                        Debug.Log($"[FaceRecognition] UNKNOWN Similarity={result.Similarity:F4}", this);
                }

                if (AsyncFrameInvalid(orientation, capturedAt, generation))
                {
                    PerformanceDiagnostics.Record(PerfMetric.StaleResults, 1);
                    yield break;
                }
                PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.Present);
                S415FrameGapProbe.SetStage("Recognition.CardUI");
                if (cloudMode)
                {
                    // Only a valid server NO_MATCH earns the bounded presentation grace.
                    if (cloudResults.Count == 0 && !cloudNoMatchObserved) cloudPresenter?.Clear();
                    else if (cloudResults.Count > 0)
                    {
                        cloudPresenter?.Observe(cloudResults, frameResult.CanonicalMat.Cols,
                            frameResult.CanonicalMat.Rows, Time.unscaledTime);
                    }
                }
                else
                    HandleRecognitionResults(recognitionResults,
                        frameResult.CanonicalMat.Cols, frameResult.CanonicalMat.Rows, false);
            }
            finally
            {
                CloudTrace(CloudFaceScanStage.ScanCompleted, durationMs:
                    (Time.realtimeSinceStartupAsDouble - capturedAt) * 1000d);
                PerformanceDiagnostics.Record(PerfMetric.PipelineWallMs,
                    (Time.realtimeSinceStartupAsDouble - capturedAt) * 1000d);
                if (previewMode == ProfileDataMode.Cloud && queryEmbedding != null)
                    Array.Clear(queryEmbedding, 0, queryEmbedding.Length);
                sFaceRecognizer?.CancelAsync();
                recognitionFrame?.Dispose();
                recognitionFrame = null;
                recognitionResults.Clear();
                recognitionProcessing = false;
                recognitionRoutine = null;
                PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.Idle);
                S415FrameGapProbe.SetStage("Recognition.Idle");
            }
        }

        private void OnCameraReady(Texture texture)
        {
            if (!previewActive)
                return;

            CloudTrace(CloudFaceScanStage.CameraReady, durationMs:
                (Time.realtimeSinceStartupAsDouble - cameraStartupAt) * 1000d);

            if (previewRawImage != null)
                previewRawImage.texture = texture;
            cameraReady = true;
            TraceCameraStartup("camera-callback");
            // Recognition activates separately after workers are ready.
            Debug.Log("[FaceRecognition] Camera ready", this);
        }

        private void OnCameraError(string error)
        {
            if (!previewActive)
                return;

            cameraReady = false;
            modelStartupFailed = true;
            S415FrameGapProbe.SetStage("Recognition.Error");
            TraceCameraStartup("camera-error");

            Debug.LogError(
                $"[FaceRecognition] camera start failed: {error}",
                this
            );
        }

        private void HandleRecognitionResults(
            IReadOnlyList<FaceRecognitionItem> faceRecognitionResults,
            int canonicalWidth,
            int canonicalHeight,
            bool clearMissingImmediately = true
        )
        {
            if (PerformanceDiagnostics.SkipPresentation) return;
            using var measurement = PerformanceDiagnostics.Measure(PerfMetric.CardUiMs);
            HashSet<long> visibleProfileIds = new HashSet<long>();

            foreach (FaceRecognitionItem item in faceRecognitionResults)
            {
                if (!item.Recognition.Matched)
                {
                    continue;
                }

                var profile = bootstrap.GetProfileUseCase.Execute(item.Recognition.ProfileId);

                if (profile == null)
                {
                    if (!PerformanceDiagnostics.Active) Debug.LogWarning(
                        $"[FaceRecognition] ProfileId={item.Recognition.ProfileId} 프로필을 찾을수 없습니다.", this
                    );

                    continue;
                }

                if (!profile.IsActive)
                {
                    if (!PerformanceDiagnostics.Active) Debug.LogWarning(
                        $"[FaceRecognition] ProfileId={item.Recognition.ProfileId} 프로필이 활성화되지 않았습니다.",
                        this
                    );

                    continue;
                }

                visibleProfileIds.Add(item.Recognition.ProfileId);
                lastVisibleTimeByProfile[item.Recognition.ProfileId] = Time.unscaledTime;
                if (enableDebugLogs && !PerformanceDiagnostics.Active)
                {
                    Debug.Log(
                        $"[FaceRecognition] ProfileId={item.Recognition.ProfileId}, Detection Bounds={item.Detection.Bounds}",
                        this
                    );
                }
                UpdateFacePresentation(item,profile,canonicalWidth,canonicalHeight);

                HandleRecognizedFace(item, profile);
            }
            List<long> profilesToRemove = new List<long>();

            foreach (long profileId in faceOverlaysByProfile.Keys)
            {
                if (!visibleProfileIds.Contains(profileId) &&
                    (clearMissingImmediately || !lastVisibleTimeByProfile.TryGetValue(profileId, out float lastVisible) ||
                     Time.unscaledTime - lastVisible >= RecognitionUiGraceSeconds))
                    profilesToRemove.Add(profileId);
            }

            foreach (long profileId in profilesToRemove)
            {
                RectTransform overlay = faceOverlaysByProfile[profileId];
                if (overlay != null)
                    Destroy(overlay.gameObject);

                faceOverlaysByProfile.Remove(profileId);
                lastVisibleTimeByProfile.Remove(profileId);
                RemoveBusinessCard(profileId);
            }
            if (businessCardsByProfile.Count == 1) cardLayoutDirty = true;
            UpdateBusinessCardPositions();
        }

        private void HandleRecognizedFace(
            FaceRecognitionItem item,
            Profile profile
        )
        {
            long profileId = item.Recognition.ProfileId;
            float similarity = item.Recognition.Similarity;

            if (lastHandledTimeByProfile.TryGetValue
                (
                    profileId, out float lastHandledTime
                )
            )
            {
                if(Time.time - lastHandledTime < recognizedFaceCooldown)
                {
                    return;
                }                
            }
            lastHandledTimeByProfile[profileId] = Time.time;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!PerformanceDiagnostics.Active) Debug.Log(
                $"[FaceRecognition] MATCH ProfileId={profileId}, Similarity={similarity:F4}",
                this
            );
#endif
        }

        private void EnsureBusinessCard(long profileId)
        {
            if (businessCardsByProfile.TryGetValue(profileId, out BusinessCardView existing) && existing != null)
                return;
            BusinessCardView card = businessCardPanelController.CreateRecognitionCard(profileId, faceOverlayRoot,
                () => businessCardPanelController.ShowRecognizedLocalProfile(profileId));
            if (card != null)
            {
                businessCardsByProfile[profileId] = card;
                cardLayoutDirty = true;
                if (!firstCardCreated)
                {
                    firstCardCreated = true;
                    TraceCameraStartup("first-card-created");
                }
            }
        }

        private void RemoveBusinessCard(long profileId)
        {
            if (businessCardsByProfile.TryGetValue(profileId, out BusinessCardView card) && card != null)
            {
                card.gameObject.SetActive(false);
                Destroy(card.gameObject);
            }
            if (businessCardsByProfile.Remove(profileId)) cardLayoutDirty = true;
        }

        private void ClearBusinessCards()
        {
            foreach (BusinessCardView card in businessCardsByProfile.Values)
            {
                if (card == null) continue;
                card.gameObject.SetActive(false);
                Destroy(card.gameObject);
            }
            businessCardsByProfile.Clear();
            cardLayoutIds.Clear();
            layoutCards.Clear();
            layoutFaces.Clear();
            cardLayoutBounds.Clear();
            cardLayoutDirty = true;
        }

        private void UpdateBusinessCardPositions()
        {
            if (faceOverlayRoot == null) return;
            UnityEngine.Rect area = faceOverlayRoot.rect;
            if (area != cardLayoutArea) cardLayoutDirty = true;
            foreach (var entry in businessCardsByProfile)
                if (entry.Value != null && faceOverlaysByProfile.TryGetValue(entry.Key, out var overlay) && overlay != null &&
                    entry.Value.gameObject.activeSelf != overlay.gameObject.activeSelf) cardLayoutDirty = true;
            // Multi-person card slots stay fixed until membership or viewport changes.
            // A single card follows its face, updated only after recognition results, not every rendered frame.
            if (!cardLayoutDirty) return;
            cardLayoutIds.Clear();
            cardLayoutIds.AddRange(businessCardsByProfile.Keys);
            cardLayoutIds.Sort();
            layoutCards.Clear();
            layoutFaces.Clear();
            foreach (long id in cardLayoutIds)
            {
                BusinessCardView card = businessCardsByProfile[id];
                if (card == null) continue;
                if (!faceOverlaysByProfile.TryGetValue(id, out RectTransform overlay) ||
                    overlay == null || !overlay.gameObject.activeSelf)
                {
                    card.gameObject.SetActive(false);
                    continue;
                }
                overlay.GetWorldCorners(overlayCorners);
                Vector2 min = faceOverlayRoot.InverseTransformPoint(overlayCorners[0]);
                Vector2 max = faceOverlayRoot.InverseTransformPoint(overlayCorners[2]);
                layoutCards.Add(card);
                layoutFaces.Add(UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y));
            }
            using (CardLayoutMarker.Auto())
            using (PerformanceDiagnostics.Measure(PerfMetric.CardLayoutMs))
                cardLayoutBounds = CalculateBusinessCardBounds(area, layoutFaces);
            cardLayoutArea = area;
            cardLayoutDirty = false;
            for (int i = 0; i < layoutCards.Count; i++)
            {
                if (cardLayoutBounds.Count == 0) { layoutCards[i].gameObject.SetActive(false); continue; }
                UpdateBusinessCardPosition(layoutCards[i], cardLayoutBounds[i]);
            }
        }

        internal static List<UnityEngine.Rect> CalculateBusinessCardBounds(UnityEngine.Rect area, List<UnityEngine.Rect> faces)
        {
            const float margin = 24f;
            const float gap = 12f;
            var placed = new List<UnityEngine.Rect>();
            var available = new UnityEngine.Rect(area.xMin + margin, area.yMin + margin,
                area.width - 2f * margin, area.height - 2f * margin);
            if (faces.Count == 0 || available.width <= 0f || available.height <= 0f) return placed;
            float scale = Mathf.Min(1f, available.width * .65f / 900f, available.height / 360f);
            Vector2 size = new Vector2(900f, 360f) * scale;
            foreach (UnityEngine.Rect face in faces)
            {
                var positions = new List<Vector2>
                {
                    new Vector2(face.center.x, face.yMin - gap - size.y * .5f),
                    new Vector2(face.center.x, face.yMax + gap + size.y * .5f),
                    new Vector2(face.xMax + gap + size.x * .5f, face.center.y),
                    new Vector2(face.xMin - gap - size.x * .5f, face.center.y)
                };
                // Search free space beside already placed cards, as well as preview edges.
                var xs = new List<float> { available.xMin + size.x * .5f, available.xMax - size.x * .5f, face.center.x };
                var ys = new List<float> { available.yMin + size.y * .5f, available.yMax - size.y * .5f, face.yMin - gap - size.y * .5f };
                foreach (UnityEngine.Rect occupied in placed)
                {
                    xs.Add(occupied.xMin - gap - size.x * .5f);
                    xs.Add(occupied.xMax + gap + size.x * .5f);
                    ys.Add(occupied.yMin - gap - size.y * .5f);
                    ys.Add(occupied.yMax + gap + size.y * .5f);
                }
                foreach (float x in xs) foreach (float y in ys) positions.Add(new Vector2(x, y));
                bool found = false;
                float bestScore = float.PositiveInfinity;
                UnityEngine.Rect best = default;
                for (int p = 0; p < positions.Count; p++)
                {
                    Vector2 center = positions[p];
                    center.x = Mathf.Clamp(center.x, available.xMin + size.x * .5f, available.xMax - size.x * .5f);
                    center.y = Mathf.Clamp(center.y, available.yMin + size.y * .5f, available.yMax - size.y * .5f);
                    var candidate = new UnityEngine.Rect(center - size * .5f, size);
                    bool overlaps = false;
                    foreach (UnityEngine.Rect occupied in placed)
                    {
                        var padded = new UnityEngine.Rect(occupied.xMin - gap, occupied.yMin - gap,
                            occupied.width + gap * 2f, occupied.height + gap * 2f);
                        if (candidate.Overlaps(padded)) { overlaps = true; break; }
                    }
                    if (overlaps) continue;
                    float score = (center - positions[0]).sqrMagnitude;
                    foreach (UnityEngine.Rect otherFace in faces)
                        if (candidate.Overlaps(otherFace)) score += available.size.sqrMagnitude * 10f;
                    if (score >= bestScore) continue;
                    found = true; bestScore = score; best = candidate;
                }
                if (!found) return ArrangeBusinessCardGrid(available, faces, scale, gap);
                placed.Add(best);
            }
            return placed;
        }

        private static List<UnityEngine.Rect> ArrangeBusinessCardGrid(UnityEngine.Rect available,
            List<UnityEngine.Rect> faces, float maximumScale, float gap)
        {
            // Crowded previews use a shared size so every card remains visible without overlap.
            int columns = 1;
            float scale = 0f;
            float gridGap = Mathf.Min(gap, available.width / (faces.Count * 2f), available.height / (faces.Count * 2f));
            for (int c = 1; c <= faces.Count; c++)
            {
                int rows = Mathf.CeilToInt(faces.Count / (float)c);
                float fitted = Mathf.Min(maximumScale, (available.width - gridGap * (c - 1)) / (c * 900f),
                    (available.height - gridGap * (rows - 1)) / (rows * 360f));
                if (fitted > scale) { scale = fitted; columns = c; }
            }
            Vector2 size = new Vector2(900f, 360f) * scale;
            var slots = new List<UnityEngine.Rect>();
            float left = available.center.x - (columns * size.x + (columns - 1) * gridGap) * .5f;
            for (int i = 0; i < faces.Count; i++)
                slots.Add(new UnityEngine.Rect(left + (i % columns) * (size.x + gridGap),
                    available.yMin + (i / columns) * (size.y + gridGap), size.x, size.y));
            var result = new List<UnityEngine.Rect>();
            foreach (UnityEngine.Rect face in faces)
            {
                int nearest = 0;
                for (int i = 1; i < slots.Count; i++)
                    if ((slots[i].center - face.center).sqrMagnitude < (slots[nearest].center - face.center).sqrMagnitude) nearest = i;
                result.Add(slots[nearest]);
                slots.RemoveAt(nearest);
            }
            return result;
        }

        private void UpdateBusinessCardPosition(BusinessCardView businessCard, UnityEngine.Rect bounds)
        {
            // Preserve the existing 900 x 360 card artwork and its aspect ratio.
            RectTransform card = (RectTransform)businessCard.transform;
            bool wasPositioned = card.gameObject.activeSelf && card.sizeDelta == new Vector2(900f, 360f);
            card.anchorMin = card.anchorMax = faceOverlayRoot.pivot;
            card.pivot = new Vector2(.5f, .5f);
            card.sizeDelta = new Vector2(900f, 360f);
            card.localScale = Vector3.one * (bounds.width / 900f);
            if (layoutCards.Count != 1 || !wasPositioned) card.anchoredPosition = bounds.center;
            else card.anchoredPosition = ClampCardCenter(card.anchoredPosition, bounds.size, faceOverlayRoot.rect);
            if (card.GetSiblingIndex() != card.parent.childCount - 1) card.SetAsLastSibling();
            card.gameObject.SetActive(true);
        }

        private static Vector2 ClampCardCenter(Vector2 center, Vector2 size, UnityEngine.Rect area)
        {
            center.x = Mathf.Clamp(center.x, area.xMin + 24f + size.x * .5f, area.xMax - 24f - size.x * .5f);
            center.y = Mathf.Clamp(center.y, area.yMin + 24f + size.y * .5f, area.yMax - 24f - size.y * .5f);
            return center;
        }

        private void SmoothSingleBusinessCard()
        {
            if (layoutCards.Count != 1 || cardLayoutBounds.Count != 1 || layoutCards[0] == null ||
                !layoutCards[0].gameObject.activeSelf) return;
            RectTransform card = (RectTransform)layoutCards[0].transform;
            Vector2 target = cardLayoutBounds[0].center;
            // Ignore sub-pixel/noisy movement; ease a lone card toward the last recognition result.
            if ((target - card.anchoredPosition).sqrMagnitude < 4f) return;
            card.anchoredPosition = ClampCardCenter(Vector2.Lerp(card.anchoredPosition, target,
                1f - Mathf.Exp(-12f * Time.unscaledDeltaTime)), cardLayoutBounds[0].size, faceOverlayRoot.rect);
        }

        private void UpdateFacePresentation(
            FaceRecognitionItem item,
            Profile profile,
            int canonicalWidth,
            int canonicalHeight
        )
        {
            UnityEngine.Rect faceBounds = item.Detection.Bounds;            

            float normalizedX = faceBounds.x / canonicalWidth;
            float normalizedY = faceBounds.y / canonicalHeight;

            float normalizedWidth = faceBounds.width / canonicalWidth;
            float normalizedHeight = faceBounds.height / canonicalHeight;

            float uiY = 1f-normalizedY-normalizedHeight;

            UnityEngine.Rect uiBounds =
                new UnityEngine.Rect
                (
                    normalizedX,
                    uiY,
                    normalizedWidth,
                    normalizedHeight
                );

            long profileId = item.Recognition.ProfileId;
            if (!CameraPreviewOrientationEffect.TryMapUprightBounds(uiBounds,
                previewOrientationEffect.VisibleUprightUVRect, out uiBounds))
            {
                if (faceOverlaysByProfile.TryGetValue(profileId, out RectTransform hiddenOverlay) && hiddenOverlay != null)
                    hiddenOverlay.gameObject.SetActive(false);
                return;
            }

            if (enableDebugLogs && !PerformanceDiagnostics.Active)
                Debug.Log($"[FaceRecognition] UI Bounds x={uiBounds.x:F3}, y={uiBounds.y:F3}, W={uiBounds.width:F3}, H={uiBounds.height:F3}",this);

            Vector2 anchorMin = new Vector2(
                uiBounds.x,
                uiBounds.y
            );

            Vector2 anchorMax = new Vector2(
                uiBounds.x + uiBounds.width,
                uiBounds.y + uiBounds.height
            );

            if(faceOverlaysByProfile.TryGetValue(
                profileId,
                out RectTransform overlay
            ) && overlay != null)
            {
                overlay.gameObject.SetActive(true);
                overlay.anchorMin = anchorMin;
                overlay.anchorMax = anchorMax;

                overlay.offsetMin = Vector2.zero;
                overlay.offsetMax = Vector2.zero;
            }
            else
            {
                RectTransform newoverlay = 
                    Instantiate(
                        faceOverlayPrefab, faceOverlayRoot
                    );
                
                faceOverlaysByProfile[profileId] = newoverlay;

                newoverlay.GetComponent<FaceRecognitionOverlayView>().Bind(profileId, null, enableDebugLogs);

                newoverlay.anchorMin = anchorMin;
                newoverlay.anchorMax = anchorMax;                
                newoverlay.offsetMin = Vector2.zero;
                newoverlay.offsetMax = Vector2.zero;
            }
            EnsureBusinessCard(profileId);
        }
    }
}
