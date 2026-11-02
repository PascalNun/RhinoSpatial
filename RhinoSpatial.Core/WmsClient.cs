using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RhinoSpatial.Core
{
    public class WmsClient
    {
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        private static readonly ServiceMetadataCache<WmsCapabilitiesInfo> SharedCapabilitiesCache = new();
        private readonly ServiceMetadataCache<WmsCapabilitiesInfo> _capabilities;
        private readonly HttpClient _http;
        private readonly ServiceRequestPolicy _policy;
        private readonly string _cacheDirectory;

        public WmsClient() : this(null) { }
        public WmsClient(HttpClient? http, string? cacheDirectory = null, ServiceRequestPolicy? policy = null)
        {
            _http = http ?? SharedHttpClient;
            _policy = policy ?? new ServiceRequestPolicy { RequestTimeout = TimeSpan.FromSeconds(30) };
            _policy.Validate();
            _capabilities = http is null && policy is null ? SharedCapabilitiesCache : new();
            _cacheDirectory = cacheDirectory ?? ImageCacheDirectory;
        }
        private static readonly HashSet<string> ReservedQueryKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "SERVICE",
            "REQUEST",
            "VERSION",
            "LAYERS",
            "STYLES",
            "CRS",
            "SRS",
            "BBOX",
            "WIDTH",
            "HEIGHT",
            "FORMAT",
            "TRANSPARENT"
        };

        private static readonly string ImageCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhinoSpatial",
            "WmsCache");

        static WmsClient()
        {
            SharedHttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RhinoSpatial", "1.0"));
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }

        public Task<List<WmsLayerInfo>> LoadLayersAsync(string baseUrl) => LoadLayersAsync(baseUrl, CancellationToken.None);
        public async Task<List<WmsLayerInfo>> LoadLayersAsync(string baseUrl, CancellationToken token) =>
            (await LoadCapabilitiesAsync(baseUrl, token).ConfigureAwait(false)).Layers;

        public Task<WmsCapabilitiesInfo> LoadCapabilitiesAsync(string baseUrl) => LoadCapabilitiesAsync(baseUrl, CancellationToken.None);
        public Task<WmsCapabilitiesInfo> LoadCapabilitiesAsync(string baseUrl, CancellationToken token) =>
            ServiceRequests.RunAsync(_policy, token, ct => _capabilities.GetAsync(
                BuildGetCapabilitiesRequestUrl(baseUrl), ct, async loadToken =>
                {
                    var metadata = await ServiceRequests.MetadataAsync(_http, BuildGetCapabilitiesRequestUrl(baseUrl), _policy, loadToken).ConfigureAwait(false);
                    var value = WmsCapabilitiesReader.ReadCapabilities(metadata.Text);
                    loadToken.ThrowIfCancellationRequested();
                    return (value, metadata.Bytes);
                }));

        public static string BuildGetMapRequestUrl(WmsRequestOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new ArgumentException("BaseUrl is required.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.LayerName))
            {
                throw new ArgumentException("LayerName is required.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.SrsName))
            {
                throw new ArgumentException("SrsName is required.", nameof(options));
            }

            if (options.Width <= 0 || options.Height <= 0)
            {
                throw new ArgumentException("Width and Height must be greater than zero.", nameof(options));
            }

            var requestBaseUrl = string.IsNullOrWhiteSpace(options.GetMapBaseUrl)
                ? options.BaseUrl
                : OgcUrlUtilities.PreferSecureSameHostOperationUrl(options.BaseUrl, options.GetMapBaseUrl);
            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(requestBaseUrl, ReservedQueryKeys);
            var queryPrefix = normalizedBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var builder = new StringBuilder();

            builder.Append(normalizedBaseUrl);
            builder.Append(queryPrefix);
            builder.Append("SERVICE=WMS");
            builder.Append("&VERSION=");
            builder.Append(Uri.EscapeDataString(options.Version));
            builder.Append("&REQUEST=GetMap");
            builder.Append("&LAYERS=");
            builder.Append(Uri.EscapeDataString(options.LayerName));
            builder.Append("&STYLES=");

            if (options.Version.StartsWith("1.3", StringComparison.Ordinal))
            {
                builder.Append("&CRS=");
            }
            else
            {
                builder.Append("&SRS=");
            }

            builder.Append(Uri.EscapeDataString(options.SrsName));
            builder.Append("&BBOX=");
            builder.Append(options.Version.StartsWith("1.3", StringComparison.Ordinal)
                ? OgcUrlUtilities.FormatBoundingBox(
                    OgcUrlUtilities.OrderBoundingBoxForAuthorityAxis(options.BoundingBox, options.SrsName))
                : OgcUrlUtilities.FormatBoundingBox(options.BoundingBox));
            builder.Append("&WIDTH=");
            builder.Append(options.Width);
            builder.Append("&HEIGHT=");
            builder.Append(options.Height);
            builder.Append("&FORMAT=");
            builder.Append(Uri.EscapeDataString(options.Format));
            builder.Append("&TRANSPARENT=");
            builder.Append(options.Transparent ? "TRUE" : "FALSE");

            return builder.ToString();
        }

        public static string BuildGetCapabilitiesRequestUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("BaseUrl is required.", nameof(baseUrl));
            }

            var normalizedBaseUrl = OgcUrlUtilities.NormalizeBaseUrl(baseUrl, ReservedQueryKeys);
            var queryPrefix = normalizedBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var builder = new StringBuilder();

            builder.Append(normalizedBaseUrl);
            builder.Append(queryPrefix);
            builder.Append("SERVICE=WMS");
            builder.Append("&REQUEST=GetCapabilities");

            return builder.ToString();
        }

        public Task<WmsImageResult> DownloadImageAsync(WmsRequestOptions options) => DownloadImageAsync(options, CancellationToken.None);
        public Task<WmsImageResult> DownloadImageAsync(WmsRequestOptions options, CancellationToken token) =>
            ServiceRequests.RunAsync(_policy, token, ct => DownloadImageCoreAsync(options, ct));

        private async Task<WmsImageResult> DownloadImageCoreAsync(WmsRequestOptions options, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(options.GetMapBaseUrl))
            {
                var capabilities = await LoadCapabilitiesAsync(options.BaseUrl, token).ConfigureAwait(false);
                options = CloneOptions(options);
                if (!string.IsNullOrWhiteSpace(capabilities.GetMapUrl)) options.GetMapBaseUrl = capabilities.GetMapUrl;
                if (!string.IsNullOrWhiteSpace(capabilities.ServiceVersion)) options.Version = capabilities.ServiceVersion;
            }
            Directory.CreateDirectory(_cacheDirectory);
            var currentOptions = CloneOptions(options);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var requestUrl = BuildGetMapRequestUrl(currentOptions);
                var hash = "v2-" + ComputeRequestHash(requestUrl);
                using var lease = await ServiceFileCache.LockAsync(Path.Combine(_cacheDirectory, hash), token).ConfigureAwait(false);
                foreach (var path in Directory.EnumerateFiles(_cacheDirectory, hash + ".*"))
                {
                    if (Path.GetExtension(path) is not (".png" or ".jpg" or ".tif" or ".gif" or ".img")) continue;
                    var type = await ServiceFileCache.ReadAsync(path, _policy.MaximumImageBytes, token).ConfigureAwait(false);
                    if (type is null) continue;
                    ServiceFileCache.ValidateImage(path, _policy.MaximumImagePixels, token, decode: false);
                    token.ThrowIfCancellationRequested();
                    return new WmsImageResult(requestUrl, path, type, UsedCachedFile: true);
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                var attempt = await ServiceRequests.SendAsync(_http, request, _policy, token, async (response, ct) =>
                {
                    await ServiceRequests.EnsureSuccessAsync(response, ct).ConfigureAwait(false);
                    var type = response.Content.Headers.ContentType?.MediaType ?? currentOptions.Format;
                    if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = await ServiceRequests.TextAsync(response, ServiceRequests.MaximumErrorBytes, ct).ConfigureAwait(false);
                        return (Result: (WmsImageResult?)null, Error: TryExtractServiceExceptionMessage(text) ?? $"The WMS server returned '{type}' instead of an image.");
                    }
                    var final = Path.Combine(_cacheDirectory, hash + ResolveFileExtension(currentOptions.Format, type));
                    var temporary = ServiceFileCache.Temporary(final);
                    try
                    {
                        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                            await ServiceRequests.CopyAsync(response.Content, output, _policy.MaximumImageBytes, ct).ConfigureAwait(false);
                        ServiceFileCache.ValidateImage(temporary, _policy.MaximumImagePixels, ct);
                        await ServiceFileCache.PublishAsync(temporary, final, type, ct).ConfigureAwait(false);
                        return (Result: (WmsImageResult?)new WmsImageResult(requestUrl, final, type), Error: (string?)null);
                    }
                    finally { File.Delete(temporary); }
                }).ConfigureAwait(false);
                if (attempt.Result is not null) return attempt.Result;
                if (IsImageTooLargeException(attempt.Error) && TryReduceImageSize(currentOptions, out var reduced))
                { currentOptions = reduced; continue; }
                throw new InvalidOperationException(attempt.Error);
            }
        }

        private static WmsRequestOptions CloneOptions(WmsRequestOptions options)
        {
            return new WmsRequestOptions
            {
                BaseUrl = options.BaseUrl,
                GetMapBaseUrl = options.GetMapBaseUrl,
                LayerName = options.LayerName,
                BoundingBox = options.BoundingBox,
                SrsName = options.SrsName,
                Width = options.Width,
                Height = options.Height,
                Version = options.Version,
                Format = options.Format,
                Transparent = options.Transparent
            };
        }

        private static bool IsImageTooLargeException(string? exceptionMessage)
        {
            return !string.IsNullOrWhiteSpace(exceptionMessage) &&
                   exceptionMessage.IndexOf("image size too large", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReduceImageSize(WmsRequestOptions options, out WmsRequestOptions reducedOptions)
        {
            const double scale = 0.85;
            const int minimumDimension = 512;

            var nextWidth = Math.Max(minimumDimension, (int)Math.Floor(options.Width * scale));
            var nextHeight = Math.Max(minimumDimension, (int)Math.Floor(options.Height * scale));

            if (nextWidth >= options.Width && nextHeight >= options.Height)
            {
                reducedOptions = options;
                return false;
            }

            if (nextWidth == options.Width && nextHeight == options.Height)
            {
                reducedOptions = options;
                return false;
            }

            reducedOptions = CloneOptions(options);
            reducedOptions.Width = nextWidth;
            reducedOptions.Height = nextHeight;
            return true;
        }

        private static string ResolveFileExtension(string requestedFormat, string contentType)
        {
            var normalized = !string.IsNullOrWhiteSpace(contentType) ? contentType : requestedFormat;

            if (normalized.Contains("png", StringComparison.OrdinalIgnoreCase))
            {
                return ".png";
            }

            if (normalized.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("jpg", StringComparison.OrdinalIgnoreCase))
            {
                return ".jpg";
            }

            if (normalized.Contains("tiff", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("tif", StringComparison.OrdinalIgnoreCase))
            {
                return ".tif";
            }

            if (normalized.Contains("gif", StringComparison.OrdinalIgnoreCase))
            {
                return ".gif";
            }

            return ".img";
        }

        private static string ComputeRequestHash(string requestUrl)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(requestUrl));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string? TryExtractServiceExceptionMessage(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }

            var match = Regex.Match(
                responseText,
                @"<ServiceException[^>]*>(?<message>.*?)</ServiceException>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (match.Success)
            {
                return match.Groups["message"].Value.Trim();
            }

            match = Regex.Match(
                responseText,
                @"<ExceptionText>(?<message>.*?)</ExceptionText>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return match.Success ? match.Groups["message"].Value.Trim() : null;
        }
    }
}
