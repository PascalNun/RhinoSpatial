using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

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

            var requestBaseUrl = string.IsNullOrWhiteSpace(options.DescribeCoverageBaseUrl)
                ? options.BaseUrl
                : OgcUrlUtilities.PreferSecureSameHostOperationUrl(options.BaseUrl, options.DescribeCoverageBaseUrl);
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

            var requestBaseUrl = string.IsNullOrWhiteSpace(options.GetCoverageBaseUrl)
                ? options.BaseUrl
                : OgcUrlUtilities.PreferSecureSameHostOperationUrl(options.BaseUrl, options.GetCoverageBaseUrl);
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
                var responseBody = await response.Content.ReadAsStringAsync();
                var serviceMessage = TryReadServiceException(responseBody);
                throw new HttpRequestException(
                    string.IsNullOrWhiteSpace(serviceMessage)
                        ? $"The WCS server returned {(int)response.StatusCode} {response.ReasonPhrase}. URL: {requestUrl}"
                        : $"The WCS server returned {(int)response.StatusCode} {response.ReasonPhrase}: {serviceMessage}. URL: {requestUrl}",
                    null,
                    response.StatusCode);
            }

            var responseBytes = await response.Content.ReadAsByteArrayAsync();
            var responseContentType = response.Content.Headers.ContentType?.MediaType ?? format;
            if (responseContentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            {
                var boundary = TryReadMultipartBoundary(response.Content.Headers.ContentType);
                if (string.IsNullOrWhiteSpace(boundary) ||
                    !TryExtractCoveragePart(responseBytes, boundary, out responseBytes, out responseContentType))
                {
                    throw new InvalidOperationException("The WCS server returned a multipart response without a readable coverage part.");
                }
            }

            if (format.Contains("tiff", StringComparison.OrdinalIgnoreCase) && !HasTiffSignature(responseBytes))
            {
                var responseText = TryDecodeText(responseBytes);
                var serviceMessage = TryReadServiceException(responseText);
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(serviceMessage)
                        ? "The WCS server returned coverage content that was not a readable TIFF."
                        : $"The WCS server could not produce the requested TIFF coverage: {serviceMessage}");
            }

            await File.WriteAllBytesAsync(localFilePath, responseBytes);

            return new CoverageDownloadResult(
                localFilePath,
                responseContentType,
                UsedCachedFile: false);
        }

        private static bool TryGetCachedCoverageFile(string localFilePath, string format, out CoverageDownloadResult result)
        {
            var fileInfo = new FileInfo(localFilePath);
            if (fileInfo.Exists && fileInfo.Length > 0)
            {
                if (format.Contains("tiff", StringComparison.OrdinalIgnoreCase) && !FileHasTiffSignature(localFilePath))
                {
                    File.Delete(localFilePath);
                    result = default!;
                    return false;
                }

                result = new CoverageDownloadResult(localFilePath, format, UsedCachedFile: true);
                return true;
            }

            result = default!;
            return false;
        }

        private static string TryReadMultipartBoundary(MediaTypeHeaderValue? contentType)
        {
            if (contentType is null)
            {
                return string.Empty;
            }

            foreach (var parameter in contentType.Parameters)
            {
                if (string.Equals(parameter.Name, "boundary", StringComparison.OrdinalIgnoreCase))
                {
                    return parameter.Value?.Trim().Trim('"') ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static bool TryExtractCoveragePart(
            byte[] multipartBytes,
            string boundary,
            out byte[] coverageBytes,
            out string contentType)
        {
            coverageBytes = Array.Empty<byte>();
            contentType = string.Empty;
            var delimiter = Encoding.ASCII.GetBytes($"--{boundary}");
            var searchIndex = 0;

            while (TryFindBytes(multipartBytes, delimiter, searchIndex, out var delimiterIndex))
            {
                var headerStart = delimiterIndex + delimiter.Length;
                if (headerStart + 1 < multipartBytes.Length &&
                    multipartBytes[headerStart] == (byte)'-' &&
                    multipartBytes[headerStart + 1] == (byte)'-')
                {
                    break;
                }

                SkipLineBreak(multipartBytes, ref headerStart);
                if (!TryFindHeaderEnd(multipartBytes, headerStart, out var headerEnd, out var separatorLength))
                {
                    break;
                }

                var headerText = Encoding.ASCII.GetString(multipartBytes, headerStart, headerEnd - headerStart);
                var dataStart = headerEnd + separatorLength;
                if (!TryFindBytes(multipartBytes, delimiter, dataStart, out var nextDelimiterIndex))
                {
                    break;
                }

                var dataEnd = nextDelimiterIndex;
                while (dataEnd > dataStart &&
                       (multipartBytes[dataEnd - 1] == (byte)'\r' || multipartBytes[dataEnd - 1] == (byte)'\n'))
                {
                    dataEnd--;
                }

                if (HeaderDescribesCoverage(headerText))
                {
                    coverageBytes = new byte[dataEnd - dataStart];
                    Buffer.BlockCopy(multipartBytes, dataStart, coverageBytes, 0, coverageBytes.Length);
                    contentType = ReadPartContentType(headerText);
                    return coverageBytes.Length > 0;
                }

                searchIndex = nextDelimiterIndex;
            }

            return false;
        }

        private static bool HeaderDescribesCoverage(string headerText)
        {
            return headerText.Contains("Content-Type: image/", StringComparison.OrdinalIgnoreCase) ||
                   headerText.Contains("Content-Description: coverage", StringComparison.OrdinalIgnoreCase) ||
                   headerText.Contains("filename=", StringComparison.OrdinalIgnoreCase) &&
                   (headerText.Contains(".tif", StringComparison.OrdinalIgnoreCase) ||
                    headerText.Contains(".tiff", StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadPartContentType(string headerText)
        {
            foreach (var line in headerText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = line.IndexOf(':');
                if (separatorIndex > 0 &&
                    string.Equals(line[..separatorIndex].Trim(), "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    return line[(separatorIndex + 1)..].Trim();
                }
            }

            return "application/octet-stream";
        }

        private static bool TryFindHeaderEnd(
            byte[] bytes,
            int startIndex,
            out int headerEnd,
            out int separatorLength)
        {
            if (TryFindBytes(bytes, new byte[] { 13, 10, 13, 10 }, startIndex, out headerEnd))
            {
                separatorLength = 4;
                return true;
            }

            if (TryFindBytes(bytes, new byte[] { 10, 10 }, startIndex, out headerEnd))
            {
                separatorLength = 2;
                return true;
            }

            separatorLength = 0;
            return false;
        }

        private static bool TryFindBytes(
            byte[] bytes,
            byte[] pattern,
            int startIndex,
            out int matchIndex)
        {
            for (var index = Math.Max(0, startIndex); index <= bytes.Length - pattern.Length; index++)
            {
                var matches = true;
                for (var patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
                {
                    if (bytes[index + patternIndex] != pattern[patternIndex])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    matchIndex = index;
                    return true;
                }
            }

            matchIndex = -1;
            return false;
        }

        private static void SkipLineBreak(byte[] bytes, ref int index)
        {
            if (index < bytes.Length && bytes[index] == (byte)'\r')
            {
                index++;
            }

            if (index < bytes.Length && bytes[index] == (byte)'\n')
            {
                index++;
            }
        }

        private static bool FileHasTiffSignature(string filePath)
        {
            Span<byte> signature = stackalloc byte[4];
            using var stream = File.OpenRead(filePath);
            return stream.Read(signature) == signature.Length && HasTiffSignature(signature);
        }

        private static bool HasTiffSignature(ReadOnlySpan<byte> bytes)
        {
            return bytes.Length >= 4 &&
                   ((bytes[0] == (byte)'I' && bytes[1] == (byte)'I' &&
                     (bytes[2] == 42 || bytes[2] == 43) && bytes[3] == 0) ||
                    (bytes[0] == (byte)'M' && bytes[1] == (byte)'M' && bytes[2] == 0 &&
                     (bytes[3] == 42 || bytes[3] == 43)));
        }

        private static string TryDecodeText(byte[] bytes)
        {
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TryReadServiceException(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return string.Empty;
            }

            try
            {
                var document = XDocument.Parse(responseText.Trim());
                return document
                    .Descendants()
                    .FirstOrDefault(element =>
                        element.Name.LocalName is "ExceptionText" or "ServiceException")
                    ?.Value
                    ?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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
