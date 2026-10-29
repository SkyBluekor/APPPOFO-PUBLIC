using AppPofo.Presentation.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Tests.PlayMode
{
    public sealed class ProfileSearchPanelRenderingTests
    {
        [TestCase(500f)]
        [TestCase(720f)]
        [TestCase(900f)]
        public void RoundedBackgrounds_FillTheirBounds_AndContainControls(float width)
        {
            var prefab = Resources.Load<ProfileSearchPanelView>("ProfileSearchPanel");
            Assert.That(prefab, Is.Not.Null);
            var view = Object.Instantiate(prefab);
            try
            {
                // Build through the public API, independently of scene/database state.
                view.Bind(null, null, null, null);
                var card = (RectTransform)view.transform;
                card.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                view.RefreshGeometry();

                // RectTransform containment alone missed the original regression:
                // the generated sprite was transparent around all four straight edges.
                AssertRoundedSurface(view.GetComponent<Image>());
                foreach (string name in new[] { "ResetButton", "PreviousButton", "NextButton" })
                    AssertRoundedSurface(view.transform.Find("PageNavigation/" + name).GetComponent<Image>());

                foreach (RectTransform child in view.GetComponentsInChildren<RectTransform>())
                {
                    if (child == card) continue;
                    var corners = new Vector3[4];
                    child.GetWorldCorners(corners);
                    foreach (Vector3 corner in corners)
                    {
                        Vector3 local = card.InverseTransformPoint(corner);
                        Assert.That(local.x, Is.InRange(card.rect.xMin + 27f, card.rect.xMax - 27f), child.name);
                        Assert.That(local.y, Is.InRange(card.rect.yMin + 21f, card.rect.yMax - 21f), child.name);
                    }
                }
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        private static void AssertRoundedSurface(Image graphic)
        {
            Assert.That(graphic.type, Is.EqualTo(Image.Type.Sliced), graphic.name);
            Texture2D texture = graphic.sprite.texture;
            int middle = texture.width / 2;
            int edge = texture.width - 3;
            foreach (Vector2Int point in new[] {
                new Vector2Int(2, middle), new Vector2Int(edge, middle),
                new Vector2Int(middle, 2), new Vector2Int(middle, edge) })
                Assert.That(texture.GetPixel(point.x, point.y).a, Is.GreaterThan(.95f),
                    graphic.name + " must fill its straight edges, including the nine-slice caps.");
            Assert.That(texture.GetPixel(0, 0).a, Is.LessThan(.05f),
                graphic.name + " must retain rounded transparent corners.");
        }
    }
}
