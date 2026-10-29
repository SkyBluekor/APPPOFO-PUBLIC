using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    // References the same ConceptD artwork and Pretendard fonts as Bootstrap.
    public sealed class CloudPanelTheme : ScriptableObject
    {
        public Sprite background, primaryButton, secondaryButton, card;
        public Sprite brandLogo;
        public Sprite summaryPanel, summaryInfo, summaryProject, summarySkill;
        public Font regularFont, boldFont;
        public static CloudPanelTheme Current => Resources.Load<CloudPanelTheme>("CloudPanelTheme");

        public void Surface(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = Color.white;
        }

        public void StyleButton(Button button, bool primary)
        {
            var image = button.GetComponent<Image>();
            button.targetGraphic = image;
            Surface(image, primary ? primaryButton : secondaryButton);
            var colors = button.colors;
            colors.highlightedColor = new Color(.94f, .96f, 1f);
            colors.pressedColor = new Color(.8f, .83f, .96f);
            button.colors = colors;
        }
    }
}
