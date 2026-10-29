using SQLite;

namespace AppPofo.Infrastructure.SQLite.Rows
{
    [Table("profile_items")]
    public sealed class ProfileItemRow
    {
        [PrimaryKey, AutoIncrement]
        [Column("id")]
        public long Id { get; set; }

        [Column("profile_id")]
        public long ProfileId { get; set; }

        [Column("item_type")]
        public string ItemType { get; set; } = string.Empty;

        [Column("title")]
        public string Title { get; set; } = string.Empty;

        [Column("description")]
        public string Description { get; set; } = string.Empty;

        [Column("url")]
        public string Url { get; set; } = string.Empty;

        [Column("sort_order")]
        public int SortOrder { get; set; }
    }
}