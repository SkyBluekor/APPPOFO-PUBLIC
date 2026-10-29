using System;
using System.Collections.Generic;
using System.Linq;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;

namespace AppPofo.Presentation.Common
{
    // Presentation data only. Cloud items never acquire synthetic SQLite IDs.
    public sealed class ProfileDetailItemViewModel
    {
        public ProfileItemType Type { get; }
        public string Title { get; }
        public string Description { get; }
        public string Url { get; }
        public ProfileItem LocalItem { get; }

        internal ProfileDetailItemViewModel(ProfileItemType type, string title,
            string description, string url, ProfileItem localItem = null)
        {
            Type = type; Title = title ?? ""; Description = description ?? "";
            Url = url ?? ""; LocalItem = localItem;
        }
    }

    public sealed class ProfileDetailViewModel
    {
        public long? LocalId { get; private set; }
        public string CloudId { get; private set; }
        public bool IsCloud => CloudId != null;
        public string DisplayName { get; private set; }
        public string Headline { get; private set; }
        public string Bio { get; private set; }
        public string Email { get; private set; }
        public string Website { get; private set; }
        public IReadOnlyList<ProfileDetailItemViewModel> Items { get; private set; }

        public static ProfileDetailViewModel FromLocal(Profile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            return new ProfileDetailViewModel {
                LocalId = profile.Id, DisplayName = profile.DisplayName,
                Headline = profile.Headline, Bio = profile.Bio, Email = profile.Email,
                Website = profile.Website, Items = profile.Items.Select(item =>
                    new ProfileDetailItemViewModel(item.ItemType, item.Title,
                        item.Description, item.Url, item)).ToArray()
            };
        }

        public static ProfileDetailViewModel FromCloud(CloudProfileDetail profile)
        {
            if (profile == null || !Guid.TryParse(profile.id, out _))
                throw new ArgumentException("Valid cloud UUID required.", nameof(profile));
            var items = new List<ProfileDetailItemViewModel>();
            foreach (var item in (profile.items ?? Array.Empty<CloudProfileItem>())
                .Where(item => item != null).OrderBy(item => item.sort_order))
            {
                ProfileItemType type;
                switch (item.item_type) {
                    case "SKILL": type = ProfileItemType.Skill; break;
                    case "PROJECT": type = ProfileItemType.Project; break;
                    case "LINK": type = ProfileItemType.Link; break;
                    default: continue;
                }
                items.Add(new ProfileDetailItemViewModel(type, item.title, item.description, item.url));
            }
            return new ProfileDetailViewModel {
                CloudId = profile.id, DisplayName = profile.display_name ?? "",
                Headline = profile.headline ?? "", Bio = profile.bio ?? "",
                Email = profile.email ?? "", Website = profile.website ?? "", Items = items
            };
        }

        public static bool CanOpenUrl(string value) => Uri.TryCreate(value, UriKind.Absolute,
            out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
