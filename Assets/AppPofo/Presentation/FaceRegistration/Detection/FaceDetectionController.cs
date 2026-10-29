using AppPofo.Presentation.Diagnostics;
using System;
using System.Collections;
using UnityEngine;
using AppPofo.Presentation.Avatar;
using Mat = OpenCvSharp.Mat;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    public sealed class FaceDetectionController
    {
        private readonly IFaceDetector detector;
        private SentisFaceDetector.PendingMultiDetection activePending;
        private Texture2D activeCanonicalTexture;
        public bool IsScheduling => activePending != null && activePending.IsScheduling;
        // First async inference in each camera-view session only.
        private bool firstYuNetTraceStarted;
        private double firstYuNetTraceAt;
        private void TraceFirstYuNet(string stage, int completedOutputs = -1)
        {
#if APPPOFO_PERF_DIAGNOSTICS && (UNITY_EDITOR || DEVELOPMENT_BUILD)
            if (!firstYuNetTraceStarted) return;
            double elapsed = (Time.realtimeSinceStartupAsDouble - firstYuNetTraceAt) * 1000d;
            Debug.Log("[PERF4] First YuNet stage=" + stage +
                " elapsedMs=" + elapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                " frame=" + Time.frameCount +
                (completedOutputs < 0 ? "" : " readbacks=" + completedOutputs + "/12"));
#endif
        }

        /// <summary>Called after stopping the recognition coroutine, before disposing its Worker.</summary>
        public void CancelAsync()
        {
            previousRearFaceRoi = null;
            previousRoiTime = -1f;
            previousRoiOrientation = null;
            rearExposureState.Reset();
            PreviewExposureGain = 1f;
            var pending = activePending;
            activePending = null;
            pending?.Dispose();
            var texture = activeCanonicalTexture;
            activeCanonicalTexture = null;
            if (texture != null)
            {
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        public CameraFrameOrientation? LastFrameOrientation { get; private set; }

        private readonly bool registrationCpuOnly;
        private float nextExposureTraceAt;
        private float lastRearExposureAt = -1f;
        private bool rearExposureHigh;
        private readonly RearCameraExposureProcessor.AdaptiveState rearExposureState =
            new RearCameraExposureProcessor.AdaptiveState();
        private RectInt? previousRearFaceRoi;
        private int previousRoiWidth, previousRoiHeight;
        private float previousRoiTime = -1f;
        private CameraFrameOrientation? previousRoiOrientation;
        public float PreviewExposureGain { get; private set; } = 1f;
        public bool IsRearExposureHigh =>
            lastRearExposureAt >= 0f &&
            Time.unscaledTime - lastRearExposureAt <= 3f && rearExposureHigh;

        public FaceDetectionController(IFaceDetector detector, bool registrationCpuOnly = false)
        {
            if (registrationCpuOnly &&
                (!(detector is SentisFaceDetector yunet) ||
                 !yunet.ExecutionPolicy.CpuInput || !yunet.ExecutionPolicy.CpuBackend))
                throw new ArgumentException("Registration CPU-only flow requires CPU YuNet.");
            this.detector = detector;
            this.registrationCpuOnly = registrationCpuOnly;
        }

        public FaceDetectionResult ProcessFrame(Texture frame)
        {
            LastFrameOrientation = null;
            return DetectFrame(frame);
        }

        public FaceDetectionResult ProcessFrame(Texture frame, CameraFrameOrientation orientation)
        {
            using var snapshot = ProcessFrameWithCrop(frame, orientation);
            return snapshot.Detection;
        }

        public FaceDetectionFrame ProcessFrameWithCrop(Texture frame, CameraFrameOrientation orientation)
        {
            LastFrameOrientation = null;
            if (frame == null) return new FaceDetectionFrame(DetectFrame(null), default, null);
            if (frame.width != orientation.SourceWidth || frame.height != orientation.SourceHeight)
                return new FaceDetectionFrame(DetectFrame(null), default, null);

            Color32[] rawPixels;
            if (frame is WebCamTexture camera)
            {
                using (PerformanceDiagnostics.Measure(PerfMetric.GetPixels32Ms))
                    rawPixels = camera.GetPixels32();
                // A camera can change its dimensions or rotation while pixels are copied.
                if (!CameraFrameOrientation.TryRead(camera, out var afterRead) ||
                    !orientation.Equals(afterRead))
                    return new FaceDetectionFrame(DetectFrame(null), default, null);
            }
            else if (frame is Texture2D image)
            {
                using (PerformanceDiagnostics.Measure(PerfMetric.GetPixels32Ms))
                    rawPixels = image.GetPixels32();
            }
            else
            {
                throw new NotSupportedException($"Camera texture type {frame.GetType().Name} is not supported for detection.");
            }

            if (rawPixels == null || rawPixels.Length != orientation.SourceWidth * orientation.SourceHeight)
                return new FaceDetectionFrame(DetectFrame(null), default, null);

            Color32[] canonicalPixels;
            using (PerformanceDiagnostics.Measure(PerfMetric.OrientationMs))
                canonicalPixels = orientation.UprightPixels(rawPixels);
            int canonicalWidth = orientation.EffectiveWidth;
            int canonicalHeight = orientation.EffectiveHeight;
            Texture2D canonicalTexture = null;
            FaceDetectionFrame result = null;
            try
            {
                // Both consumers use this frame's one canonicalPixels snapshot, including identity cameras.
                using (PerformanceDiagnostics.Measure(PerfMetric.TextureCreateMs))
                    canonicalTexture = new Texture2D(canonicalWidth, canonicalHeight, TextureFormat.RGBA32, false)
                    { wrapMode = TextureWrapMode.Clamp };
                using (PerformanceDiagnostics.Measure(PerfMetric.TextureUploadMs))
                {
                    canonicalTexture.SetPixels32(canonicalPixels);
                    canonicalTexture.Apply(false, false);
                }

                LastFrameOrientation = orientation;
                FaceDetectionResult detection = DetectFrame(canonicalTexture);
                FaceLetterboxMetadata letterbox = FaceLetterboxPreprocessor.Calculate(
                    canonicalWidth, canonicalHeight);
                if (!detection.HasCurrentFrameBounds)
                    return new FaceDetectionFrame(detection, letterbox, null);

                Mat canonicalMat = null;
                Mat faceMat = null;
                try
                {
                    using (PerformanceDiagnostics.Measure(PerfMetric.PixelsToMatMs))
                        canonicalMat = OpenCvSharp.Unity.PixelsToMat(
                            canonicalPixels, canonicalWidth, canonicalHeight);
                    Rect bounds = FaceBoundingBoxMapper.ModelToCanonical(detection.Bounds, letterbox);
                    bounds = FaceBoundingBoxMapper.ExpandMargin(bounds, 0.10f);
                    bounds = FaceBoundingBoxMapper.ClampToImage(bounds, canonicalWidth, canonicalHeight);
                    faceMat = FaceCropper.Crop(canonicalMat, bounds, out var roi);
                    if (faceMat.Empty())
                    {
                        faceMat.Dispose();
                        faceMat = null;
                    }
                    result = new FaceDetectionFrame(detection, letterbox, faceMat,
                        canonicalMat.Cols, canonicalMat.Rows, new Vector2Int(roi.X, roi.Y), canonicalMat);
                    // The result now owns both Mats; only untransferred Mats are cleaned up below.
                    faceMat = null;
                    canonicalMat = null;
                    return result;
                }
                finally
                {
                    try
                    {
                        faceMat?.Dispose();
                    }
                    finally
                    {
                        canonicalMat?.Dispose();
                    }
                }
            }
            finally
            {
                try
                {
                    if (canonicalTexture != null)
                    {
                        if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(canonicalTexture);
                        else UnityEngine.Object.DestroyImmediate(canonicalTexture);
                    }
                }
                catch
                {
                    // If cleanup prevents the return, the caller never receives ownership.
                    result?.Dispose();
                    throw;
                }
            }
        }

        /// <summary>Detects every current-frame face from one canonical snapshot.
        /// The caller owns the returned frame; no face crops or full-frame clones are created.
        /// Invalid/not-ready input returns an empty frame with no Mat.</summary>
        public MultiFaceDetectionFrame ProcessMultiFrame(Texture frame, CameraFrameOrientation orientation)
        {
            LastFrameOrientation = null;
            if (!(detector is IMultiFaceDetector multiDetector))
                throw new NotSupportedException("The configured detector does not support multi-face detection.");

            if (frame == null || frame.width != orientation.SourceWidth ||
                frame.height != orientation.SourceHeight)
                return new MultiFaceDetectionFrame(null, 0, 0, Array.Empty<FaceDetectionItem>());

            Color32[] rawPixels;
            if (frame is WebCamTexture camera)
            {
                using (PerformanceDiagnostics.Measure(PerfMetric.GetPixels32Ms))
                    rawPixels = camera.GetPixels32();
                if (!CameraFrameOrientation.TryRead(camera, out var afterRead) ||
                    !orientation.Equals(afterRead))
                    return new MultiFaceDetectionFrame(null, 0, 0, Array.Empty<FaceDetectionItem>());
            }
            else if (frame is Texture2D image)
            {
                using (PerformanceDiagnostics.Measure(PerfMetric.GetPixels32Ms))
                    rawPixels = image.GetPixels32();
            }
            else
            {
                throw new NotSupportedException($"Camera texture type {frame.GetType().Name} is not supported for detection.");
            }

            if (rawPixels == null || rawPixels.Length != orientation.SourceWidth * orientation.SourceHeight)
                return new MultiFaceDetectionFrame(null, 0, 0, Array.Empty<FaceDetectionItem>());

            Color32[] canonicalPixels;
            using (PerformanceDiagnostics.Measure(PerfMetric.OrientationMs))
                canonicalPixels = orientation.UprightPixels(rawPixels);
            int width = orientation.EffectiveWidth;
            int height = orientation.EffectiveHeight;
            Texture2D canonicalTexture = null;
            Mat canonicalMat = null;
            MultiFaceDetectionFrame result = null;
            try
            {
                using (PerformanceDiagnostics.Measure(PerfMetric.TextureCreateMs))
                    canonicalTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                    { wrapMode = TextureWrapMode.Clamp };
                using (PerformanceDiagnostics.Measure(PerfMetric.TextureUploadMs))
                {
                    canonicalTexture.SetPixels32(canonicalPixels);
                    canonicalTexture.Apply(false, false);
                }
                LastFrameOrientation = orientation;

                MultiFaceDetectionResult detection = multiDetector.DetectMulti(canonicalTexture);
                FaceLetterboxMetadata letterbox = detection.Letterbox;
                if (letterbox.SourceWidth != width || letterbox.SourceHeight != height)
                    throw new InvalidOperationException("Detection metadata does not match the canonical snapshot.");

                // Build the full Mat once, including valid frames with zero detected faces.
                using (PerformanceDiagnostics.Measure(PerfMetric.PixelsToMatMs))
                    canonicalMat = OpenCvSharp.Unity.PixelsToMat(canonicalPixels, width, height);
                var faces = new FaceDetectionItem[detection.Faces.Count];
                for (int i = 0; i < faces.Length; i++)
                {
                    FaceDetectionItem face = detection.Faces[i];
                    faces[i] = new FaceDetectionItem(
                        FaceBoundingBoxMapper.ModelToCanonical(face.Bounds, letterbox),
                        FaceBoundingBoxMapper.ModelToCanonical(face.Landmarks, letterbox),
                        face.Confidence);
                }

                result = new MultiFaceDetectionFrame(canonicalMat, width, height, faces);
                canonicalMat = null; // Ownership transferred only after successful construction.
                return result;
            }
            finally
            {
                try
                {
                    canonicalMat?.Dispose();
                    if (canonicalTexture != null)
                    {
                        if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(canonicalTexture);
                        else UnityEngine.Object.DestroyImmediate(canonicalTexture);
                    }
                }
                catch
                {
                    // A cleanup failure prevents delivery of the returned frame.
                    result?.Dispose();
                    throw;
                }
            }
        }


        /// <summary>Recognition-only coroutine. Retains its one canonical snapshot until
        /// all 12 YuNet outputs have completed an asynchronous readback.</summary>
        public IEnumerator ProcessMultiFrameAsync(Texture frame, CameraFrameOrientation orientation,
            Action<MultiFaceDetectionFrame> onComplete, bool rearFacing = false)
        {
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            LastFrameOrientation = null;
            if (!(detector is SentisFaceDetector yunet))
                throw new NotSupportedException("Async recognition requires SentisFaceDetector.");
            if (frame == null || frame.width != orientation.SourceWidth ||
                frame.height != orientation.SourceHeight)
            {
                onComplete(new MultiFaceDetectionFrame(null, 0, 0, Array.Empty<FaceDetectionItem>()));
                yield break;
            }

            bool traceFirst = !firstYuNetTraceStarted;
            if (traceFirst)
            {
                firstYuNetTraceStarted = true;
                firstYuNetTraceAt = Time.realtimeSinceStartupAsDouble;
                TraceFirstYuNet("frame-copy-begin");
            }

            Color32[] rawPixels;
            if (frame is WebCamTexture camera)
            {
                using (PerformanceDiagnostics.Measure(PerfMetric.GetPixels32Ms))
                    rawPixels = camera.GetPixels32();
                if (traceFirst) TraceFirstYuNet("frame-copy-complete");
                if (!CameraFrameOrientation.TryRead(camera, out var afterRead) ||
                    !orientation.Equals(afterRead))
                {
                    if (traceFirst)
                    {
                        TraceFirstYuNet("discarded-orientation-change");
                        firstYuNetTraceStarted = false; // Trace next valid first input.
                    }
                    onComplete(new MultiFaceDetectionFrame(null, 0, 0, Array.Empty<FaceDetectionItem>()));
                    yield break;
                }
            }
            else if (frame is Texture2D image)
            {
                using (PerformanceDiagnostics.Measure(PerfMetric.GetPixels32Ms))
                    rawPixels = image.GetPixels32();
                if (traceFirst) TraceFirstYuNet("frame-copy-complete");
            }
            else throw new NotSupportedException("Unsupported recognition camera texture.");

            if (rawPixels == null || rawPixels.Length != orientation.SourceWidth * orientation.SourceHeight)
            {
                if (traceFirst)
                {
                    TraceFirstYuNet("discarded-invalid-frame");
                    firstYuNetTraceStarted = false;
                }
                onComplete(new MultiFaceDetectionFrame(null, 0, 0, Array.Empty<FaceDetectionItem>()));
                yield break;
            }

            Color32[] canonicalPixels;
            using (PerformanceDiagnostics.Measure(PerfMetric.OrientationMs))
                canonicalPixels = orientation.UprightPixels(rawPixels);
            if (traceFirst) TraceFirstYuNet("orientation-complete");
            int width = orientation.EffectiveWidth;
            int height = orientation.EffectiveHeight;
            float capturedAt = Time.unscaledTime;
            Color32[] originalCanonicalPixels = canonicalPixels;
            bool correctionApplied = false;

            // The preview remains the untouched camera stream. For rear cameras,
            // measure true source photometry before any optional A/B correction.
            if (!rearFacing || (previousRoiOrientation.HasValue &&
                !previousRoiOrientation.Value.Equals(orientation)))
            {
                // A rotated/mirrored frame must not reuse the prior ROI or gain.
                if (rearFacing) rearExposureState.Reset();
                previousRoiOrientation = null;
                previousRearFaceRoi = null;
                previousRoiTime = -1f;
            }
            if (!rearFacing)
            {
                rearExposureHigh = false;
                lastRearExposureAt = -1f;
                PreviewExposureGain = 1f;
                rearExposureState.Reset();
                previousRearFaceRoi = null;
                previousRoiTime = -1f;
            }
            if (rearFacing)
            {
                bool traceExposure = Time.unscaledTime >= nextExposureTraceAt;
                bool correction = RearCameraExposureProcessor.ExperimentalCorrectionEnabled;
                if (traceExposure || correction)
                {
                    RectInt? faceRoi = previousRearFaceRoi.HasValue &&
                        previousRoiWidth == width && previousRoiHeight == height &&
                        previousRoiTime >= 0f &&
                        capturedAt - previousRoiTime <= .75f
                        ? previousRearFaceRoi : null;
                    var exposure = RearCameraExposureProcessor.Process(
                        canonicalPixels, width, height, true, correction,
                        rearExposureState, faceRoi);
                    correctionApplied = exposure.Corrected;
                    if (correctionApplied) canonicalPixels = exposure.Pixels;
                    PreviewExposureGain = exposure.AppliedGain;
                    rearExposureHigh = exposure.Raw.ClipRatio > .06f ||
                        (exposure.Raw.Mean > 185f &&
                         exposure.Raw.BrightRatio > .30f);
                    lastRearExposureAt = Time.unscaledTime;
                    if (traceExposure)
                    {
                        nextExposureTraceAt = Time.unscaledTime + 1f;
#if UNITY_ANDROID && DEVELOPMENT_BUILD
                        Debug.Log("[S4-14] Exposure mode=" +
                            (registrationCpuOnly ? "Registration" : "Recognition") +
                            " facing=Rear rawMean=" + exposure.Raw.Mean.ToString("F1",
                                System.Globalization.CultureInfo.InvariantCulture) +
                            " rawP95=" + exposure.Raw.P95 +
                            " rawBrightRatio=" + exposure.Raw.BrightRatio.ToString("F3",
                                System.Globalization.CultureInfo.InvariantCulture) +
                            " rawClipRatio=" + exposure.Raw.ClipRatio.ToString("F3",
                                System.Globalization.CultureInfo.InvariantCulture) +
                            " roi=" + (exposure.UsedFaceRoi ? "PreviousFace" : "Center") +
                            " observedMean=" + exposure.Observed.Mean.ToString("F1",
                                System.Globalization.CultureInfo.InvariantCulture) +
                            " observedP80=" + exposure.Observed.P80 +
                            " observedBrightRatio=" + exposure.Observed.BrightRatio.ToString("F3",
                                System.Globalization.CultureInfo.InvariantCulture) +
                            " observedClipRatio=" + exposure.Observed.ClipRatio.ToString("F3",
                                System.Globalization.CultureInfo.InvariantCulture) +
                            " reason=" + exposure.Reason +
                            " corrected=" + exposure.Corrected +
                            " gain=" + exposure.AppliedGain.ToString("F3",
                                System.Globalization.CultureInfo.InvariantCulture) +
                            " frame=" + Time.frameCount);
#endif
                    }
                }
            }
            Texture2D canonicalTexture = null;
            Mat canonicalMat = null;
            MultiFaceDetectionFrame result = null;
            SentisFaceDetector.PendingMultiDetection pending = null;
            try
            {
                // CPU-input YuNet already has canonical Color32[]; avoid redundant
                // GPU texture allocation/upload in registration AND recognition.
                bool cpuInput = yunet.ExecutionPolicy.CpuInput;
                if (!cpuInput)
                {
                    using (PerformanceDiagnostics.Measure(PerfMetric.TextureCreateMs))
                        canonicalTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                    { wrapMode = TextureWrapMode.Clamp };
                    using (PerformanceDiagnostics.Measure(PerfMetric.TextureUploadMs))
                    {
                        canonicalTexture.SetPixels32(canonicalPixels);
                        canonicalTexture.Apply(false, false);
                    }
                    if (traceFirst) TraceFirstYuNet("texture-upload-complete");
                }
                else if (traceFirst) TraceFirstYuNet("cpu-texture-upload-skipped");

                LastFrameOrientation = orientation;
                if (activePending != null)
                    throw new InvalidOperationException("YuNet already has a pending readback.");
                if (traceFirst) TraceFirstYuNet("model-input-preparation-begin");
                pending = cpuInput
                    ? yunet.BeginDetectMultiCpu(width, height, canonicalPixels)
                    : yunet.BeginDetectMulti(canonicalTexture, canonicalPixels);
                activePending = pending;
                activeCanonicalTexture = canonicalTexture;
                if (pending.IsScheduling)
                {
                    if (traceFirst) TraceFirstYuNet("cold-sliced-schedule-begin");
                    double scheduleAt = Time.realtimeSinceStartupAsDouble;
                    while (pending.IsScheduling)
                    {
                        pending.AdvanceSchedule();
                        if (!pending.IsScheduling) break;
                        if (Time.realtimeSinceStartupAsDouble - scheduleAt > 15d)
                            throw new TimeoutException("YuNet cold GPU schedule exceeded 15 seconds.");
                        yield return null;
                    }
                    if (traceFirst) TraceFirstYuNet("cold-sliced-schedule-complete");
                }
                if (traceFirst) TraceFirstYuNet("readbacks-requested");
                PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.YuNetWaiting);
                double waitStart = Time.realtimeSinceStartupAsDouble;
                if (traceFirst) TraceFirstYuNet("readback-poll-begin");
                bool firstPoll = true;
                double nextProgressAt = waitStart + 2d;
                while (true)
                {
                    // IsReady may itself block: distinguish that from an ordinary
                    // asynchronous wait by tracing both sides of its first poll.
                    bool isReady = pending.IsReady;
                    if (traceFirst && firstPoll)
                    {
                        TraceFirstYuNet("readback-first-poll-returned", pending.CompletedOutputCount);
                        firstPoll = false;
                    }
                    if (isReady) break;
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (now - waitStart > 15d)
                    {
                        if (traceFirst) TraceFirstYuNet("readback-timeout", pending.CompletedOutputCount);
                        throw new TimeoutException("YuNet asynchronous output exceeded 15 seconds; completed readbacks=" +
                            pending.CompletedOutputCount + "/" + pending.TotalOutputCount + ".");
                    }
                    if (traceFirst && now >= nextProgressAt)
                    {
                        TraceFirstYuNet("readback-still-pending", pending.CompletedOutputCount);
                        nextProgressAt = now + 2d;
                    }
                    yield return null;
                }
                if (traceFirst) TraceFirstYuNet("readback-all-ready", pending.CompletedOutputCount);
                PerformanceDiagnostics.Record(PerfMetric.YuNetWaitWallMs,
                    (Time.realtimeSinceStartupAsDouble - waitStart) * 1000d);

                PerformanceDiagnostics.SetPipelinePhase(PerfPipelinePhase.YuNetFinalize);
                MultiFaceDetectionResult detection = pending.Complete();
                if (traceFirst) TraceFirstYuNet("decode-complete");
                FaceLetterboxMetadata letterbox = detection.Letterbox;
                if (letterbox.SourceWidth != width || letterbox.SourceHeight != height)
                    throw new InvalidOperationException("Async YuNet metadata differs from captured frame.");

                using (PerformanceDiagnostics.Measure(PerfMetric.PixelsToMatMs))
                    canonicalMat = OpenCvSharp.Unity.PixelsToMat(canonicalPixels, width, height);
                var faces = new FaceDetectionItem[detection.Faces.Count];
                for (int i = 0; i < faces.Length; i++)
                {
                    var face = detection.Faces[i];
                    faces[i] = new FaceDetectionItem(
                        FaceBoundingBoxMapper.ModelToCanonical(face.Bounds, letterbox),
                        FaceBoundingBoxMapper.ModelToCanonical(face.Landmarks, letterbox),
                        face.Confidence);
                }

                RearCameraExposureProcessor.Stats rawQualityFace = default;
                bool hasRawQualityFace = false;
                RectInt? selectedQualityRoi = null;
                if (rearFacing && faces.Length > 0)
                {
                    // Use EXACTLY the same 10% margin, integer rounding and
                    // top-left bounds as CreateRegistrationFrame's Mat crop.
                    Rect bounds = faces[0].Bounds;
                    Rect expanded = FaceBoundingBoxMapper.ClampToImage(
                        FaceBoundingBoxMapper.ExpandMargin(bounds, .10f), width, height);
                    RectInt roi = FaceCropper.BuildTopLeftPixelRoi(expanded, width, height);
                    if (roi.width > 0 && roi.height > 0)
                    {
                        selectedQualityRoi = roi;
                        if (correctionApplied && registrationCpuOnly)
                        {
                            // Raw-damage gating applies to registration candidates.
                            // Recognition must not pay for a redundant full ROI scan.
                            rawQualityFace = RearCameraExposureProcessor.MeasureTopLeftRegion(
                                originalCanonicalPixels, width, height, roi, 1);
                            hasRawQualityFace = true;
                        }
                    }
                }

                // A slow/stale detection must never refresh an old ROI's lifetime.
                if (rearFacing && capturedAt <= Time.unscaledTime &&
                    Time.unscaledTime - capturedAt <= .75f)
                {
                    if (selectedQualityRoi.HasValue)
                    {
                        previousRearFaceRoi = selectedQualityRoi;
                        previousRoiWidth = width;
                        previousRoiHeight = height;
                        previousRoiOrientation = orientation;
                        previousRoiTime = capturedAt;
                    }
                    else
                    {
                        previousRearFaceRoi = null;
                        previousRoiTime = -1f;
                        previousRoiOrientation = null;
                    }
                }
                result = new MultiFaceDetectionFrame(canonicalMat, width, height, faces,
                    hasRawQualityFace, rawQualityFace, correctionApplied);
                canonicalMat = null;
                onComplete(result);
                result = null; // Ownership transferred to the caller.
            }
            finally
            {
                result?.Dispose();
                canonicalMat?.Dispose();
                pending?.Dispose();
                if (ReferenceEquals(activePending, pending)) activePending = null;
                if (ReferenceEquals(activeCanonicalTexture, canonicalTexture)) activeCanonicalTexture = null;
                if (canonicalTexture != null)
                {
                    if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(canonicalTexture);
                    else UnityEngine.Object.DestroyImmediate(canonicalTexture);
                }
            }
        }

        private FaceDetectionResult DetectFrame(Texture frame)
        {
            if(frame == null)
            {
                return new FaceDetectionResult
                {
                    Found = false,
                    Bounds = default,
                    Confidencce = 0f                
                };
            }

            return detector.Detect(frame);
        }
    }
}
