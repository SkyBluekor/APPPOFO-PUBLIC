using System;

namespace AppPofo.Presentation.Diagnostics
{
    public enum PerfScenario { Off, CameraOnly, Preprocessing, YuNet, SFace, Full }
    public enum PerfMetric
    {
        FrameMs, CameraIntervalMs, GetPixels32Ms, OrientationMs,
        TextureCreateMs, TextureUploadMs, DetectionMs, LetterboxTensorMs,
        YuNetScheduleMs, YuNetReadbackMs, SFaceMs, SFacePrepareMs,
        SFaceScheduleMs, SFaceReadbackMs, SQLiteEmbeddingsMs, CardUiMs,
        CardLayoutMs, PixelsToMatMs, GcAllocatedBytes, ManagedMemoryBytes, CsvWriteMs,
        YuNetWaitWallMs, SFaceWaitWallMs, YuNetFinalizeMs, SFaceFinalizeMs,
        PipelineWallMs, SkippedFrames, StaleResults, PipelineErrors,
        SFaceScheduleBatches, SFaceScheduleWallMs,
        YuNetScheduleBatches, YuNetScheduleWallMs
    }

    /// <summary>Bounded samples; exact totals cover all samples, percentiles cover retained samples.
    /// No allocations in Add; overflow is explicit rather than silently overwriting data.</summary>
    public sealed class PerfWindow
    {
        private readonly double[] values;
        private readonly double[] sorted;
        public long Count { get; private set; }
        public int Retained { get; private set; }
        public long Dropped => Count - Retained;
        public double Sum { get; private set; }
        public double Maximum { get; private set; }
        public double Mean => Count == 0 ? double.NaN : Sum / Count;
        public PerfWindow(int capacity = 8192)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            values = new double[capacity];
            sorted = new double[capacity];
        }
        public void Add(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return;
            Count++;
            Sum += value;
            Maximum = Math.Max(Maximum, value);
            if (Retained < values.Length) values[Retained++] = value;
        }
        public void Sort()
        {
            Array.Copy(values, sorted, Retained);
            Array.Sort(sorted, 0, Retained);
        }
        public double Percentile(double quantile)
        {
            if (quantile <= 0 || quantile > 1) throw new ArgumentOutOfRangeException(nameof(quantile));
            if (Retained == 0) return double.NaN;
            return sorted[Math.Max(0, (int)Math.Ceiling(quantile * Retained) - 1)];
        }
        public void Clear()
        {
            Count = 0; Retained = 0; Sum = 0; Maximum = 0;
        }
    }
}
