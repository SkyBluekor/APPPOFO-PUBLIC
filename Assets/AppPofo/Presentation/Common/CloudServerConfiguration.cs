using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AppPofo.Presentation.Common
{
    // Build configuration, never a credential store. Remote faces require explicit opt-in.
    public static class CloudServerConfiguration
    {
        public const string ProductionUrl = "https://portfolio-api.example.invalid";
        [Serializable] public sealed class Settings
        {
            public string environment = "offline";
            public string staging_base_url = "";
            public bool staging_faces_enabled;
            public bool production_faces_enabled;
        }
        private static Settings settings;
        private static Settings Current
        {
            get
            {
                if (settings != null) return settings;
                var asset = Resources.Load<TextAsset>("AppPofoCloudEnvironment");
                try { settings = asset == null ? new Settings() : JsonUtility.FromJson<Settings>(asset.text); }
                catch { settings = new Settings { environment = "invalid" }; }
                return settings ?? (settings = new Settings { environment = "invalid" });
            }
        }
        public static bool ValidStagingUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
                string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
                uri.AbsolutePath == "/" && uri.Host.EndsWith(".workers.dev", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Host, new Uri(ProductionUrl).Host, StringComparison.OrdinalIgnoreCase);
        }
        // Invalid staging configuration never falls back to production.
        public static string BaseUrl => Current.environment == "production" ? ProductionUrl :
            Current.environment == "staging" && ValidStagingUrl(Current.staging_base_url)
                ? "https://" + new Uri(Current.staging_base_url).IdnHost.ToLowerInvariant() : "https://invalid.invalid";
        public static bool IsStaging => Current.environment == "staging";
        public static bool IsProduction => Current.environment == "production";
        public static string EnvironmentName => Current.environment;
        public static string FaceDecisionVersion => IsProduction ? "production-v1" : "staging-unvalidated-v1";
        public static bool StagingFacesEnabled => Current.environment == "staging" &&
            Current.staging_faces_enabled && ValidStagingUrl(Current.staging_base_url);
        public static bool RemoteFacesEnabled => StagingFacesEnabled ||
            (IsProduction && Current.production_faces_enabled && BaseUrl == ProductionUrl);
        public static string Scope
        {
            get
            {
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(BaseUrl)))
                        .Replace("-", "").Substring(0, 24);
            }
        }
        public static string EnvironmentLabel => Current.environment == "staging" ? "검증 서버" :
            Current.environment == "production" ? "별도 설정 서버" : "오프라인";
    }
}
