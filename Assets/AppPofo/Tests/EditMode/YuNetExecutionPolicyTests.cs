using System;
using AppPofo.Presentation.FaceRegistration.Detection;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class YuNetExecutionPolicyTests
    {
        [Test]
        public void RegistrationCpu16DoesNotDependOnRecognitionDiagnosticState()
        {
            var policy = YuNetExecutionPolicy.Registration;
            Assert.That(policy.CpuInput, Is.True);
            Assert.That(policy.CpuBackend, Is.True);
            Assert.That(policy.CpuLayersPerFrame, Is.EqualTo(16));
            Assert.That(policy.ScheduleLayersPerFrame, Is.EqualTo(16));
            Assert.That(policy.ToString(), Is.EqualTo("input=CPU backend=CPU layers=16"));
        }

        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        public void SupportedCpuBudgetsAreBounded(int layers)
        {
            var policy = new YuNetExecutionPolicy(true, true, layers);
            Assert.That(policy.ScheduleLayersPerFrame, Is.EqualTo(layers));
        }


        [TestCase("samsung SM-T500")]
        [TestCase("SM-T500")]
        [TestCase("Samsung Electronics SM-T500")]
        [TestCase("  samsung SM-T500  ")]
        public void RecognitionOnTargetDefaultsToVerifiedCpu16AndCpu4(string device)
        {
            var recognition = RecognitionInferencePolicy.Resolve(device);
            Assert.That(recognition.YuNet.CpuInput, Is.True);
            Assert.That(recognition.YuNet.CpuBackend, Is.True);
            Assert.That(recognition.YuNet.ScheduleLayersPerFrame, Is.EqualTo(16));
            Assert.That(recognition.CpuSFace, Is.True);
            Assert.That(recognition.SFaceLayersPerFrame, Is.EqualTo(4));
            Assert.That(recognition.Source, Is.EqualTo("SM-T500-CPU16-CPU4"));
            Assert.That(recognition.ToString(), Does.Contain("SFace=CPU layers=4"));
            Assert.That(YuNetExecutionPolicy.Registration.ToString(),
                Is.EqualTo("input=CPU backend=CPU layers=16"));
        }

        [TestCase("samsung SM-T510")]
        [TestCase("samsung SM-T505")]
        [TestCase("samsung SM-T5000")]
        [TestCase("SM-T500X")]
        [TestCase("SM-T500 Pro")]
        [TestCase("Other Android tablet")]
        [TestCase("")]
        [TestCase(null)]
        public void RecognitionOnOtherDevicesRetainsGpuBaseline(string device)
        {
            var policy = RecognitionInferencePolicy.Resolve(device);
            Assert.That(policy.YuNet.CpuInput, Is.False);
            Assert.That(policy.YuNet.CpuBackend, Is.False);
            Assert.That(policy.YuNet.ScheduleLayersPerFrame, Is.EqualTo(4));
            Assert.That(policy.CpuSFace, Is.False);
            Assert.That(policy.SFaceLayersPerFrame, Is.Zero);
            Assert.That(policy.Source, Is.EqualTo("GPU-baseline"));
        }

        [Test]
        public void DeviceOverrideCanReproduceGpuBaselineOrForceCpu16()
        {
            var gpu = RecognitionInferencePolicy.Resolve("samsung SM-T500", "GPU", "GPU");
            Assert.That(gpu.YuNet.CpuInput, Is.False);
            Assert.That(gpu.YuNet.CpuBackend, Is.False);
            Assert.That(gpu.CpuSFace, Is.False);
            Assert.That(gpu.SFaceLayersPerFrame, Is.Zero);
            Assert.That(gpu.Source, Is.EqualTo("Developer-override"));
            var cpu = RecognitionInferencePolicy.Resolve("other device", "CPU16", "CPU4");
            Assert.That(cpu.YuNet.CpuInput, Is.True);
            Assert.That(cpu.YuNet.CpuBackend, Is.True);
            Assert.That(cpu.YuNet.ScheduleLayersPerFrame, Is.EqualTo(16));
            Assert.That(cpu.CpuSFace, Is.True);
            Assert.That(cpu.SFaceLayersPerFrame, Is.EqualTo(4));
            Assert.That(cpu.Source, Is.EqualTo("Developer-override"));
        }

        [Test]
        public void PartialDeveloperOverridesKeepTheOtherDeviceDefault()
        {
            var yunetOnly = RecognitionInferencePolicy.Resolve("samsung SM-T500", "GPU");
            Assert.That(yunetOnly.YuNet.CpuBackend, Is.False);
            Assert.That(yunetOnly.CpuSFace, Is.True);
            Assert.That(yunetOnly.SFaceLayersPerFrame, Is.EqualTo(4));

            var sfaceOnly = RecognitionInferencePolicy.Resolve("samsung SM-T500",
                sfaceOverride: "GPU");
            Assert.That(sfaceOnly.YuNet.CpuBackend, Is.True);
            Assert.That(sfaceOnly.CpuSFace, Is.False);
            Assert.That(sfaceOnly.SFaceLayersPerFrame, Is.Zero);

            var explicitCpuOnOtherDevice = RecognitionInferencePolicy.Resolve(
                "SM-T510", sfaceOverride: "CPU4");
            Assert.That(explicitCpuOnOtherDevice.YuNet.CpuBackend, Is.False);
            Assert.That(explicitCpuOnOtherDevice.CpuSFace, Is.True);
            Assert.That(explicitCpuOnOtherDevice.SFaceLayersPerFrame, Is.EqualTo(4));
        }

        [Test]
        public void SFaceGpu4CanBeComparedWithoutChangingYuNet()
        {
            var policy = RecognitionInferencePolicy.Resolve(
                "SM-T500", sfaceOverride: "GPU4");
            Assert.That(policy.YuNet.CpuBackend, Is.True);
            Assert.That(policy.CpuSFace, Is.False);
            Assert.That(policy.SFaceLayersPerFrame, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitPerfBenchmarkOverridesTabletDefaultUnlessDevFlagIsSet()
        {
            var gpu = new YuNetExecutionPolicy(false, false, 0);
            var benchmark = RecognitionInferencePolicy.Resolve("samsung SM-T500",
                benchmarkConfigured: true, benchmarkYuNet: gpu);
            Assert.That(benchmark.YuNet.CpuBackend, Is.False);
            Assert.That(benchmark.CpuSFace, Is.False);
            Assert.That(benchmark.Source, Is.EqualTo("PERF-benchmark"));

            // PERF CPU experiments may intentionally choose unsliced execution.
            // Device defaults must not overwrite an explicit benchmark's layer count.
            var cpuBenchmark = RecognitionInferencePolicy.Resolve("samsung SM-T500",
                benchmarkConfigured: true, benchmarkYuNet: YuNetExecutionPolicy.Registration,
                benchmarkCpuSFace: true, benchmarkSFaceLayers: 0);
            Assert.That(cpuBenchmark.CpuSFace, Is.True);
            Assert.That(cpuBenchmark.SFaceLayersPerFrame, Is.Zero);
            Assert.That(cpuBenchmark.Source, Is.EqualTo("PERF-benchmark"));

            var cpu = RecognitionInferencePolicy.Resolve("samsung SM-T500",
                yunetOverride: "CPU16",
                benchmarkConfigured: true, benchmarkYuNet: gpu);
            Assert.That(cpu.YuNet.CpuBackend, Is.True);
            Assert.That(cpu.CpuSFace, Is.False,
                "An explicit YuNet override must not change benchmark SFace.");
            Assert.That(cpu.Source, Is.EqualTo("Developer-override"));

            var overriddenSFace = RecognitionInferencePolicy.Resolve("SM-T500",
                sfaceOverride: "CPU4", benchmarkConfigured: true, benchmarkYuNet: gpu);
            Assert.That(overriddenSFace.YuNet.CpuBackend, Is.False);
            Assert.That(overriddenSFace.CpuSFace, Is.True);
            Assert.That(overriddenSFace.SFaceLayersPerFrame, Is.EqualTo(4));
            Assert.That(overriddenSFace.Source, Is.EqualTo("Developer-override"));
        }

        [Test]
        public void InvalidDeveloperRecognitionModesFailClosed()
        {
            Assert.Throws<ArgumentException>(() =>
                RecognitionInferencePolicy.Resolve("SM-T500", "CPU8"));
            Assert.Throws<ArgumentException>(() =>
                RecognitionInferencePolicy.Resolve("SM-T500", sfaceOverride: "CPU"));
            Assert.Throws<ArgumentException>(() =>
                RecognitionInferencePolicy.Resolve("SM-T500", sfaceOverride: "GPU16"));

            // FaceRecognitionCamera catches invalid options and re-resolves with
            // neither option. The fallback must retain the safe CPU defaults.
            var safeFallback = RecognitionInferencePolicy.Resolve("SM-T500");
            Assert.That(safeFallback.YuNet.CpuBackend, Is.True);
            Assert.That(safeFallback.CpuSFace, Is.True);
            Assert.That(safeFallback.SFaceLayersPerFrame, Is.EqualTo(4));
        }

        [Test]
        public void RejectsInvalidCombinationsAndKeepsGpuSlicingAtFour()
        {
            Assert.Throws<ArgumentException>(() => new YuNetExecutionPolicy(false, true, 16));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new YuNetExecutionPolicy(true, true, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new YuNetExecutionPolicy(true, true, 32));
            Assert.Throws<ArgumentException>(() => new YuNetExecutionPolicy(false, false, 16));
            var gpu = new YuNetExecutionPolicy(false, false, 0);
            Assert.That(gpu.CpuInput, Is.False);
            Assert.That(gpu.CpuBackend, Is.False);
            Assert.That(gpu.ScheduleLayersPerFrame, Is.EqualTo(4));
        }
    }
}
