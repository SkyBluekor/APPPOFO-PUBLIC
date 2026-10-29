using System;
using System.Collections.Generic;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Infrastructure.SQLite.Rows;

namespace AppPofo.Infrastructure.SQLite.Mappers
{
    public static class ProfileRowMapper
    {
        public static ProfileRow ToRow(Profile profile)
        {
            if(profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            return new ProfileRow
            {
                Id = profile.Id,
                DisplayName = profile.DisplayName,
                Headline = profile.Headline,
                Bio = profile.Bio,
                AvatarPath = profile.AvatarPath,
                Email = profile.Email,
                Website = profile.Website,
                BusinessCardTheme = ToDatabaseBusinessCardTheme(profile.BusinessCardTheme),
                IsActive = profile.IsActive ? 1 : 0,   
                CreatedAt = profile.CreatedAt.ToUnixTimeMilliseconds(),                             
                UpdatedAt = profile.UpdatedAt.ToUnixTimeMilliseconds()
            };
        }

        public static Profile ToDomain(ProfileRow row, IEnumerable<ProfileItem> items)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));                
            }
            bool isActive = ToDomainIsActive(row.IsActive);

            DateTimeOffset createdAt = DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt);
            DateTimeOffset updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAt);

            return new Profile(
                id: row.Id,
                displayName: row.DisplayName,
                headline: row.Headline,
                bio: row.Bio,
                avatarPath: row.AvatarPath,
                email: row.Email,
                website: row.Website,
                isActive: isActive,
                createdAt: createdAt,
                updatedAt: updatedAt,
                items: items,
                businessCardTheme: ToDomainBusinessCardTheme(row.BusinessCardTheme)
            );
        }

        private static string ToDatabaseBusinessCardTheme(
            BusinessCardTheme theme)
        {
            return theme switch
            {
                BusinessCardTheme.Aurora => "AURORA",
                BusinessCardTheme.Blush => "BLUSH",
                BusinessCardTheme.Neutral => "NEUTRAL",

                _ => throw new ArgumentOutOfRangeException(
                    nameof(theme),
                    theme,
                    "지원하지 않는 명함 테마입니다."),
            };
        }

        private static BusinessCardTheme ToDomainBusinessCardTheme(
            string value)
        {
            return value switch
            {
                "AURORA" => BusinessCardTheme.Aurora,
                "BLUSH" => BusinessCardTheme.Blush,
                "NEUTRAL" => BusinessCardTheme.Neutral,

                _ => BusinessCardTheme.Aurora,
            };
        }

        private static bool ToDomainIsActive(int value)
        {
            return value switch
            {
                0 => false,
                1 => true,

                _ => throw new InvalidOperationException(
                    $"DB의 is_active 값이 올바르지 않습니다: {value}")
            };
        }
    }
}
