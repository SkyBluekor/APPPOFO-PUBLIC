using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.FaceRegistration
{
    [DisallowMultipleComponent]
    public sealed class FaceSampleSlotView : MonoBehaviour
    {
        [SerializeField] private RawImage previewImage;
        [SerializeField] private GameObject emptyIndicator;
        [SerializeField] private Text indexText;

        public bool HasSample { get; private set; }

        public void SetIndex(int oneBasedIndex)
        {
            if (indexText != null)
            {
                indexText.text = oneBasedIndex.ToString();
            }
        }

        public void SetSample(Texture texture)
        {
            HasSample = texture != null;

            if (previewImage != null)
            {
                previewImage.texture = texture;
                previewImage.gameObject.SetActive(HasSample);
            }

            if (emptyIndicator != null)
            {
                emptyIndicator.SetActive(!HasSample);
            }
        }

        public void Clear()
        {
            SetSample(null);
        }
    }
}
