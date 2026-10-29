using System;
using System.Linq;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRegistration.Detection;
using NUnit.Framework;
using UnityEngine;

namespace AppPofo.Tests.EditMode
{
    public sealed class FaceDetectionControllerTests
    {
        [TestCase(0, false, "1,2,3,4,5,6")]
        [TestCase(0, true, "5,6,3,4,1,2")]
        [TestCase(90, false, "2,4,6,1,3,5")]
        [TestCase(90, true, "6,4,2,5,3,1")]
        [TestCase(180, false, "6,5,4,3,2,1")]
        [TestCase(180, true, "2,1,4,3,6,5")]
        [TestCase(270, false, "5,3,1,6,4,2")]
        [TestCase(270, true, "1,3,5,2,4,6")]
        public void ProcessFrame_WithOrientation_DetectsCanonicalPixels(int angle, bool mirror, string expectedText)
        {
            var raw = new Texture2D(2, 3, TextureFormat.RGBA32, false);
            raw.SetPixels32(Enumerable.Range(1, 6)
                .Select(value => new Color32((byte)value, 0, 0, 255)).ToArray());
            raw.Apply();
            var orientation = new CameraFrameOrientation(angle, mirror, 2, 3);
            Texture received = null;
            var detector = new InspectingDetector(texture =>
            {
                received = texture;
                Assert.That(texture.width, Is.EqualTo(orientation.EffectiveWidth));
                Assert.That(texture.height, Is.EqualTo(orientation.EffectiveHeight));
                Assert.That(((Texture2D)texture).GetPixels32().Select(pixel => pixel.r),
                    Is.EqualTo(expectedText.Split(',').Select(byte.Parse)));
            });

            try
            {
                var controller = new FaceDetectionController(detector);
                controller.ProcessFrame(raw, orientation);
                Assert.That(detector.CallCount, Is.EqualTo(1));
                Assert.That(controller.LastFrameOrientation, Is.EqualTo(orientation));
                Assert.That(ReferenceEquals(received, raw), Is.False,
                    "Detection must use a snapshot even for identity orientation.");
                Assert.That(received == null, Is.True, "The temporary canonical texture must be destroyed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(raw);
            }
        }

        [Test]
        public void ProcessFrame_WithMismatchedDimensions_SkipsDetection()
        {
            var raw = new Texture2D(2, 3);
            try
            {
                var detector = new InspectingDetector(_ => Assert.Fail("Detection must be skipped."));
                var controller = new FaceDetectionController(detector);
                controller.ProcessFrame(raw, new CameraFrameOrientation(90, false, 3, 2));
                Assert.That(detector.CallCount, Is.Zero);
                Assert.That(controller.LastFrameOrientation, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(raw);
            }
        }

        [Test]
        public void ProcessFrameWithCrop_UsesSnapshotWhenSourceChangesDuringDetection()
        {
            var raw = new Texture2D(40, 20, TextureFormat.RGBA32, false);
            raw.SetPixels32(Enumerable.Repeat(new Color32(255, 255, 255, 255), 800).ToArray());
            raw.Apply();
            var detector = new InspectingDetector(texture =>
            {
                Assert.That(ReferenceEquals(texture, raw), Is.False);
                Assert.That(((Texture2D)texture).GetPixel(20, 10).r, Is.GreaterThan(0.9f));
                raw.SetPixels32(Enumerable.Repeat(new Color32(0, 0, 0, 255), 800).ToArray());
                raw.Apply();
            }, new FaceDetectionResult
            {
                Found = true,
                FaceCount = 1,
                HasCurrentFrameBounds = true,
                Bounds = new Rect(80, 80, 80, 80)
            });

            try
            {
                var controller = new FaceDetectionController(detector);
                using var result = controller.ProcessFrameWithCrop(
                    raw, new CameraFrameOrientation(0, false, 40, 20));
                Assert.That(result.Detection.HasCurrentFrameBounds, Is.True);
                Assert.That(result.MatWidth, Is.EqualTo(40));
                Assert.That(result.MatHeight, Is.EqualTo(20));
                Assert.That(result.HasFaceCrop, Is.True);
                Assert.That(result.CropWidth, Is.GreaterThan(0));
                Assert.That(result.CropHeight, Is.GreaterThan(0));
                var canonicalMat = result.CanonicalMat;
                var faceMat = result.FaceMat;
                Assert.That(canonicalMat, Is.Not.Null);
                Assert.That(canonicalMat.IsDisposed, Is.False);
                Assert.That(canonicalMat.Cols, Is.EqualTo(40));
                Assert.That(canonicalMat.Rows, Is.EqualTo(20));
                Assert.That(ReferenceEquals(canonicalMat, faceMat), Is.False);
                // The detector changed raw to black, but the retained snapshot must remain white.
                Assert.That(OpenCvSharp.Cv2.Mean(canonicalMat).Val0, Is.GreaterThan(250));

                result.Dispose();
                Assert.That(canonicalMat.IsDisposed, Is.True);
                Assert.That(faceMat.IsDisposed, Is.True);
                Assert.That(result.CanonicalMat, Is.Null);
                Assert.That(result.FaceMat, Is.Null);
                // The using declaration will dispose the frame again at scope exit.
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(raw);
            }
        }

        [Test]
        public void ProcessFrameWithCrop_DoesNotCropStabilizedStaleBounds()
        {
            var raw = new Texture2D(40, 20);
            try
            {
                var detector = new InspectingDetector(_ => { }, new FaceDetectionResult
                {
                    Found = true,
                    FaceCount = 1,
                    Bounds = new Rect(80, 80, 80, 80)
                });
                var controller = new FaceDetectionController(detector);
                using var result = controller.ProcessFrameWithCrop(
                    raw, new CameraFrameOrientation(0, false, 40, 20));
                Assert.That(result.Detection.Found, Is.True);
                Assert.That(result.Detection.HasCurrentFrameBounds, Is.False);
                Assert.That(result.HasFaceCrop, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(raw);
            }
        }

        private sealed class InspectingDetector : IFaceDetector
        {
            private readonly Action<Texture> inspect;
            private readonly FaceDetectionResult result;
            public int CallCount { get; private set; }

            public InspectingDetector(Action<Texture> inspect, FaceDetectionResult result = default)
            {
                this.inspect = inspect;
                this.result = result;
            }

            public FaceDetectionResult Detect(Texture frame)
            {
                CallCount++;
                inspect(frame);
                return result;
            }
        }

        [Test]
        public void ProcessFrame_WithMockDetector_ReturnFace()
        {
            Texture2D frame = new Texture2D(16,16);

            try
            {
                IFaceDetector detector = new MockFaceDetector();

                FaceDetectionController controller = new FaceDetectionController(detector);
                FaceDetectionResult result = controller.ProcessFrame(frame);

                Assert.IsTrue(result.Found);
                Assert.AreEqual(0.95f,result.Confidencce);
            }

            finally
            {
                UnityEngine.Object.DestroyImmediate(frame);
            }
        }
    }
}
