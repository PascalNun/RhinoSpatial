using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RhinoSpatial.Core
{
    public class WcsClient
    {
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<WcsCapabilitiesInfo>> CapabilitiesCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<WcsCoverageDescription>> CoverageDescriptionCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<CoverageDownloadResult>> CoverageDownloadCache = new(StringComparer.Ordinal);

        private static readonly HashSet<string> ReservedQueryKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "SERVICE",
            "REQUEST",
            "VERSION",
            "COVERAGEID",
            "FORMAT",
            "SUBSET"
        };

        private static readonly string TerrainCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhinoSpatial",
            "TerrainCache");

        static WcsClient()
        {
            SharedHttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RhinoSpatial", "1.0"));
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }

        public async Task<WcsCapabilitiesInfo> LoadCapabilitiesAsync(string baseUrl)
        {
            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(baseUrl, ReservedQueryKeys);
            var loadTask = CapabilitiesCache.GetOrAdd(normalizedBaseUrl, LoadCapabilitiesUncachedAsync);

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

        public async Task<WcsCoverageDescription> LoadCoverageDescriptionAsync(WcsRequestOptions options)
        {
            var cacheKey = BuildCoverageDescriptionCacheKey(options);
            var loadTask = CoverageDescriptionCache.GetOrAdd(cacheKey, _ => LoadCoverageDescriptionUncachedAsync(options));

            try
            {
                return await loadTask;
            }
            catch
            {
                CoverageDescriptionCache.TryRemove(cacheKey, out _);
                throw;
            }
        }

        public async Task<WcsCoverageResult> DownloadCoverageAsync(WcsRequestOptions options)
        {
            Directory.CreateDirectory(TerrainCacheDirectory);

            var requestUrl = BuildGetCoverageRequestUrl(options);
            var fileExtension = ResolveFileExtension(options.Format);
            var fileName = $"{ComputeRequestHash(requestUrl)}{fileExtension}";
            var localFilePath = Path.Combine(TerrainCacheDirectory, fileName);
            var downloadResult = await DownloadCoverageFileAsync(requestUrl, localFilePath, options.Format);
            var raster = TerrainRasterReader.ReadRaster(localFilePath, options.CoverageId, options.SrsName);

            return new WcsCoverageResult(
                requestUrl,
                localFilePath,
                downloadResult.ContentType,
                raster,
                downloadResult.UsedCachedFile);
        }

        public static string BuildGetCapabilitiesRequestUrl(string baseUrl)
        {
            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(baseUrl, ReservedQueryKeys);
            var queryPrefix = normalizedBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";

            return $"{normalizedBaseUrl}{queryPrefix}SERVICE=WCS&REQUEST=GetCapabilities";
        }

        public static string BuildDescribeCoverageRequestUrl(WcsRequestOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new ArgumentException("BaseUrl is required.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.CoverageId))
            {
                throw new ArgumentException("CoverageId is required.", nameof(options));
            }

            var requestBaseUrl = string.IsNullOrWhiteSpace(options.DescribeCoverageBaseUrl) ? options.BaseUrl : options.DescribeCoverageBaseUrl;
            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(requestBaseUrl, ReservedQueryKeys);
            var queryPrefix = normalizedBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";

            var builder = new StringBuilder();
            builder.Append(normalizedBaseUrl);
            builder.Append(queryPrefix);
            builder.Append("SERVICE=WCS");
            builder.Append("&REQUEST=DescribeCoverage");
            builder.Append("&VERSION=");
            builder.Append(Uri.EscapeDataString(options.Version));
            builder.Append("&COVERAGEID=");
            builder.Append(Uri.EscapeDataString(options.CoverageId));

            return builder.ToString();
        }

        public static string BuildGetCoverageRequestUrl(WcsRequestOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new ArgumentException("BaseUrl is required.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.CoverageId))
            {
                throw new ArgumentException("CoverageId is required.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.SrsName))
            {
                throw new ArgumentException("SrsName is required.", nameof(options));
            }

            var requestBaseUrl = string.IsNullOrWhiteSpace(options.GetCoverageBaseUrl) ? options.BaseUrl : options.GetCoverageBaseUrl;
            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(requestBaseUrl, ReservedQueryKeys);
            var queryPrefix = normalizedBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";

            var bbox = options.BoundingBox;
            var axisX = string.IsNullOrWhiteSpace(options.AxisXLabel) ? "x" : options.AxisXLabel!;
            var axisY = string.IsNullOrWhiteSpace(options.AxisYLabel) ? "y" : options.AxisYLabel!;

            var builder = new StringBuilder();
            builder.Append(normalizedBaseUrl);
            builder.Append(queryPrefix);
            builder.Append("SERVICE=WCS");
            builder.Append("&REQUEST=GetCoverage");
            builder.Append("&VERSION=");
            builder.Append(Uri.EscapeDataString(options.Version));
            builder.Append("&COVERAGEID=");
            builder.Append(Uri.EscapeDataString(options.CoverageId));
            builder.Append("&FORMAT=");
            builder.Append(Uri.EscapeDataString(options.Format));
            builder.Append("&SUBSET=");
            builder.Append(axisX);
            builder.Append("(");
            builder.Append(OgcUrlUtilities.FormatCoordinate(bbox.MinX));
            builder.Append(",");
            builder.Append(OgcUrlUtilities.FormatCoordinate(bbox.MaxX));
            builder.Append(")");
            builder.Append("&SUBSET=");
            builder.Append(axisY);
            builder.Append("(");
            builder.Append(OgcUrlUtilities.FormatCoordinate(bbox.MinY));
            builder.Append(",");
            builder.Append(OgcUrlUtilities.FormatCoordinate(bbox.MaxY));
            builder.Append(")");
            builder.Append("&SUBSETTINGCRS=");
            builder.Append(Uri.EscapeDataString(options.SrsName));
            builder.Append("&OUTPUTCRS=");
            builder.Append(Uri.EscapeDataString(options.SrsName));

            return builder.ToString();
        }

        private static async Task<WcsCapabilitiesInfo> LoadCapabilitiesUncachedAsync(string normalizedBaseUrl)
        {
            var requestUrl = BuildGetCapabilitiesRequestUrl(normalizedBaseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            using var response = await SharedHttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"The WCS server returned {(int)response.StatusCode} {response.ReasonPhrase}. URL: {requestUrl}",
                    null,
                    response.StatusCode);
            }

            var xml = await response.Content.ReadAsStringAsync();
            return WcsCapabilitiesReader.ReadCapabilities(xml);
        }

        private static async Task<WcsCoverageDescription> LoadCoverageDescriptionUncachedAsync(WcsRequestOptions options)
        {
            var requestUrl = BuildDescribeCoverageRequestUrl(options);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            using var response = await SharedHttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"The WCS server returned {(int)response.StatusCode} {response.ReasonPhrase}. URL: {requestUrl}",
                    null,
                    response.StatusCode);
            }

            var xml = await response.Content.ReadAsStringAsync();
            return WcsCapabilitiesReader.ReadCoverageDescription(xml);
        }

        private async Task<CoverageDownloadResult> DownloadCoverageFileAsync(string requestUrl, string localFilePath, string format)
        {
            if (TryGetCachedCoverageFile(localFilePath, format, out var cachedResult))
            {
                return cachedResult;
            }

            var loadTask = CoverageDownloadCache.GetOrAdd(
                requestUrl,
                _ => DownloadCoverageFileUncachedAsync(requestUrl, localFilePath, format));

            try
            {
                return await loadTask;
            }
            finally
            {
                if (loadTask.IsCompleted)
                {
                    CoverageDownloadCache.TryRemove(requestUrl, out _);
                }
            }
        }

        private static async Task<CoverageDownloadResult> DownloadCoverageFileUncachedAsync(string requestUrl, string localFilePath, string format)
        {
            if (TryGetCachedCoverageFile(localFilePath, format, out var cachedResult))
            {
                return cachedResult;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            using var response = await SharedHttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"The WCS server returned {(int)response.StatusCode} {response.ReasonPhrase}. URL: {requestUrl}",
                    null,
                    response.StatusCode);
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(localFilePath);
            await responseStream.CopyToAsync(fileStream);

            return new CoverageDownloadResult(
                localFilePath,
                response.Content.Headers.ContentType?.MediaType ?? format,
                UsedCachedFile: false);
        }

        private static bool TryGetCachedCoverageFile(string localFilePath, string format, out CoverageDownloadResult result)
        {
            var fileInfo = new FileInfo(localFilePath);
            if (fileInfo.Exists && fileInfo.Length > 0)
            {
                result = new CoverageDownloadResult(localFilePath, format, UsedCachedFile: true);
                return true;
            }

            result = default!;
            return false;
        }

        private static string ResolveFileExtension(string format)
        {
            if (format.Contains("tiff", StringComparison.OrdinalIgnoreCase) || format.Contains("tif", StringComparison.OrdinalIgnoreCase))
            {
                return ".tif";
            }

            return ".bin";
        }

        private static string ComputeRequestHash(string requestUrl)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(requestUrl));
            var builder = new StringBuilder(hash.Length * 2);

            foreach (var value in hash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }

        private static string BuildCoverageDescriptionCacheKey(WcsRequestOptions options)
        {
            var requestBaseUrl = string.IsNullOrWhiteSpace(options.DescribeCoverageBaseUrl) ? options.BaseUrl : options.DescribeCoverageBaseUrl;
            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(requestBaseUrl, ReservedQueryKeys);
            return $"{normalizedBaseUrl}|{options.Version}|{options.CoverageId}";
        }

        private sealed record CoverageDownloadResult(
            string LocalFilePath,
            string ContentType,
            bool UsedCachedFile);
    }
}
