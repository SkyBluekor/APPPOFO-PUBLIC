using System;
using System.Collections.Generic;
using System.Text;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;
using AppPofo.Infrastructure.SQLite.Mappers;
using AppPofo.Infrastructure.SQLite.Rows;
using SQLite;

namespace AppPofo.Infrastructure.SQLite.Repositories
{
    public sealed class SQLiteProfileRepository : IProfileRepository, IProfileSearchRepository
    {
        private readonly AppPofoDatabase _database;

        public SQLiteProfileRepository(AppPofoDatabase database)
        {
            _database = database
                ?? throw new ArgumentNullException(nameof(database));
        }

        public Profile Find(long profileId)
        {
            if(profileId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(profileId),"프로필 ID는 1 이상이어야 합니다.");
            }

            using SQLiteConnection connection =
                _database.OpenConnection();

              List<ProfileRow> rows =
                connection.Query<ProfileRow>(
                    @"SELECT
                        id,
                        display_name,
                        headline,
                        bio,
                        avatar_path,
                        email,
                        website,
                        business_card_theme,
                        is_active,
                        created_at,
                        updated_at
                    FROM profiles
                    WHERE id = ?
                    LIMIT 1;",
                    profileId);


            // 해당 ID의 프로필이 없으면 조회 결과도 없습니다.
            if (rows.Count == 0)
            {
                return null;
            }

            ProfileRow profileRow = rows[0];
            
            IReadOnlyList<ProfileItem> items =
                LoadProfileItems(connection, profileId);

            return ProfileRowMapper.ToDomain(
                profileRow,
                items);
        }

        public IReadOnlyList<Profile> GetAll()
        {
            using SQLiteConnection connection =
                _database.OpenConnection();

            // SQL에 ORDER BY를 넣어 조회 순서를 명확하게 고정합니다.
            List<ProfileRow> profileRows =
                connection.Query<ProfileRow>(
                    @"SELECT
                        id,
                        display_name,
                        headline,
                        bio,
                        avatar_path,
                        email,
                        website,
                        business_card_theme,
                        is_active,
                        created_at,
                        updated_at
                      FROM profiles
                      ORDER BY id ASC;");

            var profiles =
                new List<Profile>(profileRows.Count);

            foreach (ProfileRow profileRow in profileRows)
            {
                IReadOnlyList<ProfileItem> items =
                    LoadProfileItems(
                        connection,
                        profileRow.Id);

                Profile profile =
                    ProfileRowMapper.ToDomain(
                        profileRow,
                        items);

                profiles.Add(profile);
            }

            return profiles;
        }

        public ProfileSearchPage Search(string name, IReadOnlyList<string> skills, int limit, int offset)
        {
            if (limit < 1 || limit > 24) throw new ArgumentOutOfRangeException(nameof(limit));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));

            // Parameterized EXISTS predicates avoid duplicate profiles from multiple
            // matching skills. Each requested skill must match the same profile.
            var where = new StringBuilder(" WHERE 1=1");
            var filters = new List<object>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                where.Append(@" AND p.display_name LIKE ? ESCAPE '\'");
                filters.Add("%" + EscapeLikeValue(name.Trim()) + "%");
            }
            if (skills != null)
            {
                foreach (string requestedSkill in skills)
                {
                    if (string.IsNullOrWhiteSpace(requestedSkill)) continue;
                    where.Append(@" AND EXISTS (
                        SELECT 1 FROM profile_items AS i
                        WHERE i.profile_id = p.id AND i.item_type = 'SKILL'
                          AND i.title LIKE ? ESCAPE '\')");
                    filters.Add("%" + EscapeLikeValue(requestedSkill.Trim()) + "%");
                }
            }

            using SQLiteConnection connection = _database.OpenConnection();
            string predicates = where.ToString();
            int total = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM profiles AS p" + predicates + ";", filters.ToArray());

            var pageParams = new List<object>(filters) { limit, offset };
            List<ProfileRow> rows = connection.Query<ProfileRow>(
                @"SELECT p.id, p.display_name, p.headline, p.bio, p.avatar_path,
                         p.email, p.website, p.business_card_theme, p.is_active,
                         p.created_at, p.updated_at
                    FROM profiles AS p" + predicates +
                " ORDER BY p.id ASC LIMIT ? OFFSET ?;", pageParams.ToArray());

            var profiles = new List<Profile>(rows.Count);
            foreach (ProfileRow row in rows)
                profiles.Add(ProfileRowMapper.ToDomain(row, LoadProfileItems(connection, row.Id)));

            return new ProfileSearchPage(profiles, total);
        }

        private static string EscapeLikeValue(string text)
        {
            // Treat user input literally, not as SQLite LIKE wildcards.
            return text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        public Profile Save(Profile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            // Id = 0은 아직 DB에 저장되지 않은 새 프로필을 의미합니다.
            // 음수 ID는 정상적인 DB ID가 아니므로 여기서 거부합니다.
            if (profile.Id < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profile),
                    "프로필 ID는 음수가 될 수 없습니다.");
            }

            long savedProfileId = 0;

            using (SQLiteConnection connection =
                   _database.OpenConnection())
            {
                connection.RunInTransaction(() =>
                {
                    ProfileRow profileRow =
                        ProfileRowMapper.ToRow(profile);

                    if (profile.Id == 0)
                    {
                        // AutoIncrement 컬럼이므로 신규 데이터는 Id = 0 상태로 INSERT합니다.
                        profileRow.Id = 0;

                        int insertedCount =
                            connection.Insert(profileRow);

                        if (insertedCount != 1)
                        {
                            throw new InvalidOperationException(
                                $"프로필 INSERT 결과가 예상과 다릅니다: {insertedCount}");
                        }

                        // sqlite-net이 INSERT 후 AutoIncrement로 발급된 ID를
                        // profileRow.Id에 다시 넣어줍니다.
                        if (profileRow.Id <= 0)
                        {
                            throw new InvalidOperationException(
                                "프로필 저장 후 유효한 ID를 받지 못했습니다.");
                        }

                        savedProfileId = profileRow.Id;
                    }
                    else
                    {
                        int updatedCount =
                            connection.Update(profileRow);

                        if (updatedCount != 1)
                        {
                            throw new InvalidOperationException(
                                $"수정할 프로필을 찾지 못했거나 UPDATE에 실패했습니다. " +
                                $"ProfileId: {profile.Id}");
                        }

                        savedProfileId = profile.Id;
                    }

                    SaveProfileItems(
                        connection,
                        profile,
                        savedProfileId);
                });
            }

            /*
             * INSERT 과정에서 ProfileItem에도 새로운 ID가 발급될 수 있습니다.
             *
             * 기존 Domain 객체는 Id를 변경할 수 없도록 설계했으므로
             * DB 저장이 끝난 뒤 실제 저장된 데이터를 다시 읽어서
             * 새로운 Profile 객체로 반환합니다.
             */
            Profile savedProfile = Find(savedProfileId);

            if (savedProfile == null)
            {
                throw new InvalidOperationException(
                    $"저장된 프로필을 다시 조회하지 못했습니다. " +
                    $"ProfileId: {savedProfileId}");
            }

            return savedProfile;
        }

        public void Delete(long profileId)
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
            * profiles 한 행만 삭제합니다.
            *
            * profile_items와 face_embeddings에는
            * ON DELETE CASCADE가 걸려 있으므로
            * SQLite가 연결된 자식 데이터를 자동으로 삭제합니다.
            */
            int deletedCount = connection.Execute(
                @"DELETE FROM profiles
                WHERE id = ?;",
                profileId);

            /*
            * 삭제 결과가 0이면 해당 ID의 프로필이 없었다는 뜻입니다.
            *
            * 아무 일도 없었던 것처럼 넘어갈 수도 있지만,
            * 현재 Repository에서는 잘못된 ID 사용을 빠르게 발견하도록
            * 예외를 발생시키는 정책으로 갑니다.
            */
            if (deletedCount == 0)
            {
                throw new InvalidOperationException(
                    $"삭제할 프로필을 찾을 수 없습니다. " +
                    $"ProfileId: {profileId}");
            }

            if (deletedCount != 1)
            {
                throw new InvalidOperationException(
                    $"프로필 삭제 결과가 예상과 다릅니다. " +
                    $"삭제된 행 수: {deletedCount}");
            }
        }

        private static IReadOnlyList<ProfileItem> LoadProfileItems(
            SQLiteConnection connection,
            long profileId)
        {
            List<ProfileItemRow> rows =
                connection.Query<ProfileItemRow>(
                    @"SELECT
                        id,
                        profile_id,
                        item_type,
                        title,
                        description,
                        url,
                        sort_order
                      FROM profile_items
                      WHERE profile_id = ?
                      ORDER BY sort_order ASC, id ASC;",
                    profileId);

            var items =
                new List<ProfileItem>(rows.Count);

            foreach (ProfileItemRow row in rows)
            {
                items.Add(
                    ProfileItemRowMapper.ToDomain(row));
            }

            return items;
        }

        private static void SaveProfileItems(
            SQLiteConnection connection,
            Profile profile,
            long savedProfileId)
        {
            /*
             * 현재 DB에 들어 있는 기존 상세 항목들을 먼저 가져옵니다.
             *
             * 나중에 현재 Profile.Items에 없는 항목을 찾아서
             * DELETE하기 위해 필요합니다.
             */
            List<ProfileItemRow> existingRows =
                connection.Query<ProfileItemRow>(
                    @"SELECT
                id,
                profile_id,
                item_type,
                title,
                description,
                url,
                sort_order
              FROM profile_items
              WHERE profile_id = ?;",
                    savedProfileId);

            /*
             * DB에 존재하는 기존 Item ID를 보관합니다.
             *
             * 현재 Profile에 남아 있는 Item은 하나씩 Remove하고,
             * 마지막까지 HashSet에 남아 있는 ID는
             * 사용자가 삭제한 Item이라고 판단합니다.
             */
            var remainingItemIds = new HashSet<long>();

            foreach (ProfileItemRow existingRow in existingRows)
            {
                remainingItemIds.Add(existingRow.Id);
            }

            foreach (ProfileItem item in profile.Items)
            {
                ProfileItemRow row =
                    ProfileItemRowMapper.ToRow(item);

                /*
                 * 신규 Profile의 경우:
                 *
                 * Domain Profile.Id = 0
                 * Domain ProfileItem.ProfileId = 0
                 *
                 * 하지만 Profile INSERT 후 실제 ID가 예를 들어 15가 됐다면
                 * DB에 저장할 Item의 profile_id도 반드시 15가 되어야 합니다.
                 */
                row.ProfileId = savedProfileId;

                if (item.Id <= 0)
                {
                    // 새 상세 항목입니다.
                    row.Id = 0;

                    int insertedCount =
                        connection.Insert(row);

                    if (insertedCount != 1)
                    {
                        throw new InvalidOperationException(
                            $"프로필 항목 INSERT에 실패했습니다. " +
                            $"Title: {item.Title}");
                    }

                    continue;
                }

                /*
                 * Id가 있는 항목은 기존 DB 행이어야 합니다.
                 *
                 * 이 검사를 하지 않으면 다른 Profile에 속한 Item ID를
                 * 실수로 현재 Profile의 데이터처럼 UPDATE할 위험이 있습니다.
                 */
                if (!remainingItemIds.Contains(item.Id))
                {
                    throw new InvalidOperationException(
                        $"현재 프로필에 속하지 않는 상세 항목입니다. " +
                        $"ItemId: {item.Id}, ProfileId: {savedProfileId}");
                }

                int updatedCount =
                    connection.Update(row);

                if (updatedCount != 1)
                {
                    throw new InvalidOperationException(
                        $"프로필 항목 UPDATE에 실패했습니다. " +
                        $"ItemId: {item.Id}");
                }

                // 현재 Profile.Items에 존재하는 항목이므로
                // 삭제 후보 목록에서 제거합니다.
                remainingItemIds.Remove(item.Id);
            }

            /*
             * 여기까지 왔는데 remainingItemIds에 남아 있다는 것은:
             *
             * DB에는 존재하지만
             * 현재 Profile.Items에서는 사라진 항목입니다.
             *
             * 즉 사용자가 삭제한 항목이므로 DB에서도 삭제합니다.
             */
            foreach (long itemId in remainingItemIds)
            {
                int deletedCount =
                    connection.Execute(
                        @"DELETE FROM profile_items
                  WHERE id = ?
                    AND profile_id = ?;",
                        itemId,
                        savedProfileId);

                if (deletedCount != 1)
                {
                    throw new InvalidOperationException(
                        $"프로필 항목 DELETE에 실패했습니다. " +
                        $"ItemId: {itemId}");
                }
            }
        }
    }
}
