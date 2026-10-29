using System;

namespace AppPofo.Presentation.Diagnostics
{
    // Phases describe what the application is doing, not proven GPU ownership of a hitch.
    public enum PerfPipelinePhase
    {
        Idle, Capture, YuNetWaiting, YuNetFinalize, SFacePrepare,
        SFaceWaiting, SFaceFinalize, Match, Present, SFaceScheduling
    }

    public enum PerfGpuStage { None, YuNet, SFace }

    /// <summary>Numeric and enum-only diagnostic row; contains no face, embedding or profile data.</summary>
    public readonly struct PerfHitchEvent
    {
        public readonly int Frame;
        public readonly double ObservedAtMs;
        public readonly double FrameMs;
        public readonly PerfPipelinePhase PreviousPhase;
        public readonly PerfPipelinePhase CurrentPhase;
        public readonly double PhaseAgeMs;
        public readonly PerfGpuStage GpuStage;
        public readonly bool GpuStagePending;
        public readonly double MsSinceGpuSubmit;
        public readonly double MsSinceGpuComplete;
        public readonly double MeasuredMainThreadMs;
        public readonly long GcAllocatedBytes;
        public readonly int Gen0Collections;

        public PerfHitchEvent(int frame, double observedAtMs, double frameMs,
            PerfPipelinePhase previousPhase, PerfPipelinePhase currentPhase,
            double phaseAgeMs, PerfGpuStage gpuStage, bool gpuStagePending,
            double msSinceGpuSubmit, double msSinceGpuComplete,
            double measuredMainThreadMs, long gcAllocatedBytes, int gen0Collections)
        {
            Frame = frame;
            ObservedAtMs = observedAtMs;
            FrameMs = frameMs;
            PreviousPhase = previousPhase;
            CurrentPhase = currentPhase;
            PhaseAgeMs = phaseAgeMs;
            GpuStage = gpuStage;
            GpuStagePending = gpuStagePending;
            MsSinceGpuSubmit = msSinceGpuSubmit;
            MsSinceGpuComplete = msSinceGpuComplete;
            MeasuredMainThreadMs = measuredMainThreadMs;
            GcAllocatedBytes = gcAllocatedBytes;
            Gen0Collections = gen0Collections;
        }
    }

    /// <summary>Bounded in-memory hitch events. No per-frame allocations or file I/O.</summary>
    public sealed class PerfHitchRecorder
    {
        public const double HitchThresholdMs = 100d;
        public const double SevereThresholdMs = 300d;
        public const double CriticalThresholdMs = 500d;
        private readonly PerfHitchEvent[] events;
        public int Count { get; private set; }
        public long Dropped { get; private set; }
        public long Over100 { get; private set; }
        public long Over300 { get; private set; }
        public long Over500 { get; private set; }

        public PerfHitchRecorder(int capacity = 128)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            events = new PerfHitchEvent[capacity];
        }

        public bool Add(PerfHitchEvent evt)
        {
            if (double.IsNaN(evt.FrameMs) || double.IsInfinity(evt.FrameMs) ||
                evt.FrameMs <= HitchThresholdMs) return false;
            Over100++;
            if (evt.FrameMs > SevereThresholdMs) Over300++;
            if (evt.FrameMs > CriticalThresholdMs) Over500++;
            if (Count == events.Length) { Dropped++; return false; }
            events[Count++] = evt;
            return true;
        }

        public PerfHitchEvent Get(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return events[index];
        }

        public void Clear()
        {
            Count = 0;
            Dropped = Over100 = Over300 = Over500 = 0;
        }
    }
}
