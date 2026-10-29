using System;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;

namespace AppPofo.Application.UseCases
{
    public sealed class UpdateBusinessCardThemeUseCase
    {
        private readonly IProfileRepository _profileRepository;

        public UpdateBusinessCardThemeUseCase(
            IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository
                ?? throw new ArgumentNullException(nameof(profileRepository));
        }

        public Profile Execute(
            long profileId,
            BusinessCardTheme theme,
            DateTimeOffset updatedAt)
        {
            if(profileId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profileId),
                    "프로필 ID는 1 이상이어야 함");
            }

            Profile profile = _profileRepository.Find(profileId);

            if(profile == null)
            {
                throw new InvalidOperationException(
                    $"수정할 프로필을 찾을 수 없습니다. ProfileId: {profileId}");
            }

            profile.ChangeBusinessCardTheme(theme,updatedAt);

            return _profileRepository.Save(profile);
        }
    }
}
