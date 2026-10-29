using System;
using System.Collections.Generic;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;

namespace AppPofo.Presentation.Common
{
    [Serializable] public sealed class ProfileSummaryLink { public string title, url; }
    [Serializable] public sealed class ProfileSummaryProject { public string title, description, url; }
    [Serializable] public sealed class ProfileSummaryData
    {
        public string id, display_name, headline, avatar_base64, bio;
        public string[] skills;
        public ProfileSummaryProject project;
        public ProfileSummaryLink[] links;
        [NonSerialized] public Profile LocalProfile;
        [NonSerialized] public byte[] LocalAvatar;

        public static string Short(string value, int limit)
        {
            value = (value ?? "").Trim();
            if (value.Length <= limit) return value;
            int end = char.IsHighSurrogate(value[limit - 1]) ? limit - 1 : limit;
            return value.Substring(0, end) + "…";
        }
        public static bool WebLink(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512 &&
            Uri.TryCreate(value, UriKind.Absolute, out var url) &&
            (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) &&
            string.IsNullOrEmpty(url.UserInfo) && !string.IsNullOrEmpty(url.Host);

        public static ProfileSummaryData FromLocal(Profile profile, byte[] avatar)
        {
            if (profile == null || !profile.IsActive) return null;
            var result = new ProfileSummaryData { display_name = profile.DisplayName, headline = profile.Headline,
                bio = profile.Bio, LocalProfile = profile, LocalAvatar = avatar };
            var skills = new List<string>(); var links = new List<ProfileSummaryLink>();
            foreach (var item in profile.Items)
            {
                if (item.ItemType == ProfileItemType.Skill && skills.Count < 5) skills.Add(Short(item.Title, 32));
                if (item.ItemType == ProfileItemType.Project && result.project == null)
                    result.project = new ProfileSummaryProject {title = Short(item.Title, 80), description = Short(item.Description, 140), url = WebLink(item.Url) ? item.Url : null};
                if (item.ItemType == ProfileItemType.Link && links.Count < 2 && WebLink(item.Url))
                    links.Add(new ProfileSummaryLink {title = Short(item.Title, 32), url = item.Url});
            }
            result.skills = skills.ToArray(); result.links = links.ToArray(); return result;
        }
    }

    public static class ProfileSummaryCodec
    {
        [Serializable] private sealed class Envelope { public ProfileSummaryData data; }
        public static bool TryRead(string json, string expectedId, out ProfileSummaryData result)
        {
            result = null;
            if (string.IsNullOrEmpty(json) || json.Length > 49152 || !Guid.TryParse(expectedId, out _)) return false;
            try
            {
                var data = UnityEngine.JsonUtility.FromJson<Envelope>(json)?.data;
                if (data == null || data.id != expectedId || string.IsNullOrWhiteSpace(data.display_name) || data.display_name.Length > 100 ||
                    (data.headline?.Length ?? 0) > 200 || (data.avatar_base64?.Length ?? 0) > CloudProfileAvatar.MaxBase64Characters ||
                    (data.bio?.Length ?? 0) > 280 || data.skills == null || data.skills.Length > 5 || data.links == null || data.links.Length > 2) return false;
                foreach (var skill in data.skills) if (string.IsNullOrWhiteSpace(skill) || skill.Length > 64) return false;
                foreach (var link in data.links) if (link == null || string.IsNullOrWhiteSpace(link.title) || link.title.Length > 64 || !ProfileSummaryData.WebLink(link.url)) return false;
                if (data.project != null && !string.IsNullOrEmpty(data.project.title) &&
                    (data.project.title.Length > 160 || (data.project.description?.Length ?? 0) > 280 ||
                    (!string.IsNullOrEmpty(data.project.url) && !ProfileSummaryData.WebLink(data.project.url)))) return false;
                result = data; return true;
            }
            catch (ArgumentException) { return false; }
        }
    }
}
