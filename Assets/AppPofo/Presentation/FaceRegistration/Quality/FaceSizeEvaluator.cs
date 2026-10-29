using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Quality
{
    public sealed class FaceSizeEvaluator
    {
        public float Evaluate(Rect faceBounds, int FrameHeight)
        {
            float faceBoundsRatio = faceBounds.height / FrameHeight;

            return faceBoundsRatio;
        }
    }
}