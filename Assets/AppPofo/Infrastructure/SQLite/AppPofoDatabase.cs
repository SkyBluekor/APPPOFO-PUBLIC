using System;
using System.IO;
using UnityApplication = UnityEngine.Application;
using SQLiteConnection = global::SQLite.SQLiteConnection;

namespace AppPofo.Infrastructure.SQLite
{
    public sealed class AppPofoDatabase
    {
        private const string DatabaseFileName = "app_pofo.db";

        // 현재 앱이 알고 있는 DB 구조의 최신 버전입니다.
        public const int CurrentSchemaVersion = 2;

        public string DatabasePath { get; }

        public AppPofoDatabase(string databasePath = null)
        {
            // 경로를 전달하지 않으면 실제 앱 저장 경로를 사용합니다.
            // 별도 경로를 받을 수 있게 한 이유는 나중에 테스트용 DB를
            // 원하는 위치에 생성할 수 있도록 하기 위해서입니다.
            DatabasePath = string.IsNullOrWhiteSpace(databasePath)
                ? Path.Combine(
                    UnityApplication.persistentDataPath,
                    DatabaseFileName)
                : databasePath;
        }

        public void Initialize()
        {
            string directoryPath =
                Path.GetDirectoryName(DatabasePath);

            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            using (SQLiteConnection connection = OpenConnection())
            {
                ApplyMigrations(connection);
            }
        }

        public SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(DatabasePath);

            try
            {
                connection.BusyTimeout = TimeSpan.FromSeconds(5);
                // SQLite 외래키 검사는 연결마다 다시 활성화해야 합니다.
                connection.Execute("PRAGMA foreign_keys = ON;");

                

                int foreignKeysEnabled =
                    connection.ExecuteScalar<int>(
                        "PRAGMA foreign_keys;");

                if (foreignKeysEnabled != 1)
                {
                    throw new InvalidOperationException(
                        "SQLite 외래키 설정을 활성화하지 못했습니다.");
                }

                return connection;
            }
            catch
            {
                // 연결 설정 도중 예외가 발생하면
                // 열린 네이티브 자원을 반드시 정리합니다.
                connection.Dispose();
                throw;
            }
        }

        private static void ApplyMigrations(
            SQLiteConnection connection)
        {
            int databaseVersion =
                connection.ExecuteScalar<int>(
                    "PRAGMA user_version;");

            // 앱보다 더 새로운 DB를 구버전 앱으로 열면
            // 데이터 구조를 잘못 해석할 수 있으므로 실행을 막습니다.
            if (databaseVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"지원하지 않는 DB 버전입니다. " +
                    $"DB: {databaseVersion}, " +
                    $"앱: {CurrentSchemaVersion}");
            }

            if (databaseVersion < 1)
            {
                ApplyVersion1(connection);
                databaseVersion = 1;
            }

            if (databaseVersion < 2)
            {
                ApplyVersion2(connection);
                databaseVersion = 2;
            }

            if (databaseVersion != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"DB 마이그레이션이 완료되지 않았습니다. " +
                    $"현재 버전: {databaseVersion}");
            }
        }

        private static void ApplyVersion1(
            SQLiteConnection connection)
        {
            // 테이블 일부만 만들어진 상태가 남지 않도록
            // 버전 1의 모든 생성 작업을 하나의 트랜잭션으로 묶습니다.
            connection.RunInTransaction(() =>
            {
                CreateProfilesTable(connection);
                CreateProfileItemsTable(connection);
                CreateFaceEmbeddingsTable(connection);
                CreateIndexes(connection);

                connection.Execute(
                    "PRAGMA user_version = 1;");
            });
        }

        private static void ApplyVersion2(
            SQLiteConnection connection)
        {
            connection.RunInTransaction(() =>
            {
                connection.Execute(
                    @"
                    ALTER TABLE profiles
                    ADD COLUMN business_card_theme TEXT NOT NULL DEFAULT 'AURORA'
                        CHECK (
                            business_card_theme IN (
                                'AURORA',
                                'BLUSH',
                                'NEUTRAL'
                            )
                        );
                    ");

                connection.Execute(
                    "PRAGMA user_version = 2;");
            });
        }

        private static void CreateProfilesTable(
            SQLiteConnection connection)
        {
            connection.Execute(
                @"
                CREATE TABLE IF NOT EXISTS profiles
                (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,

                    display_name TEXT NOT NULL
                        CHECK (
                            length(trim(display_name))
                            BETWEEN 1 AND 100
                        ),

                    headline TEXT NOT NULL DEFAULT '',
                    bio TEXT NOT NULL DEFAULT '',
                    avatar_path TEXT NOT NULL DEFAULT '',
                    email TEXT NOT NULL DEFAULT '',
                    website TEXT NOT NULL DEFAULT '',

                    is_active INTEGER NOT NULL DEFAULT 1
                        CHECK (is_active IN (0, 1)),

                    created_at INTEGER NOT NULL,
                    updated_at INTEGER NOT NULL
                        CHECK (updated_at >= created_at)
                );
                ");
        }

        private static void CreateProfileItemsTable(
            SQLiteConnection connection)
        {
            connection.Execute(
                @"
                CREATE TABLE IF NOT EXISTS profile_items
                (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    profile_id INTEGER NOT NULL,

                    item_type TEXT NOT NULL
                        CHECK (
                            item_type IN (
                                'SKILL',
                                'PROJECT',
                                'LINK'
                            )
                        ),

                    title TEXT NOT NULL
                        CHECK (length(trim(title)) > 0),

                    description TEXT NOT NULL DEFAULT '',
                    url TEXT NOT NULL DEFAULT '',

                    sort_order INTEGER NOT NULL DEFAULT 0
                        CHECK (sort_order >= 0),

                    FOREIGN KEY (profile_id)
                        REFERENCES profiles(id)
                        ON DELETE CASCADE
                );
                ");
        }

        private static void CreateFaceEmbeddingsTable(
            SQLiteConnection connection)
        {
            connection.Execute(
                @"
                CREATE TABLE IF NOT EXISTS face_embeddings
                (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    profile_id INTEGER NOT NULL,

                    embedding_blob BLOB NOT NULL,

                    embedding_dim INTEGER NOT NULL
                        CHECK (embedding_dim > 0),

                    model_version TEXT NOT NULL
                        CHECK (
                            length(trim(model_version)) > 0
                        ),

                    quality_score REAL NULL,

                    source_type TEXT NOT NULL
                        CHECK (
                            source_type IN (
                                'CAMERA',
                                'IMAGE'
                            )
                        ),

                    created_at INTEGER NOT NULL,

                    CHECK (
                        length(embedding_blob)
                        = embedding_dim * 4
                    ),

                    FOREIGN KEY (profile_id)
                        REFERENCES profiles(id)
                        ON DELETE CASCADE
                );
                ");
        }

        private static void CreateIndexes(
            SQLiteConnection connection)
        {
            connection.Execute(
                @"
                CREATE INDEX IF NOT EXISTS
                    idx_profiles_is_active
                ON profiles(is_active);
                ");

            connection.Execute(
                @"
                CREATE INDEX IF NOT EXISTS
                    idx_profile_items_profile_sort
                ON profile_items(
                    profile_id,
                    sort_order,
                    id
                );
                ");

            connection.Execute(
                @"
                CREATE INDEX IF NOT EXISTS
                    idx_face_embeddings_model_profile
                ON face_embeddings(
                    model_version,
                    profile_id
                );
                ");
        }
    }
}
