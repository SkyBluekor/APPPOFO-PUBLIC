using System;
using AppPofo.Application.Abstractions;
using AppPofo.Application.Interfaces;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.UseCases
{
    /// <summary>
    /// 아바타 파일 저장과 프로필 경로 갱신을 하나의 애플리케이션 작업으로 조정합니다.
    /// </summary>
    public sealed class UpdateAvatarUseCase
    {
        private readonly IProfileRepository profileRepository;
        private readonly IAvatarStorage avatarStorage;

        public UpdateAvatarUseCase(
            IProfileRepository profileRepository,
            IAvatarStorage avatarStorage
        )
        {
            this.profileRepository = profileRepository ?? throw new ArgumentException(nameof(profileRepository));

            this.avatarStorage = avatarStorage ?? throw new ArgumentException(nameof(avatarStorage));
        }

        public Profile Execute(
            long profileId,
            byte[] imageBytes,
            DateTimeOffset updatedAt
        )
        {
            if(profileId < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profileId));
            }

            if (imageBytes == null ||
                imageBytes.Length == 0)
            {
                throw new ArgumentException(
                    "저장할 아바타 이미지가 없습니다.",
                    nameof(imageBytes));
            }

            Profile profile = profileRepository.Find(profileId);

            if (profile == null)
            {
                throw new InvalidOperationException(
                    $"프로필을 찾을 수 없습니다. ProfileId: {profileId}");
            }

            // 파일 저장이 성공한 뒤에만 프로필이 새 경로를 가리키게 합니다.
            string avatarPath = avatarStorage.Save(profileId,imageBytes);

            profile.UpdateAvatarPath(avatarPath,updatedAt);

            return profileRepository.Save(profile);
        }
    }
}
