using System;
using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    /// <summary>
    /// Shared front/rear camera photometry for registration and recognition.
    /// A/B correction is explicitly opt-in on Android Development Builds until
    /// device parity is established. Never alter the raw WebCamTexture preview.
    /// </summary>
    public static class RearCameraExposureProcessor
    {
        public readonly struct Stats
        {
            public readonly float Mean;
            public readonly int P95;
            public readonly int P80;
            public readonly float BrightRatio;
            public readonly float ClipRatio;
            public Stats(float mean, int p95, float brightRatio, float clipRatio, int p80 = 0)
            {
                Mean = mean;
                P95 = p95;
                P80 = p80 > 0 ? p80 : p95;
                BrightRatio = brightRatio;
                ClipRatio = clipRatio;
            }
        }

        public readonly struct Result
        {
            public readonly Stats Raw;
            public readonly bool Corrected;
            public readonly float AppliedGain;
            public readonly Color32[] Pixels;
            // Raw is the original center region. Observed is the actual gain-control
            // region on the original snapshot, not the post-gain quality crop.
            public readonly Stats Observed;
            public readonly bool UsedFaceRoi;
            public readonly string Reason;
            public readonly bool ControlRoiDamaged;
            public Result(Stats raw, bool corrected, float gain, Color32[] pixels,
                Stats observed = default, bool usedFaceRoi = false, string reason = "Legacy")
            {
                Raw = raw;
                Corrected = corrected;
                AppliedGain = gain;
                Pixels = pixels;
                Observed = observed;
                UsedFaceRoi = usedFaceRoi;
                Reason = reason;
                ControlRoiDamaged = observed.ClipRatio >= .06f;
            }
        }

        public static bool ExperimentalCorrectionEnabled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ReadDeveloperOverride()
        {
            ExperimentalCorrectionEnabled = false;
#if UNITY_ANDROID && DEVELOPMENT_BUILD && !UNITY_EDITOR
            // This is a temporary A/B diagnostic, not a release default.
            // adb shell am start ... --es apppofo_rear_exposure_correction On
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                string value = intent.Call<string>("getStringExtra", "apppofo_rear_exposure_correction");
                ExperimentalCorrectionEnabled = string.Equals(value, "On", StringComparison.Ordinal);
                // The flag is read once per app launch; mutation of the Intent
                // is unnecessary and can fail during early Android initialization.
            }
            catch (Exception e)
            {
                Debug.LogWarning("[S4-14] Rear exposure A/B option unavailable: " +
                    e.GetType().Name);
            }
#endif
        }

        /// <summary>Center-weighted capture statistics. Excludes bright edges
        /// and backdrops, and never reads or logs identifying pixel values.</summary>
        public static Stats MeasureCenter(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0 ||
                pixels.Length != width * height)
                throw new ArgumentException("Invalid canonical camera frame.");
            int left = width / 4, right = width - left;
            int bottom = height / 4, top = height - bottom;
            int count = 0, sum = 0, bright = 0, clipped = 0;
            var histogram = new int[256];
            for (int y = bottom; y < top; y += 4)
            {
                int row = y * width;
                for (int x = left; x < right; x += 4)
                {
                    Color32 p = pixels[row + x];
                    int luma = Mathf.Clamp(Mathf.RoundToInt(
                        p.r * .299f + p.g * .587f + p.b * .114f), 0, 255);
                    histogram[luma]++;
                    count++;
                    sum += luma;
                    if (luma >= 210) bright++;
                    if (luma >= 250 || (p.r >= 253 && p.g >= 230 && p.b >= 230))
                        clipped++;
                }
            }
            if (count == 0) return default;
            int rank = Mathf.CeilToInt(count * .95f), running = 0, p95 = 255;
            for (int i = 0; i < histogram.Length; i++)
            {
                running += histogram[i];
                if (running >= rank) { p95 = i; break; }
            }
            return new Stats((float)sum / count, p95, (float)bright / count,
                (float)clipped / count, Percentile(histogram, count, .80f));
        }

        /// <summary>
        /// Only gently attenuates recoverable bright input; cannot restore clipped
        /// highlights. Caller should preserve the same processed pixels for detection,
        /// quality gate, alignment and embeddings in a single frame.
        /// </summary>
        public static Result Process(Color32[] pixels, int width, int height,
            bool isRear, bool enableCorrection)
        {
            if (!isRear) return new Result(default, false, 1f, pixels);
            Stats stats = MeasureCenter(pixels, width, height);
            bool recoverable = enableCorrection &&
                stats.Mean > 180f && stats.BrightRatio > .25f &&
                stats.ClipRatio < .06f && stats.P95 < 250;
            if (!recoverable) return new Result(stats, false, 1f, pixels);

            // Never amplify dark pixels or push severe clipping past the quality gate.
            float gain = Mathf.Clamp(160f / stats.Mean, .80f, .95f);
            var output = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                output[i] = new Color32(
                    (byte)Mathf.RoundToInt(p.r * gain),
                    (byte)Mathf.RoundToInt(p.g * gain),
                    (byte)Mathf.RoundToInt(p.b * gain),
                    p.a);
            }
            return new Result(stats, true, gain, output);
        }

        /// <summary>
        /// Informational near-clipping indicator for opt-in corrected rear frames.
        /// 8-bit values near white are not proof of irrecoverable sensor damage.
        /// This value must never override FrameQualityGate candidate acceptance.
        /// </summary>
        public static bool HasRawNearClipWarning(bool corrected,
            bool hasRawFaceStatistics, Stats rawFaceStatistics)
        {
            return corrected && hasRawFaceStatistics &&
                rawFaceStatistics.ClipRatio >= .06f;
        }

        /// <summary>Per-camera-session exposure filter. Never shared between lenses.</summary>
        public sealed class AdaptiveState
        {
            public float Gain { get; private set; } = 1f;
            public bool Initialized { get; private set; }
            public void Reset()
            {
                Gain = 1f;
                Initialized = false;
            }
            internal float Step(float target)
            {
                Gain = Initialized ? Mathf.Lerp(Gain, target, .30f) : target;
                Initialized = true;
                if (Mathf.Abs(Gain - 1f) < .005f) Gain = 1f;
                return Gain;
            }
        }


        /// <summary>
        /// Stateful rear-only correction. The optional face ROI is in canonical
        /// TOP-LEFT coordinates (like YuNet/OpenCV). Color32[] is BOTTOM-LEFT.
        /// Raw and Observed statistics are always measured before gain.
        /// Highlight clipping warns about damage, but does not disable bounded
        /// attenuation of the remaining pixels; acceptance must use raw stats.
        /// </summary>
        public static Result Process(Color32[] pixels, int width, int height,
            bool isRear, bool enableCorrection, AdaptiveState state, RectInt? faceRoi = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!isRear || !enableCorrection)
            {
                state.Reset();
                if (!isRear)
                    return new Result(default, false, 1f, pixels,
                        reason: "Front");
                Stats disabledCenter = MeasureCenter(pixels, width, height);
                return new Result(disabledCenter, false, 1f, pixels,
                    disabledCenter, reason: "Disabled");
            }

            Stats centerRaw = MeasureCenter(pixels, width, height);
            Stats observed = centerRaw;
            bool usedFaceRoi = false;
            if (faceRoi.HasValue)
            {
                RectInt roi = faceRoi.Value;
                if (roi.width >= 24 && roi.height >= 24 &&
                    roi.xMin >= 0 && roi.yMin >= 0 &&
                    roi.xMax <= width && roi.yMax <= height)
                {
                    observed = MeasureTopLeftRegion(pixels, width, height, roi);
                    usedFaceRoi = true;
                }
            }

            // A pure grayscale gain cannot recover already-white pixels.
            // Keep that damage visible in ControlRoiDamaged and, independently,
            // in the raw current-face stats used for registration approval.
            if (observed.Mean < 35f)
            {
                state.Reset();
                return new Result(centerRaw, false, 1f, pixels,
                    observed, usedFaceRoi, "TooDark");
            }

            bool brightMean = observed.Mean > 180f;
            bool brightTail = observed.BrightRatio > .25f;
            bool brightHold = state.Gain < .995f &&
                (observed.Mean > 175f || observed.BrightRatio > .22f);
            bool dark = observed.Mean < 55f && observed.BrightRatio < .03f &&
                observed.P95 < 140;
            bool darkHold = state.Gain > 1.005f &&
                observed.Mean < 60f && observed.BrightRatio < .05f &&
                observed.P95 < 145;
            float target = 1f;
            string reason = "Neutral";
            if (brightMean || brightTail || brightHold)
            {
                float meanGain = brightMean ? 175f / observed.Mean : 1f;
                float tailGain = brightTail || (brightHold && observed.BrightRatio > .22f)
                    ? 208f / Mathf.Max(1, observed.P80) : 1f;
                target = Mathf.Clamp(Mathf.Min(meanGain, tailGain), .80f, 1f);
                reason = observed.ClipRatio >= .06f ? "ClippedAttenuation" :
                    brightTail ? "HighlightTail" : "MeanHigh";
            }
            else if (dark || darkHold)
            {
                target = Mathf.Clamp(55f / observed.Mean, 1f, 1.10f);
                reason = "DarkBoost";
            }

            float gain = state.Step(target);
            if (gain == 1f)
                return new Result(centerRaw, false, 1f, pixels,
                    observed, usedFaceRoi, reason);

            Color32[] corrected = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                corrected[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(p.r * gain), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(p.g * gain), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(p.b * gain), 0, 255),
                    p.a);
            }
            return new Result(centerRaw, true, gain, corrected,
                observed, usedFaceRoi, reason);
        }

        /// <summary>
        /// Measure a canonical top-left image rectangle against a bottom-left
        /// Color32[] buffer. Step=1 uses the same exact pixel footprint as
        /// OpenCV's corresponding ROI; step=4 is inexpensive gain feedback.
        /// </summary>
        public static Stats MeasureTopLeftRegion(Color32[] pixels, int width,
            int height, RectInt roi, int step = 4)
        {
            if (pixels == null || width <= 0 || height <= 0 ||
                pixels.Length != width * height || step < 1 ||
                roi.width <= 0 || roi.height <= 0 ||
                roi.xMin < 0 || roi.yMin < 0 ||
                roi.xMax > width || roi.yMax > height)
                throw new ArgumentException("Invalid top-left camera region.");
            int count = 0, sum = 0, bright = 0, clipped = 0;
            var histogram = new int[256];
            for (int y = roi.yMin; y < roi.yMax; y += step)
            {
                int row = (height - 1 - y) * width;
                for (int x = roi.xMin; x < roi.xMax; x += step)
                {
                    Color32 p = pixels[row + x];
                    int luma = Mathf.Clamp(Mathf.RoundToInt(
                        p.r * .299f + p.g * .587f + p.b * .114f), 0, 255);
                    histogram[luma]++;
                    count++;
                    sum += luma;
                    if (luma >= 210) bright++;
                    if (luma >= 250 || (p.r >= 253 && p.g >= 230 && p.b >= 230))
                        clipped++;
                }
            }
            if (count == 0) return default;
            int p80 = Percentile(histogram, count, .80f);
            int p95 = Percentile(histogram, count, .95f);
            return new Stats((float)sum / count, p95,
                (float)bright / count, (float)clipped / count, p80);
        }

        private static int Percentile(int[] histogram, int count, float percentile)
        {
            int rank = Mathf.CeilToInt(count * percentile);
            int running = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                running += histogram[i];
                if (running >= rank) return i;
            }
            return 255;
        }
    }
}
