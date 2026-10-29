using System;
using System.Linq;
using AppPofo.Domain.Entities;
using UnityEngine;
using UnityEngine.UI;
using AppPofo.Domain.ValueObjects;
using AppPofo.Presentation.Common;
using AppPofo.Application.UseCases;

// 프로필 리스트 카드에 프로필 기본 정보, 소개, 기술 스택을 표시하고
// 상세 화면 및 명함 화면으로 이동하는 클릭 이벤트를 연결합니다.

namespace AppPofo.Presentation.Components
{
    public sealed class ProfileListItemView : MonoBehaviour
    {
        [SerializeField] private Text displayNameText;
        [SerializeField] private Text headlineText;
        [SerializeField] private Button button;
        [SerializeField] private Button businessCardButton;

        [SerializeField] private Image avatarImage;
        [SerializeField] private Text bioText;

        [SerializeField] private GameObject[] skillChips;
        [SerializeField] private Text[] skillTexts;
        [SerializeField] private Image[] skillIcons;

        private Texture2D avatarTexture;
        private Sprite avatarSprite;

        public long ProfileId { get; private set; }

        // Profile 데이터를 리스트 카드에 표시합니다.
        public void Bind(
            Profile profile,
            Action<long> openProfileAction,
            Action<long> openBusinessCardAction,
            LoadAvatarUseCase loadAvatarUseCase)
        {
            ProfileId = profile.Id;

            displayNameText.text = profile.DisplayName;
            headlineText.text = profile.Headline;
            LoadAvatar(profile.AvatarPath, loadAvatarUseCase);

            bioText.text = string.IsNullOrWhiteSpace(profile.Bio)
                ? string.Empty
                : profile.Bio;

            // 등록된 기술을 정렬한 뒤 최대 표시 가능한 개수만 가져옵니다.
            int maxSkillCount = Math.Min(skillChips.Length, skillTexts.Length);

            var skills = profile.Items
                .Where(item => item.ItemType == ProfileItemType.Skill)
                .OrderBy(item => item.SortOrder)
                .Take(maxSkillCount)
                .ToList();

            // 기술이 없는 남는 Chip은 숨깁니다.
            for (int i = 0; i < skillChips.Length; i++)
            {
                bool hasSkill = i < skills.Count && i < skillTexts.Length;

                skillChips[i].SetActive(hasSkill);

                if (!hasSkill)
                    continue;

                skillTexts[i].text = skills[i].Title;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(
                () => openProfileAction?.Invoke(ProfileId));

            businessCardButton.onClick.RemoveAllListeners();
            businessCardButton.interactable = openBusinessCardAction != null;
            businessCardButton.onClick.AddListener(
                () => openBusinessCardAction?.Invoke(ProfileId));
        }

        // Server cards do not have local SQLite IDs, avatar files or face embeddings.
        // A cloud UUID is only passed to the authenticated cloud-detail handler.
        public void BindCloud(CloudProfileSummary profile, Action<string> openCloudDetail,
            Action<string> openCloudCard = null)
        {
            ProfileId = 0;
            displayNameText.text = profile.display_name ?? "";
            headlineText.text = profile.headline ?? "";
            bioText.text = "서버에 저장된 프로필";
            ReleaseAvatar();
            ShowAvatar(CloudProfileAvatar.Decode(profile.avatar_base64));
            foreach (var chip in skillChips)
                if (chip != null) chip.SetActive(false);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => openCloudDetail?.Invoke(profile.id));
            // A cloud profile cannot be opened through the local long-ID business card.
            businessCardButton.onClick.RemoveAllListeners();
            businessCardButton.interactable = openCloudCard != null;
            if (openCloudCard != null) businessCardButton.onClick.AddListener(() => openCloudCard(profile.id));
        }

        // 저장된 Avatar 데이터를 Texture와 Sprite로 변환하여 표시합니다.
        private void LoadAvatar(
            string avatarPath,
            LoadAvatarUseCase loadAvatarUseCase)
        {
            ShowAvatar(loadAvatarUseCase?.Execute(avatarPath));
        }

        private void ShowAvatar(byte[] avatarBytes)
        {
            ReleaseAvatar();

            if (avatarImage == null)
                return;

            if (avatarBytes == null || avatarBytes.Length == 0)
            {
                avatarImage.sprite = null;
                avatarImage.enabled = false;
                return;
            }

            avatarTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!avatarTexture.LoadImage(avatarBytes))
            {
                Destroy(avatarTexture);
                avatarTexture = null;

                avatarImage.sprite = null;
                avatarImage.enabled = false;
                return;
            }

            float cropSize = Mathf.Min(
                avatarTexture.width,
                avatarTexture.height);

            float cropX =
                (avatarTexture.width - cropSize) * 0.5f;

            float cropY =
                (avatarTexture.height - cropSize) * 0.5f;

            avatarSprite = Sprite.Create(
                avatarTexture,
                new Rect(
                    cropX,
                    cropY,
                    cropSize,
                    cropSize),
                new Vector2(0.5f, 0.5f));

            avatarImage.sprite = avatarSprite;
            avatarImage.color = Color.white;
            avatarImage.preserveAspect = false;
            avatarImage.enabled = true;
        }
        // 리스트 아이템이 다시 바인딩되거나 제거될 때 Avatar 메모리를 해제합니다.
        private void ReleaseAvatar()
        {
            if (avatarImage != null)
                avatarImage.sprite = null;

            if (avatarSprite != null)
            {
                Destroy(avatarSprite);
                avatarSprite = null;
            }

            if (avatarTexture != null)
            {
                Destroy(avatarTexture);
                avatarTexture = null;
            }
        }
        // Prefab 인스턴스가 제거될 때 생성한 Texture/Sprite를 정리합니다.
        private void OnDestroy()
        {
            ReleaseAvatar();
        }        
    }
}
