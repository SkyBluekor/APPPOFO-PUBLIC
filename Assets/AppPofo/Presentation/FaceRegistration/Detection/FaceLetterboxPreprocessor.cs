using System;
using UnityEngine;

namespace AppPofo.Presentation.FaceRegistration.Detection
{
    public readonly struct FaceLetterboxMetadata
    {
        public int SourceWidth { get; }
        public int SourceHeight { get; }
        public int ModelWidth { get; }
        public int ModelHeight { get; }
        public float Scale { get; }
        // PaddingX is the left inset; PaddingY is the top inset.
        public float PaddingX { get; }
        public float PaddingY { get; }
        public float ResizedWidth => SourceWidth * Scale;
        public float ResizedHeight => SourceHeight * Scale;

        internal FaceLetterboxMetadata(int sourceWidth, int sourceHeight, int modelWidth, int modelHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || modelWidth <= 0 || modelHeight <= 0)
                throw new ArgumentException("Letterbox dimensions must be positive.");

            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            ModelWidth = modelWidth;
            ModelHeight = modelHeight;
            Scale = Mathf.Min((float)modelWidth / sourceWidth, (float)modelHeight / sourceHeight);
            PaddingX = (modelWidth - sourceWidth * Scale) * 0.5f;
            PaddingY = (modelHeight - sourceHeight * Scale) * 0.5f;
        }
    }

    public sealed class FaceLetterboxResult : IDisposable
    {
        public RenderTexture ModelTexture { get; }
        public FaceLetterboxMetadata Metadata { get; }

        internal FaceLetterboxResult(RenderTexture modelTexture, FaceLetterboxMetadata metadata)
        {
            ModelTexture = modelTexture;
            Metadata = metadata;
        }

        public void Dispose() => RenderTexture.ReleaseTemporary(ModelTexture);
    }

    public static class FaceLetterboxPreprocessor
    {
        public const int ModelWidth = 320;
        public const int ModelHeight = 320;

        public static FaceLetterboxMetadata Calculate(int sourceWidth, int sourceHeight) =>
            new FaceLetterboxMetadata(sourceWidth, sourceHeight, ModelWidth, ModelHeight);

        public static FaceLetterboxResult Process(Texture canonicalTexture)
        {
            if (canonicalTexture == null) throw new ArgumentNullException(nameof(canonicalTexture));

            FaceLetterboxMetadata metadata = Calculate(canonicalTexture.width, canonicalTexture.height);
            RenderTexture modelTexture = RenderTexture.GetTemporary(
                ModelWidth, ModelHeight, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            bool matrixPushed = false;
            try
            {
                RenderTexture.active = modelTexture;
                GL.Clear(true, true, Color.black);
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadPixelMatrix(0, ModelWidth, ModelHeight, 0);
                Graphics.DrawTexture(new Rect(metadata.PaddingX, metadata.PaddingY,
                    metadata.ResizedWidth, metadata.ResizedHeight), canonicalTexture);
                return new FaceLetterboxResult(modelTexture, metadata);
            }
            catch
            {
                RenderTexture.ReleaseTemporary(modelTexture);
                throw;
            }
            finally
            {
                if (matrixPushed) GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }
    }
}
