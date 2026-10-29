using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AppPofo.Domain.Entities;

namespace AppPofo.Presentation.FaceRecognition.Matching
{
    public static class FaceEmbeddingSimilarity
    {
        public static float CosineSimilarity(
            IReadOnlyList<float> a,
            IReadOnlyList<float> b
        )
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));

            if (b == null)
                throw new ArgumentNullException(nameof(b));

            if (a.Count != b.Count)
                throw new ArgumentException(
                    "두 임베딩의 차원 수가 같아야 합니다.");

            if (a.Count != 128)
                throw new ArgumentException(
                    "SFace 임베딩은 128차원이어야 합니다.");

            float sum = 0f;

            for(int i = 0 ; i < a.Count ; i++)
            {
                sum += a[i] * b[i];
            }

            return sum;
        }
    

        public static float GetBestSimilarity(
            IReadOnlyList<float> query,
            IReadOnlyList<FaceEmbedding> registeredEmbeddings)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            if (registeredEmbeddings == null)
                throw new ArgumentNullException(nameof(registeredEmbeddings));

            if (registeredEmbeddings.Count == 0)
                throw new ArgumentException(
                    "비교할 등록 임베딩이 없습니다.",
                    nameof(registeredEmbeddings));

            float best = float.NegativeInfinity;

            foreach (FaceEmbedding embedding in registeredEmbeddings)
            {
                float score =
                    CosineSimilarity(query, embedding.Values);

                best = Mathf.Max(best, score);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                $"등록 임베딩 {registeredEmbeddings.Count}개 비교, Best={best:F4}"
            );
#endif

            return best;
        }
        
        public static Dictionary<long, float> GetBestSimilaritiesByProfile(
            IReadOnlyList<float> query,
            IReadOnlyList<FaceEmbedding> registeredEmbeddings
        )
        {
            if(query == null)
                throw new ArgumentNullException(nameof(query));

            if(registeredEmbeddings == null)                
                throw new ArgumentNullException(nameof(registeredEmbeddings));

            Dictionary<long, float> results = new Dictionary<long, float>();

            var groups = registeredEmbeddings.GroupBy(embedding => embedding.ProfileId);

            foreach(var group in groups)
            {
                long profileId = group.Key;

                float best = 
                    GetBestSimilarity(query, group.ToList());
                
                results.Add(profileId,best);
            }

            return results;
        }
    }
}
