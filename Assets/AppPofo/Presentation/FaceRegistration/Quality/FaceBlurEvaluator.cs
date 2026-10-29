using UnityEngine;
using OpenCvSharp;

namespace AppPofo.Presentation.FaceRegistration.Quality
{
    public sealed class FaceBlurEvaluator
    {
        private const float SharpnessThreshold = 20f;

        public FaceQualityResult Evaluate(Mat faceMat)
        {
            if(faceMat == null || faceMat.Empty())
            {
                return new FaceQualityResult{
                    BlurScore = 0f,
                    IsSharpEnough = false
                };                
            }

            using var gray = new Mat();
            using var laplacian = new Mat();

            Cv2.CvtColor(faceMat, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.Laplacian(gray,laplacian,MatType.CV_64F);

            Cv2.MeanStdDev(laplacian, out _, out Scalar stddev);

            double variance = stddev.Val0 * stddev.Val0;

            return new FaceQualityResult
            {
                BlurScore = (float) variance,
                IsSharpEnough = variance >= SharpnessThreshold
            };                        
        }
    }
}
