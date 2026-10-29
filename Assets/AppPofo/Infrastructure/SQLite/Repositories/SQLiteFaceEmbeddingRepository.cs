using System;
using System.Collections.Generic;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;
using AppPofo.Infrastructure.SQLite.Mappers;
using AppPofo.Infrastructure.SQLite.Rows;
using SQLite;

namespace AppPofo.Infrastructure.SQLite.Repositories
{
    public sealed class SQLiteFaceEmbeddingRepository
        : IFaceEmbeddingRepository
    {
        private readonly AppPofoDatabase _database;

        public SQLiteFaceEmbeddingRepository(
            AppPofoDatabase database)
        {
            _database = database
                ?? throw new ArgumentNullException(
                    nameof(database));
        }

        public bool HasByProfile(long profileId)
        {
            if (profileId <= 0)
                throw new ArgumentOutOfRangeException(nameof(profileId));

            using SQLiteConnection connection = _database.OpenConnection();
            // Registration belongs to the profile, regardless of recognition model version.
            return connection.ExecuteScalar<int>(
                @"SELECT EXISTS(SELECT 1 FROM face_embeddings
                  WHERE profile_id = ? AND embedding_dim > 0
                    AND length(embedding_blob) = embedding_dim * 4
                    AND length(trim(model_version)) > 0);", profileId) != 0;
        }

        public IReadOnlyList<FaceEmbedding> GetByModel(
            string modelVersion)
        {
            if (string.IsNullOrWhiteSpace(modelVersion))
            {
                throw new ArgumentException(
                    "모델 버전은 비어 있을 수 없습니다.",
                    nameof(modelVersion));
            }

            using SQLiteConnection connection =
                _database.OpenConnection();

            /*
             * face_embeddings만 조회하지 않고 profiles와 JOIN합니다.
             *
             * 이유:
             * is_active = 0인 프로필은 얼굴 인식 후보에서
             * 제외해야 하기 때문입니다.
             */
            List<FaceEmbeddingRow> rows =
                connection.Query<FaceEmbeddingRow>(
                    @"SELECT
                        fe.id,
                        fe.profile_id,
                        fe.embedding_blob,
                        fe.embedding_dim,
                        fe.model_version,
                        fe.quality_score,
                        fe.source_type,
                        fe.created_at
                      FROM face_embeddings AS fe
                      INNER JOIN profiles AS p
                          ON p.id = fe.profile_id
                      WHERE fe.model_version = ?
                        AND p.is_active = 1
                      ORDER BY fe.profile_id ASC,
                               fe.id ASC;",
                    modelVersion.Trim());

            var embeddings =
                new List<FaceEmbedding>(rows.Count);

            foreach (FaceEmbeddingRow row in rows)
            {
                embeddings.Add(
                    FaceEmbeddingRowMapper.ToDomain(row));
            }

            return embeddings;
        }

        public FaceEmbedding Add(
            FaceEmbedding embedding)
        {
            if (embedding == null)
            {
                throw new ArgumentNullException(
                    nameof(embedding));
            }

            /*
             * Add는 새로운 임베딩을 등록하는 메서드입니다.
             *
             * 이미 DB ID가 있는 객체를 다시 Add하면
             * INSERT와 UPDATE의 의미가 섞이므로 거부합니다.
             */
            if (embedding.Id != 0)
            {
                throw new ArgumentException(
                    "새 얼굴 임베딩의 ID는 0이어야 합니다.",
                    nameof(embedding));
            }

            if (embedding.ProfileId <= 0)
            {
                throw new ArgumentException(
                    "얼굴 임베딩에는 저장된 프로필 ID가 필요합니다.",
                    nameof(embedding));
            }

            using SQLiteConnection connection =
                _database.OpenConnection();

            FaceEmbeddingRow row =
                FaceEmbeddingRowMapper.ToRow(embedding);

            row.Id = 0;

            int insertedCount =
                connection.Insert(row);

            if (insertedCount != 1)
            {
                throw new InvalidOperationException(
                    $"얼굴 임베딩 INSERT 결과가 예상과 다릅니다: " +
                    $"{insertedCount}");
            }

            if (row.Id <= 0)
            {
                throw new InvalidOperationException(
                    "얼굴 임베딩 저장 후 유효한 ID를 받지 못했습니다.");
            }

            /*
             * INSERT 후 row.Id에 AutoIncrement ID가 반영됐으므로
             * Mapper를 이용해 실제 ID를 가진 새 Domain 객체를 반환합니다.
             */
            return FaceEmbeddingRowMapper.ToDomain(row);
        }

        public void DeleteByProfile(long profileId)
        {
            if (profileId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profileId),
                    "프로필 ID는 1 이상이어야 합니다.");
            }

            using SQLiteConnection connection =
                _database.OpenConnection();

            /*
             * 삭제된 행이 0개여도 오류로 보지 않습니다.
             *
             * 해당 프로필에 얼굴 임베딩이 하나도 없는 상태는
             * 정상적으로 존재할 수 있기 때문입니다.
             */
            connection.Execute(
                @"DELETE FROM face_embeddings
                  WHERE profile_id = ?;",
                profileId);
        }

        public void ReplaceByProfile(
            long profileID,
            IReadOnlyList<FaceEmbedding> embeddings)
        {
            if(profileID <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profileID),
                    "프로필 ID는 1 이상이어야 합니다.");
            }

            if(embeddings == null || embeddings.Count == 0)
            {
                throw new ArgumentException(
                    "교체할 얼굴 임베딩 목록은 비어 있을 수 없습니다.",
                    nameof(embeddings));
            }

            using SQLiteConnection connection =
                _database.OpenConnection();

            connection.RunInTransaction(() =>
            {
                // 기존 임베딩 삭제
                connection.Execute(
                    @"DELETE FROM face_embeddings
                      WHERE profile_id = ?;",
                    profileID);

                // 새로운 임베딩 추가
                foreach (FaceEmbedding embedding in embeddings)
                {
                    if(embedding == null)
                    {
                        throw new ArgumentException(
                            "교체할 얼굴 임베딩 목록에 null이 포함되어 있습니다.",
                            nameof(embeddings));
                    }

                    if(embedding.ProfileId != profileID)
                    {
                        throw new ArgumentException(
                            "교체할 얼굴 임베딩의 프로필 ID가 일치하지 않습니다.",
                            nameof(embeddings));
                    }

                    if(embedding.Id != 0)
                    {
                        throw new ArgumentException(
                            "교체할 얼굴 임베딩의 ID는 0이어야 합니다.",
                            nameof(embeddings));
                    }

                    FaceEmbeddingRow row =
                        FaceEmbeddingRowMapper.ToRow(embedding);
                    
                    row.Id = 0;

                    int insertedCount = connection.Insert(row);

                    if(insertedCount != 1)
                    {
                        throw new InvalidOperationException(
                            $"얼굴 임베딩 INSERT 결과가 예상과 다릅니다: " +
                            $"{insertedCount}");
                    }                        
                }
            });
        }

    }
}
