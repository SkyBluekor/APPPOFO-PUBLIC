using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    public sealed class MockFaceDetector : IFaceDetector
    {
        public FaceDetectionResult Detect(Texture frame)
        {
            return new FaceDetectionResult
            {
                Found = true,
                Bounds = new Rect(0.30f, 0.20f,0.40f,0.60f),
                Confidencce = 0.95f
            };
        }
    }
}