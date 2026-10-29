using System;
using AppPofo.Presentation.Diagnostics;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    /// <summary>
    /// Per-worker inference policy. Registration must not inherit the optional
    /// recognition benchmark intent; release builds use the same CPU16 policy.
    /// </summary>
    public readonly struct YuNetExecutionPolicy
    {
        public readonly bool CpuInput;
        public readonly bool CpuBackend;
        public readonly int CpuLayersPerFrame;

        public YuNetExecutionPolicy(bool cpuInput, bool cpuBackend, int cpuLayersPerFrame)
        {
            if (cpuBackend && !cpuInput)
                throw new ArgumentException("CPU YuNet backend requires CPU BGR/NCHW input.");
            if (cpuBackend && cpuLayersPerFrame != 4 &&
                cpuLayersPerFrame != 8 && cpuLayersPerFrame != 16)
                throw new ArgumentOutOfRangeException(nameof(cpuLayersPerFrame));
            if (!cpuBackend && cpuLayersPerFrame != 0)
                throw new ArgumentException("CPU layer budget must be zero for GPU YuNet.");
            CpuInput = cpuInput;
            CpuBackend = cpuBackend;
            CpuLayersPerFrame = cpuLayersPerFrame;
        }

        public static YuNetExecutionPolicy Registration =>
            new YuNetExecutionPolicy(true, true, 16);

        public static YuNetExecutionPolicy RecognitionDiagnostics =>
            new YuNetExecutionPolicy(
                PerformanceDiagnostics.UseCpuYuNetInput,
                PerformanceDiagnostics.UseCpuYuNetBackend,
                PerformanceDiagnostics.YuNetCpuLayersPerFrame);

        public int ScheduleLayersPerFrame => CpuBackend ? CpuLayersPerFrame : 4;
        public override string ToString() =>
            "input=" + (CpuInput ? "CPU" : "GPU") +
            " backend=" + (CpuBackend ? "CPU" : "GPUCompute") +
            " layers=" + ScheduleLayersPerFrame;
    }

    /// <summary>
    /// Recognition-only startup policy. SM-T500 defaults to the already-used
    /// registration CPU16 YuNet path to avoid the measured 26s GPU ToTensor hitch.
    /// Its SFace CPU4 default is validated on the SM-T500, avoiding the GPU
    /// cold-start hitch. Explicit development/PERF benchmarks remain independent.
    /// </summary>
    public readonly struct RecognitionInferencePolicy
    {
        public readonly YuNetExecutionPolicy YuNet;
        public readonly bool CpuSFace;
        public readonly int SFaceLayersPerFrame;
        public readonly string Source;

        private RecognitionInferencePolicy(YuNetExecutionPolicy yunet,
            bool cpuSFace, int sfaceLayersPerFrame, string source)
        {
            YuNet = yunet;
            CpuSFace = cpuSFace;
            SFaceLayersPerFrame = sfaceLayersPerFrame;
            Source = source;
        }

        public static bool IsTargetDevice(string deviceModel)
        {
            return !string.IsNullOrWhiteSpace(deviceModel) &&
                deviceModel.Trim().EndsWith("SM-T500", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Overrides are explicit and case-sensitive: YuNet=GPU/CPU16;
        /// SFace=GPU/GPU4/CPU4. Unknown values fail closed without silently
        /// changing a benchmark's inference backend.
        /// </summary>
        public static RecognitionInferencePolicy Resolve(string deviceModel,
            string yunetOverride = null, string sfaceOverride = null,
            bool benchmarkConfigured = false,
            YuNetExecutionPolicy benchmarkYuNet = default,
            bool benchmarkCpuSFace = false, int benchmarkSFaceLayers = 0)
        {
            bool target = IsTargetDevice(deviceModel);
            var yunet = target ? YuNetExecutionPolicy.Registration :
                new YuNetExecutionPolicy(false, false, 0);
            bool cpuSFace = target;
            int sfaceLayers = target ? 4 : 0;
            string source = target ? "SM-T500-CPU16-CPU4" : "GPU-baseline";
            if (benchmarkConfigured)
            {
                yunet = benchmarkYuNet;
                cpuSFace = benchmarkCpuSFace;
                sfaceLayers = benchmarkSFaceLayers;
                source = "PERF-benchmark";
            }

            if (!string.IsNullOrEmpty(yunetOverride))
            {
                switch (yunetOverride)
                {
                    case "GPU":
                        yunet = new YuNetExecutionPolicy(false, false, 0);
                        break;
                    case "CPU16":
                        yunet = YuNetExecutionPolicy.Registration;
                        break;
                    default:
                        throw new ArgumentException("Unrecognized recognition YuNet override.",
                            nameof(yunetOverride));
                }
                source = "Developer-override";
            }
            if (!string.IsNullOrEmpty(sfaceOverride))
            {
                switch (sfaceOverride)
                {
                    case "GPU":
                        cpuSFace = false;
                        sfaceLayers = 0;
                        break;
                    case "GPU4":
                        cpuSFace = false;
                        sfaceLayers = 4;
                        break;
                    case "CPU4":
                        cpuSFace = true;
                        sfaceLayers = 4;
                        break;
                    default:
                        throw new ArgumentException("Unrecognized recognition SFace override.",
                            nameof(sfaceOverride));
                }
                source = "Developer-override";
            }
            if (cpuSFace && sfaceLayers == 0 && !benchmarkConfigured)
                sfaceLayers = 4;
            return new RecognitionInferencePolicy(yunet, cpuSFace, sfaceLayers, source);
        }

        public override string ToString()
        {
            return "YuNet(" + YuNet + ") SFace=" + (CpuSFace ? "CPU" : "GPUCompute") +
                " layers=" + SFaceLayersPerFrame + " source=" + Source;
        }
    }
}
