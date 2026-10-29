using System;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;

namespace AppPofo.Application.UseCases
{
    public sealed class AddSkillUseCase
    {
        private readonly IProfileRepository _profileRepository;

        public AddSkillUseCase(IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        }

        public Profile Execute(
            long profileId,
            string skillTitle,
            DateTimeOffset updatedAt
        )
        {
            if(profileId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(profileId),"프로필 ID는 1 이상이어야 합니다.");
            }
            Profile profile = _profileRepository.Find(profileId);

            if (profile == null)
            {
                throw new InvalidOperationException($"프로필을 찾을 수 없습니다. ProfileID: {profileId}");                
            }
            string normalizedTitle = skillTitle?.Trim();

            if(string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("기술명을 입력해주세요.",nameof(skillTitle));
            }

            int nextSortOrder = 0;

            foreach(ProfileItem existingItem in profile.Items)
            {
                if(existingItem.SortOrder >= nextSortOrder)
                {
                    nextSortOrder = existingItem.SortOrder + 1;
                }
            }
            
                var skillItem = new ProfileItem(
                    id: 0,
                    profileId: profile.Id,
                    itemType: ProfileItemType.Skill,
                    title: normalizedTitle,
                    description: string.Empty,
                    url: string.Empty,
                    sortOrder: nextSortOrder
                );

                profile.AddItem(skillItem,updatedAt);

                return _profileRepository.Save(profile);
        }
    }

}