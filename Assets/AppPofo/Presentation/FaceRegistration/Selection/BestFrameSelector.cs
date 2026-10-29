using UnityEngine;
using OpenCvSharp;
using AppPofo.Presentation.FaceRegistration.Alignment;
using AppPofo.Presentation.FaceRegistration.Detection;

namespace AppPofo.Presentation.FaceRegistration.Selection
{
    public sealed class BestFrameSelector
    {
        private float bestError = float.MaxValue;
        // Owns the best 112x112 aligned face, independent of the detection frame.
        private Mat? bestFaceMat;
        
        private float GetTargetError(float value, float target)
        {
            
            float error = Mathf.Abs(value-target);

            return error;
        }

        private float GetNormalizedError(float error, float maxError)
        {
            float normalizedError  = Mathf.InverseLerp(0f,maxError,error);

            return normalizedError ;
        }

        private float GetWeightedError(
            float faceSizeError,
            float rollError,
            float yawError,
            float pitchError
        )
        {
            float faceN = GetNormalizedError(faceSizeError,0.30f);
            float rollN = GetNormalizedError(rollError,20f);
            float yawN = GetNormalizedError(yawError,0.60f);
            float pitchN = GetNormalizedError(pitchError, 0.20f);

            float faceW = 0.15f;
            float rollW = 0.20f;
            float yawW = 0.40f;
            float pitchW = 0.25f;
                        
            float totalError = faceN * faceW + rollN * rollW + yawN * yawW + pitchN * pitchW;

            return totalError;
        }

        public float Evaluate(
            float faceSizeRatio,
            float rollDegrees,
            float yawScore,
            float pitchScore,
            float targetYaw
        )
        {
            float faceError = GetTargetError(faceSizeRatio,0.82f);
            float rollError = GetTargetError(rollDegrees,0f);
            float yawError = GetTargetError(yawScore,targetYaw);
            float pitchError = GetTargetError(pitchScore,0.60f);

            float totalError = GetWeightedError(faceError,rollError,yawError,pitchError);

            return totalError;
        }

        // Preserve the existing calibration caller's front score.
        public float EvaluateFront(
            float faceSizeRatio,
            float rollDegrees,
            float yawScore,
            float pitchScore
        )
        {
            return Evaluate(faceSizeRatio, rollDegrees, yawScore, pitchScore, 0.0f);
        }

        public bool TryUpdateBest(float currentError, Mat canonicalMat, FaceLandmarks5 canonicalLandmarks)
        {
            // Also reject NaN: only a strictly better error can trigger alignment.
            if (!(currentError < bestError))
                return false;

            Mat aligned = null;
            try
            {
                // Borrow the source; the detection frame remains its owner.
                if (canonicalMat == null || canonicalMat.Empty())
                    return false;

                aligned = FaceAligner.Align(canonicalMat, canonicalLandmarks);
                if (aligned == null || aligned.Empty() || aligned.Cols != 112 || aligned.Rows != 112)
                {
                    Debug.LogWarning("[FaceRegistration Alignment] Best update rejected: alignment did not return a non-empty 112x112 Mat.");
                    return false;
                }

                bestFaceMat?.Dispose();
                bestFaceMat = aligned;
                aligned = null; // Ownership transferred to the Selector, without a Clone.
                bestError = currentError;
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[FaceRegistration Alignment] Best update failed: {exception}");
                return false;
            }
            finally
            {
                aligned?.Dispose();
            }
        }

        public bool TryGetBest(out FaceBestCandidate candidate)
        {   
            candidate = default;

            if(bestFaceMat == null || bestFaceMat.Empty())
                return false;            

            candidate = new FaceBestCandidate
            {
                FaceMat = bestFaceMat.Clone(),
                Error = bestError
            };
            
            return true;
        }

        public void Reset()
        {
            bestFaceMat?.Dispose();
            bestFaceMat = null;
            bestError = float.MaxValue;
        }
    }
}
