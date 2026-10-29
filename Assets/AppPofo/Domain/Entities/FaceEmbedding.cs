using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AppPofo.Domain.ValueObjects;
using NUnit.Framework.Constraints;

namespace AppPofo.Domain.Entities
{
    public sealed class FaceEmbedding
    {
        private readonly ReadOnlyCollection<float> _values;

        public long Id { get; }
        public long ProfileId { get; }
        public IReadOnlyList<float> Values => _values;

        public string ModelVersion { get; }
        public float? QualityScore { get; }
        public FaceEmbeddingSourceType SourceType { get; }
        public DateTimeOffset CreatedAt { get; }

        public FaceEmbedding(
            long id,
            long profileId,
            float[] values,
            string modelVersion,
            float? qualityScore,
            FaceEmbeddingSourceType sourceType,
            DateTimeOffset createdAt
            )
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException(
                    "얼굴 임베딩은 비어 있을 수 없습니다.",
                    nameof(values));
            }

            if(string.IsNullOrWhiteSpace(modelVersion))
            {
                throw new ArgumentException(
                    "모델 버전은 비어 있을 수 없습니다.",
                    nameof(modelVersion));
            }

            Id = id;
            ProfileId = profileId;
            float[] copiedValues = (float[])values.Clone();
            _values = Array.AsReadOnly(copiedValues);

            ModelVersion = modelVersion.Trim();
            QualityScore = qualityScore;
            SourceType = sourceType;
            CreatedAt = createdAt;
        }
    }
}
