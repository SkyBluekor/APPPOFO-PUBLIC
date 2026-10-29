using System;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Application.Common;

namespace AppPofo.Application.UseCases
{
    public sealed class AddLinkUseCase
    {
        private readonly IProfileRepository _profileRepository;

        public AddLinkUseCase(IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        }

        public Profile Execute(
            long profileId,
            string title,
            string url,
            DateTimeOffset updatedAt
        )
        {
            if(profileId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(profileId),"프로필 ID는 1 이상이어야 합니다.");
            }

            Profile profile = _profileRepository.Find(profileId);

            if(profile == null)
            {
                throw new InvalidOperationException($"프로필을 찾을 수 없습니다. ProfileID: {profileId}");
            }

            string normalizedTitle = title?.Trim();
            string normalizedUrl = UrlNormalizer.NormalizeRequired(url);

            if(string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("링크 이름을 입력해주세요.",nameof(title));
            }
        
            
            int nextSortOrder = 0;
            foreach(ProfileItem existingItem in profile.Items)
            {
                if(existingItem.SortOrder >= nextSortOrder)
                {
                    nextSortOrder = existingItem.SortOrder +1 ;
                }
            }

            var linkItem = new ProfileItem(
                id: 0,
                profileId: profile.Id,
                itemType: ProfileItemType.Link,
                title: normalizedTitle,
                description: string.Empty,
                url: normalizedUrl,
                sortOrder: nextSortOrder
            );

            profile.AddItem(linkItem,updatedAt);

            return _profileRepository.Save(profile);
        }
    }
}