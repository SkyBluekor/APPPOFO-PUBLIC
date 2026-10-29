using System.Collections.Generic;
using AppPofo.Domain.Entities;

namespace AppPofo.Presentation.FaceRecognition.Matching
{
   public static class FaceRecognitionMatcher
    {
        public static bool TryFindBestCandidate(
            IReadOnlyList<float> query,
            IReadOnlyList<FaceEmbedding> registeredEmbeddings,
            out FaceRecognitionCandidate candidate
        )
        {
            candidate = null;

            Dictionary<long, float> similarities = FaceEmbeddingSimilarity.GetBestSimilaritiesByProfile(query, registeredEmbeddings);   

            if(similarities.Count == 0)
                return false;
            
            long bestProfileId = 0;
            float bestSimilarity = float.NegativeInfinity;

            foreach(var pair in similarities)
            {
                if(pair.Value > bestSimilarity)
                {
                    bestProfileId = pair.Key;
                    bestSimilarity = pair.Value;
                }
            }

            candidate = new FaceRecognitionCandidate(bestProfileId, bestSimilarity);

            return true;
        }

        public static bool TryRecognize
        (
            IReadOnlyList<float> query,
            IReadOnlyList<FaceEmbedding> registeredEmbeddings,
            float threshold,
            out FaceRecognitionResult result
        )
        {
            result = null;

            if(!TryFindBestCandidate(query, registeredEmbeddings, out FaceRecognitionCandidate candidate))
            {
                return false;
            }

            bool matched = candidate.Similarity >= threshold;

            result = new FaceRecognitionResult(matched, candidate.ProfileId, candidate.Similarity);

            return true;
        }
    }
}