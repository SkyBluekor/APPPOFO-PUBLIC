using OpenCvSharp;
using UnityEngine;
using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.FaceRegistration.Quality;

namespace AppPofo.Presentation.FaceRegistration.Selection
{
    public sealed class FrameQualityGate
    {
        // Keep the proven frontal policy. Only side captures use the wider envelope.
        // These are normalized landmark scores, not physical head-rotation degrees.
        private const float MinimumFaceSize = .65f;
        private const float MinimumSideFaceSize = .50f;
        private const float MaximumFaceSize = .95f;
        private const float MinimumSideYaw = .22f;
        private const float MaximumSideYaw = .82f;
        private const float MinimumSidePitch = .45f;
        private const float MaximumSidePitch = .75f;
        private readonly FaceSizeEvaluator sizeEvaluator = new();
        private readonly FacePoseEvaluator poseEvaluator = new();
        private readonly FaceBrightnessEvaluator brightnessEvaluator = new();
        private readonly FaceBlurEvaluator blurEvaluator = new();

        public bool TryEvaluate(
            bool hasCurrentFace,
            UnityEngine.Rect faceBounds,
            int frameHeight,
            FaceLandmarks5 landmarks,
            Mat faceMat,
            FaceRegistrationStep step,
            out FrameQualityMetrics metrics)
        {
            metrics = new FrameQualityMetrics { RejectionStage = "Detection" };
            // 1. 현재 프레임 얼굴 검출
            if (!hasCurrentFace)
                return false;

            // 2. Face Size
            float faceSize = sizeEvaluator.Evaluate(faceBounds, frameHeight);
            metrics.FaceSizeRatio = faceSize;
            metrics.HasFaceSizeMetrics = true;

            if (!PassFaceSize(faceSize, step))
            {
                metrics.RejectionStage = "FaceSize";
                return false;
            }

            // 3. Pose
            float roll = poseEvaluator.EvaluateRoll(landmarks);
            float yaw = poseEvaluator.EvaluateYaw(landmarks);
            float pitch = poseEvaluator.EvaluatePitch(landmarks);

            metrics.RollDegrees = roll;
            metrics.YawScore = yaw;
            metrics.PitchScore = pitch;
            metrics.HasPoseMetrics = true;

            if (!PassCommonPose(roll, pitch, step))
            {
                metrics.RejectionStage = Mathf.Abs(roll) > 15f ? "Roll" : "Pitch";
                return false;
            }

            if (!PassYawForStep(yaw, step))
            {
                metrics.RejectionStage = "Yaw";
                return false;
            }

            // 4. Brightness
            FaceBrightnessResult brightness =
                brightnessEvaluator.Evaluate(faceMat);

            metrics.Brightness = brightness;
            metrics.HasBrightnessMetrics = true;
            if (!PassBrightness(brightness))
            {
                metrics.RejectionStage = "Brightness";
                return false;
            }

            // 5. Blur
            FaceQualityResult blurResult = blurEvaluator.Evaluate(faceMat);
            metrics.BlurScore = blurResult.BlurScore;
            metrics.HasBlurMetrics = true;

            if (!PassBlur(blurResult.BlurScore))
            {
                metrics.RejectionStage = "Blur";
                return false;
            }

            metrics.RejectionStage = "Accepted";
            return true;
        }

        private bool PassFaceSize(float faceSize, FaceRegistrationStep step)
        {
            float min = IsSideStep(step) ? MinimumSideFaceSize : MinimumFaceSize;
            float max = MaximumFaceSize;

            bool facePass = false; 

            if(faceSize >= min && faceSize <= max)
                facePass = true;

            return facePass;
        }

        private static bool IsSideStep(FaceRegistrationStep step) =>
            step == FaceRegistrationStep.Left || step == FaceRegistrationStep.Right;

        private bool PassCommonPose(float roll, float pitch, FaceRegistrationStep step)
        {
            float maxRoll = 15f;
            float minPitch = IsSideStep(step) ? MinimumSidePitch : 0.50f;
            float maxPitch = IsSideStep(step) ? MaximumSidePitch : 0.70f;

            return Mathf.Abs(roll) <= maxRoll &&
                   pitch >= minPitch &&
                   pitch <= maxPitch;
        }

        private bool PassYawForStep(float yaw, FaceRegistrationStep step)
        {
            switch (step)
            {
                case FaceRegistrationStep.Front:
                    return Mathf.Abs(yaw) <= 0.35f;

                case FaceRegistrationStep.Left:
                    return yaw >= MinimumSideYaw && yaw <= MaximumSideYaw;

                case FaceRegistrationStep.Right:
                    return yaw >= -MaximumSideYaw && yaw <= -MinimumSideYaw;

                case FaceRegistrationStep.Complete:
                default:
                    return false;
            }
        }

        private bool PassBrightness(FaceBrightnessResult brightness)
        {
            float minMean = 55f;
            float maxMean = 180f;

            float maxDark = 0.35f;
            float maxBright= 0.25f;

            bool brightnessPass = false;

            if ( brightness.Mean >= minMean && brightness.Mean <= maxMean && brightness.DarkPixelRatio <= maxDark && brightness.BrightPixelRatio <= maxBright)
                brightnessPass =true;

            return brightnessPass;
        }

        public static string DescribeRejection(FrameQualityMetrics metrics, FaceRegistrationStep step)
        {
            switch (metrics.RejectionStage)
            {
                case "FaceSize":
                    return metrics.FaceSizeRatio < (IsSideStep(step) ? MinimumSideFaceSize : MinimumFaceSize)
                        ? "카메라에 조금 더 가까이 와주세요"
                        : "카메라에서 조금 떨어져 주세요";
                case "Roll": return "고개를 수평으로 맞춰 주세요";
                case "Pitch": return "고개를 위아래로 기울이지 말아 주세요";
                case "Yaw":
                    if (step == FaceRegistrationStep.Front) return "고개를 돌리지 말고 정면을 봐주세요";
                    float directedYaw = step == FaceRegistrationStep.Left ? metrics.YawScore : -metrics.YawScore;
                    if (directedYaw > MaximumSideYaw) return "고개를 조금 덜 돌려 주세요";
                    return step == FaceRegistrationStep.Left
                        ? "고개를 왼쪽으로 조금 더 돌려 주세요"
                        : "고개를 오른쪽으로 조금 더 돌려 주세요";
                case "Brightness":
                    return metrics.Brightness.Mean > 180 || metrics.Brightness.BrightPixelRatio > .25f
                        ? "얼굴에 비치는 강한 조명을 줄여 주세요" : "얼굴을 더 밝게 비춰 주세요";
                case "Blur": return "움직임을 줄이고 잠시 유지해 주세요";
                case "Accepted": return string.Empty;
                default: return "카메라에 얼굴이 보이도록 맞춰 주세요";
            }
        }

        private bool PassBlur(float blur)
        {
            float minBlur = 20f;
            bool blurPass = false;

            if(blur >= minBlur)
                blurPass = true;

            return blurPass;
        }
    }
}
