using System;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.FaceRegistration
{
    /// <summary>
    /// Small vector-based camera flip icon and a circular AppPofo pastel backing.
    /// The icon contains no font glyphs or scaled decorative sprite sheets.
    /// The Button component and its click handler stay on the parent GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class CameraSwitchButtonArtwork : MonoBehaviour
    {
        private Texture2D backgroundTexture;
        private Sprite backgroundSprite;
        private CameraSwitchArrowsGraphic arrows;
        private Image backing;

        private void Awake()
        {
            backing = GetComponent<Image>();
            backgroundTexture = MakeBackground();
            backgroundSprite = Sprite.Create(backgroundTexture,
                new Rect(0, 0, backgroundTexture.width, backgroundTexture.height),
                new Vector2(.5f, .5f), 100f);
            backgroundSprite.name = "CameraSwitchRoundBackground";
            backing.sprite = backgroundSprite;
            backing.type = Image.Type.Simple;
            backing.preserveAspect = true;
            backing.color = Color.white;

            var shadow = GetComponent<Shadow>() ?? gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.29f, .18f, .56f, .22f);
            shadow.effectDistance = new Vector2(0f, -3f);

            var icon = new GameObject("CameraSwitchIcon",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(CameraSwitchArrowsGraphic));
            icon.layer = gameObject.layer;
            icon.transform.SetParent(transform, false);
            RectTransform rect = (RectTransform)icon.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(57f, 57f);
            arrows = icon.GetComponent<CameraSwitchArrowsGraphic>();
            arrows.raycastTarget = false;
            arrows.color = new Color(.38f, .24f, .72f, 1f);
        }

        public void SetMode(bool recognition)
        {
            if (arrows == null) return;
            RectTransform rect = (RectTransform)arrows.transform;
            Vector2 desired = recognition ? new Vector2(46f, 46f) : new Vector2(57f, 57f);
            if ((rect.sizeDelta - desired).sqrMagnitude > .01f)
                rect.sizeDelta = desired;
        }

        public void SetFacing(bool rear)
        {
            if (arrows != null)
                arrows.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rear ? 180f : 0f);
            if (backing != null) backing.color = Color.white;
            if (arrows != null) arrows.color = new Color(.38f, .24f, .72f, 1f);
        }

        public void SetBusy()
        {
            if (arrows != null) arrows.color = new Color(.49f, .46f, .62f, .57f);
        }

        public void SetFailed()
        {
            if (arrows != null) arrows.color = new Color(.72f, .21f, .34f, 1f);
        }

        private static Texture2D MakeBackground()
        {
            const int dimension = 80;
            var texture = new Texture2D(dimension, dimension, TextureFormat.RGBA32, false);
            texture.name = "CameraSwitchButtonBackground";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[dimension * dimension];
            Vector2 center = new Vector2((dimension - 1) / 2f, (dimension - 1) / 2f);
            for (int y = 0; y < dimension; y++)
            {
                for (int x = 0; x < dimension; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(39f - radius);
                    Color fill = Color.Lerp(
                        new Color(.91f, .90f, 1f, 1f),
                        new Color(.96f, .99f, 1f, 1f), (float)y / dimension);
                    Color border = new Color(.60f, .51f, .91f, 1f);
                    Color color = Color.Lerp(border, fill, Mathf.Clamp01(37f - radius));
                    color.a = alpha * .95f;
                    pixels[y * dimension + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private void OnDestroy()
        {
            if (backgroundSprite != null) Destroy(backgroundSprite);
            if (backgroundTexture != null) Destroy(backgroundTexture);
        }
    }

}
