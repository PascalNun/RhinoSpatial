using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WfsCore
{
    internal static class OgcUrlUtilities
    {
        public static string NormalizeBaseUrl(string baseUrl, IReadOnlySet<string> reservedQueryKeys)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("BaseUrl is required.", nameof(baseUrl));
            }

            var trimmedBaseUrl = baseUrl.Trim();
            var querySeparatorIndex = trimmedBaseUrl.IndexOf('?');

            if (querySeparatorIndex < 0)
            {
                return trimmedBaseUrl;
            }

            var basePath = trimmedBaseUrl[..querySeparatorIndex];
            var query = trimmedBaseUrl[(querySeparatorIndex + 1)..];
            var preservedQueryParts = new List<string>();

            foreach (var queryPart in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equalsIndex = queryPart.IndexOf('=');
                var rawKey = equalsIndex >= 0 ? queryPart[..equalsIndex] : queryPart;
                var key = Uri.UnescapeDataString(rawKey.Replace('+', ' '));

                if (reservedQueryKeys.Contains(key))
                {
                    continue;
                }

                preservedQueryParts.Add(queryPart);
            }

            return preservedQueryParts.Count == 0
                ? basePath
                : $"{basePath}?{string.Join("&", preservedQueryParts)}";
        }

        public static string FormatBoundingBox(BoundingBox2D boundingBox, string? srsName = null)
        {
            var builder = new StringBuilder();

            builder.Append(FormatCoordinate(boundingBox.MinX));
            builder.Append(",");
            builder.Append(FormatCoordinate(boundingBox.MinY));
            builder.Append(",");
            builder.Append(FormatCoordinate(boundingBox.MaxX));
            builder.Append(",");
            builder.Append(FormatCoordinate(boundingBox.MaxY));

            if (!string.IsNullOrWhiteSpace(srsName))
            {
                builder.Append(",");
                builder.Append(Uri.EscapeDataString(srsName));
            }

            return builder.ToString();
        }

        public static string FormatCoordinate(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
