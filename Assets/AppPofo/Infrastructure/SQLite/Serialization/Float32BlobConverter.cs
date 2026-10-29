using System;
using System.Collections.Generic;

namespace AppPofo.Infrastructure.SQLite.Serialization
{
    public static class Float32BlobConverter
    {
        public static byte[] ToLittleENdianBytes(
            IReadOnlyList<float> values)
        {
            if(values == null || values.Count == 0)
            {
                throw new ArgumentException("임베딩 값은 비어 있을 수 없습니다.", nameof(values));
            }

            var floatValues = new float[values.Count];

            for (int i = 0 ; i < values.Count; i++)
            {
                floatValues[i] = values[i];
            }

            byte[] bytes = new byte[floatValues.Length * sizeof(float)];

            Buffer.BlockCopy(floatValues,0,bytes,0,bytes.Length);

            if(!BitConverter.IsLittleEndian)
            {
                ReverseFloatByteOrder(bytes);
            }
            return bytes;
        }        

        public static float[] FromLittleEndianBytes(byte[] bytes, int embeddingDim)
        {
            if(bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException("임베딩 값은 비어 있을 수 없습니다.", nameof(bytes));
            }

            if(embeddingDim <= 0)
            {
                throw new ArgumentException("임베딩 차원은 0보다 커야 합니다.", nameof(embeddingDim));
            }

            int expectedByteLength = checked(embeddingDim * sizeof(float));

            if(bytes.Length != expectedByteLength)
            {
                throw new ArgumentException(
                    $"임베딩 BLOB 크기가 올바르지 않습니다." +
                    $"예상: {expectedByteLength} bytes, " +
                    $"실제: {bytes.Length} bytes", nameof(bytes));                
            }

            byte[] copiedBytes = (byte[])bytes.Clone();

            if(!BitConverter.IsLittleEndian)
            {
                ReverseFloatByteOrder(copiedBytes);
            }

            var values = new float[embeddingDim];

            Buffer.BlockCopy(copiedBytes, 0, values, 0, copiedBytes.Length);
            return values;
        }

        private static void ReverseFloatByteOrder(byte[] bytes)
        {
            for (int offset = 0; offset < bytes.Length; offset += sizeof(float))
            {
                Array.Reverse(bytes, offset, sizeof(float));
            }
        }
    }
}