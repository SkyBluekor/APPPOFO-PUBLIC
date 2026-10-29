using System;
using AppPofo.Presentation.Diagnostics;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class PerfHitchRecorderTests
    {
        private static PerfHitchEvent Sample(int frame, double frameMs)
        {
            return new PerfHitchEvent(frame, frame * 10d, frameMs,
                PerfPipelinePhase.YuNetWaiting, PerfPipelinePhase.SFaceWaiting,
                80d, PerfGpuStage.SFace, true, 75d, -1d, 1.25d, 128L, 1);
        }

        [Test]
        public void RecordsOnlyHitchesOverOneHundredMilliseconds()
        {
            var buffer = new PerfHitchRecorder(4);
            Assert.That(buffer.Add(Sample(1, 99.999)), Is.False);
            Assert.That(buffer.Add(Sample(2, 100d)), Is.False);
            Assert.That(buffer.Add(Sample(3, 100.001)), Is.True);
            Assert.That(buffer.Add(Sample(4, 300d)), Is.True);
            Assert.That(buffer.Add(Sample(5, 500d)), Is.True);
            Assert.That(buffer.Add(Sample(6, 501d)), Is.True);
            Assert.That(buffer.Over100, Is.EqualTo(4));
            Assert.That(buffer.Over300, Is.EqualTo(2));
            Assert.That(buffer.Over500, Is.EqualTo(1));
            Assert.That(buffer.Count, Is.EqualTo(4));
            Assert.That(buffer.Get(0).Frame, Is.EqualTo(3));
            Assert.That(buffer.Get(3).CurrentPhase, Is.EqualTo(PerfPipelinePhase.SFaceWaiting));
            Assert.That(buffer.Get(3).MsSinceGpuSubmit, Is.EqualTo(75d));
            Assert.That(buffer.Get(3).Gen0Collections, Is.EqualTo(1));
        }

        [Test]
        public void BufferOverflowIsCountedWithoutDiscardingSummaryTotals()
        {
            var buffer = new PerfHitchRecorder(2);
            Assert.That(buffer.Add(Sample(1, 510)), Is.True);
            Assert.That(buffer.Add(Sample(2, 320)), Is.True);
            Assert.That(buffer.Add(Sample(3, 110)), Is.False);
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.Dropped, Is.EqualTo(1));
            Assert.That(buffer.Over100, Is.EqualTo(3));
            Assert.That(buffer.Over300, Is.EqualTo(2));
            Assert.That(buffer.Over500, Is.EqualTo(1));
            Assert.That(buffer.Get(1).Frame, Is.EqualTo(2));
            buffer.Clear();
            Assert.That(buffer.Count, Is.Zero);
            Assert.That(buffer.Dropped, Is.Zero);
            Assert.That(buffer.Over100, Is.Zero);
            Assert.That(buffer.Over300, Is.Zero);
            Assert.That(buffer.Over500, Is.Zero);
        }

        [Test]
        public void CsvKeepsLegacyColumnsAndAppendsTypedHitchFields()
        {
            string[] columns = PerformanceDiagnostics.CsvHeader.Split(',');
            Assert.That(columns.Length, Is.EqualTo(48));
            Assert.That(columns[0], Is.EqualTo("run_id"));
            Assert.That(columns[17], Is.EqualTo("metric"));
            Assert.That(columns[26], Is.EqualTo("max"));
            Assert.That(columns[27], Is.EqualTo("record_type"));
            Assert.That(columns[28], Is.EqualTo("frame_index"));
            Assert.That(columns[38], Is.EqualTo("gc_alloc_bytes"));
            Assert.That(columns[39], Is.EqualTo("gc_gen0_collections"));
            Assert.That(columns[40], Is.EqualTo("hitch_over_100"));
            Assert.That(columns[43], Is.EqualTo("hitch_dropped"));
            Assert.That(columns[44], Is.EqualTo("sface_layers_per_frame"));
            Assert.That(columns[45], Is.EqualTo("yunet_backend"));
            Assert.That(columns[46], Is.EqualTo("yunet_input"));
            Assert.That(columns[47], Is.EqualTo("yunet_cpu_layers_per_frame"));
        }

        [Test]
        public void InvalidSamplesAreIgnoredAndBoundsAreChecked()
        {
            var buffer = new PerfHitchRecorder(1);
            Assert.That(buffer.Add(Sample(1, double.NaN)), Is.False);
            Assert.That(buffer.Add(Sample(2, double.PositiveInfinity)), Is.False);
            Assert.That(buffer.Add(Sample(3, -3)), Is.False);
            Assert.That(buffer.Count, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Get(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PerfHitchRecorder(0));
        }
    }
}
