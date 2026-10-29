using System;
using System.IO;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Infrastructure.SQLite.Repositories;
using UnityEngine;
using UnityApplication = UnityEngine.Application;

namespace AppPofo.Infrastructure.SQLite.Validation
{
    public static class SQLiteRepositoryIntegrationValidation
    {
        private const string DatabaseFileName =
            "app_pofo_integration_validation.db";

        private const string ModelVersion =
            "integration-model-v1";

        [UnityEditor.MenuItem("AppPofo/Validation/SQLite/Repository Integration")]
        public static void RunTest()
        {
            string directory = Path.GetFullPath(Path.Combine(UnityApplication.dataPath, "../.utmp/sqlite-validation"));
            Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, DatabaseFileName);

            try
            {
                // 이전 테스트 결과 때문에 현재 실행 결과가 영향을 받지 않도록
                // 검증용 DB만 초기화합니다.
                DeleteDatabaseIfExists(databasePath);

                var database =
                    new AppPofoDatabase(databasePath);

                database.Initialize();

                var profileRepository =
                    new SQLiteProfileRepository(database);

                var embeddingRepository =
                    new SQLiteFaceEmbeddingRepository(database);

                DateTimeOffset fixedTime =
                    new DateTimeOffset(
                        2026,
                        8,
                        13,
                        0,
                        0,
                        0,
                        TimeSpan.Zero);

                // ---------------------------------------------------------
                // 1. 새 Profile + ProfileItem 저장
                // ---------------------------------------------------------

                var skillItem = new ProfileItem(
                    id: 0,
                    profileId: 0,
                    itemType: ProfileItemType.Skill,
                    title: "C#",
                    description: "C# 개발 기술",
                    url: "",
                    sortOrder: 0);

                var projectItem = new ProfileItem(
                    id: 0,
                    profileId: 0,
                    itemType: ProfileItemType.Project,
                    title: "AppPofo",
                    description: "Unity 얼굴 기반 프로필 앱",
                    url: "",
                    sortOrder: 1);

                var newProfile = new Profile(
                    id: 0,
                    displayName: "Integration User",
                    headline: "AppPofo Test",
                    bio: "",
                    avatarPath: "",
                    email: "",
                    website: "",
                    isActive: true,
                    createdAt: fixedTime,
                    updatedAt: fixedTime,
                    items: new[]
                    {
                        skillItem,
                        projectItem
                    });

                Profile savedProfile =
                    profileRepository.Save(newProfile);

                if (savedProfile.Id <= 0)
                {
                    throw new InvalidOperationException(
                        "저장된 Profile의 ID가 유효하지 않습니다.");
                }

                if (savedProfile.Items.Count != 2)
                {
                    throw new InvalidOperationException(
                        $"ProfileItem 저장 개수가 다릅니다. " +
                        $"예상: 2, 실제: {savedProfile.Items.Count}");
                }

                foreach (ProfileItem item in savedProfile.Items)
                {
                    if (item.Id <= 0)
                    {
                        throw new InvalidOperationException(
                            "저장된 ProfileItem의 ID가 유효하지 않습니다.");
                    }

                    if (item.ProfileId != savedProfile.Id)
                    {
                        throw new InvalidOperationException(
                            "ProfileItem의 ProfileId가 실제 Profile ID와 다릅니다.");
                    }
                }

                // ---------------------------------------------------------
                // 2. FaceEmbedding 저장
                // ---------------------------------------------------------

                float[] originalValues =
                {
                    0.125f,
                    -0.25f,
                    0.5f,
                    0.75f
                };

                var newEmbedding = new FaceEmbedding(
                    id: 0,
                    profileId: savedProfile.Id,
                    values: originalValues,
                    modelVersion: ModelVersion,
                    qualityScore: 0.9f,
                    sourceType: FaceEmbeddingSourceType.Image,
                    createdAt: fixedTime);

                FaceEmbedding savedEmbedding =
                    embeddingRepository.Add(newEmbedding);

                if (savedEmbedding.Id <= 0)
                {
                    throw new InvalidOperationException(
                        "저장된 FaceEmbedding의 ID가 유효하지 않습니다.");
                }

                // ---------------------------------------------------------
                // 3. 현재 모델의 임베딩 조회 + BLOB 왕복 확인
                // ---------------------------------------------------------

                var loadedEmbeddings =
                    embeddingRepository.GetByModel(ModelVersion);

                if (loadedEmbeddings.Count != 1)
                {
                    throw new InvalidOperationException(
                        $"FaceEmbedding 조회 개수가 다릅니다. " +
                        $"예상: 1, 실제: {loadedEmbeddings.Count}");
                }

                FaceEmbedding loadedEmbedding =
                    loadedEmbeddings[0];

                if (loadedEmbedding.Values.Count != originalValues.Length)
                {
                    throw new InvalidOperationException(
                        "복원된 임베딩 차원이 원본과 다릅니다.");
                }

                for (int i = 0; i < originalValues.Length; i++)
                {
                    // float → byte[] → SQLite BLOB → byte[] → float 과정에서
                    // 같은 Float32 비트 값이 그대로 복원되어야 합니다.
                    if (loadedEmbedding.Values[i] != originalValues[i])
                    {
                        throw new InvalidOperationException(
                            $"임베딩 값이 다릅니다. Index: {i}, " +
                            $"예상: {originalValues[i]}, " +
                            $"실제: {loadedEmbedding.Values[i]}");
                    }
                }

                // ---------------------------------------------------------
                // 4. 다른 모델 버전은 조회되지 않는지 확인
                // ---------------------------------------------------------

                var wrongModelResults =
                    embeddingRepository.GetByModel(
                        "different-model");

                if (wrongModelResults.Count != 0)
                {
                    throw new InvalidOperationException(
                        "다른 모델 버전의 임베딩이 조회됐습니다.");
                }

                // ---------------------------------------------------------
                // 5. 비활성 Profile은 얼굴 인식 후보에서 제외되는지 확인
                // ---------------------------------------------------------

                savedProfile.ChangeActiveStatus(
                    false,
                    fixedTime.AddMinutes(1));

                Profile inactiveProfile =
                    profileRepository.Save(savedProfile);

                var inactiveResults =
                    embeddingRepository.GetByModel(ModelVersion);

                if (inactiveResults.Count != 0)
                {
                    throw new InvalidOperationException(
                        "비활성 Profile의 임베딩이 인식 후보에 포함됐습니다.");
                }

                // 저장 결과도 실제로 false인지 확인합니다.
                if (inactiveProfile.IsActive)
                {
                    throw new InvalidOperationException(
                        "Profile 비활성 상태가 DB에 저장되지 않았습니다.");
                }

                // ---------------------------------------------------------
                // 6. 트랜잭션 Rollback 확인
                // ---------------------------------------------------------

                VerifyTransactionRollback(database);

                // Validate filtering and paging through real SQLite.
                VerifyPagedProfileSearch(profileRepository, fixedTime);

                // ---------------------------------------------------------
                // 7. Profile 삭제 → 자식 데이터 CASCADE 확인
                // ---------------------------------------------------------

                long savedProfileId = savedProfile.Id;

                profileRepository.Delete(savedProfileId);

                using (var connection =
                       database.OpenConnection())
                {
                    int profileCount =
                        connection.ExecuteScalar<int>(
                            @"SELECT COUNT(*)
                              FROM profiles
                              WHERE id = ?;",
                            savedProfileId);

                    int itemCount =
                        connection.ExecuteScalar<int>(
                            @"SELECT COUNT(*)
                              FROM profile_items
                              WHERE profile_id = ?;",
                            savedProfileId);

                    int embeddingCount =
                        connection.ExecuteScalar<int>(
                            @"SELECT COUNT(*)
                              FROM face_embeddings
                              WHERE profile_id = ?;",
                            savedProfileId);

                    if (profileCount != 0)
                    {
                        throw new InvalidOperationException(
                            "Profile이 삭제되지 않았습니다.");
                    }

                    if (itemCount != 0)
                    {
                        throw new InvalidOperationException(
                            "ProfileItem CASCADE 삭제에 실패했습니다.");
                    }

                    if (embeddingCount != 0)
                    {
                        throw new InvalidOperationException(
                            "FaceEmbedding CASCADE 삭제에 실패했습니다.");
                    }
                }

                Debug.Log(
                    "[SQLite Integration Test] 성공\n" +
                    "Profile 저장/조회: OK\n" +
                    "ProfileItem 저장: OK\n" +
                    "FaceEmbedding BLOB 왕복: OK\n" +
                    "ModelVersion 필터: OK\n" +
                    "비활성 Profile 필터: OK\n" +
                    "Transaction Rollback: OK\n" +
                    "Profile search/paging: OK\n" +
                    "ON DELETE CASCADE: OK\n" +
                    $"DB 경로: {databasePath}");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[SQLite Integration Test] 실패\n" +
                    $"DB 경로: {databasePath}\n" +
                    exception);
                throw;
            }
        }

        private static void VerifyPagedProfileSearch(
            SQLiteProfileRepository repository, DateTimeOffset fixedTime)
        {
            var second = repository.Save(new Profile(
                0, "Integration Developer", "", "", "", "", "", true,
                fixedTime, fixedTime,
                new[]
                {
                    new ProfileItem(0, 0, ProfileItemType.Skill, "C#", "", "", 0),
                    new ProfileItem(0, 0, ProfileItemType.Skill, "Unity", "", "", 1)
                }));
            var third = repository.Save(new Profile(
                0, "Other%Developer", "", "", "", "", "", true,
                fixedTime, fixedTime,
                new[] { new ProfileItem(0, 0, ProfileItemType.Skill, "Python", "", "", 0) }));

            try
            {
                var combined = repository.Search("Integration",
                    new[] { "C#", "Unity" }, 12, 0);
                if (combined.TotalCount != 1 || combined.Profiles.Count != 1 ||
                    combined.Profiles[0].Id != second.Id)
                    throw new InvalidOperationException("복수 기술 AND 검색이 올바르지 않습니다.");

                var first = repository.Search("Integration", new[] { "C#" }, 1, 0);
                var last = repository.Search("Integration", new[] { "C#" }, 1, 1);
                if (first.TotalCount != 2 || first.Profiles.Count != 1 ||
                    last.TotalCount != 2 || last.Profiles.Count != 1 ||
                    first.Profiles[0].Id == last.Profiles[0].Id)
                    throw new InvalidOperationException("LIMIT/OFFSET 페이지 조회가 올바르지 않습니다.");

                var literal = repository.Search("Other%", Array.Empty<string>(), 12, 0);
                if (literal.TotalCount != 1 || literal.Profiles[0].Id != third.Id)
                    throw new InvalidOperationException("검색어 % 이스케이프에 실패했습니다.");
            }
            finally
            {
                repository.Delete(second.Id);
                repository.Delete(third.Id);
            }
        }

        private static void VerifyTransactionRollback(
            AppPofoDatabase database)
        {
            const string rollbackDisplayName =
                "ROLLBACK_VALIDATION_PROFILE";

            using (var connection =
                   database.OpenConnection())
            {
                try
                {
                    connection.RunInTransaction(() =>
                    {
                        connection.Execute(
                            @"INSERT INTO profiles
                              (
                                  display_name,
                                  is_active,
                                  created_at,
                                  updated_at
                              )
                              VALUES (?, 1, ?, ?);",
                            rollbackDisplayName,
                            1L,
                            1L);

                        // 일부러 예외를 발생시킵니다.
                        // RunInTransaction이 정상이라면 위 INSERT도 취소되어야 합니다.
                        throw new InvalidOperationException(
                            "FORCED_ROLLBACK");
                    });
                }
                catch (InvalidOperationException exception)
                {
                    // 우리가 의도적으로 발생시킨 예외가 아니라면
                    // 실제 오류이므로 다시 던집니다.
                    if (exception.Message != "FORCED_ROLLBACK")
                    {
                        throw;
                    }
                }

                int count =
                    connection.ExecuteScalar<int>(
                        @"SELECT COUNT(*)
                          FROM profiles
                          WHERE display_name = ?;",
                        rollbackDisplayName);

                if (count != 0)
                {
                    throw new InvalidOperationException(
                        "트랜잭션 Rollback 이후 데이터가 DB에 남아 있습니다.");
                }
            }
        }

        private static void DeleteDatabaseIfExists(
            string databasePath)
        {
            DeleteFileIfExists(databasePath);

            // WAL을 사용하게 될 경우 생길 수 있는 보조 파일까지
            // 검증 시작 전에 같이 정리합니다.
            DeleteFileIfExists(databasePath + "-wal");
            DeleteFileIfExists(databasePath + "-shm");
        }

        private static void DeleteFileIfExists(
            string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
