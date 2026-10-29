using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    public readonly struct FaceLandmarks5
    {
        public Vector2 RightEye { get; }
        public Vector2 LeftEye { get; }
        public Vector2 NoseTip { get; }
        public Vector2 RightMouthCorner { get; }
        public Vector2 LeftMouthCorner { get; }

        public FaceLandmarks5(
            Vector2 rightEye,
            Vector2 leftEye,
            Vector2 noseTip,
            Vector2 rightMouthCorner,
            Vector2 leftMouthCorner)
        {
            RightEye = rightEye;
            LeftEye = leftEye;
            NoseTip = noseTip;
            RightMouthCorner = rightMouthCorner;
            LeftMouthCorner = leftMouthCorner;
        }
    }

    public struct FaceDetectionResult
    {
        public bool Found;
        public int FaceCount;
        public Rect Bounds;
        // Uses the same model-space coordinates as Bounds.
        public FaceLandmarks5 Landmarks;
        public float Confidencce;
        // False when Found is backed only by face-count stabilization from an earlier frame.
        public bool HasCurrentFrameBounds;
    }
}
