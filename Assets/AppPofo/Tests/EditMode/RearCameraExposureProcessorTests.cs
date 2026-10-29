using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.FaceRegistration.Quality;
using NUnit.Framework;
using UnityEngine;

namespace AppPofo.Tests.EditMode
{
    public sealed class RearCameraExposureProcessorTests
    {
        private static Color32[] Solid(int width, int height, byte value)
        {
            var result = new Color32[width * height];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Color32(value, value, value, 255);
            return result;
        }

        [Test]
        public void CenterStatisticsDetectBrightnessAndClipping()
        {
            var normal = Solid(32, 24, 160);
            var a = RearCameraExposureProcessor.MeasureCenter(normal, 32, 24);
            Assert.That(a.Mean, Is.EqualTo(160).Within(.01f));
            Assert.That(a.P95, Is.EqualTo(160));
            Assert.That(a.BrightRatio, Is.Zero);
            Assert.That(a.ClipRatio, Is.Zero);

            var blown = Solid(32, 24, 255);
            var b = RearCameraExposureProcessor.MeasureCenter(blown, 32, 24);
            Assert.That(b.Mean, Is.EqualTo(255).Within(.01f));
            Assert.That(b.P95, Is.EqualTo(255));
            Assert.That(b.ClipRatio, Is.EqualTo(1f).Within(.001f));
        }

        [Test]
        public void OverexposedButNotClippedRearCanBeAttenuatedWithoutModifyingOriginal()
        {
            Color32[] raw = Solid(32, 24, 220);
            var corrected = RearCameraExposureProcessor.Process(raw, 32, 24, true, true);
            Assert.That(corrected.Corrected, Is.True);
            Assert.That(corrected.AppliedGain, Is.InRange(.80f, .95f));
            Assert.That(raw[0].r, Is.EqualTo(220), "The raw preview must stay untouched.");
            Assert.That(corrected.Pixels, Is.Not.SameAs(raw));
            Assert.That(corrected.Pixels[0].r, Is.EqualTo(176));
            Assert.That(corrected.Pixels[0].a, Is.EqualTo(255));
        }

        [Test]
        public void DoesNotFabricateDetailsInClippedOrNormalFrames()
        {
            foreach (byte level in new byte[] { 100, 175, 255 })
            {
                Color32[] raw = Solid(32, 24, level);
                var processed = RearCameraExposureProcessor.Process(raw, 32, 24, true, true);
                Assert.That(processed.Corrected, Is.False, "Level: " + level);
                Assert.That(processed.Pixels, Is.SameAs(raw));
            }
        }

        [Test]
        public void FrontLensAndDisabledRearCorrectionNeverChangeCameraPixels()
        {
            var raw = Solid(32, 24, 220);
            var front = RearCameraExposureProcessor.Process(raw, 32, 24, false, true);
            var rearOff = RearCameraExposureProcessor.Process(raw, 32, 24, true, false);
            Assert.That(front.Pixels, Is.SameAs(raw));
            Assert.That(front.Corrected, Is.False);
            Assert.That(rearOff.Pixels, Is.SameAs(raw));
            Assert.That(rearOff.Corrected, Is.False);
            Assert.That(rearOff.Raw.Mean, Is.EqualTo(220).Within(.01f));
        }

        [Test]
        public void AdaptiveRearAttenuatesAndSmoothlyReturnsToNeutral()
        {
            var state = new RearCameraExposureProcessor.AdaptiveState();
            Color32[] bright = Solid(32, 24, 220);
            var first = RearCameraExposureProcessor.Process(
                bright, 32, 24, true, true, state);
            Assert.That(first.Corrected, Is.True);
            Assert.That(first.AppliedGain, Is.EqualTo(.80f).Within(.001f));
            Assert.That(bright[0].r, Is.EqualTo(220), "Original raw preview must not change.");

            Color32[] normal = Solid(32, 24, 160);
            var recovering = RearCameraExposureProcessor.Process(
                normal, 32, 24, true, true, state);
            Assert.That(recovering.AppliedGain, Is.GreaterThan(.80f));
            Assert.That(recovering.AppliedGain, Is.LessThan(1f),
                "Exposure gain must not jump from .80 to 1.0 in one frame.");
            for (int i = 0; i < 30; i++)
                recovering = RearCameraExposureProcessor.Process(
                    normal, 32, 24, true, true, state);
            Assert.That(recovering.AppliedGain, Is.EqualTo(1f).Within(.01f));
            Assert.That(recovering.Pixels[0].r, Is.EqualTo(160));
        }

        [Test]
        public void AdaptiveRearMayMildlyAmplifyRecoverableDarkPixels()
        {
            var state = new RearCameraExposureProcessor.AdaptiveState();
            Color32[] dark = Solid(32, 24, 50);
            var result = RearCameraExposureProcessor.Process(
                dark, 32, 24, true, true, state);
            Assert.That(result.Corrected, Is.True);
            Assert.That(result.AppliedGain, Is.EqualTo(1.10f).Within(.001f));
            Assert.That(result.Pixels[0].r, Is.EqualTo(55));
            Assert.That(dark[0].r, Is.EqualTo(50));
            Assert.That(result.Pixels[0].a, Is.EqualTo(255));

            var beyondRecovery = RearCameraExposureProcessor.Process(
                Solid(32, 24, 20), 32, 24, true, true, state);
            Assert.That(beyondRecovery.Corrected, Is.False);
            Assert.That(beyondRecovery.AppliedGain, Is.EqualTo(1f));
        }

        [Test]
        public void AdaptiveRearUsesPriorFaceRoiButLogsUnmodifiedCenterStats()
        {
            const int width = 64, height = 64;
            var raw = Solid(width, height, 140);
            // The ROI is top-left but Color32[] is bottom-left.
            for (int y = 32; y < 64; y++)
                for (int x = 0; x < 32; x++)
                    raw[y * width + x] = new Color32(220, 220, 220, 255);

            var state = new RearCameraExposureProcessor.AdaptiveState();
            var baseline = RearCameraExposureProcessor.Process(
                raw, width, height, true, true, state);
            Assert.That(baseline.Corrected, Is.False);

            var face = RearCameraExposureProcessor.Process(raw, width, height,
                true, true, state, new RectInt(0, 0, 32, 32));
            Assert.That(face.Corrected, Is.True);
            Assert.That(face.AppliedGain, Is.LessThan(1f));
            Assert.That(face.Raw.Mean, Is.LessThan(220f));
            Assert.That(raw[0].r, Is.EqualTo(140));
            Assert.That(raw[(height - 1) * width].r, Is.EqualTo(220));
            Assert.That(raw[width * height - 1].r, Is.EqualTo(140));
        }

        [Test]
        public void AdaptiveRearCannotHideClippingAndFrontOffResetState()
        {
            var state = new RearCameraExposureProcessor.AdaptiveState();
            RearCameraExposureProcessor.Process(
                Solid(32, 24, 220), 32, 24, true, true, state);
            var clipped = RearCameraExposureProcessor.Process(
                Solid(32, 24, 255), 32, 24, true, true, state);
            // Attenuation may still help the remaining pixels. Raw clipping
            // must stay visible and never be treated as recovered detail.
            Assert.That(clipped.Corrected, Is.True);
            Assert.That(clipped.ControlRoiDamaged, Is.True);
            Assert.That(clipped.Raw.ClipRatio, Is.EqualTo(1f));
            Assert.That(clipped.Pixels[0].r, Is.LessThan(255));
            Assert.That(state.Gain, Is.LessThan(1f));

            RearCameraExposureProcessor.Process(
                Solid(32, 24, 220), 32, 24, true, true, state);
            var front = RearCameraExposureProcessor.Process(
                Solid(32, 24, 220), 32, 24, false, true, state);
            Assert.That(front.Corrected, Is.False);
            Assert.That(state.Gain, Is.EqualTo(1f));

            RearCameraExposureProcessor.Process(
                Solid(32, 24, 220), 32, 24, true, true, state);
            var disabled = RearCameraExposureProcessor.Process(
                Solid(32, 24, 220), 32, 24, true, false, state);
            Assert.That(disabled.Corrected, Is.False);
            Assert.That(state.Gain, Is.EqualTo(1f));
        }

        [Test]
        public void TopLeftRoiReadsUpperPixelsFromBottomLeftColor32Buffer()
        {
            const int w = 48, h = 48;
            Color32[] pixels = Solid(w, h, 40);
            for (int y = 36; y < h; y++)
                for (int x = 4; x < 28; x++)
                    pixels[y * w + x] = new Color32(220, 220, 220, 255);

            var top = RearCameraExposureProcessor.MeasureTopLeftRegion(
                pixels, w, h, new RectInt(4, 0, 24, 12), 1);
            var bottom = RearCameraExposureProcessor.MeasureTopLeftRegion(
                pixels, w, h, new RectInt(4, 36, 24, 12), 1);
            Assert.That(top.Mean, Is.EqualTo(220f).Within(.01f));
            Assert.That(top.BrightRatio, Is.EqualTo(1f));
            Assert.That(bottom.Mean, Is.EqualTo(40f).Within(.01f));
            Assert.That(bottom.BrightRatio, Is.Zero);
        }

        [Test]
        public void AdaptiveRearTriggersOnHighlightTailEvenWithNormalMean()
        {
            const int w = 64, h = 64;
            Color32[] pixels = Solid(w, h, 150);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (((x / 4) + (y / 4)) % 10 < 4)
                        pixels[y * w + x] = new Color32(220, 220, 220, 255);
            var state = new RearCameraExposureProcessor.AdaptiveState();
            var result = RearCameraExposureProcessor.Process(pixels, w, h,
                true, true, state, new RectInt(0, 0, w, h));
            Assert.That(result.Observed.Mean, Is.LessThan(180f));
            Assert.That(result.Observed.BrightRatio, Is.GreaterThan(.25f));
            Assert.That(result.Reason, Is.EqualTo("HighlightTail"));
            Assert.That(result.Corrected, Is.True);
            Assert.That(result.AppliedGain, Is.LessThan(1f));
            Assert.That(pixels[0].r, Is.EqualTo(220), "Raw pixels cannot be mutated.");
            Assert.That(result.Pixels[0].r, Is.LessThan(220));
        }

        [Test]
        public void ExactFaceRoiMatchesSharedRegistrationCropRounding()
        {
            RectInt roi = FaceCropper.BuildTopLeftPixelRoi(
                new Rect(-2.4f, 6.3f, 22.7f, 31.2f), 48, 48);
            Assert.That(roi.xMin, Is.EqualTo(0));
            Assert.That(roi.yMin, Is.EqualTo(6));
            Assert.That(roi.xMax, Is.EqualTo(21));
            Assert.That(roi.yMax, Is.EqualTo(38));
            Assert.That(roi.width, Is.EqualTo(21));
            Assert.That(roi.height, Is.EqualTo(32));
        }

        [Test]
        public void RawTopLeftStatsAgreeWithTheActualOpenCvQualityCrop()
        {
            const int width = 64, height = 48;
            var pixels = Solid(width, height, 40);
            // Upper-left ROI only: intentionally asymmetric along Y.
            for (int y = height - 20; y < height; y++)
                for (int x = 5; x < 37; x++)
                    pixels[y * width + x] = new Color32(220, 220, 220, 255);

            UnityEngine.Rect requested = new UnityEngine.Rect(7.3f, 3.4f, 27.5f, 12.2f);
            using (OpenCvSharp.Mat mat = OpenCvSharp.Unity.PixelsToMat(
                pixels, width, height))
            using (OpenCvSharp.Mat crop = FaceCropper.Crop(
                mat, requested, out OpenCvSharp.Rect cvRoi))
            {
                var measured = RearCameraExposureProcessor.MeasureTopLeftRegion(
                    pixels, width, height,
                    new RectInt(cvRoi.X, cvRoi.Y, cvRoi.Width, cvRoi.Height), 1);
                var gateBrightness = new FaceBrightnessEvaluator().Evaluate(crop);
                Assert.That(measured.Mean,
                    Is.EqualTo(gateBrightness.Mean).Within(1f));
                Assert.That(measured.BrightRatio,
                    Is.EqualTo(gateBrightness.BrightPixelRatio).Within(.001f));
                Assert.That(measured.Mean, Is.GreaterThan(210f));
            }
        }

        [Test]
        public void NearClipWarningDoesNotReplaceCorrectedBrightnessQuality()
        {
            const int width = 64;
            const int height = 48;
            Color32[] raw = Solid(width, height, 150);
            // A high-contrast fixture: 40% nearly white, 60% midgray.
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (x % 10 < 4)
                        raw[y * width + x] = new Color32(255, 255, 255, 255);

            var state = new RearCameraExposureProcessor.AdaptiveState();
            var corrected = RearCameraExposureProcessor.Process(
                raw, width, height, true, true, state,
                new RectInt(0, 0, width, height));
            Assert.That(corrected.Corrected, Is.True);
            var rawFace = RearCameraExposureProcessor.MeasureTopLeftRegion(
                raw, width, height, new RectInt(0, 0, width, height), 1);

            Assert.That(RearCameraExposureProcessor.HasRawNearClipWarning(
                corrected.Corrected, true, rawFace), Is.True);
            Assert.That(rawFace.ClipRatio, Is.GreaterThan(.06f));
            using (var correctedMat = OpenCvSharp.Unity.PixelsToMat(
                corrected.Pixels, width, height))
            {
                var qualityBrightness = new FaceBrightnessEvaluator().Evaluate(correctedMat);
                Assert.That(qualityBrightness.Mean, Is.InRange(55f, 180f));
                Assert.That(qualityBrightness.BrightPixelRatio, Is.LessThanOrEqualTo(.25f));
                Assert.That(qualityBrightness.DarkPixelRatio, Is.LessThanOrEqualTo(.35f));
            }
            // The warning and the independently evaluated quality can coexist.
            // The production registration flow now leaves the gate result unchanged.
            Assert.That(raw[0].r, Is.EqualTo(255));
            Assert.That(corrected.Pixels[0].r, Is.LessThan(255));
        }

        [Test]
        public void NearClipWarningIsUnknownForUnmeasuredAndOffFrames()
        {
            Color32[] clipped = Solid(32, 24, 255);
            var rawFace = RearCameraExposureProcessor.MeasureTopLeftRegion(
                clipped, 32, 24, new RectInt(0, 0, 32, 24), 1);
            Assert.That(rawFace.ClipRatio, Is.EqualTo(1f));
            Assert.That(RearCameraExposureProcessor.HasRawNearClipWarning(
                true, true, rawFace), Is.True);
            Assert.That(RearCameraExposureProcessor.HasRawNearClipWarning(
                false, true, rawFace), Is.False, "Front or OFF is not corrected.");
            Assert.That(RearCameraExposureProcessor.HasRawNearClipWarning(
                true, false, rawFace), Is.False, "Unknown is not a measured warning.");
            var cleanFace = RearCameraExposureProcessor.MeasureTopLeftRegion(
                Solid(32, 24, 220), 32, 24, new RectInt(0, 0, 32, 24), 1);
            Assert.That(RearCameraExposureProcessor.HasRawNearClipWarning(
                true, true, cleanFace), Is.False);
        }

        [Test]
        public void RejectsMismatchedCanonicalBuffers()
        {
            Assert.Throws<System.ArgumentException>(() =>
                RearCameraExposureProcessor.MeasureCenter(new Color32[3], 32, 24));
        }
    }
}
