using OpenCvSharp;
using AppPofo.Presentation.FaceRegistration.Detection;

namespace AppPofo.Presentation.FaceRegistration.Alignment
{
    public static class SFaceAlignmentTemplate
    {
        public static Point2f[] CreateDestinationPoints()
        {
            Point2f[] points =
            {
                new Point2f(38.2946f, 51.6963f),
                new Point2f(73.5318f, 51.5014f),
                new Point2f(56.0252f, 71.7366f),
                new Point2f(41.5493f, 92.3655f),
                new Point2f(70.7299f, 92.2041f)
            };

            return points;            
        }

        public static Point2f[] CreateSourcePoints(FaceLandmarks5 landmarks)
        {
            Point2f[] points =
            {
                new Point2f(landmarks.RightEye.x,landmarks.RightEye.y),
                new Point2f(landmarks.LeftEye.x,landmarks.LeftEye.y),
                new Point2f(landmarks.NoseTip.x,landmarks.NoseTip.y),
                new Point2f(landmarks.RightMouthCorner.x,landmarks.RightMouthCorner.y),
                new Point2f(landmarks.LeftMouthCorner.x,landmarks.LeftMouthCorner.y)
            };

            return points;
        }
    }
}