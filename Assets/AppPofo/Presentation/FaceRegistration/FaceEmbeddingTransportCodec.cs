using System;

namespace AppPofo.Presentation.FaceRegistration
{
    /// <summary>
    /// S5-08-B transport codec only. Never posts to a server or touches SQLite.
    /// Each pose has 128 IEEE754 float32 numbers, in Front/Left/Right order.
    /// The Worker validates the result independently before encryption.
    /// </summary>
    public static class FaceEmbeddingTransportCodec
    {
        public const int PoseCount = 3;
        public const int Dimension = 128;
        public const int ByteLength = PoseCount * Dimension * sizeof(float);

        public static byte[] ToLittleEndianBytes(FaceEmbeddingSet embeddings)
        {
            if (embeddings == null)
                throw new ArgumentNullException(nameof(embeddings));

            var output = new byte[ByteLength];
            float[][] poses =
            {
                embeddings.Front,
                embeddings.Left,
                embeddings.Right
            };

            for (int poseIndex = 0; poseIndex < poses.Length; poseIndex++)
            {
                float[] values = poses[poseIndex];
                if (values.Length != Dimension)
                    throw new InvalidOperationException("얼굴 임베딩 차원이 잘못되었습니다.");

                for (int index = 0; index < values.Length; index++)
                {
                    byte[] bytes = BitConverter.GetBytes(values[index]);
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(bytes);
                    Buffer.BlockCopy(bytes, 0, output,
                        (poseIndex * Dimension + index) * sizeof(float), sizeof(float));
                }
            }

            return output;
        }

        public static string ToBase64Url(FaceEmbeddingSet embeddings)
        {
            return Convert.ToBase64String(ToLittleEndianBytes(embeddings))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
