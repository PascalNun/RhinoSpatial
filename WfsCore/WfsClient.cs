using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace WfsCore
{
    public class WfsClient
    {
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        private static readonly ConcurrentDictionary<string, Task<List<WfsLayerInfo>>> LayerCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ReservedQueryKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "SERVICE",
            "REQUEST",
            "VERSION",
            "TYPENAME",
            "TYPENAMES",
            "SRSNAME",
            "CRSNAME",
            "MAXFEATURES",
            "COUNT",
            "OUTPUTFORMAT",
            "BBOX"
        };

        static WfsClient()
        {
            SharedHttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RhinoWFS", "1.0"));
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }

        public async Task<List<WfsFeature>> LoadFeaturesAsync(WfsRequestOptions options)
        {
            var requestUrl = BuildGetFeatureRequestUrl(options);
            var response = await GetStringAsync(requestUrl);

            if (TryReadFeatures(response, options.TypeName, out var features))
            {
                return features;
            }

            var fallbackOptions = CreateGmlFallbackRequestOptions(options);

            if (fallbackOptions is null)
            {
                throw CreateUnsupportedFeatureResponseException(response);
            }

            var fallbackRequestUrl = BuildGetFeatureRequestUrl(fallbackOptions);
            var fallbackResponse = await GetStringAsync(fallbackRequestUrl);

            if (TryReadFeatures(fallbackResponse, options.TypeName, out features))
            {
                return features;
            }

            throw CreateUnsupportedFeatureResponseException(fallbackResponse);
        }

        public async Task<List<WfsLayerInfo>> LoadLayersAsync(string baseUrl)
        {
            var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
            var loadTask = LayerCache.GetOrAdd(normalizedBaseUrl, LoadLayersUncachedAsync);

            try
            {
                return await loadTask;
            }
            catch
            {
                LayerCache.TryRemove(normalizedBaseUrl, out _);
                throw;
            }
        }

        public static string BuildGetFeatureRequestUrl(WfsRequestOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new ArgumentException("BaseUrl is required.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.TypeName))
            {
                throw new ArgumentException("TypeName is required.", nameof(options));
            }

            var normalizedBaseUrl = NormalizeBaseUrl(options.BaseUrl);
            var queryPrefix = normalizedBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var builder = new StringBuilder();

            builder.Append(normalizedBaseUrl);
            builder.Append(queryPrefix);
            builder.Append("SERVICE=WFS");
            builder.Append("&VERSION=");
            builder.Append(Uri.EscapeDataString(options.Version));
            builder.Append("&REQUEST=GetFeature");
            builder.Append("&TYPENAME=");
            builder.Append(Uri.EscapeDataString(options.TypeName));

            if (!string.IsNullOrWhiteSpace(options.SrsName))
            {
                builder.Append("&SRSNAME=");
                builder.Append(Uri.EscapeDataString(options.SrsName));
            }

            if (options.MaxFeatures > 0)
            {
                builder.Append("&MAXFEATURES=");
                builder.Append(options.MaxFeatures);
            }

            if (!string.IsNullOrWhiteSpace(options.OutputFormat))
            {
                builder.Append("&OUTPUTFORMAT=");
                builder.Append(Uri.EscapeDataString(options.OutputFormat));
            }

            if (options.BoundingBox is not null)
            {
                builder.Append("&BBOX=");
                builder.Append(FormatBoundingBox(options.BoundingBox, options.SrsName));
            }

            return builder.ToString();
        }

        public static string BuildGetCapabilitiesRequestUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("BaseUrl is required.", nameof(baseUrl));
            }

            var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
            var queryPrefix = normalizedBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var builder = new StringBuilder();

            builder.Append(normalizedBaseUrl);
            builder.Append(queryPrefix);
            builder.Append("SERVICE=WFS");
            builder.Append("&REQUEST=GetCapabilities");

            return builder.ToString();
        }

        private static string FormatBoundingBox(BoundingBox2D boundingBox, string? srsName)
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

        private static string FormatCoordinate(double value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private async Task<List<WfsLayerInfo>> LoadLayersUncachedAsync(string normalizedBaseUrl)
        {
            var requestUrl = BuildGetCapabilitiesRequestUrl(normalizedBaseUrl);
            var response = await GetStringAsync(requestUrl);
            return WfsCapabilitiesReader.ReadLayers(response);
        }

        private static WfsRequestOptions? CreateGmlFallbackRequestOptions(WfsRequestOptions options)
        {
            if (!ShouldRetryWithGml(options.OutputFormat))
            {
                return null;
            }

            return new WfsRequestOptions
            {
                BaseUrl = options.BaseUrl,
                TypeName = options.TypeName,
                MaxFeatures = options.MaxFeatures,
                Version = "2.0.0",
                SrsName = options.SrsName,
                OutputFormat = "application/gml+xml; version=3.2",
                BoundingBox = options.BoundingBox
            };
        }

        private static string NormalizeBaseUrl(string baseUrl)
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

                if (ReservedQueryKeys.Contains(key))
                {
                    continue;
                }

                preservedQueryParts.Add(queryPart);
            }

            return preservedQueryParts.Count == 0
                ? basePath
                : $"{basePath}?{string.Join("&", preservedQueryParts)}";
        }

        private static bool TryReadFeatures(string responseText, string sourceLayerName, out List<WfsFeature> features)
        {
            var trimmedResponse = responseText.TrimStart();

            try
            {
                if (trimmedResponse.StartsWith("{", StringComparison.Ordinal) ||
                    trimmedResponse.StartsWith("[", StringComparison.Ordinal))
                {
                    features = GeoJsonReader.ReadFeatures(responseText, sourceLayerName);
                    return true;
                }

                if (trimmedResponse.StartsWith("<", StringComparison.Ordinal))
                {
                    features = GmlReader.ReadFeatures(responseText, sourceLayerName);
                    return true;
                }
            }
            catch
            {
            }

            features = new List<WfsFeature>();
            return false;
        }

        private static bool ShouldRetryWithGml(string? outputFormat)
        {
            if (string.IsNullOrWhiteSpace(outputFormat))
            {
                return true;
            }

            return string.Equals(outputFormat, "application/json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(outputFormat, "geojson", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(outputFormat, "GEOJSON", StringComparison.OrdinalIgnoreCase);
        }

        private static Exception CreateUnsupportedFeatureResponseException(string responseText)
        {
            var trimmedResponse = responseText.TrimStart();

            if (trimmedResponse.StartsWith("<", StringComparison.Ordinal))
            {
                try
                {
                    GmlReader.ReadFeatures(responseText, string.Empty);
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            return new InvalidOperationException("The WFS service returned a feature response that RhinoWFS could not read as GeoJSON or GML.");
        }

        private async Task<string> GetStringAsync(string requestUrl)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                using var response = await SharedHttpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"The WFS server returned {(int)response.StatusCode} {response.ReasonPhrase}. URL: {requestUrl}",
                        null,
                        response.StatusCode);
                }

                return responseText;
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("The WFS request timed out after 30 seconds.", ex);
            }
        }
    }
}
