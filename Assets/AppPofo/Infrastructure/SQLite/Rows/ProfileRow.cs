using SQLite;

namespace AppPofo.Infrastructure.SQLite.Rows
{
    [Table("Profiles")]
    public sealed class ProfileRow
    {
        [PrimaryKey, AutoIncrement]
        [Column("id")]
        public long Id { get; set; }

        [Column("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [Column("headline")]
        public string Headline { get; set; } = string.Empty;

        [Column("bio")]
        public string Bio { get; set; } = string.Empty;

        [Column("avatar_path")]
        public string AvatarPath { get; set; } = string.Empty;

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("website")]
        public string Website { get; set; } = string.Empty;

        [Column("business_card_theme")]
        public string BusinessCardTheme { get; set; } = string.Empty;

        [Column("is_active")]
        public int IsActive { get; set; }

        [Column("created_at")]
        public long CreatedAt { get; set; }

        [Column("updated_at")]
        public long UpdatedAt { get; set; }
    }
}
