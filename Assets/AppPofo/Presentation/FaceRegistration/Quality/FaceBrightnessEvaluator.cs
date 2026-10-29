using OpenCvSharp;

namespace AppPofo.Presentation.FaceRegistration.Quality
{
    public sealed class FaceBrightnessEvaluator
    {
        private const int DarkPixelValue = 40;
        private const int BrightPixelValue = 210;
        public FaceBrightnessResult Evaluate(Mat faceMat)
        {
            if(faceMat == null || faceMat.Empty())
            {
                return new FaceBrightnessResult();
            }

            using Mat gray = new Mat();

            Cv2.CvtColor(faceMat,gray,ColorConversionCodes.BGR2GRAY);

            Scalar meanbright = Cv2.Mean(gray);
            float darkPixelRatio = GetDarkPixelRatio(gray, DarkPixelValue);
            float brightPixelRatio = GetBrightPixelRatio(gray, BrightPixelValue);


            return new FaceBrightnessResult
            {
                Mean = (float)meanbright.Val0,
                DarkPixelRatio = darkPixelRatio,
                BrightPixelRatio = brightPixelRatio
            };
        }

        private float GetDarkPixelRatio(Mat FaceGrayMat ,int DarkPixelValue)
        {
            using Mat darkMask = new Mat();

            Cv2.InRange(FaceGrayMat,new Scalar(0), new Scalar(DarkPixelValue),darkMask);

            int darkPixelCount = Cv2.CountNonZero(darkMask);
            int totalPixelCount = FaceGrayMat.Rows * FaceGrayMat.Cols;
            float darkPixelRatio = (float)darkPixelCount / totalPixelCount;

            return darkPixelRatio;
        }

        private float GetBrightPixelRatio(Mat FaceGrayMat, int BrightPixelValue)
        {
            using Mat brightMask = new Mat();

            Cv2.InRange(FaceGrayMat,new Scalar(BrightPixelValue), new Scalar(255),brightMask);

            int brightPixelCount = Cv2.CountNonZero(brightMask);
            int totalPixelCount = FaceGrayMat.Rows * FaceGrayMat.Cols;
            float brightPixelRatio = (float)brightPixelCount / totalPixelCount;

            return brightPixelRatio;
        }
    }
}