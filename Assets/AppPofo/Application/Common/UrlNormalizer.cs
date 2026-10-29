using System;

namespace AppPofo.Application.Common
{
    public static class UrlNormalizer
    {
        public static string NormalizeRequired(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL을 입력해주세요.", nameof(url));
            }

            return Normalize(url);
        }

        public static string NormalizeOptional(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            return Normalize(url);
        }

        private static string Normalize(string url)
        {
            string normalized = url.Trim();

            if (!normalized.StartsWith(
                    "http://",
                    StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://" + normalized;
            }

            if (!Uri.TryCreate(
                    normalized,
                    UriKind.Absolute,
                    out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException(
                    "올바른 URL을 입력해주세요.",
                    nameof(url));
            }

            return normalized;
        }
    }
}