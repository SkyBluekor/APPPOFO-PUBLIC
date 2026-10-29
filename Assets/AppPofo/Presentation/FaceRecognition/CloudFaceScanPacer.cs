using System;
using System.Collections.Generic;

namespace AppPofo.Presentation.FaceRecognition
{
    // Count inference starts, including frames without a detectable face.
    // The rolling budget persists across camera entries and matches the server cap.
    internal sealed class CloudFaceScanPacer
    {
        internal const double RegularIntervalSeconds = 0.5;
        private const double WindowSeconds = 60.0;
        private const int MaxScansPerWindow = 120;

        private readonly Queue<double> recentScans = new Queue<double>();
        private double nextScanAt;

        // Read-only diagnostic reason; TryStartScan remains the sole reservation path.
        internal bool IsBudgetBlocked => recentScans.Count >= MaxScansPerWindow;

        internal void BeginSession(double now)
        {
            Prune(now);
            nextScanAt = Math.Max(nextScanAt, now);
            // Do NOT reset recentScans when the camera reopens: the server's
            // per-device quota persists beyond individual camera previews.
        }

        internal bool TryStartScan(double now)
        {
            Prune(now);
            if (now < nextScanAt || recentScans.Count >= MaxScansPerWindow)
                return false;

            recentScans.Enqueue(now);
            nextScanAt = now + RegularIntervalSeconds;
            return true;
        }

        private void Prune(double now)
        {
            while (recentScans.Count > 0 && now - recentScans.Peek() >= WindowSeconds)
                recentScans.Dequeue();
        }
    }
}
