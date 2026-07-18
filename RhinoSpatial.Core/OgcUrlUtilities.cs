using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RhinoSpatial.Core
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

        public static string PreferSecureSameHostOperationUrl(string entryUrl, string operationUrl)
        {
            if (!Uri.TryCreate(entryUrl, UriKind.Absolute, out var entryUri) ||
                !Uri.TryCreate(operationUrl, UriKind.Absolute, out var operationUri) ||
                !entryUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !operationUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                !operationUri.IsDefaultPort ||
                !entryUri.Host.Equals(operationUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return operationUrl;
            }

            var secureOperationUri = new UriBuilder(operationUri)
            {
                Scheme = Uri.UriSchemeHttps,
                Port = -1
            };
            return secureOperationUri.Uri.AbsoluteUri;
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

        public static string FormatBoundingBoxWithAuthorityAxisOrder(BoundingBox2D boundingBox, string? srsName = null)
        {
            return FormatBoundingBox(OrderBoundingBoxForAuthorityAxis(boundingBox, srsName), srsName);
        }

        public static BoundingBox2D OrderBoundingBoxForAuthorityAxis(BoundingBox2D boundingBox, string? srsName)
        {
            return UsesLatitudeLongitudeAxisOrder(srsName)
                ? new BoundingBox2D(boundingBox.MinY, boundingBox.MinX, boundingBox.MaxY, boundingBox.MaxX)
                : boundingBox;
        }

        public static bool UsesLatitudeLongitudeAxisOrder(string? srsName)
        {
            if (string.IsNullOrWhiteSpace(srsName) ||
                srsName.Contains("CRS:84", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return srsName.Contains("4326", StringComparison.OrdinalIgnoreCase) ||
                   srsName.Contains("4258", StringComparison.OrdinalIgnoreCase) ||
                   srsName.Contains("4283", StringComparison.OrdinalIgnoreCase) ||
                   srsName.Contains("7423", StringComparison.OrdinalIgnoreCase) ||
                   srsName.Contains("7844", StringComparison.OrdinalIgnoreCase);
        }

        public static string FormatCoordinate(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
