using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    /// <summary>Managed current-frame face data. Coordinates are model pixels in
    /// MultiFaceDetectionResult and canonical pixels in MultiFaceDetectionFrame.
    /// List position is confidence order, never a tracking ID.</summary>
    public readonly struct FaceDetectionItem
    {
        public Rect Bounds { get; }
        public FaceLandmarks5 Landmarks { get; }
        public float Confidence { get; }

        public FaceDetectionItem(Rect bounds, FaceLandmarks5 landmarks, float confidence)
        {
            Bounds = bounds;
            Landmarks = landmarks;
            Confidence = confidence;
        }
    }
}