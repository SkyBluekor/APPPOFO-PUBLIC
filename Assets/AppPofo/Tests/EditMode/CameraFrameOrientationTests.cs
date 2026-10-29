using System;
using System.Linq;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRegistration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Tests.EditMode
{
    public sealed class CameraFrameOrientationTests
    {
        // An asymmetric 2x3 image, in Unity bottom-to-top pixel order:
        // top: 5 6, middle: 3 4, bottom: 1 2.
        [TestCase(0, false, "1,2,3,4,5,6")]
        [TestCase(0, true, "5,6,3,4,1,2")]
        [TestCase(90, false, "2,4,6,1,3,5")]
        [TestCase(90, true, "6,4,2,5,3,1")]
        [TestCase(180, false, "6,5,4,3,2,1")]
        [TestCase(180, true, "2,1,4,3,6,5")]
        [TestCase(270, false, "5,3,1,6,4,2")]
        [TestCase(270, true, "1,3,5,2,4,6")]
        public void PreviewUV_AndEncodedCapture_MatchKnownUprightPixels(int angle, bool mirror, string expectedText)
        {
            byte[] expected = expectedText.Split(',').Select(byte.Parse).ToArray();
            var raw = Enumerable.Range(1,6).Select(x => new Color32((byte)x,0,0,255)).ToArray();
            var orientation = new CameraFrameOrientation(angle,mirror,2,3);
            int width = angle % 180 == 0 ? 2 : 3, height = angle % 180 == 0 ? 3 : 2;
            Assert.That(orientation.EffectiveWidth,Is.EqualTo(width));
            Assert.That(orientation.EffectiveHeight,Is.EqualTo(height));
            Assert.That(orientation.Aspect,Is.EqualTo((float)width/height));
            Assert.That(orientation.UprightPixels(raw).Select(p=>p.r),Is.EqualTo(expected));
            if (orientation.IsIdentity) Assert.That(orientation.UprightPixels(raw),Is.SameAs(raw));
            for (int y=0;y<height;y++)
                for (int x=0;x<width;x++)
                {
                    Vector2 uv = orientation.SourceUV(new Vector2((x+.5f)/width,(y+.5f)/height));
                    int sourceIndex = Mathf.FloorToInt(uv.y*3)*2 + Mathf.FloorToInt(uv.x*2);
                    Assert.That(raw[sourceIndex].r,Is.EqualTo(expected[y*width+x]));
                }
            Texture2D canonical = orientation.CreateUprightTexture(raw);
            var decoded = new Texture2D(2,2);
            try
            {
                Assert.That(decoded.LoadImage(canonical.EncodeToPNG()),Is.True);
                Assert.That(decoded.width,Is.EqualTo(width));
                Assert.That(decoded.height,Is.EqualTo(height));
                Assert.That(decoded.GetPixels32().Select(p=>p.r),Is.EqualTo(expected));
            }
            finally { UnityEngine.Object.DestroyImmediate(canonical); UnityEngine.Object.DestroyImmediate(decoded); }
        }

        [TestCase(0,false)] [TestCase(0,true)]
        [TestCase(90,false)] [TestCase(90,true)]
        [TestCase(180,false)] [TestCase(180,true)]
        [TestCase(270,false)] [TestCase(270,true)]
        public void PreviewMesh_FitsWithoutChangingRectOrGuide(int angle,bool mirror)
        {
            var root = new GameObject("Preview",typeof(RectTransform),typeof(RawImage));
            var guide = new GameObject("Guide",typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(400,200);
            var effect = root.AddComponent<CameraPreviewOrientationEffect>();
            var orientation = new CameraFrameOrientation(angle,mirror,2,3);
            using (var mesh = new VertexHelper())
            {
                var vertices = new UIVertex[4];
                var uv = new[] {Vector2.zero,Vector2.up,Vector2.one,Vector2.right};
                for(int i=0;i<4;i++)
                {
                    vertices[i]=UIVertex.simpleVert;
                    vertices[i].position=Vector2.Scale(uv[i]-.5f*Vector2.one,rect.rect.size);
                    vertices[i].uv0=uv[i];
                }
                mesh.AddUIVertexQuad(vertices);
                effect.SetOrientation(orientation);
                effect.ModifyMesh(mesh);
                var corners = new UIVertex[4];
                for(int i=0;i<4;i++)
                {
                    mesh.PopulateUIVertex(ref corners[i],i);
                    Assert.That(corners[i].position.x,Is.InRange(rect.rect.xMin-.001f,rect.rect.xMax+.001f));
                    Assert.That(corners[i].position.y,Is.InRange(rect.rect.yMin-.001f,rect.rect.yMax+.001f));
                    Assert.That((Vector2)corners[i].uv0,Is.EqualTo(orientation.SourceUV(uv[i])));
                }
                float width=corners[2].position.x-corners[0].position.x;
                float height=corners[2].position.y-corners[0].position.y;
                Assert.That(width/height,Is.EqualTo(orientation.Aspect).Within(.001f));
                Assert.That(rect.sizeDelta,Is.EqualTo(new Vector2(400,200)));
                Assert.That(rect.localRotation,Is.EqualTo(Quaternion.identity));
                Assert.That(guide.transform.localRotation,Is.EqualTo(Quaternion.identity));
                effect.ClearOrientation();
                Assert.That(effect.HasOrientation,Is.False);
            }
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(guide);
        }

        [Test]
        public void InvalidMetadata_IsRejected()
        {
            Assert.Throws<ArgumentException>(()=>new CameraFrameOrientation(45,false,640,480));
            Assert.Throws<ArgumentException>(()=>new CameraFrameOrientation(0,false,0,480));
            Assert.That(CameraFrameOrientation.TryRead(null,out _),Is.False);
            Assert.Throws<ArgumentException>(()=>new CameraFrameOrientation(0,false,2,3).UprightPixels(new Color32[1]));
        }
    }
}
