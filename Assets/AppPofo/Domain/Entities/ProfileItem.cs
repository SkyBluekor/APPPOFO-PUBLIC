using System;
using AppPofo.Domain.ValueObjects;

namespace AppPofo.Domain.Entities
{
    public sealed class ProfileItem
    {
        public long Id { get; }
        public long ProfileId { get; }
        
        public ProfileItemType ItemType { get; }
        public string Title { get; }
        public string Description { get; }
        public string Url { get; }
        public int SortOrder { get; }

        public ProfileItem(
            long id,
            long profileId,
            ProfileItemType itemType,
            string title,
            string description,
            string url,
            int sortOrder
            )
        {
            if(string.IsNullOrWhiteSpace(title))
            {
            throw new ArgumentException(
                "프로필 항목 제목은 비어 있을 수 없습니다.",
                nameof(title));
            }

            if(sortOrder < 0)
            {
            throw new ArgumentOutOfRangeException(
                nameof(sortOrder),
                "정렬 순서는 0 이상이어야 합니다.");
            }

            Id = id;
            ProfileId = profileId;
            ItemType = itemType;
            Title = title;
            Description = description?.Trim() ?? string.Empty;
            Url = url?.Trim() ?? string.Empty;
            SortOrder = sortOrder;
        }

    }
}
