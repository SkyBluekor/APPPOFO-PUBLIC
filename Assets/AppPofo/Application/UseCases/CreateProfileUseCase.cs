using System;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.UseCases
{
    public sealed class CreateProfileUseCase
    {
        private readonly IProfileRepository _profileRepository;

        public CreateProfileUseCase(
            IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository
                ?? throw new ArgumentNullException(
                    nameof(profileRepository));
        }

        public Profile Execute(
            string displayName,
            string headline,
            string bio,
            string email,
            string website,
            DateTimeOffset createdAt)
        {
            /*
             * 새 프로필이므로 Id는 0입니다.
             *
             * 실제 SQLite ID는 Repository.Save()가 INSERT한 뒤
             * 새 Profile 객체로 반환해 줍니다.
             *
             * AvatarPath는 아직 아바타 기능을 구현하지 않았으므로
             * 현재는 빈 문자열로 시작합니다.
             */
            var profile = new Profile(
                id: 0,
                displayName: displayName,
                headline: headline,
                bio: bio,
                avatarPath: "",
                email: email,
                website: website,
                isActive: true,
                createdAt: createdAt,
                updatedAt: createdAt);

            return _profileRepository.Save(profile);
        }
    }
}