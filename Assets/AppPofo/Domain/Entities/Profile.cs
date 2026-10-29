using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AppPofo.Domain.ValueObjects;

namespace AppPofo.Domain.Entities
{
    public sealed class Profile
    {
        private readonly List<ProfileItem> _items;
        private readonly ReadOnlyCollection<ProfileItem> _readOnlyItems;        

        public long Id { get; }
        public string DisplayName { get; private set; }
        public string Headline { get; private set; }
        public string Bio {  get; private set; }
        public string AvatarPath { get; private set; }
        public string Email { get; private set; }
        public string Website {  get; private set; }
        public BusinessCardTheme BusinessCardTheme { get; private set; }

        public bool IsActive { get; private set; }

        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        public IReadOnlyList<ProfileItem> Items => _readOnlyItems;
        

        public Profile(
            long id,
            string displayName,
            string headline,
            string bio,
            string avatarPath,
            string email,
            string website,
            bool isActive,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,            
            IEnumerable<ProfileItem> items = null,
            BusinessCardTheme businessCardTheme = BusinessCardTheme.Aurora)
        {
            string normalizedDisplayName = NormalizeDisplayName(displayName);

            if(updatedAt < createdAt)
            {
                throw new ArgumentException(
                    "수정 시간은 생성 시간보다 이전일 수 업습니다.",
                    nameof(updatedAt));
            }

            Id = id;
            DisplayName = normalizedDisplayName;
            Headline = NormalizeOptionalText(headline);
            Bio = NormalizeOptionalText(bio);
            AvatarPath = NormalizeOptionalText(avatarPath);
            Email = NormalizeOptionalText(email);
            Website = NormalizeOptionalText(website);
            BusinessCardTheme = businessCardTheme;

            IsActive = isActive;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;

            _items = items == null ? new List<ProfileItem>() :new List<ProfileItem>(items);

            ValidateInitialItems();
            SortItems();

            _readOnlyItems = _items.AsReadOnly();
        }

        public void UpdateBasicInfo(
            string displayName,
            string headline,
            string bio,
            string avatarPath,
            string email,
            string website,
            DateTimeOffset updatedAt)
        {
            string normalizedDisplayName = NormalizeDisplayName(displayName);

            EnsureUpdateTimeDoesNotGoBack(updatedAt);

            DisplayName = normalizedDisplayName;
            Headline = NormalizeOptionalText(headline);
            Bio= NormalizeOptionalText(bio);
            AvatarPath = NormalizeOptionalText(avatarPath);
            Website = NormalizeOptionalText(website);
            Email = NormalizeOptionalText(email);

            UpdatedAt = updatedAt;
        }

        public void ChangeActiveStatus(
            bool isActive,
            DateTimeOffset updateAt)
        {
            EnsureUpdateTimeDoesNotGoBack(updateAt);

            IsActive = isActive;
            UpdatedAt = updateAt;
        }

        public void ChangeBusinessCardTheme(
            BusinessCardTheme theme,
            DateTimeOffset updatedAt)
        {
            EnsureUpdateTimeDoesNotGoBack(updatedAt);

            BusinessCardTheme = theme;
            UpdatedAt = updatedAt;
        }

        public void AddItem(
            ProfileItem item,
            DateTimeOffset updatedAt)
        {
            ValidateItem(item);
            EnsureItemIdIsNotDuplicated(item);
            EnsureUpdateTimeDoesNotGoBack(updatedAt);

            _items.Add(item);
            SortItems();

            UpdatedAt = updatedAt;
        }
        public void ReplaceItem(
            ProfileItem item,
            DateTimeOffset updatedAt
        )
        {
            if(item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if(item.Id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(item),"수정할 프로필 항목 ID는 1이상이어야 합니다.");
            }

            ValidateItem(item);
            EnsureUpdateTimeDoesNotGoBack(updatedAt);

            int index = _items.FindIndex(existingItem => existingItem.Id == item.Id);

            if(index < 0)
            {
                throw new InvalidOperationException($"수정할 프뢸 항목을 찾을 수 없습니다. ItemId: {item.Id}");
            }

            _items[index] = item;

            SortItems();
            UpdatedAt = updatedAt;
        }

        public void RemoveItem(
            long itemId,
            DateTimeOffset updatedAt
        )
        {
            if(itemId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), "삭제할 프로필 항목 ID는 1 이상이어야 합니다.");
            }

            EnsureUpdateTimeDoesNotGoBack(updatedAt);
            
            int index = _items.FindIndex(item => item.Id == itemId);

            if(index < 0)
            {
                throw new InvalidOperationException($"삭제할 프로필 항목을 찾을수 없습니다. ItemId: {itemId}");                
            }

            _items.RemoveAt(index);

            UpdatedAt = updatedAt;
        }

        private void EnsureItemIdIsNotDuplicated(ProfileItem item)
        {
            if(item.Id <= 0)
            {
                return;
            }

            bool alreadyExists = _items.Exists(
                existingItem => existingItem.Id == item.Id);

            if (alreadyExists)
            {
                throw new ArgumentException(
                    $"이미 존재하는 프로필 항목입니다: {item.Id}",
                    nameof(item));
            }
        }

        private void ValidateItem(ProfileItem item)
        {
            if(item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if(item.ProfileId != Id)
            {
                throw new ArgumentException(
                    "프로필 항목의 ProfileId가 현재 프로필의 Id와 일치하지 않습니다.",
                    nameof(item));
            }
        }

        private void EnsureUpdateTimeDoesNotGoBack(DateTimeOffset updatedAt)
        {
            if(updatedAt < UpdatedAt)
            {
                throw new ArgumentException(
                    "새 수정시간은 기존 수정시간 보다 이전일 수 없습니다.",
                    nameof(updatedAt));
            }
        }

        private void SortItems()
        {
            _items.Sort((left,right) =>
            {
                int sortOrderComparison = left.SortOrder.CompareTo(right.SortOrder);

                if (sortOrderComparison != 0)
                {
                    return sortOrderComparison;
                }
                return left.Id.CompareTo(right.Id);
            });
        }

        private void ValidateInitialItems()
        {
            var usedItemIds = new HashSet<long>();

            foreach (ProfileItem item in _items)
            {
                ValidateItem(item);

                if(item.Id > 0 && !usedItemIds.Add(item.Id))
                {
                    throw new ArgumentException(
                        $"중복된 프로필 항목 ID가 있습니다: {item.Id}");
                }
            }
        }

        private static string NormalizeOptionalText(string value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static string NormalizeDisplayName(string displayName)
        {
            if(string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "표시 이름은 비어 있을 수 없습니다.",
                    nameof(displayName));
            }

            string trimmedDisplayName = displayName.Trim();

            if(trimmedDisplayName.Length >100 )
            {
                throw new ArgumentException(
                    "표시 이름은 100자를 초가활 수 없습니다.",
                    nameof (displayName));
            }
            return trimmedDisplayName;
        }
        public void UpdateAvatarPath(string avatarPath, DateTimeOffset updatedAt)
        {
            EnsureUpdateTimeDoesNotGoBack(updatedAt);

            AvatarPath = avatarPath?.Trim() ?? string.Empty;
            UpdatedAt = updatedAt;
        }
    }
}
