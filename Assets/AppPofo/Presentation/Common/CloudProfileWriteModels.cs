using System;
using System.Collections.Generic;
using UnityEngine;

namespace AppPofo.Presentation.Common
{
    // POST/PUT are full replacements. Require an explicit items array to avoid
    // deleting existing skills, projects or links by accident.
    [Serializable]
    public sealed class CloudProfileWritePayload
    {
        public string display_name;
        public string headline = "";
        public string bio = "";
        public string email = "";
        public string website = "";
        public string business_card_theme = "AURORA";
        public bool is_active = true;
        public CloudProfileItem[] items;

        // Build a brand-new cloud profile from the onboarding draft. This has no
        // SQLite side effects and never converts cloud UUIDs into local long IDs.
        public static CloudProfileWritePayload FromRegistration(
            string name, string headline, string bio, string email, string website,
            IReadOnlyList<string> skills)
        {
            int count = skills?.Count ?? 0;
            var items = new CloudProfileItem[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = new CloudProfileItem
                {
                    item_type = "SKILL",
                    title = skills[i]?.Trim(),
                    description = "",
                    url = "",
                    sort_order = i
                };
            }
            return new CloudProfileWritePayload
            {
                display_name = name?.Trim(),
                headline = headline?.Trim() ?? "",
                bio = bio?.Trim() ?? "",
                email = email?.Trim() ?? "",
                website = website?.Trim() ?? "",
                items = items
            };
        }

        public static CloudProfileWritePayload FromDetail(CloudProfileDetail detail)
        {
            if (detail == null || detail.items == null)
                throw new ArgumentException("서버 프로필이 불완전합니다.", nameof(detail));
            return new CloudProfileWritePayload
            {
                display_name = detail.display_name,
                headline = detail.headline,
                bio = detail.bio,
                email = detail.email,
                website = detail.website,
                business_card_theme = detail.business_card_theme,
                is_active = detail.is_active,
                items = (CloudProfileItem[])detail.items.Clone()
            };
        }

        public static bool TryToJson(CloudProfileWritePayload payload, out string json, out string error)
        {
            json = null;
            error = null;
            if (payload == null)
                return Fail("저장할 프로필이 없습니다.", out error);
            if (string.IsNullOrWhiteSpace(payload.display_name) ||
                payload.display_name.Trim().Length > 100)
                return Fail("이름은 1~100자로 입력해 주세요.", out error);
            if (!ValidOptional(payload.headline, 200) || !ValidOptional(payload.bio, 5000) ||
                !ValidOptional(payload.email, 320) || !ValidOptional(payload.website, 2048))
                return Fail("입력 길이를 확인해 주세요.", out error);
            if (payload.business_card_theme != "AURORA" &&
                payload.business_card_theme != "BLUSH" &&
                payload.business_card_theme != "NEUTRAL")
                return Fail("지원하지 않는 명함 테마입니다.", out error);
            if (payload.items == null || payload.items.Length > 45)
                return Fail("기술·프로젝트·링크 목록이 누락되었거나 너무 많습니다.", out error);
            foreach (var item in payload.items)
            {
                if (item == null || (item.item_type != "SKILL" &&
                    item.item_type != "PROJECT" && item.item_type != "LINK") ||
                    string.IsNullOrWhiteSpace(item.title) || item.title.Trim().Length > 150 ||
                    !ValidOptional(item.description, 2000) ||
                    !ValidOptional(item.url, 2048) ||
                    item.sort_order < 0 || item.sort_order > 999999)
                    return Fail("기술·프로젝트·링크 항목을 확인해 주세요.", out error);
            }
            json = JsonUtility.ToJson(payload);
            return true;
        }

        private static bool ValidOptional(string text, int max) =>
            text == null || text.Trim().Length <= max;

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }

    [Serializable]
    internal sealed class CloudProfileWriteAckEnvelope
    {
        public CloudProfileWriteAckData data;
    }
    [Serializable]
    internal sealed class CloudProfileWriteAckData
    {
        public string id;
    }

    public static class CloudProfileWriteJson
    {
        public static bool TryReadAck(string body, out string cloudId)
        {
            cloudId = null;
            if (string.IsNullOrEmpty(body)) return false;
            try
            {
                var ack = JsonUtility.FromJson<CloudProfileWriteAckEnvelope>(body);
                if (ack?.data == null || !Guid.TryParse(ack.data.id, out _)) return false;
                cloudId = ack.data.id;
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
