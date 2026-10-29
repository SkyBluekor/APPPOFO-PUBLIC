using AppPofo.Presentation.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using System;
using Unity.InferenceEngine;
using System.Collections.Generic;
using System.Collections;
using AppPofo.Presentation.FaceRegistration.Embedding;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    public sealed class SentisFaceDetector : IFaceDetector, IMultiFaceDetector, IDisposable
    {
        private readonly Worker _worker;
        private readonly bool useCpuBackend;
        private readonly YuNetExecutionPolicy executionPolicy;
        public YuNetExecutionPolicy ExecutionPolicy => executionPolicy;
        private static readonly ProfilerMarker ScheduleMarker = new ProfilerMarker("PERF1.YuNet.Schedule");
        private static readonly ProfilerMarker ReadbackMarker = new ProfilerMarker("PERF1.YuNet.Readback");
        private readonly Tensor<float> _inputTensor;
        private float[] cpuInputBuffer;
        private readonly Queue<int> _recentFaceCounts = new Queue<int>();
        public FaceLetterboxMetadata? LastLetterboxMetadata { get; private set; }
        public bool EnableDetectionDiagnostics { get; set; } = false;
        internal bool DiagnosticPreprocessingOnly { get; set; }
        private bool firstAsyncGpuTraceObserved;
        // GPU cold inference always uses four layers per frame; CPU diagnostic
        // 4/8/16 selection is resolved by PerformanceDiagnostics only.
        private static void TraceFirstGpu(string stage, double startedAt)
        {
#if APPPOFO_PERF_DIAGNOSTICS && (UNITY_EDITOR || DEVELOPMENT_BUILD)
            double elapsedMs = (Time.realtimeSinceStartupAsDouble - startedAt) * 1000d;
            Debug.Log("[PERF4] First YuNet GPU stage=" + stage +
                " elapsedMs=" + elapsedMs.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " frame=" + Time.frameCount);
#endif
        }

        private void TraceFirstStage(string stage, double startedAt)
        {
            if (!executionPolicy.CpuBackend)
            {
                TraceFirstGpu(stage, startedAt);
                return;
            }
#if UNITY_ANDROID && DEVELOPMENT_BUILD
            double elapsedMs = (Time.realtimeSinceStartupAsDouble - startedAt) * 1000d;
            Debug.Log("[S4-14] YuNet CPU stage=" + stage +
                " elapsedMs=" + elapsedMs.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " frame=" + Time.frameCount);
#endif
        }

        private const int FaceCountStabilizationWindow = 5;
        private bool _hasLastBestCandidate;
        private DetectionCandidate _lastBestCandidate;

        private struct DetectionCandidate
        {
            public float Score;
            public Rect Bounds;
            public FaceLandmarks5 Landmarks;

            public int Stride;
            public int Index;
        }
        public SentisFaceDetector(ModelAsset modelAsset)
            : this(modelAsset, YuNetExecutionPolicy.RecognitionDiagnostics) { }

        public SentisFaceDetector(ModelAsset modelAsset, YuNetExecutionPolicy policy)
        {
            executionPolicy = policy;
            if(modelAsset == null)
                throw new ArgumentNullException(nameof(modelAsset));

            Model SourceModel = ModelLoader.Load(modelAsset);
            
            var graph = new FunctionalGraph();
            var input = graph.AddInput(SourceModel,0);
            var scaledINput = input * 255f;
            var outputs = Functional.Forward(SourceModel,scaledINput);

            graph.AddOutputs(outputs);

            Model runtimeModel = graph.Compile();

            useCpuBackend = executionPolicy.CpuBackend;
            _worker = new Worker(
                runtimeModel,
                useCpuBackend ? BackendType.CPU : BackendType.GPUCompute
            );

            _inputTensor = new Tensor<float>(new TensorShape(1,3,
                FaceLetterboxPreprocessor.ModelHeight, FaceLetterboxPreprocessor.ModelWidth));
        }

        public FaceDetectionResult Detect(Texture frame)
        {
            List<DetectionCandidate> finalCandiates = DetectCandidates(frame, out int candidateCount, true);
            int stableFaceCount = StabilizeFaceCount(finalCandiates.Count);
            if (EnableDetectionDiagnostics)
            {
                Debug.Log(
                    $"NMS 전={candidateCount}, " +
                    $"NMS 후={finalCandiates.Count}, " +
                    $"안정화 후={stableFaceCount}"
                );
            }

            if(finalCandiates.Count == 0)
            {
                if (stableFaceCount > 0 && _hasLastBestCandidate)
                {
                    return new FaceDetectionResult
                    {
                        Found = true,
                        FaceCount = stableFaceCount,
                        Bounds = _lastBestCandidate.Bounds,
                        Confidencce = _lastBestCandidate.Score,
                        HasCurrentFrameBounds = false
                    };
                }

                return new FaceDetectionResult
                {
                    Found = false,
                    FaceCount = 0,
                    Bounds = default,
                    Confidencce = 0f
                };
            }

            DetectionCandidate best = finalCandiates[0];
            _lastBestCandidate = best;
            _hasLastBestCandidate = true;

            return new FaceDetectionResult
            {
                Found = true,
                FaceCount = stableFaceCount,
                Bounds = best.Bounds,
                Landmarks = best.Landmarks,
                Confidencce = best.Score,
                HasCurrentFrameBounds = true
            };


        }

        public MultiFaceDetectionResult DetectMulti(Texture canonicalFrame)
        {
            List<DetectionCandidate> candidates = DetectCandidates(canonicalFrame, out _, false);
            var faces = new FaceDetectionItem[candidates.Count];
            for (int i = 0; i < faces.Length; i++)
            {
                DetectionCandidate candidate = candidates[i];
                faces[i] = new FaceDetectionItem(candidate.Bounds, candidate.Landmarks, candidate.Score);
            }
            // DetectCandidates just populated metadata for this exact inference.
            return new MultiFaceDetectionResult(LastLetterboxMetadata.Value, faces);
        }

        private List<DetectionCandidate> DetectCandidates(Texture frame, out int candidateCount, bool logDiagnostics)
        {
            var candidates = new List<DetectionCandidate>();

            if(frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            LastLetterboxMetadata = null;
            FaceLetterboxResult prepared;
            using (PerformanceDiagnostics.Measure(PerfMetric.LetterboxTensorMs))
            {
                prepared = FaceLetterboxPreprocessor.Process(frame);
                try
                {
                    LastLetterboxMetadata = prepared.Metadata;
                    var transform = new TextureTransform().SetChannelSwizzle(ChannelSwizzle.BGRA);
                    TextureConverter.ToTensor(prepared.ModelTexture,_inputTensor,transform);
                }
                catch { prepared.Dispose(); throw; }
            }
            using var letterbox = prepared;
            // Only the developer recognition experiment can bypass inference.
            if (DiagnosticPreprocessingOnly && PerformanceDiagnostics.IsScenario(PerfScenario.Preprocessing))
            {
                candidateCount = 0;
                return candidates;
            }

            using (ScheduleMarker.Auto())
            using (PerformanceDiagnostics.Measure(PerfMetric.YuNetScheduleMs))
                _worker.Schedule(_inputTensor);
            
            Tensor<float> cls8 = _worker.PeekOutput("cls_8") as Tensor<float>;

            using Tensor<float> cpuCls8 = Readback(cls8);

            Tensor<float> obj8 = _worker.PeekOutput("obj_8") as Tensor<float>;

            using Tensor<float> cpuObj8 = Readback(obj8);

            Tensor<float> cls16 = _worker.PeekOutput("cls_16") as Tensor<float>;
            Tensor<float> obj16 = _worker.PeekOutput("obj_16") as Tensor<float>;
            Tensor<float> cls32 = _worker.PeekOutput("cls_32") as Tensor<float>;
            Tensor<float> obj32 = _worker.PeekOutput("obj_32") as Tensor<float>;

            Tensor<float> bbox8 = _worker.PeekOutput("bbox_8") as Tensor<float>;
            Tensor<float> bbox16 = _worker.PeekOutput("bbox_16") as Tensor<float>;
            Tensor<float> bbox32 = _worker.PeekOutput("bbox_32") as Tensor<float>;

            Tensor<float> kps8 = _worker.PeekOutput("kps_8") as Tensor<float>;
            Tensor<float> kps16 = _worker.PeekOutput("kps_16") as Tensor<float>;
            Tensor<float> kps32 = _worker.PeekOutput("kps_32") as Tensor<float>;

            using Tensor<float> cpuCls16 = Readback(cls16);
            using Tensor<float> cpuObj16 = Readback(obj16);

            using Tensor<float> cpuCls32 = Readback(cls32);
            using Tensor<float> cpuObj32 = Readback(obj32);        

            using Tensor<float> cpuBbox8 = Readback(bbox8);
            using Tensor<float> cpuBbox16 = Readback(bbox16);
            using Tensor<float> cpuBbox32 = Readback(bbox32);

            using Tensor<float> cpuKps8 = Readback(kps8);
            using Tensor<float> cpuKps16 = Readback(kps16);
            using Tensor<float> cpuKps32 = Readback(kps32);

            return DecodeCandidates(cpuCls8, cpuObj8, cpuCls16, cpuObj16, cpuCls32, cpuObj32, cpuBbox8, cpuBbox16, cpuBbox32, cpuKps8, cpuKps16, cpuKps32, logDiagnostics, out candidateCount);
        }

        // Shared decoding logic: registration sync and recognition async use identical thresholds/NMS.
        private List<DetectionCandidate> DecodeCandidates(Tensor<float> cpuCls8, Tensor<float> cpuObj8, Tensor<float> cpuCls16, Tensor<float> cpuObj16, Tensor<float> cpuCls32, Tensor<float> cpuObj32, Tensor<float> cpuBbox8, Tensor<float> cpuBbox16, Tensor<float> cpuBbox32, Tensor<float> cpuKps8, Tensor<float> cpuKps16, Tensor<float> cpuKps32, bool logDiagnostics, out int candidateCount)
        {
            var candidates = new List<DetectionCandidate>();
            const float threshold = 0.5f;
            // Preserve registration diagnostics; multi detection does not need per-stride best probes.
            if (logDiagnostics)
            {
                FindBestCandidate(
                    cpuCls8,
                    cpuObj8,
                    1600,
                    out int best8Index,
                    out float best8Score
                );

                FindBestCandidate(
                    cpuCls16,
                    cpuObj16,
                    400,
                    out int best16Index,
                    out float best16Score
                );

                FindBestCandidate(
                    cpuCls32,
                    cpuObj32,
                    100,
                    out int best32Index,
                    out float best32Score
                );

                if (EnableDetectionDiagnostics)
                {
                    Debug.Log(
                        $"8  → index={best8Index}, score={best8Score:F4} | " +
                        $"16 → index={best16Index}, score={best16Score:F4} | " +
                        $"32 → index={best32Index}, score={best32Score:F4}"
                    );
                }
                int cols = 320 /32;
                int row = best32Index / cols;
                int col = best32Index % cols;

                int baseIndex = best32Index *4;
                float dx = cpuBbox32[baseIndex + 0];
                float dy = cpuBbox32[baseIndex + 1];
                float dw = cpuBbox32[baseIndex + 2];
                float dh = cpuBbox32[baseIndex + 3];

                float centerX = (col+ dx) *32f;
                float centerY = (row+ dy) *32f;

                float width = Mathf.Exp(dw) * 32f;
                float height = Mathf.Exp(dh) * 32f;

                float x = centerX - width * 0.5f;
                float y = centerY - height * 0.5f;

                if (EnableDetectionDiagnostics)
                {
                    Debug.Log(
                        $"decoded bbox: " +
                        $"x={x:F1}, y={y:F1}, " +
                        $"w={width:F1}, h={height:F1}, " +
                        $"score={best32Score:F4}"
                    );
                }



                int passed8 = CountCandidates(
                    cpuCls8,
                    cpuObj8,
                    1600,
                    threshold
                );

                int passed16 = CountCandidates(
                    cpuCls16,
                    cpuObj16,
                    400,
                    threshold
                );

                int passed32 = CountCandidates(
                    cpuCls32,
                    cpuObj32,
                    100,
                    threshold
                );

                if (EnableDetectionDiagnostics)
                {
                    Debug.Log(
                        $"threshold >= {threshold} | " +
                        $"8={passed8}, " +
                        $"16={passed16}, " +
                        $"32={passed32}, " +
                        $"total={passed8 + passed16 + passed32}"
                    );
                }
            }

            CollectCandidates(
                cpuCls8,
                cpuObj8,
                cpuBbox8,
                cpuKps8,
                1600,
                8,
                threshold,
                candidates
            );

            CollectCandidates(
                cpuCls16,
                cpuObj16,
                cpuBbox16,
                cpuKps16,
                400,
                16,
                threshold,
                candidates
            );

            CollectCandidates(
                cpuCls32,
                cpuObj32,
                cpuBbox32,
                cpuKps32,
                100,
                32,
                threshold,
                candidates
            );

            if (logDiagnostics && EnableDetectionDiagnostics)
            {
                Debug.Log(
                    $"threshold 통과 후보 수 = {candidates.Count}"
                );
            }

            const float nmsThreshold = 0.5f;
            candidateCount = candidates.Count;
            return ApplyNms(candidates,nmsThreshold);
        }


        private static readonly string[] AsyncOutputNames =
        {
            "cls_8", "obj_8", "cls_16", "obj_16", "cls_32", "obj_32",
            "bbox_8", "bbox_16", "bbox_32", "kps_8", "kps_16", "kps_32"
        };

        /// <summary>Recognition-only; one pending request at a time per detector Worker.</summary>
        public PendingMultiDetection BeginDetectMulti(Texture canonicalFrame,
            Color32[] canonicalPixels = null)
        {
            if (canonicalFrame == null) throw new ArgumentNullException(nameof(canonicalFrame));
            return BeginDetectMultiInternal(
                canonicalFrame, canonicalFrame.width, canonicalFrame.height, canonicalPixels);
        }

        /// <summary>CPU-input YuNet: avoid making a redundant GPU texture for canonical pixels.
        /// to describe canonical pixels that already exist on the CPU.</summary>
        public PendingMultiDetection BeginDetectMultiCpu(
            int width, int height, Color32[] canonicalPixels)
        {
            if (!executionPolicy.CpuInput)
                throw new InvalidOperationException("Raw CPU frame API requires CPU input.");
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (canonicalPixels == null || canonicalPixels.Length != width * height)
                throw new ArgumentException("Canonical pixels do not match the input size.");
            return BeginDetectMultiInternal(null, width, height, canonicalPixels);
        }

        private PendingMultiDetection BeginDetectMultiInternal(Texture canonicalFrame,
            int frameWidth, int frameHeight, Color32[] canonicalPixels)
        {
            LastLetterboxMetadata = null;
            // Trace the first GPU inference of this Worker, not every recognition frame.
            bool firstGpu = !firstAsyncGpuTraceObserved;
            firstAsyncGpuTraceObserved = true;
            double firstGpuAt = Time.realtimeSinceStartupAsDouble;
            if (firstGpu) TraceFirstStage("preprocess-begin", firstGpuAt);
            FaceLetterboxResult prepared = null;
            FaceLetterboxMetadata metadata;
            using (PerformanceDiagnostics.Measure(PerfMetric.LetterboxTensorMs))
            {
                if (executionPolicy.CpuInput)
                {
                    if (canonicalPixels == null)
                        throw new InvalidOperationException("CPU YuNet input requires canonical pixels.");
                    int n = 3 * FaceLetterboxPreprocessor.ModelWidth *
                        FaceLetterboxPreprocessor.ModelHeight;
                    if (cpuInputBuffer == null) cpuInputBuffer = new float[n];
                    metadata = FaceLetterboxCpuInput.FillBgrNchw(canonicalPixels,
                        frameWidth, frameHeight, cpuInputBuffer);
                    if (firstGpu) TraceFirstStage("cpu-resample-complete", firstGpuAt);
                    _inputTensor.Upload(cpuInputBuffer);
                    if (firstGpu) TraceFirstStage("cpu-input-upload-complete", firstGpuAt);
                }
                else
                {
                    if (canonicalFrame == null)
                        throw new InvalidOperationException("GPU preprocessing requires a texture.");
                    prepared = FaceLetterboxPreprocessor.Process(canonicalFrame);
                    if (firstGpu) TraceFirstStage("letterbox-complete", firstGpuAt);
                    try
                    {
                        var transform = new TextureTransform().SetChannelSwizzle(ChannelSwizzle.BGRA);
                        TextureConverter.ToTensor(prepared.ModelTexture, _inputTensor, transform);
                        if (firstGpu) TraceFirstStage("tensor-conversion-complete", firstGpuAt);
                    }
                    catch { prepared.Dispose(); throw; }
                    metadata = prepared.Metadata;
                }
                LastLetterboxMetadata = metadata;
            }
            try
            {
                var outputs = new Tensor<float>[AsyncOutputNames.Length];

                if (firstGpu || useCpuBackend)
                {
                    // Use the iterable for first GPU cold inference and for ALL
                    // experimental CPU inferences so CPU jobs are spread over frames.
                    // Returning the iterator does not submit every YuNet kernel in
                    // one frame. AdvanceSchedule is called once per rendered frame.
                    if (firstGpu) TraceFirstStage("worker-schedule-iterable-begin", firstGpuAt);
                    IEnumerator iterable;
                    using (ScheduleMarker.Auto())
                    using (PerformanceDiagnostics.Measure(PerfMetric.YuNetScheduleMs))
                        iterable = _worker.ScheduleIterable(_inputTensor);
                    if (iterable == null)
                        throw new InvalidOperationException("YuNet ScheduleIterable returned null.");
                    if (firstGpu) TraceFirstStage("worker-schedule-iterable-ready", firstGpuAt);
                    return new PendingMultiDetection(this, outputs, prepared,
                        metadata, iterable, firstGpuAt, firstGpu);
                }

                using (ScheduleMarker.Auto())
                using (PerformanceDiagnostics.Measure(PerfMetric.YuNetScheduleMs))
                    _worker.Schedule(_inputTensor);
                if (!useCpuBackend) PerformanceDiagnostics.GpuSubmitted(PerfGpuStage.YuNet);
                for (int i = 0; i < outputs.Length; i++)
                {
                    outputs[i] = _worker.PeekOutput(AsyncOutputNames[i]) as Tensor<float>;
                    if (outputs[i] == null)
                        throw new InvalidOperationException("YuNet missing output: " + AsyncOutputNames[i]);
                    outputs[i].ReadbackRequest();
                }
                return new PendingMultiDetection(this, outputs, prepared, metadata);
            }
            catch
            {
                prepared?.Dispose();
                throw;
            }
        }

        public sealed class PendingMultiDetection : IDisposable
        {
            private readonly SentisFaceDetector owner;
            private readonly Tensor<float>[] outputs; // Worker-owned: do not dispose.
            // An AsyncGPUReadbackRequest's completed data is short lived. Retain each CPU
            // copy as soon as THAT output is ready, not after all 12 requests are ready.
            private readonly Tensor<float>[] copies;
            private readonly FaceLetterboxResult prepared;
            private readonly FaceLetterboxMetadata metadata;
            private int completedCopies;
            private IEnumerator pendingSchedule;
            private readonly double firstGpuAt;
            private int scheduleBatches;
            private double scheduleStartedAt;
            private readonly bool traceSchedule;
            public bool IsScheduling => pendingSchedule != null;
            // Exposes readback progress for a timeout diagnosis without touching tensor contents.
            public int CompletedOutputCount => completedCopies;
            public int TotalOutputCount => outputs.Length;
            private bool disposed;

            internal PendingMultiDetection(SentisFaceDetector owner, Tensor<float>[] outputs,
                FaceLetterboxResult prepared, FaceLetterboxMetadata metadata,
                IEnumerator firstSchedule = null, double firstScheduleAt = 0d,
                bool traceFirstSchedule = false)
            {
                this.owner = owner;
                this.outputs = outputs;
                this.copies = new Tensor<float>[outputs.Length];
                this.prepared = prepared;
                this.metadata = metadata;
                pendingSchedule = firstSchedule;
                firstGpuAt = firstScheduleAt;
                traceSchedule = traceFirstSchedule;
            }

            /// <summary>Advance a bounded model layer budget per rendered frame.
            /// GPU cold startup stays at four; CPU diagnostic can select 4/8/16.
            /// Do not peek outputs until all YuNet layers have been submitted.</summary>
            public void AdvanceSchedule()
            {
                if (disposed) throw new ObjectDisposedException(nameof(PendingMultiDetection));
                if (pendingSchedule == null)
                    throw new InvalidOperationException("No pending YuNet cold schedule.");
                if (scheduleBatches == 0)
                {
                    scheduleStartedAt = Time.realtimeSinceStartupAsDouble;
                    if (!owner.useCpuBackend)
                        PerformanceDiagnostics.GpuSubmitted(PerfGpuStage.YuNet);
                }
                int layersPerFrame = owner.executionPolicy.ScheduleLayersPerFrame;
                scheduleBatches++;
                bool finished;
                using (ScheduleMarker.Auto())
                using (PerformanceDiagnostics.Measure(PerfMetric.YuNetScheduleMs))
                    finished = SFaceLayerBudget.Advance(pendingSchedule, layersPerFrame);
                PerformanceDiagnostics.Record(PerfMetric.YuNetScheduleBatches, 1);
                if (traceSchedule && (scheduleBatches == 1 || scheduleBatches % 8 == 0 || finished))
                    owner.TraceFirstStage("worker-schedule-batch-" + scheduleBatches, firstGpuAt);
                if (!finished) return;

                PerformanceDiagnostics.Record(PerfMetric.YuNetScheduleWallMs,
                    (Time.realtimeSinceStartupAsDouble - scheduleStartedAt) * 1000d);
                (pendingSchedule as IDisposable)?.Dispose();
                pendingSchedule = null;
                if (traceSchedule)
                {
                    owner.TraceFirstStage("worker-schedule-returned", firstGpuAt);
                    owner.TraceFirstStage("output-readbacks-begin", firstGpuAt);
                }
                for (int i = 0; i < outputs.Length; i++)
                {
                    outputs[i] = owner._worker.PeekOutput(AsyncOutputNames[i]) as Tensor<float>;
                    if (outputs[i] == null)
                        throw new InvalidOperationException("YuNet missing output: " + AsyncOutputNames[i]);
                    outputs[i].ReadbackRequest();
                }
                if (traceSchedule) owner.TraceFirstStage("output-readbacks-complete", firstGpuAt);
            }

            public bool IsReady
            {
                get
                {
                    if (disposed) throw new ObjectDisposedException(nameof(PendingMultiDetection));
                    if (pendingSchedule != null) return false;

                    // Poll ALL outstanding outputs every frame. Short-circuiting at the
                    // first unfinished output can let other completed requests expire.
                    for (int i = 0; i < outputs.Length; i++)
                    {
                        if (copies[i] != null) continue;
                        try
                        {
                            if (!outputs[i].IsReadbackRequestDone()) continue;
                            using (PerformanceDiagnostics.Measure(PerfMetric.YuNetFinalizeMs))
                                copies[i] = outputs[i].ReadbackAndClone();
                            completedCopies++;
                        }
                        catch (Exception error)
                        {
                            throw new InvalidOperationException(
                                "YuNet async readback failed for output " + AsyncOutputNames[i] +
                                " (" + i + "/12): " + error.Message, error);
                        }
                    }
                    if (completedCopies == outputs.Length)
                        if (!owner.useCpuBackend)
                            PerformanceDiagnostics.GpuCompleted(PerfGpuStage.YuNet);
                    return completedCopies == outputs.Length;
                }
            }

            public MultiFaceDetectionResult Complete()
            {
                if (disposed) throw new ObjectDisposedException(nameof(PendingMultiDetection));
                if (completedCopies != outputs.Length)
                    throw new InvalidOperationException("YuNet result not fully downloaded.");

                using (PerformanceDiagnostics.Measure(PerfMetric.YuNetFinalizeMs))
                {
                    int candidateCount;
                    var candidates = owner.DecodeCandidates(
                        copies[0], copies[1], copies[2], copies[3], copies[4], copies[5],
                        copies[6], copies[7], copies[8], copies[9], copies[10], copies[11],
                        false, out candidateCount);
                    var faces = new FaceDetectionItem[candidates.Count];
                    for (int i = 0; i < faces.Length; i++)
                    {
                        var candidate = candidates[i];
                        faces[i] = new FaceDetectionItem(candidate.Bounds, candidate.Landmarks, candidate.Score);
                    }
                    return new MultiFaceDetectionResult(metadata, faces);
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    (pendingSchedule as IDisposable)?.Dispose();
                    pendingSchedule = null;
                    for (int i = 0; i < copies.Length; i++)
                        copies[i]?.Dispose();
                }
                finally { prepared?.Dispose(); }
            }
        }

        private static Tensor<float> Readback(Tensor<float> output)
        {
            using (ReadbackMarker.Auto())
            using (PerformanceDiagnostics.Measure(PerfMetric.YuNetReadbackMs))
                return output.ReadbackAndClone();
        }

        private static void FindBestCandidate(
            Tensor<float> cls,
            Tensor<float> obj,
            int count,
            out int bestIndex,
            out float bestScore
        )
        {
            bestIndex = -1;
            bestScore = 0f;

            for (int i = 0 ; i < count; i++)
            {
                float clsScore = Mathf.Clamp01(cls[i]);
                float objScore = Mathf.Clamp01(obj[i]);

                float score = Mathf.Sqrt(clsScore * objScore);

                if(score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }    
        }

        private static int CountCandidates(
            Tensor<float> clsTensor,
            Tensor<float> objTensor,
            int count,
            float threshold
        )
        {
            
            int passedCount = 0;

            for(int i =0 ; i< count; i++)
            {
                float cls = Mathf.Clamp01(clsTensor[i]);
                float obj = Mathf.Clamp01(objTensor[i]);

                float score = Mathf.Sqrt(cls * obj);

                if(score >= threshold)
                {
                    passedCount++;
                }                
            }
            return passedCount;
        }

        private static void CollectCandidates(
            Tensor<float> clsTensor,
            Tensor<float> objTensor,
            Tensor<float> bboxTenser,
            Tensor<float> kpsTensor,
            int count,
            int stride,
            float threshold,
            List<DetectionCandidate> candidates
        )
        {
            int cols = 320 / stride;

            for(int i =0; i < count ; i++)
            {
                float cls = Mathf.Clamp01(clsTensor[i]);
                float obj = Mathf.Clamp01(objTensor[i]);

                float score = Mathf.Sqrt(cls * obj);

                if(score < threshold)
                    continue;

                int row = i / cols;
                int col = i % cols;

                int baseIndex = i * 4;

                float dx = bboxTenser[baseIndex + 0];
                float dy = bboxTenser[baseIndex + 1];
                float dw = bboxTenser[baseIndex + 2];
                float dh = bboxTenser[baseIndex + 3];

                float centerX = (col + dx) * stride;
                float centerY = (row + dy) * stride;

                float width = Mathf.Exp(dw) * stride;
                float height = Mathf.Exp(dh) * stride;

                float x = centerX - width * 0.5f;
                float y = centerY - height * 0.5f;

                candidates.Add(new DetectionCandidate
                {
                    Score = score,
                    Bounds = new Rect(x,y, width, height),
                    Landmarks = DecodeLandmarks(kpsTensor, i, row, col, stride),
                    Stride = stride,
                    Index = i
                });
                
            }
        }

        private static FaceLandmarks5 DecodeLandmarks(
            Tensor<float> kpsTensor,
            int index,
            int row,
            int col,
            int stride)
        {
            int baseIndex = index * 10;
            Vector2 DecodePoint(int pointIndex)
            {
                int offset = baseIndex + pointIndex * 2;
                return new Vector2(
                    (col + kpsTensor[offset]) * stride,
                    (row + kpsTensor[offset + 1]) * stride);
            }

            return new FaceLandmarks5(
                DecodePoint(0),
                DecodePoint(1),
                DecodePoint(2),
                DecodePoint(3),
                DecodePoint(4));
        }
        
        private static float CalculateIoU(Rect a, Rect b)
        {
            float left = Mathf.Max(a.xMin, b.xMin);
            float right = Mathf.Min(a.xMax,b.xMax);
            float bottom = Mathf.Max(a.yMin,b.yMin);
            float top = MathF.Min(a.yMax,b.yMax);

            float overlayWidth = MathF.Max(0f, right - left);
            float overlayHeight = Mathf.Max(0f, top - bottom);
            
            float intersection = overlayWidth * overlayHeight;

            float areaA = a.width * a.height;
            float areaB = b.width * b.height;

            float union = areaA + areaB - intersection;

            if(union <= 0f)
                return 0f;
            
            return intersection / union;
        }

        private static List<DetectionCandidate> ApplyNms(
            List<DetectionCandidate> candidates,
            float iouThreshold
        )
        {
            var result = new List<DetectionCandidate>();

            candidates.Sort((a,b) => b.Score.CompareTo(a.Score));

            foreach(DetectionCandidate candidate in candidates)
            {
                bool shouldKeep = true;
                foreach(DetectionCandidate kept in result)
                {
                    float iou = CalculateIoU(candidate.Bounds,kept.Bounds);

                    if(iou >= iouThreshold)
                    {
                        shouldKeep = false;
                        break;
                    }                                 
                }

                if(shouldKeep)
                {
                    result.Add(candidate);
                }
                
            }
            return result;
        }

        private int StabilizeFaceCount(int currentFaceCount)
        {
            _recentFaceCounts.Enqueue(currentFaceCount);

            while (_recentFaceCounts.Count > FaceCountStabilizationWindow)
                _recentFaceCounts.Dequeue();

            int stableFaceCount = 0;
            foreach (int faceCount in _recentFaceCounts)
            {
                if (faceCount > stableFaceCount)
                    stableFaceCount = faceCount;
            }

            if (stableFaceCount == 0)
                _hasLastBestCandidate = false;

            return stableFaceCount;
        }

        public void Dispose()
        {
            _inputTensor.Dispose();
            _worker.Dispose();
        }
    }
}
