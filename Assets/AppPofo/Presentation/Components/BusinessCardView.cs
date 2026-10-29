using System.Collections.Generic;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using UnityEngine;
using UnityEngine.UI;

namespace AppPofo.Presentation.Components
{
    public sealed class BusinessCardView : MonoBehaviour
    {
        [SerializeField] private Text displayNameText;
        [SerializeField] private Text headlineText;
        [SerializeField] private Text skillText;
        [SerializeField] private Image avatarImage;
        [SerializeField] private Text emptyAvatarText;        
        private Texture2D avatarTexture;
        private Sprite avatarSprite;        
        private string displayedCloudAvatar;
        private bool cloudAvatarBound;

        public Transform AvatarDisplayRoot => avatarImage.transform.parent;
        internal void HideSkillsForSummary()
        {
            if (skillText != null) skillText.gameObject.SetActive(false);
            if (emptyAvatarText != null) emptyAvatarText.text = "사진 없음";
        }

        public void Bind(Profile profile , byte[] avatarBytes = null)
        {
            cloudAvatarBound = false;
            if(profile == null)
            {
                Debug.LogError("[BusinessCardView] profile이 null입니다");
                return;
            }

            ConfigureText(displayNameText, 22, 38);
            ConfigureText(headlineText, 18, 28);
            ConfigureText(skillText, 16, 24);
            displayNameText.text = profile.DisplayName;
            headlineText.text = profile.Headline;

            var skillNames = new List<string>();

            foreach ( ProfileItem item in profile.Items)
            {
                if(item.ItemType == ProfileItemType.Skill)
                {
                    skillNames.Add(item.Title);
                }
            }
            skillText.text = skillNames.Count > 0 ? string.Join(" · ",skillNames) : "등록된 기술이 없습니다";

            ShowAvatar(avatarBytes);
        }

        // Server recognition returns ONLY fields approved for public display.
        // Never look up a cloud UUID in the local SQLite repository.
        public void BindCloudPublic(string displayName, string headline, string avatarBase64 = null)
        {
            ConfigureText(displayNameText, 22, 38);
            ConfigureText(headlineText, 18, 28);
            ConfigureText(skillText, 16, 24);
            if (displayNameText != null) displayNameText.text = displayName ?? "";
            if (headlineText != null) headlineText.text = headline ?? "";
            if (skillText != null) skillText.text = ""; // Not in server public card contract.
            if (!cloudAvatarBound || displayedCloudAvatar != avatarBase64)
                ShowAvatar(AppPofo.Presentation.Common.CloudProfileAvatar.Decode(avatarBase64));
            displayedCloudAvatar = avatarBase64; cloudAvatarBound = true;
        }

        public void BindCloud(AppPofo.Presentation.Common.CloudProfileDetail profile)
        {
            BindCloudPublic(profile.display_name, profile.headline, profile.avatar_base64);
            var skills = new List<string>();
            foreach (var item in profile.items ?? System.Array.Empty<AppPofo.Presentation.Common.CloudProfileItem>())
                if (item.item_type == "SKILL") skills.Add(item.title);
            if (skillText != null) skillText.text = skills.Count == 0 ? "등록된 기술이 없습니다" : string.Join(" · ", skills);
        }

        private static void ConfigureText(Text text, int minimum, int maximum)
        {
            if (text == null) return;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = minimum;
            text.resizeTextMaxSize = maximum;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void ShowAvatar(byte[] avatarBytes)
        {
            ClearAvatar();

            if(avatarBytes == null || avatarBytes.Length == 0)
            {
                avatarImage.sprite = null;
                avatarImage.color = new Color(1f,1f,1f,0f);

                if(emptyAvatarText != null)
                    emptyAvatarText.gameObject.SetActive(true);

                return;
            }

            Texture2D texture = new Texture2D(2,2);

            if(!texture.LoadImage(avatarBytes))
            {
                Destroy(texture);

                Debug.LogError("[BusinessCardView] 아바타 이미지를 불러오지 못했습니다.");

                return;
            }

            avatarTexture = texture;

            avatarSprite = Sprite.Create(avatarTexture,new Rect(0,0,avatarTexture.width,avatarTexture.height),new Vector2(0.5f,0.5f));

            avatarImage.sprite = avatarSprite;

            avatarImage.color = Color.white;

            avatarImage.preserveAspect = true;

            if(emptyAvatarText != null)
                emptyAvatarText.gameObject.SetActive(false);
        }

        private void ClearAvatar()
        {
            if(avatarSprite != null)
            {
                Destroy(avatarSprite);
                avatarSprite = null;
            }

            if(avatarTexture != null)
            {
                Destroy(avatarTexture);
                avatarTexture = null;
            }
        }

        private void OnDestroy()
        {
            ClearAvatar();
        }
    }
}
