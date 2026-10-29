using OpenCvSharp;
using AppPofo.Presentation.FaceRegistration.Detection;

namespace AppPofo.Presentation.FaceRegistration.Alignment
{
    public static class FaceAligner
    {
        public static Mat EstimateTransform(FaceLandmarks5 cropLocalLandmarks)
        {
            Point2f[] source = SFaceAlignmentTemplate.CreateSourcePoints(cropLocalLandmarks);
            Point2f[] destination = SFaceAlignmentTemplate.CreateDestinationPoints();

            using InputArray sourceInput = InputArray.Create(source);
            using InputArray destinationInput = InputArray.Create(destination);

            Mat transform = Cv2.EstimateAffinePartial2D(sourceInput, destinationInput);

            return transform;
        }

        public static Mat Align(Mat faceMat, FaceLandmarks5 cropLocalLandmarks)
        {                                    
            using Mat transform = EstimateTransform(cropLocalLandmarks);

            Mat aligned = new Mat();

            Cv2.WarpAffine(faceMat,aligned,transform,new Size(112,112));

            return aligned;
        }
    }
}