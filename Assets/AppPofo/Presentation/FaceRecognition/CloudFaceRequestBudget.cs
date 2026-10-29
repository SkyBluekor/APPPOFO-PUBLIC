using System.Collections.Generic;

namespace AppPofo.Presentation.FaceRecognition
{
    // Account for actual dispatches as well as the inference-start pacer. Different
    // inference delays can compress HTTP calls into a shorter window than starts.
    public sealed class CloudFaceRequestBudget
    {
        private readonly Queue<double> dispatched = new Queue<double>();
        public bool TryReserve(double now)
        {
            while (dispatched.Count > 0 && now - dispatched.Peek() >= 60d) dispatched.Dequeue();
            if (dispatched.Count >= 120) return false;
            dispatched.Enqueue(now); // Failed starts also consume the conservative budget.
            return true;
        }
    }
}
