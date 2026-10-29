using System;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Application.Common;

namespace AppPofo.Application.UseCases
{
    public sealed class AddProjectUseCase
    {
        private readonly IProfileRepository _profileRepository;
        public AddProjectUseCase(IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        }

        public Profile Execute(
            long profileId,
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

            Profile profile = _profileRepository.Find(profileId);

            if(profile == null)
            {
                throw new InvalidOperationException($"프로필을 찾을 수 없습니다. ProfileId: {profileId}");
            }

            string normalizedTitle = title?.Trim();
            string normalizedUrl = UrlNormalizer.NormalizeOptional(url);

            if(string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("프로젝트 명을 입력해주세요.",nameof(title));
            }
            int nextSordOrder = 0;

            foreach(ProfileItem existingItem in profile.Items)
            {
                if(existingItem.SortOrder >= nextSordOrder)
                {
                    nextSordOrder = existingItem.SortOrder + 1;
                }
            }

            var projectItem = new ProfileItem(
                id: 0,
                profileId: profile.Id,
                itemType: ProfileItemType.Project,
                title: normalizedTitle,
                description: description,
                url: normalizedUrl,
                sortOrder: nextSordOrder
            );

            profile.AddItem(projectItem,updatedAt);
            return _profileRepository.Save(profile);
        }
    }
}