using System;
using OpenCvSharp;
using Unity.InferenceEngine;

namespace AppPofo.Presentation.FaceRegistration.Embedding
{
    public static class SFaceInputPreprocessor
    {
        public const int InputWidth = 112;
        public const int InputHeight = 112;

        public static void FillInput(
            Mat alignedFace,
            Tensor<float> inputTensor,
            float[] inputBuffer
        )
        {
            if (alignedFace == null || alignedFace.Empty())
            {
                throw new ArgumentException("alignedFace가 비어 있습니다.");
            }

            if (alignedFace.Cols != InputWidth || alignedFace.Rows != InputHeight)
            {
                throw new ArgumentException(
                    $"SFace 입력 크기는 {InputWidth}x{InputHeight}여야 합니다. " +
                    $"현재: {alignedFace.Cols}x{alignedFace.Rows}"
                );
            }

            if(inputTensor == null)
            {
                throw new ArgumentNullException(nameof(inputTensor));
            }
            var shape = inputTensor.shape;            

            if(shape.rank != 4)
            {
                throw new ArgumentException("shape.rank는 4여야 합니다.");
            }

            if(shape[0] != 1 || shape[1] != 3 || shape[2] != InputHeight || shape[3] != InputWidth)
            {
                throw new ArgumentException("잘못된 Tensor shape");
            }

            if (inputBuffer == null)
            {
                throw new ArgumentNullException(nameof(inputBuffer));
            }

            if (inputBuffer.Length != 3 * InputWidth * InputHeight)
            {
                throw new ArgumentException("잘못된 SFace 입력 버퍼 크기", nameof(inputBuffer));
            }

            if(alignedFace.Type() != MatType.CV_8UC3)
            {
                throw new ArgumentException($"SFace 입력은 Mat은 CV_8UC3이어야 합니다. 현재: {alignedFace.Type()}");
            }


            int channelSize = InputWidth * InputHeight;
            for(int y = 0; y < InputHeight; y++)
            {
                for(int x = 0; x < InputWidth; x++)
                {
                    Vec3b pixel = alignedFace.At<Vec3b>(y,x);

                    byte b = pixel.Item0;
                    byte g = pixel.Item1;
                    byte r = pixel.Item2;

                    int pixelIndex = y * InputWidth + x;

                    int rIndex = pixelIndex;
                    int gIndex = channelSize + pixelIndex;
                    int bIndex = channelSize * 2 + pixelIndex;

                    inputBuffer[rIndex] = r;
                    inputBuffer[gIndex] = g;
                    inputBuffer[bIndex] = b;
                }
            }

            inputTensor.Upload(inputBuffer);
        }
    }
}
