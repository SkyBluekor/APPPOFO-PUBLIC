using UnityEngine;
using AppPofo.Presentation.FaceRegistration.Detection;

namespace AppPofo.Presentation.FaceRegistration.Quality
{
    public sealed class FacePoseEvaluator
    {
        public float EvaluateRoll(FaceLandmarks5 landmarks)
        {
            float dx = landmarks.LeftEye.x - landmarks.RightEye.x;
            float dy = landmarks.LeftEye.y - landmarks.RightEye.y;

            float rollRadians = Mathf.Atan2(dy,dx);
            float rollDegrees = rollRadians * Mathf.Rad2Deg;

            return rollDegrees;
        }

        public float EvaluateYaw(FaceLandmarks5 landmarks)
        {
            float eyeCenterX = (landmarks.LeftEye.x + landmarks.RightEye.x) / 2f;
            float eyeDistance = Mathf.Abs(landmarks.LeftEye.x - landmarks.RightEye.x );

            float noseoffset = landmarks.NoseTip.x - eyeCenterX;
            float yawScore = noseoffset / eyeDistance;

            return yawScore;
        }

        public float EvaluatePitch(FaceLandmarks5 landmarks)
        {
            float eyeCenterY = (landmarks.LeftEye.y + landmarks.RightEye.y) / 2f;
            float mouthCenterY = (landmarks.LeftMouthCorner.y + landmarks.RightMouthCorner.y) / 2f;

            float eyeToMouthDistance = mouthCenterY - eyeCenterY;
            float noseOffset = landmarks.NoseTip.y - eyeCenterY;

            float PitchScore = noseOffset / eyeToMouthDistance;
            return PitchScore;   
        }                        
    }
}