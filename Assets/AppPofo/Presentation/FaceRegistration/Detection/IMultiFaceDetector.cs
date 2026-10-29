using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    public interface IMultiFaceDetector
    {
        /// <summary>Runs detection once and returns all current-frame NMS survivors
        /// in descending confidence order. No stabilization or previous-frame fallback.</summary>
        MultiFaceDetectionResult DetectMulti(Texture canonicalFrame);
    }
}