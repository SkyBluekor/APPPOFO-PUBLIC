using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.FaceRegistration.Selection;
using AppPofo.Presentation.FaceRegistration.Quality;
using AppPofo.Presentation.FaceRegistration.Embedding;
using Unity.InferenceEngine;
using System;
using System.Collections.Generic;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Presentation.Common;

namespace AppPofo.Presentation.FaceRegistration
{

    [DisallowMultipleComponent]
    public sealed class FaceRegistrationCamera : MonoBehaviour
    {
        [SerializeField] private RawImage cameraPreview;
        [SerializeField] private FaceRegistrationImageSourceAdapter imageSource;
        [SerializeField] private ModelAsset faceDetectionModel;
        [Header("Voice guidance")]
        [SerializeField] private AudioSource guidanceAudioSource;
        [SerializeField] private AudioClip centerVoice;
        [SerializeField] private AudioClip frontVoice;
        [SerializeField] private AudioClip leftVoice;
        [SerializeField] private AudioClip rightVoice;
        [SerializeField] private AudioClip completeVoice;
        [SerializeField] private AudioClip tooDarkVoice;
        [SerializeField] private ModelAsset faceRecognitionModel;
        [SerializeField] private AppPofoBootstrap bootstrap;
        private SFaceRecognizer sFaceRecognizer;
        private const float DarkWarningDelaySeconds = 1.5f;
        // Match the existing gate's darkness criteria without changing candidate acceptance.
        private const float DarkWarningMinimumMean = 55f;
        private const float DarkWarningMaximumDarkRatio = 0.35f;
        private readonly FaceBrightnessEvaluator guidanceBrightnessEvaluator = new FaceBrightnessEvaluator();
        private FaceRegistrationStep? lastAnnouncedStep;
        private float darkSince = -1f;
        private bool darkWarningLatched;
        private float completionCloseTime = -1f;
        [SerializeField, Tooltip("Record every Unity frame during a camera session. Takes effect on the next session.")]
        private bool collectCalibrationLogs = false;
        [SerializeField] private bool enableDetectionDiagnostics = false;
        [SerializeField] private bool enableRegistrationDiagnostics = false;
        private float nextRegistrationDiagnosticTime;
        private float nextRearRoiDiagnosticTime;
        public string RegistrationFeedback { get; private set; }
        public FaceRegistrationStep RegistrationFeedbackStep { get; private set; }
        [SerializeField] private FaceCalibrationLabel calibrationLabel = FaceCalibrationLabel.NORMAL;
        private FaceRegistrationStep currentStep = FaceRegistrationStep.Front;
        private int validCandidateCount = 0;
        private const int RequiredCandidateCount = 10;

        private const float DetectionInterval = 0.2f;
        private readonly FrameQualityGate qualityGate = new FrameQualityGate();
        private readonly BestFrameSelector bestFrameSelector = new BestFrameSelector();
        private readonly FaceCalibrationCsvLogger calibrationLogger = new FaceCalibrationCsvLogger();
        private bool calibrationSessionActive;
        private int lastCalibrationFrame = -1;
        private SentisFaceDetector detector;
        private FaceDetectionController detectionController;
        private float nextDetectionTime;
        private bool detectionSuccessLogged;
        private bool snapshotSuccessLogged;

        private int requestVersion;
        private bool ownsCameraRequest;
        private int previewReadyFrame = -1;
        private bool firstDetectionObserved;
        private bool firstGateObserved;
        private Coroutine registrationInferenceRoutine;
        private bool registrationInferencePending;
        private Coroutine embeddingFinalizationRoutine;
        private bool embeddingFinalizationPending;
        private double cameraRequestStartedAt;
        private void TraceStartup(string stage)
        {
#if UNITY_ANDROID && DEVELOPMENT_BUILD && APPPOFO_PERF_DIAGNOSTICS
            Debug.Log("[S4-13] Registration stage=" + stage +
                " request=" + requestVersion + " elapsedMs=" +
                ((Time.realtimeSinceStartupAsDouble - cameraRequestStartedAt) * 1000d)
                    .ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                " frame=" + Time.frameCount, this);
#endif
        }
        private CameraPreviewOrientationEffect orientationEffect;

        private FaceBestCandidate? frontCandidate;
        private FaceBestCandidate? leftCandidate;
        private FaceBestCandidate? rightCandidate;
        // Full-resolution crop from the same frame selected for Front embedding.
        private OpenCvSharp.Mat frontAvatarMat;

        private float[] frontEmbedding;
        private float[] leftEmbedding;
        private float[] rightEmbedding;
        private IFaceEnrollmentWriter enrollmentWriter;

        // Set the enrollment target independently from the face inference pipeline.
        public void ConfigureLocalEnrollment(long profileId)
        {
            enrollmentWriter = new LocalFaceEnrollmentWriter(
                () => bootstrap != null && bootstrap.IsInitialized
                    ? bootstrap.FaceEmbeddingRepository : null, profileId);
        }

        public void ConfigureEnrollmentWriter(IFaceEnrollmentWriter writer)
        {
            enrollmentWriter = writer;
        }

        public float PreviewExposureGain => detectionController != null ?
            detectionController.PreviewExposureGain : 1f;
        public FaceRegistrationStep CurrentStep => currentStep;
        public int ValidCandidateCount => validCandidateCount;
        public int RequiredCandidatesPerStep => RequiredCandidateCount;
        public bool IsFinalizingRegistration => embeddingFinalizationPending;

        private void RefreshRegistrationProgressUi()
        {
            GetComponentInParent<FaceRegistrationScreen>()?.RefreshRegistrationProgressUi(this);
        }

        private void LateUpdate()
        {
            if (currentStep == FaceRegistrationStep.Complete)
            {
                if (completionCloseTime >= 0f && Time.unscaledTime >= completionCloseTime)
                {
                    completionCloseTime = -1f;
                    var screen = GetComponentInParent<FaceRegistrationScreen>();
                    if (screen != null && screen.HasEnrollmentTarget) screen.Hide();
                }
                return;
            }
            if (ownsCameraRequest && cameraPreview != null && imageSource != null &&
                imageSource.TryGetPreviewFrame(out Texture texture, out var orientation))
            {
                if (cameraPreview.texture != texture) cameraPreview.texture = texture;
                if (orientationEffect == null)
                    orientationEffect = cameraPreview.GetComponent<CameraPreviewOrientationEffect>() ??
                        cameraPreview.gameObject.AddComponent<CameraPreviewOrientationEffect>();
                orientationEffect.SetOrientation(orientation);
                // Allow the new preview at least one rendered frame before running AI.
                // The first registration GPU submission may otherwise block the first paint.
                if (previewReadyFrame >= 0 && Time.frameCount > previewReadyFrame + 1)
                {
                    if (!firstDetectionObserved)
                    {
                        firstDetectionObserved = true;
                        TraceStartup("first-inference-request");
                    }
                    ProcessDetectionFrame(texture, orientation);
                }
            }
            else
            {
                darkSince = -1f;
            }
        }

        private void OnEnable()
        {
            StartPreview();
        }

        private void OnDisable()
        {
            StopPreview();
        }

        private void OnDestroy()
        {
            StopPreview();
        }

        private void OnApplicationQuit()
        {
            calibrationLogger.Dispose();
        }

        private void StartPreview()
        {
            RefreshRegistrationProgressUi();
            // SFace is only needed after Front/Left/Right best candidates are captured.
            // Do not construct a GPU Worker before requesting the first camera frame.
            if (faceRecognitionModel == null)
                throw new InvalidOperationException("SFace faceRecognitionModel이 연결되지 않았습니다.");

            if (cameraPreview == null)
            {
                Debug.LogError(
                    "[FaceRegistration Camera] CameraPreview가 연결되지 않았습니다.",
                    this);
                return;
            }

            if (imageSource == null)
            {
                imageSource = GetComponentInParent<FaceRegistrationImageSourceAdapter>(true);
            }

            if (imageSource == null)
            {
                Debug.LogError(
                    "[FaceRegistration Camera] 이미지 입력 Source를 찾지 못했습니다.",
                    this);
                return;
            }

            int currentRequest = ++requestVersion;
            cameraRequestStartedAt = Time.realtimeSinceStartupAsDouble;
            previewReadyFrame = -1;
            firstDetectionObserved = false;
            firstGateObserved = false;
            TraceStartup("camera-request");
            EnsureGuidanceAudioSource();
            PlayVoice(centerVoice);
            ownsCameraRequest = true;
            cameraPreview.color = Color.white;
            // Cloud enrollment never produces face calibration CSV artifacts.
            bool cloudEnrollment = GetComponentInParent<FaceRegistrationScreen>()?.IsCloudEnrollment == true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            calibrationSessionActive = collectCalibrationLogs && !cloudEnrollment;
#else
            calibrationSessionActive = false;
#endif
            lastCalibrationFrame = -1;
            if (calibrationSessionActive) calibrationLogger.BeginSession();

            imageSource.StartCamera(
                texture => ShowPreview(texture, currentRequest),
                message => HandleFailure(message, currentRequest));
        }

        private void ShowPreview(Texture texture, int currentRequest)
        {
            if (!isActiveAndEnabled ||
                currentRequest != requestVersion ||
                texture == null)
            {
                return;
            }

            cameraPreview.texture = texture;
            previewReadyFrame = Time.frameCount;
            TraceStartup("camera-ready-callback");
            Debug.Log("[FaceRegistration] Camera ready", this);
            // Do not call LateUpdate here: it may synchronously schedule/read back
            // cold GPU inference before Unity renders this very first preview.
        }

        private void HandleFailure(string message, int currentRequest)
        {
            if (!isActiveAndEnabled || currentRequest != requestVersion)
            {
                return;
            }

            ownsCameraRequest = false;
            StopGuidance();
            calibrationLogger.Dispose();
            calibrationSessionActive = false;
            DisposeDetection();
            sFaceRecognizer?.Dispose();
            sFaceRecognizer = null;
            if (orientationEffect != null) orientationEffect.ClearOrientation();
            cameraPreview.texture = null;
            Debug.LogError(
                $"[FaceRegistration Camera] {message}",
                this);
        }

        private void StopPreview()
        {
            StopGuidance();
            currentStep = FaceRegistrationStep.Front;
            validCandidateCount = 0;
            RegistrationFeedback = string.Empty;
            RegistrationFeedbackStep = FaceRegistrationStep.Front;
            lastAnnouncedStep = null;
            darkSince = -1f;
            darkWarningLatched = false;
            detectionSuccessLogged = false;
            snapshotSuccessLogged = false;
            // Candidates own independent aligned clones; release them when their session ends.
            frontCandidate?.FaceMat?.Dispose();
            leftCandidate?.FaceMat?.Dispose();
            rightCandidate?.FaceMat?.Dispose();
            frontAvatarMat?.Dispose();
            frontAvatarMat = null;
            frontCandidate = leftCandidate = rightCandidate = null;
            frontEmbedding = null;
            leftEmbedding = null;
            rightEmbedding = null;            
            requestVersion++;
            if (registrationInferenceRoutine != null)
            {
                StopCoroutine(registrationInferenceRoutine);
                registrationInferenceRoutine = null;
            }
            registrationInferencePending = false;
            if (embeddingFinalizationRoutine != null)
            {
                StopCoroutine(embeddingFinalizationRoutine);
                embeddingFinalizationRoutine = null;
            }
            embeddingFinalizationPending = false;
            previewReadyFrame = -1;
            firstDetectionObserved = false;
            firstGateObserved = false;
            calibrationLogger.Dispose();
            calibrationSessionActive = false;
            DisposeDetection();

            sFaceRecognizer?.Dispose();
            sFaceRecognizer = null;

            if (orientationEffect != null) orientationEffect.ClearOrientation();

            if (ownsCameraRequest)
            {
                imageSource?.StopCamera();
                ownsCameraRequest = false;
            }

            if (cameraPreview != null)
            {
                cameraPreview.texture = null;
            }
            RefreshRegistrationProgressUi();
        }

        private void ProcessDetectionFrame(Texture texture, CameraFrameOrientation orientation)
        {
            if (registrationInferencePending || embeddingFinalizationPending ||
                texture == null || faceDetectionModel == null) return;
            if (!calibrationSessionActive && Time.unscaledTime < nextDetectionTime) return;
            nextDetectionTime = Time.unscaledTime + DetectionInterval;
            try
            {
                if (detector == null)
                {
                    TraceStartup("yunet-worker-begin");
                    detector = new SentisFaceDetector(
                        faceDetectionModel, YuNetExecutionPolicy.Registration);
                    detectionController = new FaceDetectionController(detector, registrationCpuOnly: true);
#if UNITY_ANDROID && DEVELOPMENT_BUILD
                    Debug.Log("[S4-14] Registration YuNet " + detector.ExecutionPolicy, this);
#endif
                    TraceStartup("yunet-worker-end");
                }
                detector.EnableDetectionDiagnostics = enableDetectionDiagnostics;
                registrationInferencePending = true;
                registrationInferenceRoutine = StartCoroutine(ProcessDetectionAsync(
                    texture, orientation, requestVersion));
            }
            catch (Exception exception)
            {
                AbortRegistrationInference(exception);
            }
        }

        private IEnumerator ProcessDetectionAsync(Texture texture,
            CameraFrameOrientation orientation, int generation)
        {
            MultiFaceDetectionFrame asyncFrame = null;
            try
            {
                // Drive the nested iterator ourselves, so failures inside a GPU
                // schedule/readback also invalidate the Worker instead of leaving
                // a half-submitted Worker for the next detection.
                IEnumerator iterator = detectionController.ProcessMultiFrameAsync(
                    texture, orientation, frame => asyncFrame = frame,
                    rearFacing: imageSource != null &&
                        imageSource.CurrentFacing == CameraFacing.Rear);
                Exception detectionError = null;
                try
                {
                    while (true)
                    {
                        bool more = false;
                        object yielded = null;
                        try
                        {
                            more = iterator.MoveNext();
                            if (more) yielded = iterator.Current;
                        }
                        catch (Exception error) { detectionError = error; }
                        if (detectionError != null || !more) break;
                        yield return yielded;
                    }
                }
                finally { (iterator as IDisposable)?.Dispose(); }
                // The nested iterator must finish releasing PendingMultiDetection
                // BEFORE DisposeDetection can dispose its Worker.
                if (detectionError != null)
                {
                    if (generation == requestVersion && isActiveAndEnabled)
                        AbortRegistrationInference(detectionError);
                    yield break;
                }

                if (!isActiveAndEnabled || generation != requestVersion ||
                    currentStep == FaceRegistrationStep.Complete) yield break;
                // A camera/device orientation change invalidates the queued snapshot.
                if (!imageSource.TryGetPreviewFrame(out _, out CameraFrameOrientation latest) ||
                    latest.SourceWidth != orientation.SourceWidth ||
                    latest.SourceHeight != orientation.SourceHeight ||
                    latest.RotationAngle != orientation.RotationAngle ||
                    latest.VerticallyMirrored != orientation.VerticallyMirrored)
                    yield break;
                Exception processingError = null;
                try
                {
                    using (FaceDetectionFrame frame = CreateRegistrationFrame(asyncFrame))
                    {
                        if (!firstGateObserved)
                        {
                            firstGateObserved = true;
                            TraceStartup("first-registration-result");
                        }
                        ProcessDetectionResult(frame, orientation, DateTime.UtcNow,
                            asyncFrame.RawNearClipWarning,
                            asyncFrame.HasRawQualityFace,
                            asyncFrame.RawQualityFace);
                    }
                }
                catch (Exception error) { processingError = error; }
                if (processingError != null && generation == requestVersion && isActiveAndEnabled)
                    AbortRegistrationInference(processingError);
            }
            finally
            {
                asyncFrame?.Dispose();
                registrationInferencePending = false;
                registrationInferenceRoutine = null;
            }
        }

        private void AbortRegistrationInference(Exception exception)
        {
            // The preview remains usable while inference is disabled for this session.
            calibrationLogger.Dispose();
            calibrationSessionActive = false;
            DisposeDetection();
            sFaceRecognizer?.Dispose();
            sFaceRecognizer = null;
            nextDetectionTime = float.PositiveInfinity;
            StopGuidance();
            RegistrationFeedbackStep = currentStep;
            RegistrationFeedback = "얼굴 분석에 문제가 생겼어요. 화면을 다시 열어주세요";
            RefreshRegistrationProgressUi();
            Debug.LogException(exception, this);
        }

        private static FaceDetectionFrame CreateRegistrationFrame(MultiFaceDetectionFrame frame)
        {
            if (frame == null || frame.CanonicalMat == null || frame.Faces.Count == 0)
                return new FaceDetectionFrame(new FaceDetectionResult
                    { Found = false, FaceCount = 0, HasCurrentFrameBounds = false },
                    default, null);
            FaceDetectionItem face = frame.Faces[0];
            // Async multi detection already returns canonical pixels, not model pixels.
            // Reconstruct a legacy frame from that SAME snapshot; registration's
            // gate, crop, alignment, best-frame selector and S4-12 policy are unchanged.
            var metadata = FaceLetterboxPreprocessor.Calculate(
                frame.CanonicalWidth, frame.CanonicalHeight);
            // Keep the original model-to-canonical bounding rectangle UNCLAMPED
            // until after the existing ten-percent crop expansion.
            UnityEngine.Rect bounds = face.Bounds;
            UnityEngine.Rect expanded = FaceBoundingBoxMapper.ClampToImage(
                FaceBoundingBoxMapper.ExpandMargin(bounds, .10f),
                frame.CanonicalWidth, frame.CanonicalHeight);
            OpenCvSharp.Mat canonical = frame.CanonicalMat.Clone();
            OpenCvSharp.Mat crop = null;
            try
            {
                crop = FaceCropper.Crop(canonical, expanded, out OpenCvSharp.Rect roi);
                if (crop != null && crop.Empty())
                {
                    crop.Dispose();
                    crop = null;
                }
                var k = face.Landmarks;
                UnityEngine.Vector2 Encode(UnityEngine.Vector2 point)
                    => new UnityEngine.Vector2(point.x * metadata.Scale + metadata.PaddingX,
                        point.y * metadata.Scale + metadata.PaddingY);
                var landmarks = new FaceLandmarks5(Encode(k.RightEye), Encode(k.LeftEye),
                    Encode(k.NoseTip), Encode(k.RightMouthCorner), Encode(k.LeftMouthCorner));
                var modelBounds = new UnityEngine.Rect(
                    bounds.x * metadata.Scale + metadata.PaddingX,
                    bounds.y * metadata.Scale + metadata.PaddingY,
                    bounds.width * metadata.Scale, bounds.height * metadata.Scale);
                var detection = new FaceDetectionResult
                {
                    Found = true, FaceCount = frame.Faces.Count,
                    HasCurrentFrameBounds = true, Bounds = modelBounds,
                    Landmarks = landmarks, Confidencce = face.Confidence
                };
                var result = new FaceDetectionFrame(detection, metadata,
                    crop, canonical.Cols, canonical.Rows,
                    new Vector2Int(roi.X, roi.Y), canonical);
                canonical = null;
                crop = null;
                return result;
            }
            finally
            {
                crop?.Dispose();
                canonical?.Dispose();
            }
        }

        private void ProcessDetectionResult(FaceDetectionFrame detectionFrame,
            CameraFrameOrientation orientation, System.DateTime frameTimestamp,
            bool rawNearClipWarning, bool rawFaceMeasured,
            RearCameraExposureProcessor.Stats rawFaceStats)
        {
                if (detectionFrame.Detection.HasCurrentFrameBounds &&
                    detectionFrame.FaceMat != null && !detectionFrame.FaceMat.Empty())
                {
                    Rect canonicalFaceBounds = FaceBoundingBoxMapper.ModelToCanonical(
                        detectionFrame.Detection.Bounds, detectionFrame.Letterbox);
                    canonicalFaceBounds = FaceBoundingBoxMapper.ClampToImage(
                        canonicalFaceBounds, detectionFrame.CanonicalWidth, detectionFrame.CanonicalHeight);
                    bool gatePass = qualityGate.TryEvaluate(
                        detectionFrame.Detection.HasCurrentFrameBounds,
                        canonicalFaceBounds, detectionFrame.CanonicalHeight,
                        detectionFrame.CanonicalLandmarks, detectionFrame.FaceMat,
                        currentStep,
                        out FrameQualityMetrics metrics);
                    // Near-clipping is a diagnostic observation, NOT an additional
                    // registration rejection gate. Only FrameQualityGate decides
                    // whether this frame qualifies as a candidate.
                    UpdateRegistrationFeedback(orientation, metrics, gatePass,
                        rawFaceMeasured, rawFaceStats, rawNearClipWarning);
                    float? totalError = null;
                    bool bestUpdated = false;
                    // The gate can reject size/pose before brightness. Observe the same crop
                    // for voice feedback without changing candidate acceptance or ownership.
                    FaceBrightnessResult guidanceBrightness = metrics.HasBrightnessMetrics
                        ? metrics.Brightness
                        : guidanceBrightnessEvaluator.Evaluate(detectionFrame.FaceMat);
                    bool tooDark = guidanceBrightness.Mean < DarkWarningMinimumMean ||
                        guidanceBrightness.DarkPixelRatio > DarkWarningMaximumDarkRatio;
                    if (gatePass)
                    {
                        if (currentStep == FaceRegistrationStep.Front && !lastAnnouncedStep.HasValue)
                            PlayStepVoiceOnce(FaceRegistrationStep.Front);
                        if (currentStep == FaceRegistrationStep.Front)
                        {
                            validCandidateCount++;

                            totalError = bestFrameSelector.Evaluate(
                                metrics.FaceSizeRatio,
                                metrics.RollDegrees,
                                metrics.YawScore,
                                metrics.PitchScore,
                                0.0f);

                            bestUpdated = bestFrameSelector.TryUpdateBest(
                                totalError.Value,
                                detectionFrame.CanonicalMat,
                                detectionFrame.CanonicalLandmarks);

                            if (bestUpdated) CaptureFrontAvatar(detectionFrame.FaceMat);

                            if (validCandidateCount >= RequiredCandidateCount)
                            {
                                if (bestFrameSelector.TryGetBest(out FaceBestCandidate candidate))
                                {
                                    frontCandidate = candidate;
                                    Debug.Log($"[FaceRegistration] Accepted Front {validCandidateCount}/{RequiredCandidateCount}", this);

                                    bestFrameSelector.Reset();
                                    validCandidateCount = 0;
                                    EnterStep(FaceRegistrationStep.Left);
                                    ValidateAlignmentResult(frontCandidate.Value, "Front", "front_aligned.png");
                                }
                            }
                        }
                        else if (currentStep == FaceRegistrationStep.Left)
                        {
                            validCandidateCount++;

                            totalError = bestFrameSelector.Evaluate(
                                metrics.FaceSizeRatio,
                                metrics.RollDegrees,
                                metrics.YawScore,
                                metrics.PitchScore,
                                0.5f
                            );

                            bestUpdated = bestFrameSelector.TryUpdateBest(
                                totalError.Value,
                                detectionFrame.CanonicalMat,
                                detectionFrame.CanonicalLandmarks
                            );

                            if(validCandidateCount >= RequiredCandidateCount)
                            {
                                if(bestFrameSelector.TryGetBest(out FaceBestCandidate candidate))
                                {
                                    leftCandidate = candidate;
                                    Debug.Log($"[FaceRegistration] Accepted Left {validCandidateCount}/{RequiredCandidateCount}", this);

                                    bestFrameSelector.Reset();
                                    validCandidateCount = 0;
                                    EnterStep(FaceRegistrationStep.Right);
                                    ValidateAlignmentResult(leftCandidate.Value, "Left", "left_aligned.png");
                                }
                            }
                        }
                        else if (currentStep == FaceRegistrationStep.Right)
                        {
                            validCandidateCount++;

                            totalError = bestFrameSelector.Evaluate(
                                metrics.FaceSizeRatio,
                                metrics.RollDegrees,
                                metrics.YawScore,
                                metrics.PitchScore,
                                -0.5f
                            );

                            bestUpdated = bestFrameSelector.TryUpdateBest(
                                totalError.Value,
                                detectionFrame.CanonicalMat,
                                detectionFrame.CanonicalLandmarks
                            );

                            if(validCandidateCount >= RequiredCandidateCount)
                            {
                                if(bestFrameSelector.TryGetBest(out FaceBestCandidate candidate))
                                {
                                    if (rightCandidate.HasValue)
                                    {
                                        rightCandidate.Value.FaceMat?.Dispose();
                                    }

                                    rightCandidate = candidate;
                                    Debug.Log($"[FaceRegistration] Accepted Right {validCandidateCount}/{RequiredCandidateCount}", this);

                                    bestFrameSelector.Reset();
                                    validCandidateCount = 0;

                                    ValidateAlignmentResult(
                                        rightCandidate.Value,
                                        "Right",
                                        "right_aligned.png"
                                    );

                                    // GPU SFace embedding and readback must not block the
                                    // same render frame in which the final candidate arrives.
                                    if (!embeddingFinalizationPending)
                                    {
                                        embeddingFinalizationPending = true;
                                        embeddingFinalizationRoutine = StartCoroutine(
                                            FinalizeRegistrationAsync(requestVersion));
                                        RefreshRegistrationProgressUi();
                                    }
                                }
                            }
                        }
                        RefreshRegistrationProgressUi();
                    }
                    UpdateDarkWarning(tooDark);
                    // Rejected frames retain the gate's partial metrics; later stages remain default.
                    if (enableDetectionDiagnostics)
                    {
                        Debug.Log($"[Face Quality] " +
                            $"BlurScore={metrics.BlurScore.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"Mean={metrics.Brightness.Mean.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"DarkPixelRatio={metrics.Brightness.DarkPixelRatio.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"BrightPixelRatio={metrics.Brightness.BrightPixelRatio.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"FaceSizeRatio={metrics.FaceSizeRatio.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"RollDegrees={metrics.RollDegrees.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"YawScore={metrics.YawScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"PitchScore={metrics.PitchScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"GatePass={gatePass} " +
                            $"TotalError={(totalError.HasValue ? totalError.Value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) : "N/A")} " +
                            $"BestUpdated={bestUpdated}", this);
                    }
                    if (calibrationSessionActive)
                        calibrationLogger.Append(frameTimestamp, calibrationLabel, detectionFrame,
                            metrics, gatePass, totalError, bestUpdated, bestFrameSelector);
                }
                else if (calibrationSessionActive)
                {
                    UpdateRegistrationFeedback(orientation, default, false);
                    darkSince = -1f;
                    calibrationLogger.Append(frameTimestamp, calibrationLabel, detectionFrame,
                        default, false, null, false, bestFrameSelector);
                }
                else
                {
                    UpdateRegistrationFeedback(orientation, default, false);
                    darkSince = -1f;
                }
                if (enableDetectionDiagnostics && !detectionSuccessLogged && detectionController.LastFrameOrientation.HasValue &&
                    detector.LastLetterboxMetadata.HasValue)
                {
                    detectionSuccessLogged = true;
                    var letterbox = detector.LastLetterboxMetadata.Value;
                    Debug.Log($"[FaceRegistration Detection]\n" +
                        $"Raw={orientation.SourceWidth}x{orientation.SourceHeight}\n" +
                        $"Rotation={orientation.RotationAngle}\n" +
                        $"Mirrored={orientation.VerticallyMirrored}\n" +
                        $"Canonical={orientation.EffectiveWidth}x{orientation.EffectiveHeight}", this);
                    Debug.Log($"[FaceRegistration Letterbox]\n" +
                        $"Source={letterbox.SourceWidth}x{letterbox.SourceHeight}\n" +
                        $"Resized={letterbox.ResizedWidth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}x" +
                        $"{letterbox.ResizedHeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\n" +
                        $"Scale={letterbox.Scale.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}\n" +
                        $"PaddingX={letterbox.PaddingX.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\n" +
                        $"PaddingY={letterbox.PaddingY.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\n" +
                        $"Model={letterbox.ModelWidth}x{letterbox.ModelHeight}", this);
                }
                if (enableDetectionDiagnostics && !snapshotSuccessLogged && detectionFrame.Detection.HasCurrentFrameBounds &&
                    detectionFrame.HasFaceCrop)
                {
                    snapshotSuccessLogged = true;
                    Debug.Log($"[FaceRegistration Snapshot]\n" +
                        $"Canonical={detectionFrame.CanonicalWidth}x{detectionFrame.CanonicalHeight}\n" +
                        $"Mat={detectionFrame.MatWidth}x{detectionFrame.MatHeight}\n" +
                        $"Model={detectionFrame.Letterbox.ModelWidth}x{detectionFrame.Letterbox.ModelHeight}\n" +
                        $"CurrentDetection={detectionFrame.Detection.HasCurrentFrameBounds}\n" +
                        $"Crop={detectionFrame.CropWidth}x{detectionFrame.CropHeight}", this);
                }
        }

        public void RestartSession()
        {
            StopPreview();
            if (isActiveAndEnabled && UnityEngine.Application.isPlaying) StartPreview();
        }

        private void UpdateRegistrationFeedback(CameraFrameOrientation orientation,
            FrameQualityMetrics metrics, bool accepted, bool rawFaceMeasured = false,
            RearCameraExposureProcessor.Stats rawFaceStats = default,
            bool rawNearClipWarning = false)
        {
            // Accepted frames already refresh the 0/10 candidate counter below.
            // Rejected or missing faces must refresh as soon as the reason changes;
            // otherwise the screen keeps its previous (often wrong) instruction.
            FaceRegistrationStep previousFeedbackStep = RegistrationFeedbackStep;
            string previousFeedback = RegistrationFeedback;
            RegistrationFeedbackStep = currentStep;
            RegistrationFeedback = accepted ? string.Empty : FrameQualityGate.DescribeRejection(metrics, currentStep);
            // When YuNet cannot find a face because rear input is washed out,
            // show a useful explanation rather than endless generic alignment.
            if (!accepted && !metrics.HasFaceSizeMetrics &&
                imageSource != null && imageSource.CurrentFacing == CameraFacing.Rear &&
                detectionController != null && detectionController.IsRearExposureHigh)
                RegistrationFeedback = "후면 카메라 영상이 너무 밝아요. 조명 방향을 바꿔 주세요";
            if (previousFeedbackStep != RegistrationFeedbackStep ||
                !string.Equals(previousFeedback, RegistrationFeedback, StringComparison.Ordinal))
                RefreshRegistrationProgressUi();
            TraceRegistrationQuality(orientation, metrics, accepted, rawFaceMeasured,
                rawFaceStats, rawNearClipWarning);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void TraceRegistrationQuality(CameraFrameOrientation orientation,
            FrameQualityMetrics metrics, bool accepted, bool rawFaceMeasured,
            RearCameraExposureProcessor.Stats rawFaceStats, bool rawNearClipWarning)
        {
#if UNITY_ANDROID && DEVELOPMENT_BUILD && APPPOFO_PERF_DIAGNOSTICS
            if (imageSource != null && imageSource.CurrentFacing == CameraFacing.Rear &&
                Time.unscaledTime >= nextRearRoiDiagnosticTime)
            {
                nextRearRoiDiagnosticTime = Time.unscaledTime + 1f;
                Debug.Log("[S4-14] ExposureROI mode=Registration facing=Rear" +
                    " faceDetected=" + metrics.HasFaceSizeMetrics +
                    " brightnessMeasured=" + metrics.HasBrightnessMetrics +
                    " rawFaceMeasured=" + rawFaceMeasured +
                    " rawNearClipWarning=" + rawNearClipWarning +
                    (rawFaceMeasured
                        ? " rawFaceMean=" + rawFaceStats.Mean.ToString("F1",
                            System.Globalization.CultureInfo.InvariantCulture) +
                          " rawFaceBrightRatio=" + rawFaceStats.BrightRatio.ToString("F3",
                            System.Globalization.CultureInfo.InvariantCulture) +
                          " rawFaceClipRatio=" + rawFaceStats.ClipRatio.ToString("F3",
                            System.Globalization.CultureInfo.InvariantCulture)
                        : "") +
                    " roiMean=" + (metrics.HasBrightnessMetrics
                        ? metrics.Brightness.Mean.ToString("F1",
                            System.Globalization.CultureInfo.InvariantCulture) : "N/A") +
                    " roiBrightRatio=" + (metrics.HasBrightnessMetrics
                        ? metrics.Brightness.BrightPixelRatio.ToString("F3",
                            System.Globalization.CultureInfo.InvariantCulture) : "N/A") +
                    " rejection=" + (metrics.RejectionStage ?? "Detection") +
                    " frame=" + Time.frameCount, this);
            }
#endif
            if (!enableRegistrationDiagnostics || Time.unscaledTime < nextRegistrationDiagnosticTime) return;
            nextRegistrationDiagnosticTime = Time.unscaledTime + 1f;
            Debug.Log($"[FaceRegistration] Step={currentStep}, FaceSize={metrics.FaceSizeRatio:F4}, " +
                $"SizeMeasured={metrics.HasFaceSizeMetrics}, PoseMeasured={metrics.HasPoseMetrics}, " +
                $"Yaw={metrics.YawScore:F4}, Roll={metrics.RollDegrees:F2}, " +
                $"Pitch={metrics.PitchScore:F4}, Rejection={metrics.RejectionStage ?? "Detection"}, " +
                $"Brightness={metrics.Brightness.Mean:F2}, Blur={metrics.BlurScore:F2}, " +
                $"BrightnessMeasured={metrics.HasBrightnessMetrics}, BlurMeasured={metrics.HasBlurMetrics}, " +
                $"QualityPass={accepted}, RawNearClipWarning={rawNearClipWarning}, " +
                $"Candidates={validCandidateCount}/{RequiredCandidateCount}, " +
                $"Rotation={orientation.RotationAngle}, VerticalMirror={orientation.VerticallyMirrored}", this);
        }

        public void StopSession() => StopPreview();

        private void EnsureGuidanceAudioSource()
        {
            if (guidanceAudioSource == null)
            {
                guidanceAudioSource = GetComponent<AudioSource>();
            }

            // Unity missing/destroyed component references also compare equal to null.
            if (guidanceAudioSource == null)
            {
                guidanceAudioSource = gameObject.AddComponent<AudioSource>();
                guidanceAudioSource.spatialBlend = 0f;
                guidanceAudioSource.pitch = 1f;
            }

            guidanceAudioSource.playOnAwake = false;
            guidanceAudioSource.loop = false;
        }

        private void StopGuidance()
        {
            completionCloseTime = -1f;
            if (guidanceAudioSource != null) guidanceAudioSource.Stop();
        }

        private void PlayVoice(AudioClip clip)
        {
            if (guidanceAudioSource == null) return;
            guidanceAudioSource.Stop();
            guidanceAudioSource.clip = clip;
            if (clip != null) guidanceAudioSource.Play();
        }

        private void PlayStepVoiceOnce(FaceRegistrationStep step)
        {
            if (lastAnnouncedStep == step) return;
            lastAnnouncedStep = step;
            AudioClip clip = step switch
            {
                FaceRegistrationStep.Front => frontVoice,
                FaceRegistrationStep.Left => leftVoice,
                FaceRegistrationStep.Right => rightVoice,
                FaceRegistrationStep.Complete => completeVoice,
                _ => null
            };
            PlayVoice(clip);
        }

        private void EnterStep(FaceRegistrationStep step)
        {
            if (currentStep == step) return;
            currentStep = step;
            RefreshRegistrationProgressUi();
            darkSince = -1f;
            PlayStepVoiceOnce(step);
            if (step == FaceRegistrationStep.Complete)
            {
                completionCloseTime = Time.unscaledTime + Mathf.Max(0.3f,
                    completeVoice != null ? completeVoice.length : 0f);
            }
        }

        private void UpdateDarkWarning(bool tooDark)
        {
            if (currentStep == FaceRegistrationStep.Complete) return;
            if (!tooDark)
            {
                darkSince = -1f;
                darkWarningLatched = false;
                return;
            }
            if (darkSince < 0f) darkSince = Time.unscaledTime;
            if (darkWarningLatched || Time.unscaledTime - darkSince < DarkWarningDelaySeconds)
                return;
            // A warning cannot interrupt Center or any step announcement.
            if (guidanceAudioSource != null && guidanceAudioSource.isPlaying) return;
            darkWarningLatched = true;
            PlayVoice(tooDarkVoice);
        }

        private void ValidateAlignmentResult(FaceBestCandidate candidate, string direction, string fileName)
        {
            // In cloud mode, never write aligned biometric face crops to disk,
            // including when developer diagnostic flags are enabled.
            if (GetComponentInParent<FaceRegistrationScreen>()?.IsCloudEnrollment == true)
                return;
            // Run once after each direction's candidate is committed and the step advances.
            // Borrow candidate.FaceMat; its existing owner retains responsibility for disposal.
            try
            {
                if (candidate.FaceMat == null || candidate.FaceMat.Empty())
                {
                    Debug.LogWarning($"[FaceRegistration Alignment] {direction} FaceMat is missing or empty.", this);
                    return;
                }

                int cols = candidate.FaceMat.Cols;
                int rows = candidate.FaceMat.Rows;
                // The candidate is already aligned; validate and save its borrowed Mat directly.
                bool expectedSize = cols == 112 && rows == 112;
                string result = $"[FaceRegistration Alignment] {direction} result: " +
                    $"Cols={cols}, Rows={rows}, Expected=112x112, SizeValid={expectedSize}";
                if (expectedSize && enableDetectionDiagnostics)
                    Debug.Log(result, this);
                else if (!expectedSize)
                    Debug.LogWarning(result, this);

                try
                {
                    string directory = System.IO.Path.Combine(
                        UnityEngine.Application.persistentDataPath, "FaceRegistrationDebug");
                    string path = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(directory, fileName));
                    System.IO.Directory.CreateDirectory(directory);
                    if (!OpenCvSharp.Cv2.ImWrite(path, candidate.FaceMat))
                        throw new System.IO.IOException($"Cv2.ImWrite returned false: {path}");

                    if (enableDetectionDiagnostics)
                    {
                            Debug.Log($"[FaceRegistration Alignment] {direction}\nSaved aligned face:\n{path}", this);
                    }
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"[FaceRegistration Alignment] {direction} failed to save aligned face PNG: {exception}", this);
                }
            }
            catch (System.Exception exception)
            {
                // Diagnostic failure must not stop detection or change the registration steps.
                Debug.LogError($"[FaceRegistration Alignment] {direction} validation failed: {exception}", this);
            }
        }

        private void DisposeDetection()
        {
            bestFrameSelector.Reset();
            detectionController?.CancelAsync();
            detectionController = null;
            detector?.Dispose();
            detector = null;
            nextDetectionTime = 0f;
        }

        private IEnumerator FinalizeRegistrationAsync(int generation)
        {
            IEnumerator worker = ComputeRegistrationEmbeddingsAsync(generation);
            try
            {
                while (true)
                {
                    bool more = false;
                    Exception error = null;
                    object yielded = null;
                    try
                    {
                        more = worker.MoveNext();
                        if (more) yielded = worker.Current;
                    }
                    catch (Exception exception) { error = exception; }
                    if (error != null)
                    {
                        // A partially submitted SFace schedule cannot be reused.
                        sFaceRecognizer?.Dispose();
                        sFaceRecognizer = null;
                        if (generation == requestVersion && isActiveAndEnabled)
                        {
                            RegistrationFeedbackStep = currentStep;
                            RegistrationFeedback = "등록 마무리에 문제가 생겼어요. 다시 시도해 주세요";
                            RefreshRegistrationProgressUi();
                        }
                        Debug.LogException(error, this);
                        yield break;
                    }
                    if (!more) break;
                    yield return yielded;
                }
            }
            finally
            {
                (worker as IDisposable)?.Dispose();
                sFaceRecognizer?.CancelAsync();
                embeddingFinalizationPending = false;
                embeddingFinalizationRoutine = null;
                if (generation == requestVersion && isActiveAndEnabled)
                    RefreshRegistrationProgressUi();
            }
        }

        private IEnumerator ComputeRegistrationEmbeddingsAsync(int generation)
        {
                if (faceRecognitionModel == null)
                    throw new InvalidOperationException("SFace 모델이 연결되지 않았습니다.");
                if (!frontCandidate.HasValue || !leftCandidate.HasValue || !rightCandidate.HasValue)
                    throw new InvalidOperationException("등록 후보 세 단계가 아직 준비되지 않았습니다.");
                // Let the progress indicator show completion work before any SFace model load.
                yield return null;
                if (generation != requestVersion || !isActiveAndEnabled) yield break;
                if (sFaceRecognizer == null)
                {
                    sFaceRecognizer = new SFaceRecognizer(
                        faceRecognitionModel, Unity.InferenceEngine.BackendType.CPU);
#if UNITY_ANDROID && DEVELOPMENT_BUILD
                    Debug.Log("[S4-14] Registration SFace backend=CPU layers=4", this);
#endif
                }

                var faces = new[]
                {
                    frontCandidate.Value.FaceMat,
                    leftCandidate.Value.FaceMat,
                    rightCandidate.Value.FaceMat
                };
                var embeddings = new float[3][];
                for (int index = 0; index < faces.Length; index++)
                {
                    if (generation != requestVersion || !isActiveAndEnabled) yield break;
                    // Four model layers per frame (as in the existing inference API).
                    sFaceRecognizer.BeginAsync(faces[index], 4);
                    double scheduleAt = Time.realtimeSinceStartupAsDouble;
                    while (sFaceRecognizer.IsScheduling)
                    {
                        sFaceRecognizer.AdvanceAsyncSchedule();
                        if (Time.realtimeSinceStartupAsDouble - scheduleAt > 15d)
                            throw new TimeoutException("Registration SFace schedule exceeded 15 seconds.");
                        yield return null;
                    }
                    double waitAt = Time.realtimeSinceStartupAsDouble;
                    while (!sFaceRecognizer.IsAsyncComplete)
                    {
                        if (Time.realtimeSinceStartupAsDouble - waitAt > 15d)
                            throw new TimeoutException("Registration SFace readback exceeded 15 seconds.");
                        yield return null;
                    }
                    if (generation != requestVersion || !isActiveAndEnabled) yield break;
                    embeddings[index] = sFaceRecognizer.CompleteAsync();
                }
                foreach (float[] embedding in embeddings) ValidateEmbedding(embedding, "등록");
                frontEmbedding = embeddings[0];
                leftEmbedding = embeddings[1];
                rightEmbedding = embeddings[2];
                Debug.Log("[FaceRegistration] Front/Left/Right 임베딩 생성 완료.", this);
                if (generation != requestVersion || !isActiveAndEnabled) yield break;
                if (enrollmentWriter == null)
                    throw new InvalidOperationException("얼굴 저장 대상이 설정되지 않았습니다.");
                var set = new FaceEmbeddingSet(frontEmbedding, leftEmbedding,
                    rightEmbedding, SFaceRecognizer.ModelVersion);
                FaceEnrollmentSaveResult result = null;
                yield return enrollmentWriter.Save(set, completed => result = completed);
                if (generation != requestVersion || !isActiveAndEnabled) yield break;
                if (result != null && result.Status == FaceEnrollmentSaveStatus.Success)
                {
                    FaceRegistrationScreen screen = GetComponentInParent<FaceRegistrationScreen>();
                    if (screen != null && !screen.IsCloudEnrollment && screen.CurrentProfileId > 0)
                        TrySaveDefaultFrontAvatar(screen.CurrentProfileId);
                    if (screen != null && screen.IsCloudEnrollment)
                    {
                        string photoError = null;
                        byte[] photo = null;
                        try { if (frontAvatarMat != null && !frontAvatarMat.Empty()) photo = AppPofo.Presentation.Common.CloudProfileAvatar.Thumbnail(frontAvatarMat.ImEncode(".png")); }
                        catch (Exception) { }
                        if (photo != null)
                            yield return AppPofo.Presentation.Common.CloudProfileAvatar.Save(screen.CurrentCloudProfileId, photo, true, e => photoError = e);
                        if (generation != requestVersion || !isActiveAndEnabled) yield break;
                        if (photo == null || photoError != null)
                        {
                            RegistrationFeedbackStep = FaceRegistrationStep.Complete;
                            RegistrationFeedback = photoError ?? "얼굴 등록은 완료됐습니다. 프로필 사진은 명함에서 지정해 주세요.";
                            Debug.LogWarning("[FaceRegistration] 얼굴 등록 완료, 기본 프로필 사진 저장 미완료.", this);
                        }
                    }
                    EnterStep(FaceRegistrationStep.Complete);
                }
                else
                {
                    RegistrationFeedbackStep = currentStep;
                    RegistrationFeedback = result == null || string.IsNullOrWhiteSpace(result.Message)
                        ? "얼굴정보를 저장하지 못했습니다. 다시 시도해 주세요."
                        : result.Message;
                    RefreshRegistrationProgressUi();
                    Debug.LogWarning("[FaceRegistration] 저장이 완료되지 않았습니다: " +
                        (result == null ? "No callback" : result.Status.ToString()), this);
                }
        }

        private bool TryCreateEmbeddings()
        {
            if (!frontCandidate.HasValue ||
                !leftCandidate.HasValue ||
                !rightCandidate.HasValue)
            {
                Debug.LogError(
                    "[FaceRegistration] 임베딩 생성에 필요한 3개 후보가 준비되지 않았습니다.",
                    this
                );

                return false;
            }

            if (sFaceRecognizer == null)
            {
                if (faceRecognitionModel == null)
                {
                    Debug.LogError("[FaceRegistration] SFace 모델이 연결되지 않았습니다.", this);
                    return false;
                }
                sFaceRecognizer = new SFaceRecognizer(
                    faceRecognitionModel, Unity.InferenceEngine.BackendType.CPU);
            }

            try
            {
                float[] newFrontEmbedding =
                    sFaceRecognizer.ExtractEmbedding(
                        frontCandidate.Value.FaceMat
                    );

                float[] newLeftEmbedding =
                    sFaceRecognizer.ExtractEmbedding(
                        leftCandidate.Value.FaceMat
                    );

                float[] newRightEmbedding =
                    sFaceRecognizer.ExtractEmbedding(
                        rightCandidate.Value.FaceMat
                    );
                if (enableDetectionDiagnostics)
                {
                    Debug.Log(
                        $"[FaceRegistration Embedding] " +
                        $"Front={newFrontEmbedding.Length}, Norm={CalculateEmbeddingNorm(newFrontEmbedding):F6} | " +
                        $"Left={newLeftEmbedding.Length}, Norm={CalculateEmbeddingNorm(newLeftEmbedding):F6} | " +
                        $"Right={newRightEmbedding.Length}, Norm={CalculateEmbeddingNorm(newRightEmbedding):F6}",
                        this
                    );
                }

                ValidateEmbedding(newFrontEmbedding, "Front");
                ValidateEmbedding(newLeftEmbedding, "Left");
                ValidateEmbedding(newRightEmbedding, "Right");

                frontEmbedding = newFrontEmbedding;
                leftEmbedding = newLeftEmbedding;
                rightEmbedding = newRightEmbedding;
                
                Debug.Log(
                    "[FaceRegistration] Front/Left/Right 임베딩 생성 완료.",
                    this
                );

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[FaceRegistration] 임베딩 생성 실패: {exception}",
                    this
                );

                return false;
            }
        }

        private float CalculateEmbeddingNorm(float[] embedding)
        {
            float sumSquares = 0f;

            for(int i = 0; i <embedding.Length; i++)
            {
                sumSquares += embedding[i] * embedding[i];
            }

            return Mathf.Sqrt(sumSquares);
        }

        private void ValidateEmbedding(float[] embedding, string direction)
        {
            if(embedding == null)
            {
                throw new InvalidOperationException(
                    $"{direction} 임베딩이 null입니다."
                );                
            }

            if(embedding.Length != SFaceRecognizer.EmbeddingSize)
            {
                throw new InvalidOperationException(
                    $"{direction} 임베딩 길이가 잘못되었습니다. " +
                    $"현재: {embedding.Length}"
                );                
            }

            float norm = CalculateEmbeddingNorm(embedding);
            const float tolerance = 0.001f;

            if (float.IsNaN(norm) ||
                float.IsInfinity(norm) ||
                Mathf.Abs(norm - 1f) > tolerance)
            {
                throw new InvalidOperationException(
                    $"{direction} 임베딩 L2 Norm이 비정상입니다. Norm={norm:F6}"
                );
            }       
        }
        
        private void CaptureFrontAvatar(OpenCvSharp.Mat faceMat)
        {
            // Retain the original crop before the detection frame is disposed, not the aligned 112x112 input.
            frontAvatarMat?.Dispose();
            frontAvatarMat = null;
            try
            {
                if (faceMat == null || faceMat.Empty())
                    throw new InvalidOperationException("Front 사진의 얼굴 영역이 비어 있습니다.");
                frontAvatarMat = faceMat.Clone();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[FaceRegistration] Front 프로필 사진 캡처 실패: {exception.Message}", this);
            }
        }

        private bool TrySaveDefaultFrontAvatar(long profileId)
        {
            try
            {
                var profile = bootstrap.GetProfileUseCase.Execute(profileId);
                if (profile == null) throw new InvalidOperationException("프로필을 찾을 수 없습니다.");
                // Preserve an existing user photo on re-registration.
                if (!string.IsNullOrWhiteSpace(profile.AvatarPath)) return true;
                if (frontAvatarMat == null || frontAvatarMat.Empty())
                    throw new InvalidOperationException("저장할 Front 프로필 사진이 없습니다.");
                byte[] imageBytes = frontAvatarMat.ImEncode(".png");
                bootstrap.UpdateAvatarUseCase.Execute(profileId, imageBytes, DateTimeOffset.UtcNow);
                return true;
            }
            catch (Exception exception)
            {
                // Embeddings are already saved: an optional photo failure must not repeat registration.
                Debug.LogWarning($"[FaceRegistration] 기본 프로필 사진 저장 실패. 프로필에서 사진을 변경해 주세요: {exception.Message}", this);
                return false;
            }
        }


    }
}
