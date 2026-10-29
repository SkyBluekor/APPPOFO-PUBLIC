namespace AppPofo.Presentation.FaceRecognition.Matching
{
    public sealed class FaceRecognitionCandidate
    {
        public long ProfileId { get; }
        public float Similarity { get; }

        public FaceRecognitionCandidate(
            long profileId,
            float similarity)
        {
            ProfileId = profileId;
            Similarity = similarity;
        }
    }
}