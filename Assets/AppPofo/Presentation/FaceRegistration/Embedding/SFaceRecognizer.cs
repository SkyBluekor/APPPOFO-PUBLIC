using AppPofo.Presentation.Diagnostics;
using Unity.Profiling;
using System;
using System.Collections;
using Unity.InferenceEngine;
using OpenCvSharp;
using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Embedding
{
    public sealed class SFaceRecognizer : IDisposable
    {
        private readonly Worker worker;
        private readonly BackendType backendType;
        private bool firstAsyncTraceStarted;
        private bool firstAsyncTraceActive;
        private double firstAsyncStartedAt;
        private void TraceFirst(string stage)
        {
#if UNITY_ANDROID && DEVELOPMENT_BUILD
            if (!firstAsyncTraceActive) return;
            Debug.Log("[S4-15] SFace backend=" + backendType +
                " stage=" + stage + " elapsedMs=" +
                ((Time.realtimeSinceStartupAsDouble - firstAsyncStartedAt) * 1000d)
                    .ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                " frame=" + Time.frameCount);
#endif
        }
        private static readonly ProfilerMarker ScheduleMarker = new ProfilerMarker("PERF1.SFace.Schedule");
        private static readonly ProfilerMarker ReadbackMarker = new ProfilerMarker("PERF1.SFace.Readback");
        private readonly Tensor<float> inputTensor;
        private readonly float[] inputBuffer;
        private Tensor<float> pendingOutput;
        private IEnumerator pendingSchedule;
        private int scheduleLayersPerFrame;
        private bool scheduleStarted;
        private bool interruptedSchedule;
        public const int EmbeddingSize = 128;
        public const string ModelVersion = "sface_2021dec";
        public SFaceRecognizer(ModelAsset modelAsset, BackendType backend = BackendType.GPUCompute)
        {
            if(modelAsset == null)
            {
                throw new ArgumentNullException(nameof(modelAsset));
            }
            Model model = ModelLoader.Load(modelAsset);

            backendType = backend;
            worker = new Worker(model, backend);

            inputTensor = new Tensor<float>(
                new TensorShape(1,3,SFaceInputPreprocessor.InputHeight,SFaceInputPreprocessor.InputWidth)
            );
            inputBuffer = new float[3 * SFaceInputPreprocessor.InputHeight * SFaceInputPreprocessor.InputWidth];
        }

        public void Dispose()
        {
            CancelAsync();
            inputTensor?.Dispose();
            worker?.Dispose();
        }

        public void PrepareInput(Mat alignedFace)
        {
            using var measurement = PerformanceDiagnostics.Measure(PerfMetric.SFacePrepareMs);
            SFaceInputPreprocessor.FillInput(
                alignedFace,inputTensor,inputBuffer
            );
        }

        public void Run()
        {
            using (ScheduleMarker.Auto())
            using (PerformanceDiagnostics.Measure(PerfMetric.SFaceScheduleMs))
                worker.Schedule(inputTensor);
        }

        public Tensor<float> PeekOutput()
        {
            return worker.PeekOutput("fc1") as Tensor<float>;
        }

        public Tensor<float> ReadOutput()
        {
            Tensor<float> output = PeekOutput();

            if(output == null)
            {
                throw new InvalidOperationException("SFace 출력 fc1을 찾을수 없습니다.");                
            }

            using (ReadbackMarker.Auto())
            using (PerformanceDiagnostics.Measure(PerfMetric.SFaceReadbackMs))
                return output.ReadbackAndClone();
        }

        public float[] ReadEmbedding()
        {
            using Tensor<float> output = ReadOutput();
            return DecodeEmbedding(output);
        }

        // Recognition-only path. Synchronous ExtractEmbedding remains unchanged for registration.
        // layersPerFrame==0 preserves S2 Schedule(inputTensor), while positive values spread
        // ScheduleIterable(inputTensor) layer submission across Unity coroutine frames.
        public void BeginAsync(Mat alignedFace, int layersPerFrame = 0)
        {
            if (layersPerFrame < 0 || layersPerFrame > 64)
                throw new ArgumentOutOfRangeException(nameof(layersPerFrame));
            if (interruptedSchedule)
                throw new InvalidOperationException("SFace partial schedule was cancelled; recreate the Worker.");
            if (pendingOutput != null || pendingSchedule != null)
                throw new InvalidOperationException("SFace inference already pending.");
            bool first = !firstAsyncTraceStarted;
            if (first)
            {
                firstAsyncTraceStarted = true;
                firstAsyncTraceActive = true;
                firstAsyncStartedAt = Time.realtimeSinceStartupAsDouble;
                TraceFirst("prepare-begin");
            }
            PrepareInput(alignedFace);
            if (first) TraceFirst("prepare-complete");
            if (layersPerFrame == 0)
            {
                Run();
                if (first) TraceFirst("schedule-complete");
                if (backendType == BackendType.GPUCompute)
                    PerformanceDiagnostics.GpuSubmitted(PerfGpuStage.SFace);
                RequestAsyncOutput();
                if (first) TraceFirst("readback-requested");
                PerformanceDiagnostics.Record(PerfMetric.SFaceScheduleBatches, 1);
                return;
            }
            scheduleLayersPerFrame = layersPerFrame;
            scheduleStarted = false;
            using (ScheduleMarker.Auto())
            using (PerformanceDiagnostics.Measure(PerfMetric.SFaceScheduleMs))
                pendingSchedule = worker.ScheduleIterable(inputTensor);
            if (first) TraceFirst("schedule-iterable-ready");
            if (pendingSchedule == null)
                throw new InvalidOperationException("SFace ScheduleIterable returned no enumerator.");
        }

        public bool IsScheduling => pendingSchedule != null;

        /// <summary>Schedule at most the configured model layers on the calling Unity frame.</summary>
        public bool AdvanceAsyncSchedule()
        {
            if (pendingSchedule == null)
                throw new InvalidOperationException("No pending SFace layer schedule.");
            if (!scheduleStarted)
            {
                if (backendType == BackendType.GPUCompute)
                    PerformanceDiagnostics.GpuSubmitted(PerfGpuStage.SFace);
                scheduleStarted = true;
                TraceFirst("schedule-first-batch");
            }
            bool finished;
            using (ScheduleMarker.Auto())
            using (PerformanceDiagnostics.Measure(PerfMetric.SFaceScheduleMs))
                finished = SFaceLayerBudget.Advance(pendingSchedule, scheduleLayersPerFrame);
            PerformanceDiagnostics.Record(PerfMetric.SFaceScheduleBatches, 1);
            if (!finished) return false;
            TraceFirst("schedule-all-batches-complete");

            // All model layers have been submitted. Only now may we peek the output
            // and start the async readback; doing this earlier can expose stale data.
            (pendingSchedule as IDisposable)?.Dispose();
            pendingSchedule = null;
            RequestAsyncOutput();
            TraceFirst("readback-requested");
            return true;
        }

        private void RequestAsyncOutput()
        {
            Tensor<float> output = PeekOutput();
            if (output == null)
                throw new InvalidOperationException("SFace fc1 output missing.");
            output.ReadbackRequest();
            pendingOutput = output;
        }

        public bool IsAsyncComplete
        {
            get
            {
                if (pendingSchedule != null) return false;
                if (pendingOutput == null || !pendingOutput.IsReadbackRequestDone()) return false;
                if (backendType == BackendType.GPUCompute)
                    PerformanceDiagnostics.GpuCompleted(PerfGpuStage.SFace);
                return true;
            }
        }

        public float[] CompleteAsync()
        {
            if (!IsAsyncComplete)
                throw new InvalidOperationException("SFace result is not ready.");
            Tensor<float> output = pendingOutput;
            TraceFirst("readback-ready");
            try
            {
                // Read back immediately after the request reports completion.
                using (PerformanceDiagnostics.Measure(PerfMetric.SFaceFinalizeMs))
                using (Tensor<float> cpu = output.ReadbackAndClone())
                {
                    float[] embedding = DecodeEmbedding(cpu);
                    TraceFirst("embedding-finalized");
                    firstAsyncTraceActive = false;
                    return embedding;
                }
            }
            finally { pendingOutput = null; }
        }

        public void CancelAsync()
        {
            if (pendingSchedule != null)
            {
                // An unfinished ScheduleIterable must not be followed by a new schedule
                // on the same Worker. Screen reentry constructs a fresh Worker.
                interruptedSchedule = true;
                (pendingSchedule as IDisposable)?.Dispose();
                pendingSchedule = null;
            }
            pendingOutput = null; // Worker owns its output tensor.
            firstAsyncTraceActive = false;
        }

        private float[] DecodeEmbedding(Tensor<float> output)
        {
            var shape = output.shape;

            if(shape.rank != 2)
            {
                throw new ArgumentException("SFace 출력 Tensor의 rank는 2여야 합니다.");
            }

            if(shape[0] != 1 || shape[1] != EmbeddingSize)
            {
                    throw new ArgumentException(
                        $"SFace 출력 shape는 [1,{EmbeddingSize}]여야 합니다. " +
                        $"현재: [{shape[0]},{shape[1]}]"
                    );
            }
            float[] embedding = new float[EmbeddingSize];

            for(int i =0; i < EmbeddingSize; i++)
            {
                embedding[i] = output[i];
            }
            L2Normalize(embedding);

            return embedding;
        }

        public void L2Normalize(float[] embedding)
        {
            float sumSquares = 0f;
            for( int i = 0; i < embedding.Length; i++)
            {
                sumSquares += embedding[i] *embedding[i];
            }

            float norm = Mathf.Sqrt(sumSquares);

            if(norm != 0)
            {
                for(int i = 0; i < embedding.Length; i++)
                {
                    embedding[i] /= norm;
                }
            }            
        }
        public float[] ExtractEmbedding(Mat alignedFace)
        {
            PrepareInput(alignedFace);
            Run();

            return ReadEmbedding();
        }        

    }
}
