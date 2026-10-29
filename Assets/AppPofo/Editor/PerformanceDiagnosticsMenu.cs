#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AppPofo.Editor
{
    public static class PerformanceDiagnosticsMenu
    {
        // SessionState is consumed once by the runtime. No PlayerPrefs or shipped UI.
        private const string SelectionKey = "AppPofo.PERF1.Scenario";
        [MenuItem("APPPOFO/PERF-1/Off")] private static void Off() => Select("Off");
        [MenuItem("APPPOFO/PERF-1/CameraOnly")] private static void CameraOnly() => Select("CameraOnly");
        [MenuItem("APPPOFO/PERF-1/Preprocessing")] private static void Preprocessing() => Select("Preprocessing");
        [MenuItem("APPPOFO/PERF-1/YuNet")] private static void YuNet() => Select("YuNet");
        [MenuItem("APPPOFO/PERF-1/SFace")] private static void SFace() => Select("SFace");
        [MenuItem("APPPOFO/PERF-1/Full")] private static void Full() => Select("Full");
        // One-shot A/B selectors: only the next Editor Play uses the selected worker backend.
        [MenuItem("APPPOFO/PERF-2/SFace GPUCompute")]
        private static void SFaceGpu() => SelectSFace("GPUCompute");
        [MenuItem("APPPOFO/PERF-2/SFace CPU")]
        private static void SFaceCpu() => SelectSFace("CPU");
        private static void SelectSFace(string backend)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[PERF2] Stop Play before selecting the backend.");
                return;
            }
#if APPPOFO_PERF_DIAGNOSTICS
            SessionState.SetString(SelectionKey, "SFace");
            SessionState.SetString("AppPofo.PERF2.SFaceBackend", backend);
            Debug.Log("[PERF2] Next Play SFace backend: " + backend);
#else
            Debug.LogWarning("[PERF2] Add APPPOFO_PERF_DIAGNOSTICS to the active target defines first.");
#endif
        }

        private static void Select(string scenario)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[PERF1] Stop Play before selecting the next scenario.");
                return;
            }
#if APPPOFO_PERF_DIAGNOSTICS
            SessionState.SetString(SelectionKey, scenario);
            Debug.Log("[PERF1] Next Play scenario: " + scenario);
#else
            SessionState.EraseString(SelectionKey);
            Debug.LogWarning("[PERF1] Add APPPOFO_PERF_DIAGNOSTICS to the active target defines first.");
#endif
        }
    }
}
#endif
