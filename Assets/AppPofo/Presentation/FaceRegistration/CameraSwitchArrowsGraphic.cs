using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.FaceRegistration
{
    /// <summary>Two smooth arrow arcs, generated with native UI vertices.</summary>
    [DisallowMultipleComponent]
    public sealed class CameraSwitchArrowsGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float scale = Mathf.Min(rect.width, rect.height) / 57f;
            float radius = 17f * scale;
            float halfStroke = 2.8f * scale;
            DrawArc(vh, center, radius, halfStroke, -35f, 115f);
            DrawArc(vh, center, radius, halfStroke, 145f, 295f);
        }

        private void DrawArc(VertexHelper vh, Vector2 center, float radius,
            float halfStroke, float startDegrees, float endDegrees)
        {
            const int segments = 25;
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(startDegrees, endDegrees, i / (float)segments);
                float b = Mathf.Deg2Rad * Mathf.Lerp(startDegrees, endDegrees, (i + 1f) / segments);
                Vector2 innerA = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) *
                    (radius - halfStroke);
                Vector2 outerA = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) *
                    (radius + halfStroke);
                Vector2 innerB = center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) *
                    (radius - halfStroke);
                Vector2 outerB = center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) *
                    (radius + halfStroke);
                AddQuad(vh, innerA, outerA, outerB, innerB);
            }

            float angle = Mathf.Deg2Rad * endDegrees;
            Vector2 location = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            Vector2 tangent = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            float scale = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 57f;
            Vector2 tip = location + tangent * 8f * scale;
            Vector2 baseCenter = location - tangent * 7f * scale;
            AddTriangle(vh, tip, baseCenter + normal * 7f * scale,
                baseCenter - normal * 7f * scale);
        }

        private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int first = vh.currentVertCount;
            AddVertex(vh, a); AddVertex(vh, b); AddVertex(vh, c); AddVertex(vh, d);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }

        private void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c)
        {
            int first = vh.currentVertCount;
            AddVertex(vh, a); AddVertex(vh, b); AddVertex(vh, c);
            vh.AddTriangle(first, first + 1, first + 2);
        }

        private void AddVertex(VertexHelper vh, Vector2 location)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = location;
            vh.AddVert(vertex);
        }
    }
}
