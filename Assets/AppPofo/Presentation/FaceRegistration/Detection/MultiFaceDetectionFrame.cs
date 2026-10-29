using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    /// <summary>Owns one full canonical snapshot. Faces use that snapshot's
    /// top-left-origin canonical pixel coordinates and own no native resources.</summary>
    public sealed class MultiFaceDetectionFrame : IDisposable
    {
        public Mat CanonicalMat { get; private set; }
        public int CanonicalWidth { get; }
        public int CanonicalHeight { get; }
        public IReadOnlyList<FaceDetectionItem> Faces { get; }
        // Measured on the uncorrected canonical pixels with the exact registration
        // quality crop footprint, not the center or previous-face control ROI.
        public bool HasRawQualityFace { get; }
        public RearCameraExposureProcessor.Stats RawQualityFace { get; }
        public bool ExposureCorrected { get; }
        // Informational only: a near-white source pixel does not prove
        // irrecoverable sensor detail loss and never overrides the quality gate.
        public bool RawNearClipWarning =>
            RearCameraExposureProcessor.HasRawNearClipWarning(
                ExposureCorrected, HasRawQualityFace, RawQualityFace);

        internal MultiFaceDetectionFrame(Mat canonicalMat, int canonicalWidth,
            int canonicalHeight, IReadOnlyList<FaceDetectionItem> faces,
            bool hasRawQualityFace = false,
            RearCameraExposureProcessor.Stats rawQualityFace = default,
            bool exposureCorrected = false)
        {
            if (faces == null) throw new ArgumentNullException(nameof(faces));
            var copy = new FaceDetectionItem[faces.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = faces[i];
            Faces = Array.AsReadOnly(copy);
            CanonicalWidth = canonicalWidth;
            CanonicalHeight = canonicalHeight;
            CanonicalMat = canonicalMat;
            HasRawQualityFace = hasRawQualityFace;
            RawQualityFace = rawQualityFace;
            ExposureCorrected = exposureCorrected;
        }

        public void Dispose()
        {
            Mat canonicalMat = CanonicalMat;
            CanonicalMat = null;
            canonicalMat?.Dispose();
        }
    }
}