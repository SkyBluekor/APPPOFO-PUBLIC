using System;
using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    /// <summary>
    /// Experimental CPU alternative to TextureConverter.ToTensor for YuNet.
    /// Produces normalized BGR, NCHW [1,3,320,320] directly from the
    /// already-canonical upright camera pixels (Unity bottom-left pixel origin).
    /// The YuNet FunctionalGraph still multiplies inputs by 255.
    /// Not enabled by default until numerical parity and real-device AR are checked.
    /// </summary>
    public static class FaceLetterboxCpuInput
    {
        public static FaceLetterboxMetadata FillBgrNchw(Color32[] canonicalPixels,
            int sourceWidth, int sourceHeight, float[] output)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            if (canonicalPixels == null ||
                canonicalPixels.Length != sourceWidth * sourceHeight)
                throw new ArgumentException("Canonical pixels do not match source size.",
                    nameof(canonicalPixels));

            int modelWidth = FaceLetterboxPreprocessor.ModelWidth;
            int modelHeight = FaceLetterboxPreprocessor.ModelHeight;
            int plane = modelWidth * modelHeight;
            if (output == null || output.Length != plane * 3)
                throw new ArgumentException("Expected 3-channel NCHW model-sized buffer.",
                    nameof(output));

            FaceLetterboxMetadata metadata =
                FaceLetterboxPreprocessor.Calculate(sourceWidth, sourceHeight);
            float width = metadata.ResizedWidth;
            float height = metadata.ResizedHeight;
            float left = metadata.PaddingX;
            float top = metadata.PaddingY;
            float inverseScale = 1f / metadata.Scale;
            const float norm = 1f / 255f;

            // Tensor pixel origin is top-left, while Color32[] uses bottom-left.
            // Sample at pixel centers to mimic Graphics.DrawTexture linear filtering.
            for (int y = 0; y < modelHeight; y++)
            {
                float cy = y + 0.5f;
                int row = y * modelWidth;
                if (cy < top || cy >= top + height)
                {
                    for (int x = 0; x < modelWidth; x++)
                        ZeroPixel(output, row + x, plane);
                    continue;
                }
                float sourceY = (cy - top) * inverseScale - 0.5f;
                int y0 = Mathf.FloorToInt(sourceY);
                int y1 = y0 + 1;
                float ty = sourceY - y0;
                y0 = Mathf.Clamp(y0, 0, sourceHeight - 1);
                y1 = Mathf.Clamp(y1, 0, sourceHeight - 1);
                int bottomRow0 = (sourceHeight - 1 - y0) * sourceWidth;
                int bottomRow1 = (sourceHeight - 1 - y1) * sourceWidth;

                for (int x = 0; x < modelWidth; x++)
                {
                    int i = row + x;
                    float cx = x + 0.5f;
                    if (cx < left || cx >= left + width)
                    {
                        ZeroPixel(output, i, plane);
                        continue;
                    }
                    float sourceX = (cx - left) * inverseScale - 0.5f;
                    int x0 = Mathf.FloorToInt(sourceX);
                    int x1 = x0 + 1;
                    float tx = sourceX - x0;
                    x0 = Mathf.Clamp(x0, 0, sourceWidth - 1);
                    x1 = Mathf.Clamp(x1, 0, sourceWidth - 1);

                    Color32 a = canonicalPixels[bottomRow0 + x0];
                    Color32 b = canonicalPixels[bottomRow0 + x1];
                    Color32 c = canonicalPixels[bottomRow1 + x0];
                    Color32 d = canonicalPixels[bottomRow1 + x1];

                    // ChannelSwizzle.BGRA. NCHW channels are B, G, R.
                    float b0 = Mathf.Lerp(a.b, b.b, tx);
                    float b1 = Mathf.Lerp(c.b, d.b, tx);
                    float g0 = Mathf.Lerp(a.g, b.g, tx);
                    float g1 = Mathf.Lerp(c.g, d.g, tx);
                    float r0 = Mathf.Lerp(a.r, b.r, tx);
                    float r1 = Mathf.Lerp(c.r, d.r, tx);
                    output[i] = Mathf.Lerp(b0, b1, ty) * norm;
                    output[plane + i] = Mathf.Lerp(g0, g1, ty) * norm;
                    output[2 * plane + i] = Mathf.Lerp(r0, r1, ty) * norm;
                }
            }
            return metadata;
        }

        private static void ZeroPixel(float[] output, int i, int plane)
        {
            output[i] = 0f;
            output[plane + i] = 0f;
            output[2 * plane + i] = 0f;
        }
    }
}
