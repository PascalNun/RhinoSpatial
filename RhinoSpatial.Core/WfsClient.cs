using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace RhinoSpatial.Core
{
    public class WfsClient
    {
        private static readonly TimeSpan FeatureResponseCacheLifetime = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan FeatureResponseStaleLifetime = TimeSpan.FromMinutes(15);
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        private static readonly ConcurrentDictionary<string, Task<WfsCapabilitiesInfo>> CapabilitiesCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, FeatureResponseCacheEntry> FeatureResponseCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, Task<WfsFeatureResponse>> FeatureResponseTaskCache = new(StringComparer.Ordinal);
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
            SharedHttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RhinoSpatial", "1.0"));
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }

        private sealed record FeatureResponseCacheEntry(WfsFeatureResponse Response, DateTimeOffset StoredAtUtc);

        public async Task<List<WfsFeature>> LoadFeaturesAsync(WfsRequestOptions options)
        {
            var result = await LoadFeaturesWithStatusAsync(options);
            return result.Features;
        }

        public async Task<WfsFeatureLoadResult> LoadFeaturesWithStatusAsync(WfsRequestOptions options)
        {
            var featureResponse = await LoadFeatureResponseAsync(options);

            if (TryReadFeatures(featureResponse.ResponseText, featureResponse.AppliedOptions.TypeName, out var features))
            {
                return new WfsFeatureLoadResult(features, featureResponse.StatusNote);
            }

            throw CreateUnsupportedFeatureResponseException(featureResponse.ResponseText);
        }

        public async Task<WfsFeatureResponse> LoadFeatureResponseAsync(WfsRequestOptions options)
        {
            options = await PrepareRequestOptionsAsync(options);
            var cacheKey = BuildFeatureResponseCacheKey(options);

            if (TryGetCachedFeatureResponse(cacheKey, allowStale: false, out var cachedResponse))
            {
                return cachedResponse;
            }

            var loadTask = FeatureResponseTaskCache.GetOrAdd(cacheKey, _ => LoadFeatureResponseUncachedAsync(options));

            try
            {
                var response = await loadTask;
                FeatureResponseCache[cacheKey] = new FeatureResponseCacheEntry(response, DateTimeOffset.UtcNow);
                return response;
            }
            catch (Exception ex) when (IsTransientFailure(ex) &&
                                       TryGetCachedFeatureResponse(cacheKey, allowStale: true, out var staleCachedResponse))
            {
                return staleCachedResponse with
                {
                    StatusNote = "Using cached WFS result because the live WFS service was temporarily unavailable."
                };
            }
            finally
            {
                if (loadTask.IsCompleted)
                {
                    FeatureResponseTaskCache.TryRemove(cacheKey, out _);
                }
            }
        }

        public async Task<List<WfsLayerInfo>> LoadLayersAsync(string baseUrl)
        {
            return (await LoadCapabilitiesAsync(baseUrl)).Layers;
        }

        public async Task<WfsCapabilitiesInfo> LoadCapabilitiesAsync(string baseUrl)
        {
            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(baseUrl, ReservedQueryKeys);
            var requestedVersion = TryReadQueryValue(baseUrl, "VERSION");
            var cacheKey = string.IsNullOrWhiteSpace(requestedVersion)
                ? normalizedBaseUrl
                : $"{normalizedBaseUrl}|VERSION={requestedVersion}";
            var loadTask = CapabilitiesCache.GetOrAdd(cacheKey, _ => LoadCapabilitiesUncachedAsync(normalizedBaseUrl, requestedVersion));

            try
            {
                return await loadTask;
            }
            catch
            {
                CapabilitiesCache.TryRemove(normalizedBaseUrl, out _);
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

            var requestBaseUrl = string.IsNullOrWhiteSpace(options.GetFeatureBaseUrl) ? options.BaseUrl : options.GetFeatureBaseUrl;
            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(requestBaseUrl, ReservedQueryKeys);
            var queryPrefix = normalizedBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var builder = new StringBuilder();

            builder.Append(normalizedBaseUrl);
            builder.Append(queryPrefix);
            builder.Append("SERVICE=WFS");
            builder.Append("&VERSION=");
            builder.Append(Uri.EscapeDataString(options.Version));
            builder.Append("&REQUEST=GetFeature");
            builder.Append(options.Version.StartsWith("2.", StringComparison.Ordinal) ? "&TYPENAMES=" : "&TYPENAME=");
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
                builder.Append("&COUNT=");
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
                builder.Append(OgcUrlUtilities.FormatBoundingBox(options.BoundingBox, options.SrsName));
            }

            return builder.ToString();
        }

        public static string BuildGetCapabilitiesRequestUrl(
            string baseUrl,
            string? version = null,
            bool useDefaultVersion = true)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("BaseUrl is required.", nameof(baseUrl));
            }

            var requestedVersion = string.IsNullOrWhiteSpace(version)
                ? TryReadQueryValue(baseUrl, "VERSION")
                : version.Trim();
            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(baseUrl, ReservedQueryKeys);
            var queryPrefix = normalizedBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var builder = new StringBuilder();

            builder.Append(normalizedBaseUrl);
            builder.Append(queryPrefix);
            builder.Append("SERVICE=WFS");
            if (!string.IsNullOrWhiteSpace(requestedVersion) || useDefaultVersion)
            {
                builder.Append("&VERSION=");
                builder.Append(Uri.EscapeDataString(string.IsNullOrWhiteSpace(requestedVersion) ? "2.0.0" : requestedVersion));
            }

            builder.Append("&REQUEST=GetCapabilities");

            return builder.ToString();
        }

        private async Task<WfsCapabilitiesInfo> LoadCapabilitiesUncachedAsync(string normalizedBaseUrl, string? requestedVersion)
        {
            Exception? lastException = null;

            foreach (var versionCandidate in CreateCapabilitiesVersionCandidates(requestedVersion))
            {
                var requestUrl = BuildGetCapabilitiesRequestUrl(
                    normalizedBaseUrl,
                    versionCandidate,
                    useDefaultVersion: false);

                try
                {
                    var response = await GetStringAsync(requestUrl);
                    return WfsCapabilitiesReader.ReadCapabilities(response);
                }
                catch (Exception ex) when (requestedVersion is null && IsCapabilitiesVersionRetryCandidate(ex))
                {
                    lastException = ex;
                }
            }

            throw lastException ?? new InvalidOperationException("The WFS capabilities request sequence did not produce a capabilities response.");
        }

        private async Task<WfsFeatureResponse> LoadFeatureResponseUncachedAsync(WfsRequestOptions options)
        {
            Exception? lastRequestException = null;

            foreach (var candidateOptions in CreateRequestSequence(options))
            {
                var requestUrl = BuildGetFeatureRequestUrl(candidateOptions);

                try
                {
                    var response = await GetStringAsync(requestUrl);
                    var statusNote = lastRequestException is null
                        ? string.Empty
                        : $"Retried WFS request with output format '{candidateOptions.OutputFormat}' after the provider rejected an earlier request.";

                    return new WfsFeatureResponse(response, candidateOptions, statusNote);
                }
                catch (Exception ex) when (ShouldTryNextRequestCandidate(ex))
                {
                    lastRequestException = ex;
                }
            }

            throw lastRequestException ?? new InvalidOperationException("The WFS service request sequence did not produce a feature response.");
        }

        private async Task<WfsRequestOptions> PrepareRequestOptionsAsync(WfsRequestOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.GetFeatureBaseUrl))
            {
                return options;
            }

            var capabilities = await LoadCapabilitiesAsync(options.BaseUrl);
            var preparedOptions = CloneOptions(options);

            if (!string.IsNullOrWhiteSpace(capabilities.GetFeatureUrl))
            {
                preparedOptions.GetFeatureBaseUrl = capabilities.GetFeatureUrl;
            }

            if (!string.IsNullOrWhiteSpace(capabilities.ServiceVersion))
            {
                preparedOptions.Version = capabilities.ServiceVersion;
            }

            return preparedOptions;
        }

        private static List<WfsRequestOptions> CreateRequestSequence(WfsRequestOptions options)
        {
            var sequence = new List<WfsRequestOptions> { CloneOptions(options) };

            if (!ShouldRetryWithGml(options.OutputFormat))
            {
                return sequence;
            }

            AppendIfMissing(sequence, options, "2.0.0", "application/gml+xml; version=3.2");
            AppendIfMissing(sequence, options, "1.1.0", "text/xml; subtype=gml/3.1.1");

            return sequence;
        }

        private static void AppendIfMissing(List<WfsRequestOptions> sequence, WfsRequestOptions source, string version, string outputFormat)
        {
            foreach (var existingOptions in sequence)
            {
                if (string.Equals(existingOptions.Version, version, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existingOptions.OutputFormat, outputFormat, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            sequence.Add(new WfsRequestOptions
            {
                BaseUrl = source.BaseUrl,
                GetFeatureBaseUrl = source.GetFeatureBaseUrl,
                TypeName = source.TypeName,
                MaxFeatures = source.MaxFeatures,
                Version = version,
                SrsName = source.SrsName,
                OutputFormat = outputFormat,
                BoundingBox = source.BoundingBox
            });
        }

        private static WfsRequestOptions CloneOptions(WfsRequestOptions options)
        {
            return new WfsRequestOptions
            {
                BaseUrl = options.BaseUrl,
                GetFeatureBaseUrl = options.GetFeatureBaseUrl,
                TypeName = options.TypeName,
                MaxFeatures = options.MaxFeatures,
                Version = options.Version,
                SrsName = options.SrsName,
                OutputFormat = options.OutputFormat,
                BoundingBox = options.BoundingBox
            };
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

        private static bool ShouldTryNextRequestCandidate(Exception ex)
        {
            return ex is HttpRequestException or TimeoutException;
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

            return new InvalidOperationException("The WFS service returned a feature response that RhinoSpatial could not read as GeoJSON or GML.");
        }

        private static string BuildFeatureResponseCacheKey(WfsRequestOptions options)
        {
            var builder = new StringBuilder();
            builder.Append(OgcUrlUtilities.NormalizeBaseUrl(options.BaseUrl, ReservedQueryKeys));
            builder.Append('|');
            builder.Append(options.GetFeatureBaseUrl is null
                ? string.Empty
                : OgcUrlUtilities.NormalizeBaseUrl(options.GetFeatureBaseUrl, ReservedQueryKeys));
            builder.Append('|');
            builder.Append(options.TypeName);
            builder.Append('|');
            builder.Append(options.MaxFeatures);
            builder.Append('|');
            builder.Append(options.Version);
            builder.Append('|');
            builder.Append(options.SrsName);
            builder.Append('|');
            builder.Append(options.OutputFormat);
            builder.Append('|');
            builder.Append(options.BoundingBox is null
                ? string.Empty
                : OgcUrlUtilities.FormatBoundingBox(options.BoundingBox, options.SrsName));
            return builder.ToString();
        }

        private static bool TryGetCachedFeatureResponse(string cacheKey, bool allowStale, out WfsFeatureResponse response)
        {
            response = default!;

            if (!FeatureResponseCache.TryGetValue(cacheKey, out var entry))
            {
                return false;
            }

            var age = DateTimeOffset.UtcNow - entry.StoredAtUtc;
            if (!allowStale && age > FeatureResponseCacheLifetime)
            {
                return false;
            }

            if (allowStale && age > FeatureResponseStaleLifetime)
            {
                return false;
            }

            response = entry.Response;
            return true;
        }

        private static string? TryReadQueryValue(string url, string key)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var queryStart = url.IndexOf('?');
            if (queryStart < 0 || queryStart == url.Length - 1)
            {
                return null;
            }

            var query = url[(queryStart + 1)..];
            foreach (var queryPart in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equalsIndex = queryPart.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var rawKey = queryPart[..equalsIndex];
                var queryKey = Uri.UnescapeDataString(rawKey.Replace('+', ' '));
                if (!string.Equals(queryKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawValue = queryPart[(equalsIndex + 1)..];
                return Uri.UnescapeDataString(rawValue.Replace('+', ' ')).Trim();
            }

            return null;
        }

        private static IEnumerable<string?> CreateCapabilitiesVersionCandidates(string? requestedVersion)
        {
            if (!string.IsNullOrWhiteSpace(requestedVersion))
            {
                yield return requestedVersion.Trim();
                yield break;
            }

            yield return "2.0.0";
            yield return "1.1.0";
            yield return "1.0.0";
            yield return null;
        }

        private static bool IsCapabilitiesVersionRetryCandidate(Exception ex)
        {
            return ex is HttpRequestException or TimeoutException or TaskCanceledException;
        }

        private static bool IsTransientFailure(Exception ex)
        {
            return ex switch
            {
                TimeoutException => true,
                TaskCanceledException => true,
                HttpRequestException httpRequestException when !httpRequestException.StatusCode.HasValue => true,
                HttpRequestException httpRequestException
                    when httpRequestException.StatusCode is var statusCode &&
                         statusCode.HasValue &&
                         (int)statusCode.Value >= 500 => true,
                HttpRequestException httpRequestException
                    when httpRequestException.StatusCode is var statusCode &&
                         statusCode.HasValue &&
                         (int)statusCode.Value == 408 => true,
                HttpRequestException httpRequestException
                    when httpRequestException.StatusCode is var statusCode &&
                         statusCode.HasValue &&
                         (int)statusCode.Value == 429 => true,
                _ => false
            };
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
