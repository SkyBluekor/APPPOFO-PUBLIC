using AppPofo.Presentation.Avatar;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.FaceRegistration
{
    /// <summary>Fits and orients only the camera mesh inside its authored RectTransform.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(RawImage))]
    public sealed class CameraPreviewOrientationEffect : BaseMeshEffect
    {
        public CameraFrameOrientation Orientation { get; private set; }
        public bool HasOrientation { get; private set; }
        public bool AspectFill { get; private set; }

        public void SetAspectFill(bool aspectFill)
        {
            if (AspectFill == aspectFill) return;
            AspectFill = aspectFill;
            graphic.SetVerticesDirty();
        }

        // Both mesh sampling and bounding boxes use this upright source rectangle.
        public Rect VisibleUprightUVRect => AspectFill && HasOrientation
            ? CalculateFillUVRect(graphic.GetPixelAdjustedRect(), Orientation.Aspect,
                ((RawImage)graphic).uvRect)
            : ((RawImage)graphic).uvRect;

        public static Rect CalculateFillUVRect(Rect displayRect, float sourceAspect, Rect uvRect)
        {
            if (displayRect.width <= 0 || displayRect.height <= 0 || sourceAspect <= 0 ||
                uvRect.width <= 0 || uvRect.height <= 0) return uvRect;
            float displayAspect = displayRect.width / displayRect.height;
            float sampledAspect = sourceAspect * uvRect.width / uvRect.height;
            Vector2 size = uvRect.size;
            if (sampledAspect > displayAspect) size.x *= displayAspect / sampledAspect;
            else size.y *= sampledAspect / displayAspect;
            return new Rect(uvRect.center - size * .5f, size);
        }

        public static bool TryMapUprightBounds(Rect uprightBounds, Rect visibleUV, out Rect anchors)
        {
            float xMin = Mathf.Max(uprightBounds.xMin, visibleUV.xMin);
            float yMin = Mathf.Max(uprightBounds.yMin, visibleUV.yMin);
            float xMax = Mathf.Min(uprightBounds.xMax, visibleUV.xMax);
            float yMax = Mathf.Min(uprightBounds.yMax, visibleUV.yMax);
            anchors = default;
            if (visibleUV.width <= 0 || visibleUV.height <= 0 || xMax <= xMin || yMax <= yMin)
                return false;
            anchors = Rect.MinMaxRect((xMin - visibleUV.xMin) / visibleUV.width,
                (yMin - visibleUV.yMin) / visibleUV.height,
                (xMax - visibleUV.xMin) / visibleUV.width,
                (yMax - visibleUV.yMin) / visibleUV.height);
            return true;
        }

        public void SetOrientation(CameraFrameOrientation orientation)
        {
            if (HasOrientation && Orientation.Equals(orientation)) return;
            Orientation = orientation;
            HasOrientation = true;
            graphic.SetVerticesDirty();
        }

        public void ClearOrientation()
        {
            if (!HasOrientation) return;
            HasOrientation = false;
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || !HasOrientation) return;
            Rect bounds = graphic.GetPixelAdjustedRect();
            if (bounds.width <= 0 || bounds.height <= 0) return;
            float width = Mathf.Min(bounds.width, bounds.height * Orientation.Aspect);
            float height = width / Orientation.Aspect;
            var scale = new Vector2(width / bounds.width, height / bounds.height);
            Rect visibleUV = VisibleUprightUVRect;
            Rect rawUV = ((RawImage)graphic).uvRect;
            var vertex = new UIVertex();
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                Vector2 offset = (Vector2)vertex.position - bounds.center;
                if (!AspectFill)
                    vertex.position = bounds.center + Vector2.Scale(offset, scale);
                Vector2 uprightUV = vertex.uv0;
                if (AspectFill && rawUV.width > 0 && rawUV.height > 0)
                    uprightUV = visibleUV.min + Vector2.Scale(visibleUV.size, new Vector2(
                        (uprightUV.x - rawUV.x) / rawUV.width,
                        (uprightUV.y - rawUV.y) / rawUV.height));
                vertex.uv0 = Orientation.SourceUV(uprightUV);
                mesh.SetUIVertex(vertex, i);
            }
        }
    }
}
