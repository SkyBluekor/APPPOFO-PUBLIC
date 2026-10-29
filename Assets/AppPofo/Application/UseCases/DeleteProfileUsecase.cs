using System;
using AppPofo.Application.Abstractions;
using AppPofo.Application.Interfaces;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.UseCases
{
    public sealed class DeleteProfileUsecase
    {
        private readonly IProfileRepository _profileRepository;
        private readonly IAvatarStorage _avatarStorage;
        private readonly Action<Exception> _avatarCleanupWarning;

        // avatarStorage is optional for legacy in-memory callers. Production
        // supplies it so successful profile deletion also cleans up its image.
        public DeleteProfileUsecase(
            IProfileRepository profileRepository,
            IAvatarStorage avatarStorage = null,
            Action<Exception> avatarCleanupWarning = null)
        {
            _profileRepository = profileRepository
                ?? throw new ArgumentNullException(nameof(profileRepository));
            _avatarStorage = avatarStorage;
            _avatarCleanupWarning = avatarCleanupWarning;
        }

        public void Execute(long profileId)
        {
            if (profileId <= 0)
                throw new ArgumentOutOfRangeException(nameof(profileId),
                    "프로필 ID는 1 이상이어야 합니다.");

            // Read the avatar path before the profile row is deleted.
            Profile profile = _avatarStorage == null ? null : _profileRepository.Find(profileId);

            // This operation must succeed first; never delete the image on a DB failure.
            // The repository's ON DELETE CASCADE also clears items and embeddings.
            _profileRepository.Delete(profileId);

            if (profile == null || string.IsNullOrWhiteSpace(profile.AvatarPath))
                return;

            try
            {
                // Filesystem deletion cannot share the SQLite transaction. Treat a
                // cleanup error as a warning instead of reporting the DB delete as failed.
                _avatarStorage.Delete(profile.AvatarPath);
            }
            catch (Exception exception)
            {
                _avatarCleanupWarning?.Invoke(exception);
            }
        }
    }
}
