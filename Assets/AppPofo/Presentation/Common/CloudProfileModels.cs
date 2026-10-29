using System;
using UnityEngine;

namespace AppPofo.Presentation.Common
{
    // Cloud IDs are UUID strings. Never interpret these as local SQLite long IDs.
    [Serializable]
    public sealed class CloudProfileSummary
    {
        public string id;
        public string display_name;
        public string headline;
        public string business_card_theme;
        public string avatar_base64;
        public long updated_at;
    }

    [Serializable]
    public sealed class CloudProfileItem
    {
        public string item_type;
        public string title;
        public string description;
        public string url;
        public int sort_order;
    }

    [Serializable]
    public sealed class CloudProfileDetail
    {
        public string id;
        public string display_name;
        public string headline;
        public string bio;
        public string email;
        public string website;
        public string business_card_theme;
        public string avatar_base64;
        public bool is_active = true;
        public CloudProfileItem[] items;
    }

    [Serializable]
    public sealed class CloudProfilePage
    {
        public CloudProfileSummary[] data;
        public int total;
        public int limit;
        public int offset;
    }

    [Serializable]
    public sealed class CloudProfileDetailEnvelope
    {
        public CloudProfileDetail data;
    }

    public static class CloudProfileJson
    {
        public static bool TryReadPage(string payload, out CloudProfilePage result)
        {
            result = null;
            if (string.IsNullOrEmpty(payload)) return false;
            try
            {
                var decoded = JsonUtility.FromJson<CloudProfilePage>(payload);
                if (decoded == null || decoded.data == null || decoded.total < 0 ||
                    decoded.offset < 0 || decoded.limit < 1 || decoded.limit > 24 ||
                    decoded.data.Length > decoded.limit)
                    return false;
                foreach (var item in decoded.data)
                    if (item == null || !Guid.TryParse(item.id, out _) ||
                        string.IsNullOrWhiteSpace(item.display_name))
                        return false;
                result = decoded;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool TryReadDetail(string payload, out CloudProfileDetail result)
        {
            result = null;
            if (string.IsNullOrEmpty(payload)) return false;
            try
            {
                var response = JsonUtility.FromJson<CloudProfileDetailEnvelope>(payload);
                if (response?.data == null || !Guid.TryParse(response.data.id, out _) ||
                    string.IsNullOrWhiteSpace(response.data.display_name) ||
                    response.data.items == null || response.data.items.Length > 45)
                    return false;
                result = response.data;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool CanReadCloudProfiles(ProfileDataMode mode, bool authenticated)
        {
            return mode == ProfileDataMode.Cloud && authenticated;
        }
    }
}
