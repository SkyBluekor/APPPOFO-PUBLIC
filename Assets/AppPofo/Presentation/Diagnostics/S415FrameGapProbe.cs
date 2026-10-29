using System.Globalization;
using UnityEngine;

namespace AppPofo.Presentation.Diagnostics
{
    /// <summary>Development-only frame-gap monitor, including camera warm-up
    /// and screen transitions. No images, identity or profile data are logged.</summary>
    public sealed class S415FrameGapProbe : MonoBehaviour
    {
        private double previousFrameAt;
        private static string activeStage = "Idle";

        /// <summary>Annotate long gaps in explicitly instrumented Android
        /// Development Builds, including before CameraReady.</summary>
        public static void SetStage(string stage)
        {
#if UNITY_ANDROID && DEVELOPMENT_BUILD && APPPOFO_PERF_DIAGNOSTICS
            activeStage = string.IsNullOrEmpty(stage) ? "Idle" : stage;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if UNITY_ANDROID && DEVELOPMENT_BUILD && APPPOFO_PERF_DIAGNOSTICS
            var host = new GameObject("S4-15 Frame Gap Probe");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideInHierarchy;
            host.AddComponent<S415FrameGapProbe>();
#endif
        }

        private void LateUpdate()
        {
#if UNITY_ANDROID && DEVELOPMENT_BUILD && APPPOFO_PERF_DIAGNOSTICS
            double now = Time.realtimeSinceStartupAsDouble;
            if (previousFrameAt > 0d)
            {
                double gapMs = (now - previousFrameAt) * 1000d;
                if (gapMs >= 300d)
                    Debug.LogWarning("[S4-15] FrameGap ms=" +
                        gapMs.ToString("F1", CultureInfo.InvariantCulture) +
                        " frame=" + Time.frameCount +
                        " phase=" + PerformanceDiagnostics.PipelinePhaseName +
                        " stage=" + activeStage);
            }
            previousFrameAt = now;
#endif
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) previousFrameAt = 0d;
        }
    }
}
