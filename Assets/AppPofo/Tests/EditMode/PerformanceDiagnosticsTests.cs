using System;
using System.Collections;
using AppPofo.Presentation.FaceRegistration.Embedding;
using AppPofo.Presentation.Diagnostics;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class PerformanceDiagnosticsTests
    {
        [Test]
        public void PercentilesUseNearestRankAndTotalsCoverTheWholeWindow()
        {
            var window = new PerfWindow(20);
            for (int i = 20; i >= 1; i--) window.Add(i);
            window.Sort();
            Assert.That(window.Count, Is.EqualTo(20));
            Assert.That(window.Sum, Is.EqualTo(210));
            Assert.That(window.Mean, Is.EqualTo(10.5));
            Assert.That(window.Percentile(.5), Is.EqualTo(10));
            Assert.That(window.Percentile(.95), Is.EqualTo(19));
            Assert.That(window.Maximum, Is.EqualTo(20));
        }

        [Test]
        public void OverflowIsExplicitAndDoesNotLoseExactTotals()
        {
            var window = new PerfWindow(2);
            window.Add(1); window.Add(3); window.Add(100);
            window.Sort();
            Assert.That(window.Count, Is.EqualTo(3));
            Assert.That(window.Retained, Is.EqualTo(2));
            Assert.That(window.Dropped, Is.EqualTo(1));
            Assert.That(window.Sum, Is.EqualTo(104));
            Assert.That(window.Maximum, Is.EqualTo(100));
            Assert.That(window.Percentile(.95), Is.EqualTo(3));
            window.Clear();
            window.Sort();
            Assert.That(window.Count, Is.Zero);
            Assert.That(window.Sum, Is.Zero);
            Assert.That(double.IsNaN(window.Percentile(.5)), Is.True);
        }

        [Test]
        public void InvalidSamplesAndEmptyWindowsNeverBecomeZeroMeasurements()
        {
            var window = new PerfWindow();
            window.Add(double.NaN); window.Add(double.PositiveInfinity); window.Add(-1);
            window.Sort();
            Assert.That(window.Count, Is.Zero);
            Assert.That(double.IsNaN(window.Mean), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => new PerfWindow(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => window.Percentile(0));
        }

        [TestCase("Off", PerfScenario.Off)]
        [TestCase("CameraOnly", PerfScenario.CameraOnly)]
        [TestCase("Preprocessing", PerfScenario.Preprocessing)]
        [TestCase("YuNet", PerfScenario.YuNet)]
        [TestCase("SFace", PerfScenario.SFace)]
        [TestCase("Full", PerfScenario.Full)]
        public void OnlyNamedScenariosAreAccepted(string name, PerfScenario expected)
        {
            Assert.That(PerformanceDiagnostics.TryParseScenario(name, out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("4")]
        [TestCase("999")]
        [TestCase("Full,CameraOnly")]
        [TestCase("full")]
        public void MalformedSelectorsRemainOff(string name)
        {
            Assert.That(PerformanceDiagnostics.TryParseScenario(name, out var parsed), Is.False);
            Assert.That(parsed, Is.EqualTo(PerfScenario.Off));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("GPUCompute", false)]
        [TestCase("CPU", true)]
        public void SFaceBackendSelectorAcceptsOnlyExplicitSupportedBackends(string requested, bool cpuExpected)
        {
            Assert.That(PerformanceDiagnostics.TryParseSFaceBackend(requested, out bool cpu), Is.True);
            Assert.That(cpu, Is.EqualTo(cpuExpected));
        }

        [TestCase("cpu")]
        [TestCase("GPU")]
        [TestCase("GPUPixel")]
        [TestCase("CPU,GPUCompute")]
        [TestCase(" CPU ")]
        public void InvalidSFaceBackendSelectorIsRejected(string requested)
        {
            Assert.That(PerformanceDiagnostics.TryParseSFaceBackend(requested, out bool cpu), Is.False);
            Assert.That(cpu, Is.False);
        }

        [TestCase(null, false, true)]
        [TestCase("", false, true)]
        [TestCase("GPU", false, true)]
        [TestCase("CPU", true, true)]
        [TestCase("cpu", false, false)]
        [TestCase("GPUCompute", false, false)]
        [TestCase("CPU,GPU", false, false)]
        public void YuNetInputSelectorIsExplicitAndDefaultsToGpu(
            string value, bool expectedCpu, bool valid)
        {
            Assert.That(PerformanceDiagnostics.TryParseYuNetInput(value, out bool cpu),
                Is.EqualTo(valid));
            Assert.That(cpu, Is.EqualTo(expectedCpu));
        }

        [TestCase(null, false, true)]
        [TestCase("", false, true)]
        [TestCase("GPUCompute", false, true)]
        [TestCase("CPU", true, true)]
        [TestCase("cpu", false, false)]
        [TestCase("GPU", false, false)]
        [TestCase("GPUPixel", false, false)]
        [TestCase("CPU,GPUCompute", false, false)]
        public void YuNetBackendSelectorIsExplicitAndDefaultsToGpu(
            string value, bool expectedCpu, bool valid)
        {
            Assert.That(PerformanceDiagnostics.TryParseYuNetBackend(value, out bool cpu),
                Is.EqualTo(valid));
            Assert.That(cpu, Is.EqualTo(expectedCpu));
        }

        [TestCase(null, 4, true)]
        [TestCase("", 4, true)]
        [TestCase("4", 4, true)]
        [TestCase("8", 8, true)]
        [TestCase("16", 16, true)]
        [TestCase("0", 4, false)]
        [TestCase("1", 4, false)]
        [TestCase("2", 4, false)]
        [TestCase("32", 4, false)]
        [TestCase("64", 4, false)]
        [TestCase("-1", 4, false)]
        [TestCase("08", 4, false)]
        [TestCase("4,8", 4, false)]
        [TestCase(" 8 ", 4, false)]
        [TestCase("CPU", 4, false)]
        public void CpuYuNetLayerBudgetAcceptsOnlyFourEightSixteen(
            string requested, int expected, bool valid)
        {
            Assert.That(PerformanceDiagnostics.TryParseYuNetCpuLayersPerFrame(
                requested, out int layers), Is.EqualTo(valid));
            Assert.That(layers, Is.EqualTo(expected));
        }

        [Test]
        public void CpuYuNetBudgetRequiresCpuBackendAndValidSelection()
        {
            foreach (string requested in new[] { null, "", "4" })
            {
                Assert.That(PerformanceDiagnostics.TryResolveYuNetCpuLayerBudget(
                    requested, false, out int layers), Is.True);
                Assert.That(layers, Is.Zero);
            }
            foreach (string requested in new[] { "8", "16", "0", "32", " 4 " })
            {
                Assert.That(PerformanceDiagnostics.TryResolveYuNetCpuLayerBudget(
                    requested, false, out int layers), Is.False);
                Assert.That(layers, Is.Zero);
            }
            foreach (string requested in new[] { null, "", "4", "8", "16" })
            {
                Assert.That(PerformanceDiagnostics.TryResolveYuNetCpuLayerBudget(
                    requested, true, out int layers), Is.True);
                Assert.That(layers, Is.EqualTo(
                    string.IsNullOrEmpty(requested) ? 4 : int.Parse(requested)));
            }
        }

        [Test]
        public void YuNetLayerBudgetAffectsOnlyCpuAndGpuColdAlwaysUsesFour()
        {
            Assert.That(PerformanceDiagnostics.SelectYuNetScheduleLayers(false, 0), Is.EqualTo(4));
            Assert.That(PerformanceDiagnostics.SelectYuNetScheduleLayers(false, 16), Is.EqualTo(4));
            foreach (int choice in new[] { 4, 8, 16 })
                Assert.That(PerformanceDiagnostics.SelectYuNetScheduleLayers(true, choice),
                    Is.EqualTo(choice));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PerformanceDiagnostics.SelectYuNetScheduleLayers(true, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PerformanceDiagnostics.SelectYuNetScheduleLayers(true, 32));
        }

        [Test]
        public void CpuYuNetBatchesBoundActualLayersPerFrame()
        {
            foreach (int budget in new[] { 4, 8, 16 })
            {
                // 89 yields plus terminal MoveNext(false), for 90 scheduler advances.
                IEnumerator schedule = FakeModelLayers(89);
                int frames = 0;
                bool finished;
                do
                {
                    finished = SFaceLayerBudget.Advance(schedule,
                        PerformanceDiagnostics.SelectYuNetScheduleLayers(true, budget));
                    frames++;
                    Assert.That(frames, Is.LessThanOrEqualTo(90));
                } while (!finished);

                Assert.That(frames, Is.EqualTo((int)Math.Ceiling(90d / budget)));
            }
        }

        [Test]
        public void YuNetCsvExtensionKeepsAllFortyFiveLegacyColumns()
        {
            string[] columns = PerformanceDiagnostics.CsvHeader.Split(',');
            string[] expectedLegacy = (
                "run_id,start_utc,environment,device_model,unity,graphics_api,scenario," +
                "sface_backend,window,reason,seconds,fps,camera_width,camera_height," +
                "camera_updates,pipeline_passes,face_inputs,metric,unit,count,retained," +
                "dropped,sum,mean,p50,p95,max,record_type,frame_index,observed_ms," +
                "previous_phase,current_phase,phase_age_ms,gpu_stage,gpu_pending," +
                "ms_since_gpu_submit,ms_since_gpu_complete,main_thread_measured_ms," +
                "gc_alloc_bytes,gc_gen0_collections,hitch_over_100,hitch_over_300," +
                "hitch_over_500,hitch_dropped,sface_layers_per_frame").Split(',');
            Assert.That(expectedLegacy.Length, Is.EqualTo(45));
            Assert.That(columns.Length, Is.EqualTo(48));
            for (int i = 0; i < expectedLegacy.Length; i++)
                Assert.That(columns[i], Is.EqualTo(expectedLegacy[i]),
                    "Legacy CSV column " + (i + 1) + " must not move.");
            Assert.That(columns[45], Is.EqualTo("yunet_backend"));
            Assert.That(columns[46], Is.EqualTo("yunet_input"));
            Assert.That(columns[47], Is.EqualTo("yunet_cpu_layers_per_frame"));
        }

        [Test]
        public void CsvYuNetSuffixIsSharedBySummaryAndHitchAndIdentifiesAbChoices()
        {
            foreach (int layers in new[] { 4, 8, 16 })
            {
                string fields = PerformanceDiagnostics.CsvYuNetMetadataFields(true, true, layers);
                Assert.That(fields, Is.EqualTo("CPU,CPU," + layers));
                // Both emitted record types append this exact three-field suffix
                // after the original sface_layers_per_frame column.
                foreach (string recordType in new[] { "summary", "hitch" })
                {
                    var legacy = new string[45];
                    legacy[27] = recordType;
                    legacy[44] = "0";
                    string[] extended = (string.Join(",", legacy) + "," + fields).Split(',');
                    Assert.That(extended.Length, Is.EqualTo(48));
                    Assert.That(extended[27], Is.EqualTo(recordType));
                    Assert.That(extended[44], Is.EqualTo("0"));
                    Assert.That(extended[45], Is.EqualTo("CPU"));
                    Assert.That(extended[46], Is.EqualTo("CPU"));
                    Assert.That(extended[47], Is.EqualTo(layers.ToString()));
                }
            }
            Assert.That(PerformanceDiagnostics.CsvYuNetMetadataFields(false, false, 0),
                Is.EqualTo("GPUCompute,GPU,0"));
            Assert.That(PerformanceDiagnostics.CsvYuNetMetadataFields(false, true, 0),
                Is.EqualTo("GPUCompute,CPU,0"));
            Assert.Throws<ArgumentException>(
                () => PerformanceDiagnostics.CsvYuNetMetadataFields(true, false, 4));
            Assert.Throws<ArgumentException>(
                () => PerformanceDiagnostics.CsvYuNetMetadataFields(true, true, 0));
            Assert.Throws<ArgumentException>(
                () => PerformanceDiagnostics.CsvYuNetMetadataFields(false, false, 8));
        }

        [Test]
        public void BenchmarkUsesOneFixedNinetySecondDuration()
        {
            Assert.That(PerformanceDiagnostics.WarmupSeconds, Is.EqualTo(15d));
            Assert.That(PerformanceDiagnostics.MeasurementSeconds, Is.EqualTo(75d));
            Assert.That(PerformanceDiagnostics.WarmupSeconds + PerformanceDiagnostics.MeasurementSeconds,
                Is.EqualTo(90d));
            Assert.That(PerformanceDiagnostics.FlushSeconds, Is.EqualTo(10d));
        }

        [Test]
        public void AsyncMetricsAppendWithoutChangingExistingMetricIdentifiers()
        {
            Assert.That((int)PerfMetric.FrameMs, Is.EqualTo(0));
            Assert.That((int)PerfMetric.CsvWriteMs, Is.EqualTo(20));
            Assert.That((int)PerfMetric.YuNetWaitWallMs, Is.EqualTo(21));
            Assert.That((int)PerfMetric.SFaceWaitWallMs, Is.EqualTo(22));
            Assert.That((int)PerfMetric.YuNetFinalizeMs, Is.EqualTo(23));
            Assert.That((int)PerfMetric.SFaceFinalizeMs, Is.EqualTo(24));
            Assert.That((int)PerfMetric.PipelineWallMs, Is.EqualTo(25));
            Assert.That((int)PerfMetric.SkippedFrames, Is.EqualTo(26));
            Assert.That((int)PerfMetric.StaleResults, Is.EqualTo(27));
            Assert.That((int)PerfMetric.PipelineErrors, Is.EqualTo(28));
            Assert.That((int)PerfMetric.SFaceScheduleBatches, Is.EqualTo(29));
            Assert.That((int)PerfMetric.SFaceScheduleWallMs, Is.EqualTo(30));
            Assert.That((int)PerfMetric.YuNetScheduleBatches, Is.EqualTo(31));
            Assert.That((int)PerfMetric.YuNetScheduleWallMs, Is.EqualTo(32));
        }

        [TestCase(null, 0)]
        [TestCase("", 0)]
        [TestCase("0", 0)]
        [TestCase("4", 4)]
        [TestCase("8", 8)]
        [TestCase("16", 16)]
        [TestCase("32", 32)]
        public void SFaceSlicingAcceptsExplicitBudgets(string requested, int expected)
        {
            Assert.That(PerformanceDiagnostics.TryParseSFaceLayersPerFrame(requested, out int layers), Is.True);
            Assert.That(layers, Is.EqualTo(expected));
        }

        [TestCase("-1")]
        [TestCase("1")]
        [TestCase("3")]
        [TestCase("64")]
        [TestCase("8,16")]
        [TestCase(" 8 ")]
        [TestCase("SFace")]
        public void SFaceSlicingRejectsInvalidOrAmbiguousBudgets(string requested)
        {
            Assert.That(PerformanceDiagnostics.TryParseSFaceLayersPerFrame(requested, out int layers), Is.False);
            Assert.That(layers, Is.Zero);
        }

        private static IEnumerator FakeModelLayers(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        [Test]
        public void SFaceScheduleIterableIsBoundedByConfiguredLayersPerFrame()
        {
            var schedule = FakeModelLayers(9);
            Assert.That(SFaceLayerBudget.Advance(schedule, 4), Is.False);
            Assert.That(SFaceLayerBudget.Advance(schedule, 4), Is.False);
            Assert.That(SFaceLayerBudget.Advance(schedule, 4), Is.True);
            Assert.That(SFaceLayerBudget.Advance(FakeModelLayers(0), 4), Is.True);
            Assert.That(SFaceLayerBudget.Advance(FakeModelLayers(4), 4), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => SFaceLayerBudget.Advance(FakeModelLayers(1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SFaceLayerBudget.Advance(FakeModelLayers(1), 65));
            Assert.Throws<ArgumentNullException>(() => SFaceLayerBudget.Advance(null, 8));
        }

        [Test]
        public void AsyncCountersRemainIndependentFromCrossFrameWallTime()
        {
            var count = new PerfWindow(4);
            var elapsed = new PerfWindow(4);
            count.Add(1);
            count.Add(1);
            elapsed.Add(210);
            Assert.That(count.Sum, Is.EqualTo(2));
            Assert.That(elapsed.Sum, Is.EqualTo(210));
            Assert.That(elapsed.Mean, Is.EqualTo(210));
            Assert.That(count.Count, Is.EqualTo(2));
        }

        [Test]
        public void EditModeWithoutRuntimeSessionDoesNotBypassExistingPipeline()
        {
            Assert.That(PerformanceDiagnostics.Active, Is.False);
            Assert.That(PerformanceDiagnostics.UseCpuSFace, Is.False);
            Assert.That(PerformanceDiagnostics.UseCpuYuNetBackend, Is.False);
            Assert.That(PerformanceDiagnostics.UseCpuYuNetInput, Is.False);
            Assert.That(PerformanceDiagnostics.YuNetCpuLayersPerFrame, Is.Zero);
            Assert.That(PerformanceDiagnostics.SFaceBackendName, Is.EqualTo("GPUCompute"));
            Assert.That(PerformanceDiagnostics.SkipMatching, Is.False);
            Assert.That(PerformanceDiagnostics.SkipPresentation, Is.False);
            foreach (PerfScenario value in Enum.GetValues(typeof(PerfScenario)))
                Assert.That(PerformanceDiagnostics.IsScenario(value), Is.False);
            using (PerformanceDiagnostics.Measure(PerfMetric.DetectionMs)) { }
        }
    }
}
