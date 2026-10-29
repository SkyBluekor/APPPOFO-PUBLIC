using System;
using System.Globalization;
using UnityEngine;

namespace AppPofo.Presentation.FaceRecognition
{
    // Only fixed stage names and numeric operational metadata can enter this log.
    // Never accept response bodies, identities, vectors, credentials or exceptions.
    public enum CloudFaceScanStage
    {
        PreviewStarted, PreviewFrame, ModelsReady, QueryAllowed, QueryBlocked,
        ModelsPending, PipelineFaulted, Busy, WaitingForScan, Detection,
        DetectionMissing, SFaceReady, InferenceFailed, PayloadRejected,
        RequestSent, HttpCompleted, ResponseInvalid, MATCH, NO_MATCH, REJECTED,
        UNAVAILABLE, NOT_READY, DiscardGeneration, DiscardCamera, DiscardMode,
        DiscardAge, DiscardOrientation, ConfirmationOne, ConfirmationTwo,
        TrackReset, BoundsRejected, PresenterInvalid, CardCreated, CardFailed,
        CardVisible, CardInactive, LayoutUnavailable, Cleared, PermissionRevoked,
        CameraReady, YuNetModelReady, SFaceModelReady, ScanStarted, ScanCompleted,
        NoFace, WaitingForBudget, ScanWait, BudgetWait, ConfirmationScheduled,
        SFaceStarted, PreviewStopped, CardPresented, QueryWait
    }

    public static class CloudFaceScanDiagnostics
    {
        private static CloudFaceScanStage? lastGate;
        private static double gateStartedAt;
        private static double previewStartedAt = -1d;
        private static readonly int[] occurrences = new int[Enum.GetValues(typeof(CloudFaceScanStage)).Length];
        internal static double ClockSeconds => Enabled ?
            (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency : 0d;
        public static bool Enabled
        {
            get
            {
#if UNITY_ANDROID && DEVELOPMENT_BUILD && APPPOFO_FACE_SCAN_DIAGNOSTICS && !UNITY_EDITOR
                // Explicit build-only opt-in via BuildPlayerOptions.extraScriptingDefines.
                // Never enables consent, authentication or permission to query.
                return true;
#elif UNITY_EDITOR_WIN
                return UnityEngine.Application.isEditor && Environment.GetEnvironmentVariable(
                    "APPPOFO_FACE_SCAN_DIAGNOSTICS") == "enabled";
#else
                return false;
#endif
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        internal static void Gate(CloudFaceScanStage stage)
        {
            if (!Enabled) { lastGate = null; return; }
            if (lastGate == stage) return;
            EndGate();
            lastGate = stage;
            gateStartedAt = ClockSeconds;
            Record(stage);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        internal static void EndGate()
        {
            if (lastGate == CloudFaceScanStage.WaitingForScan ||
                lastGate == CloudFaceScanStage.WaitingForBudget ||
                lastGate == CloudFaceScanStage.QueryBlocked)
                Record(lastGate == CloudFaceScanStage.WaitingForScan ? CloudFaceScanStage.ScanWait :
                    lastGate == CloudFaceScanStage.WaitingForBudget ? CloudFaceScanStage.BudgetWait :
                    CloudFaceScanStage.QueryWait,
                    durationMs: (ClockSeconds - gateStartedAt) * 1000d);
            lastGate = null;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Record(CloudFaceScanStage stage, int count = -1,
            double durationMs = -1d, long httpStatus = -1)
        {
#if UNITY_EDITOR_WIN || (UNITY_ANDROID && DEVELOPMENT_BUILD && APPPOFO_FACE_SCAN_DIAGNOSTICS)
            if (!Enabled || !Enum.IsDefined(typeof(CloudFaceScanStage), stage)) return;
            double now = ClockSeconds;
            if (stage == CloudFaceScanStage.PreviewStarted)
            {
                Array.Clear(occurrences, 0, occurrences.Length);
                lastGate = null;
                previewStartedAt = now;
            }
            int occurrence = ++occurrences[(int)stage];
            Debug.Log("[LocalFaceScan] stage=" + stage +
                " count=" + (count < 0 ? occurrence : count) +
                (durationMs < 0d ? "" : " elapsedMs=" +
                    durationMs.ToString("F1", CultureInfo.InvariantCulture)) +
                (httpStatus < 0 ? "" : " http=" + httpStatus) +
                (previewStartedAt < 0d ? "" : " sincePreviewMs=" +
                    ((now - previewStartedAt) * 1000d).ToString("F1", CultureInfo.InvariantCulture)));
#endif
        }
    }
}
