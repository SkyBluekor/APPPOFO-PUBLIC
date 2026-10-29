namespace AppPofo.Presentation.FaceRecognition.Matching
{
    public sealed class FaceRecognitionResult
    {
        public bool Matched {get;}
        public long ProfileId {get;}
        public float Similarity {get;}

        public FaceRecognitionResult(
            bool matched,
            long profileId,
            float similarity
        )
        {
            Matched = matched;
            ProfileId = profileId;
            Similarity = similarity;
        }
    }
}