using System.Collections.Generic;
using AppPofo.Presentation.FaceRecognition.Matching;
using AppPofo.Presentation.FaceRegistration.Detection;

namespace AppPofo.Presentation.FaceRecognition
{
    public sealed class FaceRecognitionItem
    {
        public FaceDetectionItem Detection { get; }
        public FaceRecognitionResult Recognition { get; }

        public FaceRecognitionItem(
            FaceDetectionItem detection,
            FaceRecognitionResult recognition
        )
        {
            Detection = detection;
            Recognition = recognition;
        }
    }
}