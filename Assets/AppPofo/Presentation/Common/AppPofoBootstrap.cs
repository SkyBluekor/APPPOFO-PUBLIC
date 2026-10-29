using System;
using AppPofo.Application.Abstractions;
using AppPofo.Application.UseCases;
using AppPofo.Infrastructure.SQLite;
using AppPofo.Infrastructure.SQLite.Repositories;
using AppPofo.Application.Interfaces;
using AppPofo.Infrastructure.Files;
using UnityEngine;

namespace AppPofo.Presentation.Common
{
    /// <summary>
    /// 인프라 구현체와 애플리케이션 유스케이스를 조립하는 Composition Root입니다.
    /// </summary>
    public sealed class AppPofoBootstrap : MonoBehaviour
    {
        public CreateProfileUseCase CreateProfileUseCase { get; private set; }
        public GetProfilesUseCase GetProfilesUseCase { get; private set; }
        public SearchProfilesUseCase SearchProfilesUseCase { get; private set; }
        public GetProfileUsecase GetProfileUseCase { get; private set; }
        public UpdateProfileUseCase UpdateProfileUseCase { get; private set; }
        public UpdateBusinessCardThemeUseCase UpdateBusinessCardThemeUseCase { get; private set; }

        public DeleteProfileUsecase DeleteProfileUsecase {get; private set;}
        public AddSkillUseCase AddSkillUseCase { get; private set; }
        public AddProjectUseCase AddProjectUseCase { get; private set; }
        public AddLinkUseCase AddLinkUseCase { get; private set; }
        public bool IsInitialized {get ; private set; }
        public UpdateProfileItemUseCase UpdateProfileItemUseCase{ get; private set;}
        public DeleteProfileItemUseCase DeleteProfileItemUseCase{ get; private set;}
        public UpdateAvatarUseCase UpdateAvatarUseCase { get; private set; }
        public LoadAvatarUseCase LoadAvatarUseCase { get; private set; }
        private AppPofoDatabase _database;
        public IFaceEmbeddingRepository FaceEmbeddingRepository { get; private set; }


        private void Awake()
        {
            try
            {
                _database = new AppPofoDatabase();
                _database.Initialize();

                IProfileRepository profileRepository = new SQLiteProfileRepository(_database);
                FaceEmbeddingRepository = new SQLiteFaceEmbeddingRepository(_database);
                CreateProfileUseCase = new CreateProfileUseCase(profileRepository);
                GetProfilesUseCase = new GetProfilesUseCase(profileRepository);
                SearchProfilesUseCase = new SearchProfilesUseCase((IProfileSearchRepository)profileRepository);
                GetProfileUseCase = new GetProfileUsecase(profileRepository);
                UpdateProfileUseCase = new UpdateProfileUseCase(profileRepository);
                UpdateBusinessCardThemeUseCase = new UpdateBusinessCardThemeUseCase(profileRepository);
                AddSkillUseCase = new AddSkillUseCase(profileRepository);
                AddProjectUseCase = new AddProjectUseCase(profileRepository);
                AddLinkUseCase = new AddLinkUseCase(profileRepository);
                UpdateProfileItemUseCase = new UpdateProfileItemUseCase(profileRepository);
                DeleteProfileItemUseCase = new DeleteProfileItemUseCase(profileRepository);
                IAvatarStorage avatarStorage = new LocalAvatarStorage();
                DeleteProfileUsecase = new DeleteProfileUsecase(profileRepository,
                    avatarStorage, ex => Debug.LogWarning($"[Profile Delete] Avatar cleanup failed: {ex}", this));
                UpdateAvatarUseCase =   new UpdateAvatarUseCase(profileRepository,avatarStorage);
                LoadAvatarUseCase = new LoadAvatarUseCase(avatarStorage);
                IsInitialized = true;
                Debug.Log("[AppPofo Bootstrap] Initialization successfull");
            }
            catch (Exception ex)
            {
                IsInitialized = false;

                Debug.LogException(ex, this);

                // 초기화에 실패한 Composition Root가 후속 처리를 수행하지 않도록 비활성화합니다.
                enabled = false;
            }
        }

    }
}
