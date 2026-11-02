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
            Timeout = Timeout.InfiniteTimeSpan
        };

        private static readonly ServiceMetadataCache<WcsCapabilitiesInfo> SharedCapabilities = new();
        private static readonly ServiceMetadataCache<WcsCoverageDescription> SharedDescriptions = new();
        private readonly ServiceMetadataCache<WcsCapabilitiesInfo> _capabilities;
        private readonly ServiceMetadataCache<WcsCoverageDescription> _descriptions;
        private readonly HttpClient _http;
        private readonly ServiceRequestPolicy _policy;
        private readonly string _cacheDirectory;

        public WcsClient() : this(null) { }
        public WcsClient(HttpClient? http, string? cacheDirectory = null, ServiceRequestPolicy? policy = null)
        {
            _http = http ?? SharedHttpClient;
            _policy = policy ?? new ServiceRequestPolicy();
            _policy.Validate();
            _capabilities = http is null && policy is null ? SharedCapabilities : new();
            _descriptions = http is null && policy is null ? SharedDescriptions : new();
            _cacheDirectory = cacheDirectory ?? TerrainCacheDirectory;
        }

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

        public Task<WcsCapabilitiesInfo> LoadCapabilitiesAsync(string baseUrl) => LoadCapabilitiesAsync(baseUrl, CancellationToken.None);
        public Task<WcsCapabilitiesInfo> LoadCapabilitiesAsync(string baseUrl, CancellationToken token) =>
            ServiceRequests.RunAsync(_policy, token, ct => _capabilities.GetAsync(BuildGetCapabilitiesRequestUrl(baseUrl), ct, async loadToken =>
            {
                var metadata = await ServiceRequests.MetadataAsync(_http, BuildGetCapabilitiesRequestUrl(baseUrl), _policy, loadToken).ConfigureAwait(false);
                var value = WcsCapabilitiesReader.ReadCapabilities(metadata.Text);
                loadToken.ThrowIfCancellationRequested();
                return (value, metadata.Bytes);
            }));

        public Task<WcsCoverageDescription> LoadCoverageDescriptionAsync(WcsRequestOptions options) => LoadCoverageDescriptionAsync(options, CancellationToken.None);
        public Task<WcsCoverageDescription> LoadCoverageDescriptionAsync(WcsRequestOptions options, CancellationToken token) =>
            ServiceRequests.RunAsync(_policy, token, ct => _descriptions.GetAsync(BuildDescribeCoverageRequestUrl(options), ct, async loadToken =>
            {
                var metadata = await ServiceRequests.MetadataAsync(_http, BuildDescribeCoverageRequestUrl(options), _policy, loadToken).ConfigureAwait(false);
                var value = WcsCapabilitiesReader.ReadCoverageDescription(metadata.Text);
                loadToken.ThrowIfCancellationRequested();
                return (value, metadata.Bytes);
            }));

        public Task<WcsCoverageResult> DownloadCoverageAsync(WcsRequestOptions options) => DownloadCoverageAsync(options, CancellationToken.None);
        public Task<WcsCoverageResult> DownloadCoverageAsync(WcsRequestOptions options, CancellationToken token) =>
            ServiceRequests.RunAsync(_policy, token, ct => DownloadCoverageCoreAsync(options, ct));

        private async Task<WcsCoverageResult> DownloadCoverageCoreAsync(WcsRequestOptions options, CancellationToken token)
        {
            Directory.CreateDirectory(_cacheDirectory);
            var requestUrl = BuildGetCoverageRequestUrl(options);
            var final = Path.Combine(_cacheDirectory, "v2-" + ComputeRequestHash(requestUrl) + ResolveFileExtension(options.Format));
            using var lease = await ServiceFileCache.LockAsync(final, token).ConfigureAwait(false);
            var cachedType = await ServiceFileCache.ReadAsync(final, _policy.MaximumCoverageBytes, token).ConfigureAwait(false);
            if (cachedType is not null)
            {
                var cachedRaster = TerrainRasterReader.ReadRaster(final, options.CoverageId, options.SrsName, token);
                return new WcsCoverageResult(requestUrl, final, cachedType, cachedRaster, true);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            return await ServiceRequests.SendAsync(_http, request, _policy, token, async (response, ct) =>
            {
                await ServiceRequests.EnsureSuccessAsync(response, ct).ConfigureAwait(false);
                var type = response.Content.Headers.ContentType?.MediaType ?? options.Format;
                var spool = ServiceFileCache.Temporary(final);
                var extracted = ServiceFileCache.Temporary(final);
                try
                {
                    await using (var output = new FileStream(spool, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                        await ServiceRequests.CopyAsync(response.Content, output, _policy.MaximumCoverageBytes, ct).ConfigureAwait(false);
                    var selected = spool;
                    if (type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
                    {
                        type = await MultipartCoverage.ExtractAsync(spool, extracted, response.Content.Headers.ContentType, ct).ConfigureAwait(false);
                        selected = extracted;
                    }
                    if (!FileHasTiffSignature(selected))
                    {
                        using var input = File.OpenRead(selected);
                        var bytes = new byte[Math.Min(ServiceRequests.MaximumErrorBytes, checked((int)Math.Min(input.Length, int.MaxValue)))];
                        input.ReadExactly(bytes);
                        throw new InvalidDataException("The WCS server did not return a readable TIFF coverage. " + TryReadServiceException(Encoding.UTF8.GetString(bytes)));
                    }
                    var raster = TerrainRasterReader.ReadRaster(selected, options.CoverageId, options.SrsName, ct);
                    await ServiceFileCache.PublishAsync(selected, final, type, ct).ConfigureAwait(false);
                    return new WcsCoverageResult(requestUrl, final, type, raster, false);
                }
                finally { File.Delete(spool); File.Delete(extracted); }
            }).ConfigureAwait(false);
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

    }
}
