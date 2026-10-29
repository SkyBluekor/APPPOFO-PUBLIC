using AppPofo.Presentation.FaceRegistration.Detection;
using NUnit.Framework;
using UnityEngine;

namespace AppPofo.Tests.EditMode
{
    public sealed class FaceLetterboxPreprocessorTests
    {
        [TestCase(1920, 1080, 320, 180, 0, 70)]
        [TestCase(1080, 1920, 180, 320, 70, 0)]
        [TestCase(1000, 1000, 320, 320, 0, 0)]
        public void Calculate_PreservesAspectAndOneSidedPadding(
            int sourceWidth, int sourceHeight, int resizedWidth, int resizedHeight,
            int paddingX, int paddingY)
        {
            FaceLetterboxMetadata metadata = FaceLetterboxPreprocessor.Calculate(sourceWidth, sourceHeight);

            Assert.That(metadata.SourceWidth, Is.EqualTo(sourceWidth));
            Assert.That(metadata.SourceHeight, Is.EqualTo(sourceHeight));
            Assert.That(metadata.ModelWidth, Is.EqualTo(320));
            Assert.That(metadata.ModelHeight, Is.EqualTo(320));
            Assert.That(metadata.Scale,
                Is.EqualTo(Mathf.Min(320f / sourceWidth, 320f / sourceHeight)).Within(0.00001f));
            Assert.That(metadata.ResizedWidth, Is.EqualTo(resizedWidth).Within(0.0001f));
            Assert.That(metadata.ResizedHeight, Is.EqualTo(resizedHeight).Within(0.0001f));
            Assert.That(metadata.PaddingX, Is.EqualTo(paddingX).Within(0.0001f));
            Assert.That(metadata.PaddingY, Is.EqualTo(paddingY).Within(0.0001f));
        }

        [Test]
        public void CpuTensorPreservesBlackBarsAndTopLeftBgrOrder()
        {
            // Unity Color32[] is bottom-left first; the YuNet NCHW tensor is top-left first.
            var upright = new Color32[4 * 2];
            for (int x = 0; x < 4; x++)
            {
                upright[x] = new Color32(0, 0, 255, 255); // bottom blue
                upright[4 + x] = new Color32(255, 0, 0, 255); // top red
            }
            int plane = 320 * 320;
            var buffer = new float[plane * 3];
            var meta = FaceLetterboxCpuInput.FillBgrNchw(upright, 4, 2, buffer);
            Assert.That(meta.PaddingY, Is.EqualTo(80));
            int topPixel = 100 * 320 + 160;
            int bottomPixel = 220 * 320 + 160;
            int border = 20 * 320 + 160;

            Assert.That(buffer[topPixel], Is.EqualTo(0f).Within(0.001f)); // B
            Assert.That(buffer[plane + topPixel], Is.EqualTo(0f).Within(0.001f));
            Assert.That(buffer[2 * plane + topPixel], Is.EqualTo(1f).Within(0.001f)); // R
            Assert.That(buffer[bottomPixel], Is.EqualTo(1f).Within(0.001f)); // B
            Assert.That(buffer[2 * plane + bottomPixel], Is.EqualTo(0f).Within(0.001f));
            for (int channel = 0; channel < 3; channel++)
                Assert.That(buffer[channel * plane + border], Is.EqualTo(0f));

            Assert.That(buffer[plane * 3 - 1], Is.EqualTo(0f));
        }

        [Test]
        public void CpuTensorUsesBilinearSamplingAndRejectsInvalidBuffers()
        {
            // Pixel layout: bottom-left blue, bottom-right white,
            // top-left red, top-right green.
            var upright = new[]
            {
                new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255)
            };
            var buffer = new float[3 * 320 * 320];
            FaceLetterboxCpuInput.FillBgrNchw(upright, 2, 2, buffer);
            int plane = 320 * 320;
            int topLeft = 40 * 320 + 40;
            int topRight = 40 * 320 + 280;
            int bottomLeft = 280 * 320 + 40;
            Assert.That(buffer[2 * plane + topLeft], Is.EqualTo(1f).Within(0.02f));
            Assert.That(buffer[plane + topRight], Is.EqualTo(1f).Within(0.02f));
            Assert.That(buffer[bottomLeft], Is.EqualTo(1f).Within(0.02f));
            Assert.Throws<System.ArgumentException>(
                () => FaceLetterboxCpuInput.FillBgrNchw(upright, 2, 2, new float[99]));
            Assert.Throws<System.ArgumentException>(
                () => FaceLetterboxCpuInput.FillBgrNchw(new Color32[1], 2, 2, buffer));
        }

        [Test]
        public void Process_FillsPaddingWithBlack()
        {
            // Canonical camera textures use Clamp so bilinear filtering cannot
            // sample the opposite edge of a two-row synthetic image.
            var source = new Texture2D(4, 2, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            source.SetPixels(new[]
            {
                Color.blue, Color.blue, Color.blue, Color.blue,
                Color.red, Color.red, Color.red, Color.red
            });
            source.Apply();
            try
            {
                using (FaceLetterboxResult result = FaceLetterboxPreprocessor.Process(source))
                {
                    Assert.That(result.ModelTexture.width, Is.EqualTo(320));
                    Assert.That(result.ModelTexture.height, Is.EqualTo(320));
                    Assert.That(result.Metadata.PaddingY, Is.EqualTo(80));

                    RenderTexture previous = RenderTexture.active;
                    var pixels = new Texture2D(320, 320, TextureFormat.RGBA32, false);
                    try
                    {
                        RenderTexture.active = result.ModelTexture;
                        pixels.ReadPixels(new Rect(0, 0, 320, 320), 0, 0);
                        pixels.Apply();
                        // The contract is black padding plus correct content ordering.
                        // DrawTexture bilinear sampling / GPU color conversion can reduce
                        // saturated channel values (e.g. 0.886), so comparing against
                        // an arbitrary 0.90 magnitude is not a valid correctness check.
                        Assert.That(pixels.GetPixel(160, 20).maxColorComponent, Is.LessThan(0.01f));
                        Color bottomBlue = pixels.GetPixel(160, 100);
                        Color topRed = pixels.GetPixel(160, 220);
                        // Capture exact row colors on failure instead of repeatedly
                        // relaxing thresholds without identifying color contamination.
                        // Include the interior rows, boundary and source color so the
                        // next failing Test Runner report is actionable on its own.
                        string sampleRows = "";
                        foreach (int y in new[] { 20, 90, 100, 120, 140, 160, 180, 200, 220, 230, 300 })
                        {
                            Color c = pixels.GetPixel(160, y);
                            sampleRows += " y=" + y + ":(" +
                                c.r.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," +
                                c.g.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," +
                                c.b.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ")";
                        }
                        string sampleDiagnostic =
                            " GPU=" + SystemInfo.graphicsDeviceType +
                            " colorSpace=" + QualitySettings.activeColorSpace +
                            " sourceFilter=" + source.filterMode +
                            " sourceBottom=" + source.GetPixel(0, 0) +
                            " sourceTop=" + source.GetPixel(0, 1) +
                            " samples[x=160, y:(r,g,b)]" + sampleRows;
                        Assert.That(bottomBlue.b, Is.GreaterThan(0.75f),
                            "Bottom blue channel unexpectedly weak." + sampleDiagnostic);
                        Assert.That(bottomBlue.b - bottomBlue.r, Is.GreaterThan(0.50f),
                            "The bottom image half must remain blue." + sampleDiagnostic);
                        Assert.That(topRed.r, Is.GreaterThan(0.75f),
                            "Top red channel unexpectedly weak." + sampleDiagnostic);
                        Assert.That(topRed.r - topRed.b, Is.GreaterThan(0.50f),
                            "The top image half must remain red." + sampleDiagnostic);
                        Assert.That(pixels.GetPixel(160, 300).maxColorComponent, Is.LessThan(0.01f));
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        Object.DestroyImmediate(pixels);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }
    }
}
