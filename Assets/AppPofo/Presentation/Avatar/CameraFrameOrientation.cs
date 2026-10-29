using System;
using UnityEngine;

namespace AppPofo.Presentation.Avatar
{
    /// <summary>Raw sensor -> upright, non-selfie image. Pixel arrays use Unity's bottom-left origin.</summary>
    public readonly struct CameraFrameOrientation : IEquatable<CameraFrameOrientation>
    {
        public int RotationAngle { get; }
        public bool VerticallyMirrored { get; }
        public int SourceWidth { get; }
        public int SourceHeight { get; }
        public int EffectiveWidth => RotationAngle % 180 == 0 ? SourceWidth : SourceHeight;
        public int EffectiveHeight => RotationAngle % 180 == 0 ? SourceHeight : SourceWidth;
        public float Aspect => (float)EffectiveWidth / EffectiveHeight;
        public bool IsIdentity => RotationAngle == 0 && !VerticallyMirrored;

        public CameraFrameOrientation(int rotationAngle, bool verticallyMirrored, int width, int height)
        {
            int normalized = (rotationAngle % 360 + 360) % 360;
            if (normalized % 90 != 0 || width <= 0 || height <= 0)
                throw new ArgumentException("Camera orientation requires quarter turns and positive dimensions.");
            RotationAngle = normalized;
            VerticallyMirrored = verticallyMirrored;
            SourceWidth = width;
            SourceHeight = height;
        }

        // The caller must establish receipt of a real frame (didUpdateThisFrame/updateCount).
        public static bool TryRead(WebCamTexture camera, out CameraFrameOrientation orientation)
        {
            orientation = default;
            if (camera == null || !camera.isPlaying || camera.width <= 16 || camera.height <= 16)
                return false;
            int rotation = camera.videoRotationAngle;
            if (rotation % 90 != 0) return false;
            orientation = new CameraFrameOrientation(rotation, camera.videoVerticallyMirrored,
                camera.width, camera.height);
            return true;
        }

        // Inverse sampling: undo the clockwise display rotation, then sample the
        // vertically corrected sensor. Capture uses the identical ordering.
        public Vector2 SourceUV(Vector2 uprightUV)
        {
            Vector2 raw;
            switch (RotationAngle)
            {
                case 90: raw = new Vector2(1 - uprightUV.y, uprightUV.x); break;
                case 180: raw = Vector2.one - uprightUV; break;
                case 270: raw = new Vector2(uprightUV.y, 1 - uprightUV.x); break;
                default: raw = uprightUV; break;
            }
            if (VerticallyMirrored) raw.y = 1 - raw.y;
            return raw;
        }

        public Color32[] UprightPixels(Color32[] raw)
        {
            if (raw == null || raw.Length != SourceWidth * SourceHeight)
                throw new ArgumentException("Camera pixels do not match orientation dimensions.", nameof(raw));
            if (IsIdentity) return raw;
            var result = new Color32[raw.Length];
            for (int y = 0; y < EffectiveHeight; y++)
                for (int x = 0; x < EffectiveWidth; x++)
                {
                    int sx, sy;
                    switch (RotationAngle)
                    {
                        case 90: sx = SourceWidth - 1 - y; sy = x; break;
                        case 180: sx = SourceWidth - 1 - x; sy = SourceHeight - 1 - y; break;
                        case 270: sx = y; sy = SourceHeight - 1 - x; break;
                        default: sx = x; sy = y; break;
                    }
                    if (VerticallyMirrored) sy = SourceHeight - 1 - sy;
                    result[y * EffectiveWidth + x] = raw[sy * SourceWidth + sx];
                }
            return result;
        }

        public Texture2D CreateUprightTexture(Color32[] raw)
        {
            Color32[] pixels = UprightPixels(raw);
            var texture = new Texture2D(EffectiveWidth, EffectiveHeight, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        public bool Equals(CameraFrameOrientation other) => RotationAngle == other.RotationAngle &&
            VerticallyMirrored == other.VerticallyMirrored && SourceWidth == other.SourceWidth && SourceHeight == other.SourceHeight;
        public override bool Equals(object obj) => obj is CameraFrameOrientation other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RotationAngle, VerticallyMirrored, SourceWidth, SourceHeight);
    }
}
