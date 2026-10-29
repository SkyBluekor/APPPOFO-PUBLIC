using System;
using AppPofo.Application.Abstractions;
using AppPofo.Application.Common;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;

namespace AppPofo.Application.UseCases
{
    public sealed class UpdateProfileItemUseCase
    {
        private readonly IProfileRepository _profileRepository;

        public UpdateProfileItemUseCase(IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        }

        public Profile Execute(
            long profileId,
            long itemId,
            string title,
            string description,
            string url,
            DateTimeOffset updatedAt
        )
        {
            if(profileId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(profileId),"프로필 ID는 1 이상이어야 합니다.");
            }

            if(itemId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId),"프로필 항목 ID는 1 이상이어야 합니다.");
            }

            Profile profile = _profileRepository.Find(profileId);

            if(profile == null)
            {
                throw new InvalidOperationException($"프로필을 찾을 수 없습니다. ProfileId: {profileId}");
            }

            ProfileItem existingItem = null;

            foreach (ProfileItem item in profile.Items)
            {
                if(item.Id == itemId)
                {
                    existingItem = item;
                    break;
                }
            }

            if(existingItem == null)
            {
                throw new InvalidOperationException($"수정할 프로필 항목을 찾을 수 없습니다. ItemId: {itemId}");
            }

            string normalizedTitle = title?.Trim();

            if(string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("제목을 입력해주세요.",nameof(title));
            }

            string normalizedDescription;
            string normalizedUrl;

            switch(existingItem.ItemType)
            {
                case ProfileItemType.Skill:
                    normalizedDescription = string.Empty;
                    normalizedUrl = string.Empty;
                    break;
                
                case ProfileItemType.Project:
                    normalizedDescription = description?.Trim() ?? string.Empty;
                    normalizedUrl = UrlNormalizer.NormalizeOptional(url);
                    break;
                
                case ProfileItemType.Link:
                    normalizedDescription = string.Empty;
                    normalizedUrl = UrlNormalizer.NormalizeRequired(url);
                    break;
                
                default:
                    throw new InvalidOperationException($"지원하지 않는 프로필 항목 유형입니다. {existingItem.ItemType}");
            }

            var replacement = new ProfileItem(
                id: existingItem.Id,
                profileId: existingItem.ProfileId,
                itemType: existingItem.ItemType,
                title: normalizedTitle,
                description: normalizedDescription,
                url: normalizedUrl,
                sortOrder: existingItem.SortOrder
            );

            profile.ReplaceItem(replacement, updatedAt);

            return _profileRepository.Save(profile);
        }
    }
}