using System;
using OpenCvSharp;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    /// <summary>Owns the canonical frame and its independent face ROI from the same pixel snapshot.</summary>
    public sealed class FaceDetectionFrame : IDisposable
    {
        public FaceDetectionResult Detection { get; }
        public FaceLetterboxMetadata Letterbox { get; }
        public int CanonicalWidth => Letterbox.SourceWidth;
        public int CanonicalHeight => Letterbox.SourceHeight;
        public int MatWidth { get; }
        public int MatHeight { get; }
        public bool HasFaceCrop => FaceMat != null;
        public int CropWidth => FaceMat?.Cols ?? 0;
        public int CropHeight => FaceMat?.Rows ?? 0;
        public Mat FaceMat { get; private set; }
        public Mat CanonicalMat { get; private set; }
        public UnityEngine.Vector2Int CropOrigin { get; }
        public FaceLandmarks5 CanonicalLandmarks => Detection.HasCurrentFrameBounds
            ? FaceBoundingBoxMapper.ModelToCanonical(Detection.Landmarks, Letterbox)
            : default;

        internal FaceDetectionFrame(FaceDetectionResult detection, FaceLetterboxMetadata letterbox,
            Mat faceMat, int matWidth = 0, int matHeight = 0,
            UnityEngine.Vector2Int cropOrigin = default, Mat canonicalMat = null)
        {
            Detection = detection;
            Letterbox = letterbox;
            FaceMat = faceMat;
            CanonicalMat = canonicalMat;
            CropOrigin = cropOrigin;
            MatWidth = matWidth;
            MatHeight = matHeight;
        }

        public void Dispose()
        {
            Mat faceMat = FaceMat;
            Mat canonicalMat = CanonicalMat;
            FaceMat = null;
            CanonicalMat = null;
            try
            {
                faceMat?.Dispose();
            }
            finally
            {
                if (!ReferenceEquals(faceMat, canonicalMat))
                    canonicalMat?.Dispose();
            }
        }
    }
}
