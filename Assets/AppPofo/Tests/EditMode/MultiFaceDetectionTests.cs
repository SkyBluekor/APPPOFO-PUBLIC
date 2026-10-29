using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRegistration.Detection;
using NUnit.Framework;
using UnityEngine;

namespace AppPofo.Tests.EditMode
{
    public sealed class MultiFaceDetectionTests
    {
        [Test]
        public void Nms_KeepsDistinctFacesInDescendingConfidenceOrder()
        {
            // Exercise the existing CPU NMS without loading a model or creating a GPU Worker.
            Type detector = typeof(SentisFaceDetector);
            Type candidate = detector.GetNestedType("DetectionCandidate", BindingFlags.NonPublic);
            var candidates = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(candidate));
            Action<float, Rect> add = (score, bounds) =>
            {
                object value = Activator.CreateInstance(candidate);
                candidate.GetField("Score").SetValue(value, score);
                candidate.GetField("Bounds").SetValue(value, bounds);
                candidates.Add(value);
            };
            add(0.7f, new Rect(100, 100, 30, 30));
            add(0.8f, new Rect(0, 0, 30, 30));
            add(0.95f, new Rect(1, 1, 30, 30)); // Suppresses the overlapping 0.8 candidate.
            add(0.6f, new Rect(200, 200, 30, 30));

            var result = (IList)detector.GetMethod("ApplyNms", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { candidates, 0.5f });
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result.Cast<object>().Select(value => (float)candidate.GetField("Score").GetValue(value)),
                Is.EqualTo(new[] { 0.95f, 0.7f, 0.6f }));
        }

        [TestCase(0, false)]
        [TestCase(90, true)]
        [TestCase(270, false)]
        public void MultiFrame_UsesOneSnapshotAndMapsEveryFace(int angle, bool mirror)
        {
            var raw = new Texture2D(40, 20, TextureFormat.RGBA32, false);
            Color32[] pixels = Enumerable.Repeat(new Color32(255, 255, 255, 255), 800).ToArray();
            raw.SetPixels32(pixels);
            raw.Apply();
            var orientation = new CameraFrameOrientation(angle, mirror, 40, 20);
            var landmarks = new FaceLandmarks5(new Vector2(110, 110), new Vector2(130, 110),
                new Vector2(120, 120), new Vector2(112, 130), new Vector2(128, 130));
            var modelFaces = new[]
            {
                new FaceDetectionItem(new Rect(100, 100, 40, 40), landmarks, 0.95f),
                new FaceDetectionItem(new Rect(150, 150, 30, 30), landmarks, 0.7f)
            };
            var detector = new MultiDetector(modelFaces);
            detector.Inspect = texture =>
            {
                Assert.That(texture.width, Is.EqualTo(orientation.EffectiveWidth));
                Assert.That(texture.height, Is.EqualTo(orientation.EffectiveHeight));
                Assert.That(((Texture2D)texture).GetPixels32(), Is.EqualTo(orientation.UprightPixels(pixels)));
                // Mutating the input during detection must not change the retained Mat.
                raw.SetPixels32(new Color32[800]);
                raw.Apply();
            };
            try
            {
                var controller = new FaceDetectionController(detector);
                using var frame = controller.ProcessMultiFrame(raw, orientation);
                Assert.That(detector.MultiCalls, Is.EqualTo(1));
                Assert.That(detector.SingleCalls, Is.Zero);
                Assert.That(frame.Faces.Count, Is.EqualTo(2));
                Assert.That(frame.Faces.Select(face => face.Confidence), Is.EqualTo(new[] { 0.95f, 0.7f }));
                Assert.That(frame.CanonicalWidth, Is.EqualTo(orientation.EffectiveWidth));
                Assert.That(frame.CanonicalHeight, Is.EqualTo(orientation.EffectiveHeight));
                var letterbox = FaceLetterboxPreprocessor.Calculate(frame.CanonicalWidth, frame.CanonicalHeight);
                for (int i = 0; i < modelFaces.Length; i++)
                {
                    Assert.That(frame.Faces[i].Bounds,
                        Is.EqualTo(FaceBoundingBoxMapper.ModelToCanonical(modelFaces[i].Bounds, letterbox)));
                    var expected = FaceBoundingBoxMapper.ModelToCanonical(modelFaces[i].Landmarks, letterbox);
                    Assert.That(frame.Faces[i].Landmarks.RightEye, Is.EqualTo(expected.RightEye));
                    Assert.That(frame.Faces[i].Landmarks.LeftEye, Is.EqualTo(expected.LeftEye));
                    Assert.That(frame.Faces[i].Landmarks.NoseTip, Is.EqualTo(expected.NoseTip));
                    Assert.That(frame.Faces[i].Landmarks.RightMouthCorner, Is.EqualTo(expected.RightMouthCorner));
                    Assert.That(frame.Faces[i].Landmarks.LeftMouthCorner, Is.EqualTo(expected.LeftMouthCorner));
                }
                var mat = frame.CanonicalMat;
                Assert.That(OpenCvSharp.Cv2.Mean(mat).Val0, Is.GreaterThan(250));
                frame.Dispose();
                Assert.That(mat.IsDisposed, Is.True);
                Assert.That(frame.CanonicalMat, Is.Null);
                frame.Dispose(); // Idempotent: using will also dispose.
                Assert.That(detector.Received == null, Is.True, "Temporary canonical texture must be destroyed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(raw);
            }
        }

        [Test]
        public void MultiFrame_DoesNotRetainFacesFromPreviousCall()
        {
            var raw = new Texture2D(40, 20, TextureFormat.RGBA32, false);
            var detector = new MultiDetector(new[]
            {
                new FaceDetectionItem(new Rect(100, 100, 40, 40), default, 0.9f)
            });
            try
            {
                var controller = new FaceDetectionController(detector);
                var orientation = new CameraFrameOrientation(0, false, 40, 20);
                using var first = controller.ProcessMultiFrame(raw, orientation);
                Assert.That(first.Faces.Count, Is.EqualTo(1));
                detector.Faces = Array.Empty<FaceDetectionItem>();
                using var second = controller.ProcessMultiFrame(raw, orientation);
                Assert.That(second.Faces, Is.Empty);
                Assert.That(second.CanonicalMat, Is.Not.Null);
                Assert.That(ReferenceEquals(first.CanonicalMat, second.CanonicalMat), Is.False);
                Assert.That(detector.MultiCalls, Is.EqualTo(2));
                Assert.That(detector.SingleCalls, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(raw); }
        }

        [Test]
        public void MultiFrame_MismatchedInputSkipsDetection()
        {
            var raw = new Texture2D(40, 20);
            try
            {
                var detector = new MultiDetector(Array.Empty<FaceDetectionItem>());
                var controller = new FaceDetectionController(detector);
                using var frame = controller.ProcessMultiFrame(raw, new CameraFrameOrientation(0, false, 20, 40));
                Assert.That(frame.Faces, Is.Empty);
                Assert.That(frame.CanonicalMat, Is.Null);
                Assert.That(detector.MultiCalls, Is.Zero);
                Assert.That(controller.LastFrameOrientation, Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(raw); }
        }

        [Test]
        public void MultiFrame_DetectorFailureDestroysSnapshot()
        {
            var raw = new Texture2D(40, 20);
            var detector = new MultiDetector(Array.Empty<FaceDetectionItem>());
            detector.Inspect = _ => throw new InvalidOperationException("Expected detector failure.");
            try
            {
                var controller = new FaceDetectionController(detector);
                Assert.Throws<InvalidOperationException>(() =>
                    controller.ProcessMultiFrame(raw, new CameraFrameOrientation(0, false, 40, 20)));
                Assert.That(detector.Received == null, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(raw); }
        }

        [Test]
        public void ExistingSingleDetector_RemainsUsableAndRejectsOnlyMultiApi()
        {
            var controller = new FaceDetectionController(new MockFaceDetector());
            Assert.That(controller.ProcessFrame(null).Found, Is.False);
            Assert.Throws<NotSupportedException>(() => controller.ProcessMultiFrame(null, default));
        }

        private sealed class MultiDetector : IFaceDetector, IMultiFaceDetector
        {
            public FaceDetectionItem[] Faces;
            public Action<Texture> Inspect;
            public Texture Received;
            public int MultiCalls;
            public int SingleCalls;

            public MultiDetector(FaceDetectionItem[] faces) { Faces = faces; }

            public FaceDetectionResult Detect(Texture frame)
            {
                SingleCalls++;
                throw new InvalidOperationException("Multi path must not invoke single detection.");
            }

            public MultiFaceDetectionResult DetectMulti(Texture canonicalFrame)
            {
                MultiCalls++;
                Received = canonicalFrame;
                Inspect?.Invoke(canonicalFrame);
                return new MultiFaceDetectionResult(
                    FaceLetterboxPreprocessor.Calculate(canonicalFrame.width, canonicalFrame.height), Faces);
            }
        }
    }
}