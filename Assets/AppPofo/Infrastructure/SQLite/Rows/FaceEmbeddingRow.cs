using System;
using SQLite;

namespace AppPofo.Infrastructure.SQLite.Rows
{
    [Table("face_embeddings")]
    public sealed class FaceEmbeddingRow
    {
        [PrimaryKey, AutoIncrement]
        [Column("id")]
        public long Id { get; set; }

        [Column("profile_id")]
        public long ProfileId { get; set; }

        [Column("embedding_blob")]
        public byte[] EmbeddingBlob { get; set; } = Array.Empty<byte>();

        [Column("embedding_dim")]
        public int EmbeddingDim { get; set; }

        [Column("model_version")]
        public string ModelVersion { get; set; } = string.Empty;

        [Column("quality_score")]
        public float? QualityScore { get; set; }

        [Column("source_type")]
        public string SourceType { get; set; } = string.Empty;

        [Column("created_at")]
        public long CreatedAt { get; set; }
    }
}