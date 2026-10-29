using System;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.UseCases
{
    public sealed class UpdateProfileUseCase
    {
        private readonly IProfileRepository _profileRepository;

        public UpdateProfileUseCase(IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        }

        public Profile Execute
        (
            long profileId,
            string displayName,
            string headline,
            string bio,
            string email,
            string website,
            DateTimeOffset updatedAt
        )
        {
            if (profileId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(profileId),"프로필 ID는 1 이상이어야 함");
            }
            Profile profile = _profileRepository.Find(profileId);

            if(profile == null)
            {
                throw new InvalidOperationException($"수정할 프로필을 찾을 수 없습니다. ProfileId: {profileId}");
            }

            profile.UpdateBasicInfo(
                displayName,
                headline,
                bio,
                profile.AvatarPath,
                email,
                website,
                updatedAt
            );
            return _profileRepository.Save(profile);
        }
    }
}