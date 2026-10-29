using System.Collections.Generic;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.Abstractions
{
    public interface IFaceEmbeddingRepository
    {
        bool HasByProfile(long profileId);
        IReadOnlyList<FaceEmbedding> GetByModel(string modelVersion);
        // DB가 발급한 ID가 반영된 FaceEmbedding 객체를 반환합니다.
        FaceEmbedding Add(FaceEmbedding embedding);
        void DeleteByProfile(long profileId);

        void ReplaceByProfile(long profileID, IReadOnlyList<FaceEmbedding> embeddings);
    }
}
