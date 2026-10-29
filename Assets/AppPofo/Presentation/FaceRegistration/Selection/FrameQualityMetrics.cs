using AppPofo.Presentation.FaceRegistration.Quality;

namespace AppPofo.Presentation.FaceRegistration.Selection
{
    public struct FrameQualityMetrics
    {
        public float FaceSizeRatio;
        public float RollDegrees;
        public float YawScore;
        public float PitchScore;
        public FaceBrightnessResult Brightness;
        public float BlurScore;
        // Calibration metadata: distinguish unmeasured stages from measured zero values.
        public bool HasFaceSizeMetrics;
        public bool HasPoseMetrics;
        public bool HasBrightnessMetrics;
        public bool HasBlurMetrics;
        public string RejectionStage;
    }
}
