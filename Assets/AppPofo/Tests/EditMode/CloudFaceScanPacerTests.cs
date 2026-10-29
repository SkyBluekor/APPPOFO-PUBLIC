using System;
using System.Reflection;
using AppPofo.Presentation.FaceRecognition;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class CloudFaceScanPacerTests
    {
        private const BindingFlags Methods = BindingFlags.Instance |
            BindingFlags.NonPublic | BindingFlags.Public;

        private static Type PacerType => typeof(FaceRecognitionCamera).Assembly.GetType(
            "AppPofo.Presentation.FaceRecognition.CloudFaceScanPacer", true);

        private static object NewPacer() => Activator.CreateInstance(PacerType, true);

        private static void Begin(object pacer, double at) =>
            PacerType.GetMethod("BeginSession", Methods).Invoke(pacer, new object[] { at });

        private static bool Scan(object pacer, double at) =>
            (bool)PacerType.GetMethod("TryStartScan", Methods).Invoke(pacer, new object[] { at });

        [Test]
        public void Scans_AreAvailableEveryHalfSecond_WithoutBursting()
        {
            object pacer = NewPacer();
            Begin(pacer, 0d);
            Assert.That(Scan(pacer, 0d), Is.True);
            Assert.That(Scan(pacer, .499d), Is.False);
            Assert.That(Scan(pacer, .5d), Is.True);
            Assert.That(Scan(pacer, .5d), Is.False);
            Assert.That(Scan(pacer, 1d), Is.True);
        }

        [Test]
        public void InferenceSlowerThanInterval_DoesNotAddAnotherHalfSecondWait()
        {
            object pacer = NewPacer();
            Begin(pacer, 0d);
            Assert.That(Scan(pacer, 0d), Is.True);
            Assert.That(Scan(pacer, 1.3d), Is.True);
            Assert.That(Scan(pacer, 1.79d), Is.False);
            Assert.That(Scan(pacer, 1.8d), Is.True);
        }

        [Test]
        public void ReopeningCamera_DoesNotResetRollingMinuteQuota()
        {
            object pacer = NewPacer();
            for (int i = 0; i < 120; i++)
            {
                double now = i * .5d;
                Begin(pacer, now);
                Assert.That(Scan(pacer, now), Is.True);
            }
            Begin(pacer, 59.9d);
            Assert.That((bool)PacerType.GetProperty("IsBudgetBlocked", Methods).GetValue(pacer), Is.True);
            Assert.That(Scan(pacer, 59.9d), Is.False,
                "A new camera session must not bypass the 120-per-minute limit.");
            Assert.That(Scan(pacer, 59.9d), Is.False);
            Assert.That(Scan(pacer, 60d), Is.True,
                "A slot becomes available only after the oldest reservation expires.");
        }

        [Test]
        public void ReopeningCamera_DoesNotBypassMinimumSpacing()
        {
            object pacer = NewPacer();
            Begin(pacer, 10d);
            Assert.That(Scan(pacer, 10d), Is.True);
            Begin(pacer, 10.1d);
            Assert.That(Scan(pacer, 10.1d), Is.False);
            Assert.That(Scan(pacer, 10.5d), Is.True);
        }

        [Test]
        public void ReservationWithoutHttp_StillConsumesHalfSecond()
        {
            object pacer = NewPacer();
            Begin(pacer, 0d);
            Assert.That(Scan(pacer, 0d), Is.True);
            // The camera reserves BEFORE detection. NoFace never refunds this.
            Assert.That(Scan(pacer, .499d), Is.False);
            Assert.That(Scan(pacer, .5d), Is.True);
        }
    }
}
