using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AppPofo.Presentation.Diagnostics
{
    /// <summary>Developer-only opt-in. Never accepts face, embedding or profile data.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class PerformanceDiagnostics : MonoBehaviour
    {
        public const string EditorSelectionKey = "AppPofo.PERF1.Scenario";
        public const string AndroidExtra = "apppofo_perf_scenario";
        public const string EditorBackendSelectionKey = "AppPofo.PERF2.SFaceBackend";
        public const string EditorSliceSelectionKey = "AppPofo.PERF2.SFaceLayersPerFrame";
        public const string AndroidBackendExtra = "apppofo_sface_backend";
        public const string AndroidSliceExtra = "apppofo_sface_layers_per_frame";
        public const string AndroidYuNetInputExtra = "apppofo_yunet_input";
        public const string EditorYuNetInputKey = "AppPofo.PERF4.YuNetInput";
        public const string AndroidYuNetBackendExtra = "apppofo_yunet_backend";
        public const string EditorYuNetBackendKey = "AppPofo.PERF4.YuNetBackend";
        public const string AndroidYuNetCpuLayersExtra = "apppofo_yunet_cpu_layers_per_frame";
        public const string EditorYuNetCpuLayersKey = "AppPofo.PERF4.YuNetCpuLayersPerFrame";
        public const double WarmupSeconds = 15;
        public const double MeasurementSeconds = 75;
        public const double FlushSeconds = 10;
        public const string CsvHeader = "run_id,start_utc,environment,device_model,unity,graphics_api,scenario,sface_backend,window,reason,seconds,fps,camera_width,camera_height,camera_updates,pipeline_passes,face_inputs,metric,unit,count,retained,dropped,sum,mean,p50,p95,max,record_type,frame_index,observed_ms,previous_phase,current_phase,phase_age_ms,gpu_stage,gpu_pending,ms_since_gpu_submit,ms_since_gpu_complete,main_thread_measured_ms,gc_alloc_bytes,gc_gen0_collections,hitch_over_100,hitch_over_300,hitch_over_500,hitch_dropped,sface_layers_per_frame,yunet_backend,yunet_input,yunet_cpu_layers_per_frame";
        private static PerformanceDiagnostics instance;
        private PerfScenario scenario;
        private bool useCpuSFace;
        private int sfaceLayersPerFrame;
        private bool useCpuYuNetInput;
        private bool useCpuYuNetBackend;
        private int yunetCpuLayersPerFrame;
        private PerfWindow[] windows;
        private ProfilerRecorder gcAllocated;
        private ProfilerRecorder managedMemory;
        private PerfHitchRecorder hitchRecorder;
        private PerfPipelinePhase currentPhase;
        private PerfPipelinePhase previousFramePhase;
        private double phaseChangedAt;
        private PerfGpuStage lastGpuStage;
        private bool gpuStagePending;
        private double lastGpuSubmittedAt = -1d;
        private double lastGpuCompletedAt = -1d;
        private double measuredMainThreadMs;
        private int lastGen0CollectionCount;
        private bool sessionOpen;
        private bool completed;
        private double measurementStart;
        private double nextFlush;
        private double windowStart;
        private double lastCameraTime = -1;
        private int lastCameraFrame = -1;
        private int cameraWidth;
        private int cameraHeight;
        private long cameraUpdates;
        private long pipelinePasses;
        private long faceInputs;
        private int windowNumber;
        private int generation;
        private string runId;
        private string startUtc;
        private string csvPath;
        private bool headerWritten;
        private readonly StringBuilder csv = new StringBuilder(16384);
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static bool Available
        {
            get
            {
#if APPPOFO_PERF_DIAGNOSTICS && (UNITY_EDITOR || DEVELOPMENT_BUILD)
                return true;
#else
                return false;
#endif
            }
        }
        public static bool Active => Available && instance != null && instance.sessionOpen;
        // Distinguish an explicitly configured PERF benchmark from a normal
        // Development Build, where Available may also be true.
        public static bool Configured => Available && instance != null;
        public static string PipelinePhaseName =>
            instance != null && instance.sessionOpen ? instance.currentPhase.ToString() : "Inactive";
        // Available before CameraReady, because the worker is created during StartPreview.
        // PERF-only SFace selection. Device-specific production defaults are resolved
        // independently by RecognitionInferencePolicy; registration is unchanged.
        public static bool UseCpuSFace => Available && instance != null && instance.useCpuSFace;
        public static string SFaceBackendName => UseCpuSFace ? "CPU" : "GPUCompute";
        // PERF-only SFace slicing. SM-T500's normal CPU4 policy does not depend on PERF.
        public static int SFaceLayersPerFrame =>
            Available && instance != null ? instance.sfaceLayersPerFrame : 0;
        // PERF-only YuNet input. SM-T500's CPU16 default is selected separately.
        public static bool UseCpuYuNetInput =>
            Available && instance != null && instance.useCpuYuNetInput;
        public static bool UseCpuYuNetBackend =>
            Available && instance != null && instance.useCpuYuNetBackend;
        // 0 indicates not applicable: GPU YuNet retains its fixed cold four-layer policy.
        public static int YuNetCpuLayersPerFrame =>
            Available && instance != null && instance.useCpuYuNetBackend ?
                instance.yunetCpuLayersPerFrame : 0;
        private bool Collecting => sessionOpen && !completed &&
            Time.realtimeSinceStartupAsDouble >= measurementStart;
        public static bool IsScenario(PerfScenario value) => Active && instance.scenario == value;
        public static bool SkipMatching => IsScenario(PerfScenario.SFace);
        public static bool SkipPresentation => Active && instance.scenario != PerfScenario.Full;

        public static bool TryParseScenario(string value, out PerfScenario parsed)
        {
            // Exact names only: reject numeric enum values and arbitrary strings.
            foreach (PerfScenario candidate in Enum.GetValues(typeof(PerfScenario)))
                if (string.Equals(value, candidate.ToString(), StringComparison.Ordinal))
                { parsed = candidate; return true; }
            parsed = PerfScenario.Off;
            return false;
        }

        /// <summary>Missing backend selects the unchanged GPU baseline. Invalid values fail closed.</summary>
        public static bool TryParseSFaceBackend(string value, out bool useCpu)
        {
            useCpu = false;
            if (string.IsNullOrEmpty(value) || string.Equals(value, "GPUCompute", StringComparison.Ordinal))
                return true;
            if (string.Equals(value, "CPU", StringComparison.Ordinal))
            {
                useCpu = true;
                return true;
            }
            return false;
        }

        /// <summary>0 = original scheduling; positive options = layers scheduled per rendered frame.</summary>
        public static bool TryParseSFaceLayersPerFrame(string value, out int layers)
        {
            layers = 0;
            if (string.IsNullOrEmpty(value) || value == "0") return true;
            switch (value)
            {
                case "4": layers = 4; return true;
                case "8": layers = 8; return true;
                case "16": layers = 16; return true;
                case "32": layers = 32; return true;
                default: return false;
            }
        }

        public static bool TryParseYuNetInput(string requested, out bool cpu)
        {
            cpu = false;
            if (string.IsNullOrEmpty(requested) || requested == "GPU") return true;
            if (requested == "CPU") { cpu = true; return true; }
            return false;
        }

        public static bool TryParseYuNetBackend(string requested, out bool cpu)
        {
            cpu = false;
            if (string.IsNullOrEmpty(requested) || requested == "GPUCompute") return true;
            if (requested == "CPU") { cpu = true; return true; }
            return false;
        }

        /// <summary>CPU YuNet A/B only. Missing selects the proven four-layer baseline.</summary>
        public static bool TryParseYuNetCpuLayersPerFrame(string value, out int layers)
        {
            layers = 4;
            if (string.IsNullOrEmpty(value) || value == "4") return true;
            if (value == "8") { layers = 8; return true; }
            if (value == "16") { layers = 16; return true; }
            return false;
        }

        /// <summary>Reject experimental CPU budgets when CPU YuNet is not selected.</summary>
        public static bool TryResolveYuNetCpuLayerBudget(
            string requested, bool cpuBackend, out int effectiveLayers)
        {
            effectiveLayers = 0;
            if (!TryParseYuNetCpuLayersPerFrame(requested, out int parsed)) return false;
            if (!cpuBackend && parsed != 4) return false;
            effectiveLayers = cpuBackend ? parsed : 0;
            return true;
        }

        /// <summary>GPU first inference stays at four; only CPU A/B uses the selection.</summary>
        public static int SelectYuNetScheduleLayers(bool cpuBackend, int cpuLayers)
        {
            if (!cpuBackend) return 4;
            if (cpuLayers != 4 && cpuLayers != 8 && cpuLayers != 16)
                throw new ArgumentOutOfRangeException(nameof(cpuLayers));
            return cpuLayers;
        }

        // Every emitted CSV record uses the same validated metadata suffix.
        // For GPU experiments, 0 means "not applicable", not four CPU layers.
        public static string CsvYuNetMetadataFields(bool cpuBackend, bool cpuInput, int cpuLayers)
        {
            if (cpuBackend && (!cpuInput || (cpuLayers != 4 && cpuLayers != 8 && cpuLayers != 16)))
                throw new ArgumentException("CPU YuNet requires CPU input and a valid layer budget.");
            if (!cpuBackend && cpuLayers != 0)
                throw new ArgumentException("GPU YuNet must not report a CPU layer budget.");
            return (cpuBackend ? "CPU" : "GPUCompute") + "," +
                (cpuInput ? "CPU" : "GPU") + "," +
                cpuLayers.ToString(Invariant);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if APPPOFO_PERF_DIAGNOSTICS && (UNITY_EDITOR || DEVELOPMENT_BUILD)
            string selected = null;
            string selectedBackend = null;
            string selectedLayers = null;
            string selectedYuNetInput = null;
            string selectedYuNetBackend = null;
            string selectedYuNetCpuLayers = null;
#if UNITY_EDITOR
            // Consumed once; stopping Play returns to OFF, including with domain reload disabled.
            selected = UnityEditor.SessionState.GetString(EditorSelectionKey, "Off");
            UnityEditor.SessionState.EraseString(EditorSelectionKey);
            selectedBackend = UnityEditor.SessionState.GetString(EditorBackendSelectionKey, "GPUCompute");
            UnityEditor.SessionState.EraseString(EditorBackendSelectionKey);
            selectedLayers = UnityEditor.SessionState.GetString(EditorSliceSelectionKey, "0");
            UnityEditor.SessionState.EraseString(EditorSliceSelectionKey);
            selectedYuNetInput = UnityEditor.SessionState.GetString(EditorYuNetInputKey, "GPU");
            UnityEditor.SessionState.EraseString(EditorYuNetInputKey);
            selectedYuNetBackend = UnityEditor.SessionState.GetString(EditorYuNetBackendKey, "GPUCompute");
            UnityEditor.SessionState.EraseString(EditorYuNetBackendKey);
            selectedYuNetCpuLayers = UnityEditor.SessionState.GetString(EditorYuNetCpuLayersKey, "4");
            UnityEditor.SessionState.EraseString(EditorYuNetCpuLayersKey);
#elif UNITY_ANDROID
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                selected = intent.Call<string>("getStringExtra", AndroidExtra);
                selectedBackend = intent.Call<string>("getStringExtra", AndroidBackendExtra);
                selectedLayers = intent.Call<string>("getStringExtra", AndroidSliceExtra);
                selectedYuNetInput = intent.Call<string>("getStringExtra", AndroidYuNetInputExtra);
                selectedYuNetBackend = intent.Call<string>("getStringExtra", AndroidYuNetBackendExtra);
                selectedYuNetCpuLayers = intent.Call<string>("getStringExtra", AndroidYuNetCpuLayersExtra);
                intent.Call<AndroidJavaObject>("removeExtra", AndroidExtra)?.Dispose();
                intent.Call<AndroidJavaObject>("removeExtra", AndroidBackendExtra)?.Dispose();
                intent.Call<AndroidJavaObject>("removeExtra", AndroidSliceExtra)?.Dispose();
                intent.Call<AndroidJavaObject>("removeExtra", AndroidYuNetInputExtra)?.Dispose();
                intent.Call<AndroidJavaObject>("removeExtra", AndroidYuNetBackendExtra)?.Dispose();
                intent.Call<AndroidJavaObject>("removeExtra", AndroidYuNetCpuLayersExtra)?.Dispose();
            }
            catch { Debug.LogWarning("[PERF1] Intent unavailable; diagnostics OFF."); }
#endif
            if (!TryParseScenario(selected, out var chosen) || chosen == PerfScenario.Off) return;
            if (!TryParseSFaceBackend(selectedBackend, out bool cpuBackend))
            {
                Debug.LogWarning("[PERF2] Invalid SFace backend; diagnostics OFF.");
                return;
            }
            if (!TryParseSFaceLayersPerFrame(selectedLayers, out int layersPerFrame))
            {
                Debug.LogWarning("[PERF2] Invalid SFace layers/frame; diagnostics OFF.");
                return;
            }
            if (!TryParseYuNetInput(selectedYuNetInput, out bool cpuYuNetInput))
            {
                Debug.LogWarning("[PERF4] Invalid YuNet input; diagnostics OFF.");
                return;
            }
            if (!TryParseYuNetBackend(selectedYuNetBackend, out bool cpuYuNetBackend))
            {
                Debug.LogWarning("[PERF4] Invalid YuNet backend; diagnostics OFF.");
                return;
            }
            if (cpuYuNetBackend && !cpuYuNetInput)
            {
                Debug.LogWarning("[PERF4] YuNet CPU backend requires CPU input; diagnostics OFF.");
                return;
            }
            // CPU A/B is scoped to recognition experiments; unrelated scenarios keep GPU.
            if (chosen != PerfScenario.SFace && chosen != PerfScenario.Full) cpuBackend = false;
            // Frame slicing experiment is GPUCompute-only.
            if (cpuBackend || (chosen != PerfScenario.SFace && chosen != PerfScenario.Full))
                layersPerFrame = 0;
            if (chosen != PerfScenario.YuNet &&
                chosen != PerfScenario.SFace && chosen != PerfScenario.Full)
            {
                cpuYuNetInput = false;
                cpuYuNetBackend = false;
            }
            if (!TryResolveYuNetCpuLayerBudget(selectedYuNetCpuLayers,
                cpuYuNetBackend, out int effectiveYuNetCpuLayers))
            {
                Debug.LogWarning("[PERF4] Invalid YuNet CPU layer budget or backend combination; diagnostics OFF.");
                return;
            }
            var host = new GameObject("PERF1 Developer Diagnostics");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<PerformanceDiagnostics>();
            instance.scenario = chosen;
            instance.useCpuSFace = cpuBackend;
            instance.sfaceLayersPerFrame = layersPerFrame;
            instance.useCpuYuNetInput = cpuYuNetInput;
            instance.useCpuYuNetBackend = cpuYuNetBackend;
            instance.yunetCpuLayersPerFrame = effectiveYuNetCpuLayers;
            instance.windows = new PerfWindow[Enum.GetValues(typeof(PerfMetric)).Length];
            for (int i = 0; i < instance.windows.Length; i++) instance.windows[i] = new PerfWindow();
#endif
        }

        // Called only by the recognition screen; camera/model startup is outside the timed window.
        public static void CameraReady()
        {
            if (!Available || instance == null || instance.sessionOpen) return;
            instance.Begin();
        }
        private void Begin()
        {
            sessionOpen = true;
            completed = false;
            hitchRecorder = new PerfHitchRecorder();
            currentPhase = previousFramePhase = PerfPipelinePhase.Idle;
            phaseChangedAt = Time.realtimeSinceStartupAsDouble;
            lastGpuStage = PerfGpuStage.None;
            gpuStagePending = false;
            lastGpuSubmittedAt = lastGpuCompletedAt = -1d;
            measuredMainThreadMs = 0d;
            lastGen0CollectionCount = GC.CollectionCount(0);
            generation++;
            ClearWindow();
            windowNumber = 0;
            headerWritten = false;
            cameraWidth = cameraHeight = 0;
            lastCameraTime = -1;
            lastCameraFrame = -1;
            measurementStart = Time.realtimeSinceStartupAsDouble + WarmupSeconds;
            windowStart = measurementStart;
            nextFlush = measurementStart + FlushSeconds;
            runId = Guid.NewGuid().ToString("N");
            startUtc = DateTime.UtcNow.ToString("O", Invariant);
            csvPath = Path.Combine(UnityEngine.Application.persistentDataPath, "perf1",
                runId + ".csv");
            gcAllocated = StartRecorder("GC Allocated In Frame");
            managedMemory = StartRecorder("GC Used Memory");
            Debug.Log("[PERF4] YuNet backend " + (useCpuYuNetBackend ? "CPU (experimental)" : "GPUCompute (baseline)"));
            Debug.Log("[PERF4] YuNet CPU layers/frame " + yunetCpuLayersPerFrame +
                (useCpuYuNetBackend ? " (experimental)" : " (not applicable)"));
            Debug.Log("[PERF4] YuNet input " + (useCpuYuNetInput ? "CPU" : "GPU") +
                (useCpuYuNetInput ? " (experimental)" : " (baseline)"));
            Debug.Log("[PERF1] " + scenario + "; SFace backend " + SFaceBackendName +
                "; SFace layers/frame " + sfaceLayersPerFrame + "; warmup " + WarmupSeconds.ToString("0", Invariant) + "s, measurement " + MeasurementSeconds.ToString("0", Invariant) + "s.");
        }
        private static ProfilerRecorder StartRecorder(string counter)
        {
            try { return ProfilerRecorder.StartNew(ProfilerCategory.Memory, counter, 1); }
            catch { return default; } // Missing counters are reported as no samples, never as zero.
        }
        private void LateUpdate()
        {
            if (!Collecting) return;
            double now = Time.realtimeSinceStartupAsDouble;
            double frameMs = Time.unscaledDeltaTime * 1000d;
            windows[(int)PerfMetric.FrameMs].Add(frameMs);
            // LastValue represents the completed preceding Profiler frame.
            if (gcAllocated.Valid && gcAllocated.Count > 0)
                windows[(int)PerfMetric.GcAllocatedBytes].Add(gcAllocated.LastValue);
            if (managedMemory.Valid && managedMemory.Count > 0)
                windows[(int)PerfMetric.ManagedMemoryBytes].Add(managedMemory.LastValue);

            int gen0CollectionsNow = GC.CollectionCount(0);
            int gen0CollectionsDelta = Math.Max(0, gen0CollectionsNow - lastGen0CollectionCount);
            lastGen0CollectionCount = gen0CollectionsNow;

            if (frameMs > PerfHitchRecorder.HitchThresholdMs)
            {
                long frameGc = gcAllocated.Valid && gcAllocated.Count > 0 ?
                    gcAllocated.LastValue : -1L;
                hitchRecorder.Add(new PerfHitchEvent(Time.frameCount,
                    (now - measurementStart) * 1000d, frameMs,
                    previousFramePhase, currentPhase,
                    (now - phaseChangedAt) * 1000d,
                    lastGpuStage, gpuStagePending,
                    lastGpuSubmittedAt >= 0d ? (now - lastGpuSubmittedAt) * 1000d : -1d,
                    lastGpuCompletedAt >= 0d ? (now - lastGpuCompletedAt) * 1000d : -1d,
                    measuredMainThreadMs, frameGc, gen0CollectionsDelta));
            }
            previousFramePhase = currentPhase;
            measuredMainThreadMs = 0d;

            if (now >= nextFlush)
            {
                Flush(now, now >= measurementStart + MeasurementSeconds ? "complete" : "periodic");
                nextFlush = now + FlushSeconds;
            }
            if (now >= measurementStart + MeasurementSeconds)
            {
                if (windows[(int)PerfMetric.FrameMs].Count > 0) Flush(now, "complete");
                completed = true;
                DisposeRecorders();
                // Keep this scenario selected until the screen closes; never resume Full silently.
                Debug.Log("[PERF1] Measurement complete.");
            }
        }
        public static void StopSession()
        {
            if (!Available || instance == null || !instance.sessionOpen) return;
            if (instance.Collecting && instance.windows[(int)PerfMetric.FrameMs].Count > 0)
                instance.Flush(Time.realtimeSinceStartupAsDouble, "screen_closed");
            instance.sessionOpen = false;
            instance.DisposeRecorders();
            instance.generation++;
        }
        private void OnApplicationPause(bool paused)
        {
            if (!paused || !sessionOpen || completed) return;
            if (Collecting && windows[(int)PerfMetric.FrameMs].Count > 0)
                Flush(Time.realtimeSinceStartupAsDouble, "pause_aborted");
            completed = true;
            DisposeRecorders();
            Debug.Log("[PERF1] Pause aborted measurement; close/reopen recognition to restart.");
        }
        private void OnDestroy()
        {
            if (instance != this) return;
            StopSession();
            instance = null;
        }
        private void DisposeRecorders() { gcAllocated.Dispose(); managedMemory.Dispose(); }

        public static Scope Measure(PerfMetric metric)
        {
            var target = instance;
            return Available && target != null && target.Collecting ?
                new Scope(target, metric) : default;
        }
        public readonly struct Scope : IDisposable
        {
            private readonly PerformanceDiagnostics target;
            private readonly PerfMetric metric;
            private readonly long start;
            private readonly int version;
            internal Scope(PerformanceDiagnostics target, PerfMetric metric)
            {
                this.target = target; this.metric = metric;
                start = Stopwatch.GetTimestamp(); version = target.generation;
            }
            public void Dispose()
            {
                if (target == null || version != target.generation || !target.Collecting) return;
                double ms = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                target.windows[(int)metric].Add(ms);
                if (CountsAsMainThreadWork(metric)) target.measuredMainThreadMs += ms;
            }
        }
        // Only non-overlapping synchronous scopes contribute to this diagnostic lower bound.
        // This is NOT the CPU time of the entire frame, and NOT GPU execution time.
        private static bool CountsAsMainThreadWork(PerfMetric metric)
        {
            switch (metric)
            {
                case PerfMetric.GetPixels32Ms:
                case PerfMetric.OrientationMs:
                case PerfMetric.TextureCreateMs:
                case PerfMetric.TextureUploadMs:
                case PerfMetric.LetterboxTensorMs:
                case PerfMetric.YuNetScheduleMs:
                case PerfMetric.YuNetFinalizeMs:
                case PerfMetric.PixelsToMatMs:
                case PerfMetric.SFacePrepareMs:
                case PerfMetric.SFaceScheduleMs:
                case PerfMetric.SFaceFinalizeMs:
                case PerfMetric.SQLiteEmbeddingsMs:
                case PerfMetric.CardUiMs:
                    return true;
                default:
                    return false;
            }
        }

        public static void SetPipelinePhase(PerfPipelinePhase phase)
        {
            if (!Active) return;
            var target = instance;
            if (target.currentPhase == phase) return;
            target.currentPhase = phase;
            target.phaseChangedAt = Time.realtimeSinceStartupAsDouble;
        }

        public static void GpuSubmitted(PerfGpuStage stage)
        {
            if (!Active) return;
            instance.lastGpuStage = stage;
            instance.lastGpuSubmittedAt = Time.realtimeSinceStartupAsDouble;
            instance.gpuStagePending = true;
        }

        public static void GpuCompleted(PerfGpuStage stage)
        {
            if (!Active) return;
            if (instance.lastGpuStage != stage) return;
            instance.lastGpuCompletedAt = Time.realtimeSinceStartupAsDouble;
            instance.gpuStagePending = false;
        }

        public static void ObserveCamera(WebCamTexture camera)
        {
            if (!Active || camera == null || !camera.isPlaying || camera.width <= 16 ||
                camera.height <= 16 || !camera.didUpdateThisFrame) return;
            var target = instance;
            if (target.lastCameraFrame == Time.frameCount) return;
            target.lastCameraFrame = Time.frameCount;
            target.cameraWidth = camera.width;
            target.cameraHeight = camera.height;
            double now = Time.realtimeSinceStartupAsDouble;
            if (target.Collecting)
            {
                target.cameraUpdates++;
                if (target.lastCameraTime >= target.measurementStart)
                    target.windows[(int)PerfMetric.CameraIntervalMs].Add((now - target.lastCameraTime) * 1000d);
            }
            target.lastCameraTime = now;
        }
        // Async timing scopes span yielded frames; ordinary Scope is intentionally per-frame only.
        public static void Record(PerfMetric metric, double value)
        {
            if (!Active || !instance.Collecting) return;
            instance.windows[(int)metric].Add(value);
        }

        public static void PipelinePass(int detectedFaces)
        {
            if (!Active || !instance.Collecting) return;
            instance.pipelinePasses++;
            instance.faceInputs += detectedFaces;
        }
        private void ClearWindow()
        {
            foreach (var window in windows) window.Clear();
            cameraUpdates = pipelinePasses = faceInputs = 0;
            hitchRecorder?.Clear();
        }
        private static string Number(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? "" : value.ToString("F4", Invariant);

        private void Flush(double now, string reason)
        {
            long writeStart = Stopwatch.GetTimestamp();
            csv.Clear();
            if (!headerWritten)
                csv.AppendLine(CsvHeader);
            double fps = 1000d / windows[(int)PerfMetric.FrameMs].Mean;
            string environment = UnityEngine.Application.isEditor ? "UnityEditor" :
                UnityEngine.Application.platform == RuntimePlatform.Android ? "AndroidDevice" : "OtherPlayer";
            string yunetCsvFields = CsvYuNetMetadataFields(
                useCpuYuNetBackend, useCpuYuNetInput, yunetCpuLayersPerFrame);
            for (int i = 0; i < windows.Length; i++)
            {
                var window = windows[i];
                window.Sort();
                csv.Append(runId).Append(',').Append(startUtc).Append(',').Append(environment).Append(',');
                // CSV escape fixed system metadata; no device unique ID, names or application data.
                AppendQuoted(SystemInfo.deviceModel);
                csv.Append(','); AppendQuoted(UnityEngine.Application.unityVersion);
                csv.Append(',').Append(SystemInfo.graphicsDeviceType).Append(',').Append(scenario)
                    .Append(',').Append(SFaceBackendName).Append(',').Append(windowNumber).Append(',').Append(reason)
                    .Append(',').Append(Number(now - windowStart)).Append(',').Append(Number(fps))
                    .Append(',').Append(cameraWidth).Append(',').Append(cameraHeight)
                    .Append(',').Append(cameraUpdates).Append(',').Append(pipelinePasses)
                    .Append(',').Append(faceInputs).Append(',').Append((PerfMetric)i)
                    .Append(',').Append(i == (int)PerfMetric.GcAllocatedBytes ||
                        i == (int)PerfMetric.ManagedMemoryBytes ? "bytes" :
                        i == (int)PerfMetric.SkippedFrames ||
                        i == (int)PerfMetric.StaleResults ||
                        i == (int)PerfMetric.PipelineErrors ||
                        i == (int)PerfMetric.SFaceScheduleBatches ||
                        i == (int)PerfMetric.YuNetScheduleBatches ? "count" : "ms")
                    .Append(',').Append(window.Count).Append(',').Append(window.Retained)
                    .Append(',').Append(window.Dropped).Append(',').Append(Number(window.Sum))
                    .Append(',').Append(Number(window.Mean))
                    .Append(',').Append(Number(window.Percentile(.50)))
                    .Append(',').Append(Number(window.Percentile(.95)))
                    .Append(',').Append(window.Count == 0 ? "" : Number(window.Maximum))
                    .Append(",summary,,,,,,,,,,,,,")
                    .Append(hitchRecorder.Over100).Append(',').Append(hitchRecorder.Over300)
                    .Append(',').Append(hitchRecorder.Over500).Append(',').Append(hitchRecorder.Dropped)
                    .Append(',').Append(sfaceLayersPerFrame)
                    .Append(',').Append(yunetCsvFields).AppendLine();
            }
            // The single CSV has unchanged metric columns plus typed hitch rows.
            // Older readers selecting metric == FrameMs continue to see only summary rows.
            for (int i = 0; i < hitchRecorder.Count; i++)
            {
                PerfHitchEvent hitch = hitchRecorder.Get(i);
                csv.Append(runId).Append(',').Append(startUtc).Append(',').Append(environment).Append(',');
                AppendQuoted(SystemInfo.deviceModel);
                csv.Append(','); AppendQuoted(UnityEngine.Application.unityVersion);
                csv.Append(',').Append(SystemInfo.graphicsDeviceType).Append(',')
                    .Append(scenario).Append(',').Append(SFaceBackendName)
                    .Append(',').Append(windowNumber).Append(",hitch,")
                    .Append(Number(now - windowStart)).Append(",,")
                    .Append(cameraWidth).Append(',').Append(cameraHeight)
                    .Append(",,,,FrameHitch,ms,1,1,0,")
                    .Append(Number(hitch.FrameMs)).Append(',')
                    .Append(Number(hitch.FrameMs)).Append(',')
                    .Append(Number(hitch.FrameMs)).Append(',')
                    .Append(Number(hitch.FrameMs)).Append(',')
                    .Append(Number(hitch.FrameMs)).Append(",hitch,")
                    .Append(hitch.Frame).Append(',').Append(Number(hitch.ObservedAtMs))
                    .Append(',').Append(hitch.PreviousPhase).Append(',').Append(hitch.CurrentPhase)
                    .Append(',').Append(Number(hitch.PhaseAgeMs))
                    .Append(',').Append(hitch.GpuStage).Append(',').Append(hitch.GpuStagePending ? 1 : 0)
                    .Append(',').Append(hitch.MsSinceGpuSubmit < 0d ? "" : Number(hitch.MsSinceGpuSubmit))
                    .Append(',').Append(hitch.MsSinceGpuComplete < 0d ? "" : Number(hitch.MsSinceGpuComplete))
                    .Append(',').Append(Number(hitch.MeasuredMainThreadMs)).Append(',')
                    .Append(hitch.GcAllocatedBytes < 0L ? "" : hitch.GcAllocatedBytes.ToString(Invariant))
                    .Append(',').Append(hitch.Gen0Collections)
                    // Columns 41-44 are blank for a hitch row; column 45 is SFace budget.
                    // Older code emitted only four commas, shifting this row left once.
                    .Append(",,,,,").Append(sfaceLayersPerFrame)
                    .Append(',').Append(yunetCsvFields).AppendLine();
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(csvPath));
                File.AppendAllText(csvPath, csv.ToString());
                headerWritten = true;
            }
            catch
            {
                // Deliberately omit exception text: paths or identifiers may appear in it.
                Debug.LogWarning("[PERF1] CSV write failed; numeric summary follows.");
                Debug.Log(csv.ToString());
            }
            windowNumber++;
            generation++;
            ClearWindow();
            windowStart = now;
            windows[(int)PerfMetric.CsvWriteMs].Add(
                (Stopwatch.GetTimestamp() - writeStart) * 1000d / Stopwatch.Frequency);
        }
        private void AppendQuoted(string value)
        {
            csv.Append('"');
            if (value != null) csv.Append(value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " "));
            csv.Append('"');
        }
    }
}
