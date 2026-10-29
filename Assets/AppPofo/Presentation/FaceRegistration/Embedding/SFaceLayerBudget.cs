using System;
using System.Collections;

namespace AppPofo.Presentation.FaceRegistration.Embedding
{
    /// <summary>Advances a model schedule by at most N layers, without reading GPU outputs.</summary>
    public static class SFaceLayerBudget
    {
        // True only after MoveNext signals the whole model has been scheduled.
        public static bool Advance(IEnumerator schedule, int maxLayers)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (maxLayers < 1 || maxLayers > 64)
                throw new ArgumentOutOfRangeException(nameof(maxLayers));
            for (int i = 0; i < maxLayers; i++)
                if (!schedule.MoveNext()) return true;
            return false;
        }
    }
}
