using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.FaceRegistration.Quality;
using AppPofo.Presentation.FaceRegistration.Selection;

namespace AppPofo.Presentation.FaceRegistration
{
    public enum FaceCalibrationLabel
    {
        NORMAL, TOO_CLOSE, TOO_FAR, YAW_LEFT, YAW_RIGHT,
        PITCH_UP, PITCH_DOWN, ROLL, DARK, BRIGHT, MOTION_BLUR
    }

    /// <summary>Writes numeric measurements only; never retains or disposes a frame's Mat.</summary>
    internal sealed class FaceCalibrationCsvLogger : IDisposable
    {
        private const string Header = "Timestamp,Label,FaceSizeRatio,RollDegrees,YawScore,PitchScore,Mean,DarkPixelRatio,BrightPixelRatio,BlurScore,GatePass,TotalError,BestUpdated";
        private readonly FacePoseEvaluator poseEvaluator = new FacePoseEvaluator();
        private readonly FaceBrightnessEvaluator brightnessEvaluator = new FaceBrightnessEvaluator();
        private readonly FaceBlurEvaluator blurEvaluator = new FaceBlurEvaluator();
        private StreamWriter writer;

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public void BeginSession()
        {
            Dispose();
            try
            {
                string directory = Path.Combine(UnityEngine.Application.persistentDataPath, "FaceRegistrationCalibration");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory,
                    $"calibration_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.csv");
                writer = new StreamWriter(new FileStream(path, FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                writer.WriteLine(Header);
                Debug.Log($"[Face Calibration] CSV={path}");
            }
            catch (Exception exception)
            {
                DisableRecording(exception);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public void Append(DateTime timestamp, FaceCalibrationLabel label, FaceDetectionFrame frame,
            FrameQualityMetrics metrics, bool gatePass, float? totalError, bool bestUpdated,
            BestFrameSelector selector)
        {
            if (writer == null) return;
            try
            {
                bool hasCurrentFace = frame != null && frame.Detection.HasCurrentFrameBounds &&
                    frame.FaceMat != null && !frame.FaceMat.Empty();
                if (hasCurrentFace)
                {
                    // Work on a metrics copy: fill only stages skipped by the gate.
                    if (!metrics.HasPoseMetrics)
                    {
                        var landmarks = frame.CanonicalLandmarks;
                        metrics.RollDegrees = poseEvaluator.EvaluateRoll(landmarks);
                        metrics.YawScore = poseEvaluator.EvaluateYaw(landmarks);
                        metrics.PitchScore = poseEvaluator.EvaluatePitch(landmarks);
                    }
                    if (!metrics.HasBrightnessMetrics)
                        metrics.Brightness = brightnessEvaluator.Evaluate(frame.FaceMat);
                    if (!metrics.HasBlurMetrics)
                        metrics.BlurScore = blurEvaluator.Evaluate(frame.FaceMat).BlurScore;
                    // A diagnostic score for rejected frames never updates the best candidate.
                    if (!totalError.HasValue)
                        totalError = selector.EvaluateFront(metrics.FaceSizeRatio, metrics.RollDegrees,
                            metrics.YawScore, metrics.PitchScore);
                }

                writer.WriteLine(string.Join(",", new[]
                {
                    timestamp.ToString("O", CultureInfo.InvariantCulture), label.ToString(),
                    Number(hasCurrentFace ? metrics.FaceSizeRatio : (float?)null),
                    Number(hasCurrentFace ? metrics.RollDegrees : (float?)null),
                    Number(hasCurrentFace ? metrics.YawScore : (float?)null),
                    Number(hasCurrentFace ? metrics.PitchScore : (float?)null),
                    Number(hasCurrentFace ? metrics.Brightness.Mean : (float?)null),
                    Number(hasCurrentFace ? metrics.Brightness.DarkPixelRatio : (float?)null),
                    Number(hasCurrentFace ? metrics.Brightness.BrightPixelRatio : (float?)null),
                    Number(hasCurrentFace ? metrics.BlurScore : (float?)null),
                    gatePass.ToString(), Number(totalError), bestUpdated.ToString()
                }));
            }
            catch (Exception exception)
            {
                DisableRecording(exception);
            }
        }

        private static string Number(float? value)
        {
            return value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value)
                ? value.Value.ToString("R", CultureInfo.InvariantCulture) : string.Empty;
        }

        private void DisableRecording(Exception exception)
        {
            Dispose();
            Debug.LogWarning($"[Face Calibration] Recording stopped: {exception.Message}");
        }

        public void Dispose()
        {
            var previousWriter = writer;
            writer = null;
            try { previousWriter?.Dispose(); }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Face Calibration] CSV close failed: {exception.Message}");
            }
        }
    }
}
