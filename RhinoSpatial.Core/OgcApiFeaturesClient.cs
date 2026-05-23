using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace RhinoSpatial.Core
{
    public class OgcApiFeaturesClient
    {
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static OgcApiFeaturesClient()
        {
            SharedHttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RhinoSpatial", "1.0"));
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/geo+json"));
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }

        public async Task<List<WfsFeature>> LoadFeaturesAsync(
            string sourceUrl,
            string sourceLayerName,
            BoundingBox2D? wgs84BoundingBox,
            int maxFeatures)
        {
            var requestUrl = BuildItemsRequestUrl(sourceUrl, wgs84BoundingBox, maxFeatures);
            var response = await SharedHttpClient.GetAsync(requestUrl).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"The OGC API Features endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}. URL: {requestUrl}");
            }

            return GeoJsonReader.ReadFeatures(body, sourceLayerName);
        }

        public static bool LooksLikeOgcApiFeaturesUrl(string? sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl) ||
                !Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            var path = uri.AbsolutePath;
            return path.Contains("/collections/", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("/items", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildItemsRequestUrl(
            string sourceUrl,
            BoundingBox2D? wgs84BoundingBox,
            int maxFeatures)
        {
            var builder = new UriBuilder(sourceUrl.Trim());
            if (!builder.Path.Contains("/items", StringComparison.OrdinalIgnoreCase) &&
                builder.Path.Contains("/collections/", StringComparison.OrdinalIgnoreCase))
            {
                builder.Path = builder.Path.TrimEnd('/') + "/items";
            }

            var query = ParseQuery(builder.Query);
            query["f"] = "json";
            if (wgs84BoundingBox is not null)
            {
                query["bbox"] = string.Join(
                    ",",
                    FormatNumber(wgs84BoundingBox.MinX),
                    FormatNumber(wgs84BoundingBox.MinY),
                    FormatNumber(wgs84BoundingBox.MaxX),
                    FormatNumber(wgs84BoundingBox.MaxY));
            }

            if (maxFeatures > 0)
            {
                query["limit"] = maxFeatures.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            builder.Query = BuildQuery(query);
            return builder.Uri.ToString();
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var trimmed = query.TrimStart('?');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return values;
            }

            foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
                if (separatorIndex < 0)
                {
                    values[Uri.UnescapeDataString(part)] = string.Empty;
                    continue;
                }

                values[Uri.UnescapeDataString(part[..separatorIndex])] =
                    Uri.UnescapeDataString(part[(separatorIndex + 1)..]);
            }

            return values;
        }

        private static string BuildQuery(IReadOnlyDictionary<string, string> values)
        {
            var builder = new StringBuilder();
            foreach (var entry in values)
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }

                builder.Append(Uri.EscapeDataString(entry.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(entry.Value));
            }

            return builder.ToString();
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
