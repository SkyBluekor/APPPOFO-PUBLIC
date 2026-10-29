using System;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Infrastructure.SQLite.Rows;

namespace AppPofo.Infrastructure.SQLite.Mappers
{
    public static class ProfileItemRowMapper
    {
        public static ProfileItemRow ToRow(ProfileItem item)
        {
            if(item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return new ProfileItemRow
            {
                Id = item.Id,
                ProfileId = item.ProfileId,
                ItemType = ToDatabaseItemType(item.ItemType),
                Title = item.Title,
                Description = item.Description,
                Url = item.Url,
                SortOrder = item.SortOrder,
            };
        }

        public static ProfileItem ToDomain(ProfileItemRow row)
        {
            if(row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            return new ProfileItem(
                id: row.Id,
                profileId: row.ProfileId,
                itemType: ToDomainItemType(row.ItemType),
                title: row.Title,
                description: row.Description,
                url: row.Url,
                sortOrder: row.SortOrder);
        }

        private static string ToDatabaseItemType(ProfileItemType itemType)
        {
            return itemType switch
            {
                ProfileItemType.Skill => "SKILL",
                ProfileItemType.Project => "PROJECT",
                ProfileItemType.Link => "LINK",

                _ => throw new ArgumentOutOfRangeException(
                    nameof(itemType),
                    itemType,
                    "지원하지 않는 프로필 항목 종류입니다."),
            };
        }

        private static ProfileItemType ToDomainItemType(string value)
        {
            return value switch
            {
                "SKILL" => ProfileItemType.Skill,
                "PROJECT" => ProfileItemType.Project,
                "LINK" => ProfileItemType.Link,

                _ => throw new InvalidOperationException(
                    $"DB에 알 수 없는 item_type 값이 있습니다: '{value}'")
            };
        }
    }
}