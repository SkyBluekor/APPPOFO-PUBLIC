using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    public interface IFaceDetector
    {
        FaceDetectionResult Detect(Texture frame);
    }
}