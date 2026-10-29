using System;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.UseCases
{
    public sealed class DeleteProfileItemUseCase
    {
        private readonly IProfileRepository _profileRepository;

        public DeleteProfileItemUseCase(
            IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository
                ?? throw new ArgumentNullException(
                    nameof(profileRepository));
        }

        public Profile Execute(
            long profileId,
            long itemId,
            DateTimeOffset updatedAt)
        {
            if (profileId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profileId),
                    "프로필 ID는 1 이상이어야 합니다.");
            }

            if (itemId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(itemId),
                    "프로필 항목 ID는 1 이상이어야 합니다.");
            }

            Profile profile =
                _profileRepository.Find(profileId);

            if (profile == null)
            {
                throw new InvalidOperationException(
                    $"프로필을 찾을 수 없습니다. ProfileId: {profileId}");
            }

            profile.RemoveItem(
                itemId,
                updatedAt);

            return _profileRepository.Save(profile);
        }
    }
}