using System;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Infrastructure.SQLite.Rows;
using AppPofo.Infrastructure.SQLite.Serialization;

namespace AppPofo.Infrastructure.SQLite.Mappers
{
    public static class FaceEmbeddingRowMapper
    {
        public static FaceEmbeddingRow ToRow(FaceEmbedding embedding)
        {
            if(embedding == null)
            {
                throw new ArgumentNullException(nameof(embedding));
            }

            return new FaceEmbeddingRow
            {
                Id = embedding.Id,
                ProfileId = embedding.ProfileId,
                EmbeddingBlob = Float32BlobConverter.ToLittleENdianBytes(embedding.Values),
                EmbeddingDim = embedding.Values.Count,
                ModelVersion = embedding.ModelVersion,
                QualityScore = embedding.QualityScore,
                SourceType = ToDatabaseSourceType(embedding.SourceType),
                CreatedAt = embedding.CreatedAt.ToUnixTimeMilliseconds()
            };
        }

        public static FaceEmbedding ToDomain(FaceEmbeddingRow row)
        {
            if(row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            float[] values = Float32BlobConverter.FromLittleEndianBytes(row.EmbeddingBlob, row.EmbeddingDim);

            return new FaceEmbedding(
                id: row.Id,
                profileId: row.ProfileId,
                values: values,
                modelVersion: row.ModelVersion,
                qualityScore: row.QualityScore,
                sourceType: ToDomainSourceType(row.SourceType),
                createdAt: DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt)
            );
        }

        private static string ToDatabaseSourceType(FaceEmbeddingSourceType sourceType)
        {
            return sourceType switch
            {
                FaceEmbeddingSourceType.Camera => "CAMERA",
                FaceEmbeddingSourceType.Image => "IMAGE",
                _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, "지원하지 않는 얼굴 임베딩 출처입니다.")
            };
        }

        private static FaceEmbeddingSourceType ToDomainSourceType(string value)
        {
            return value switch
            {
                "CAMERA" => FaceEmbeddingSourceType.Camera,
                "IMAGE" => FaceEmbeddingSourceType.Image,
                _ => throw new InvalidOperationException($"DB에 알 수 없는 source_type 값이 있습니다: '{value}'")
            };
        }
    }
}