using System;
using System.Collections.Generic;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    /// <summary>Current-frame faces in model coordinates, paired with this inference's letterbox.</summary>
    public sealed class MultiFaceDetectionResult
    {
        public FaceLetterboxMetadata Letterbox { get; }
        public IReadOnlyList<FaceDetectionItem> Faces { get; }

        public MultiFaceDetectionResult(FaceLetterboxMetadata letterbox, IReadOnlyList<FaceDetectionItem> faces)
        {
            if (faces == null) throw new ArgumentNullException(nameof(faces));
            var copy = new FaceDetectionItem[faces.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = faces[i];
            Letterbox = letterbox;
            Faces = Array.AsReadOnly(copy);
        }
    }
}