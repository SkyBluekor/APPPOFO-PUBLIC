using OpenCvSharp;

namespace AppPofo.Presentation.FaceRegistration.Selection
{
    public struct FaceBestCandidate
    {
        // Owns an independent Clone of the selected 112x112 SFace-aligned face.
        public Mat FaceMat;
        public float Error;
    }
}
