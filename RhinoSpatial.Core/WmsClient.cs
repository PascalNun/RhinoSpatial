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
            Timeout = TimeSpan.FromSeconds(30)
        };
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<WmsCapabilitiesInfo>> CapabilitiesCache = new(StringComparer.OrdinalIgnoreCase);
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

        public async Task<List<WmsLayerInfo>> LoadLayersAsync(string baseUrl)
        {
            return (await LoadCapabilitiesAsync(baseUrl)).Layers;
        }

        public async Task<WmsCapabilitiesInfo> LoadCapabilitiesAsync(string baseUrl)
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

            var requestBaseUrl = string.IsNullOrWhiteSpace(options.GetMapBaseUrl) ? options.BaseUrl : options.GetMapBaseUrl;
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
            builder.Append(OgcUrlUtilities.FormatBoundingBox(options.BoundingBox));
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

        public async Task<WmsImageResult> DownloadImageAsync(WmsRequestOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.GetMapBaseUrl))
            {
                var capabilities = await LoadCapabilitiesAsync(options.BaseUrl);
                options = CloneOptions(options);

                if (!string.IsNullOrWhiteSpace(capabilities.GetMapUrl))
                {
                    options.GetMapBaseUrl = capabilities.GetMapUrl;
                }

                if (!string.IsNullOrWhiteSpace(capabilities.ServiceVersion))
                {
                    options.Version = capabilities.ServiceVersion;
                }
            }

            Directory.CreateDirectory(ImageCacheDirectory);
            var currentOptions = CloneOptions(options);
            HttpResponseMessage? response = null;
            string requestUrl = string.Empty;
            string contentType = options.Format;

            while (true)
            {
                requestUrl = BuildGetMapRequestUrl(currentOptions);
                response?.Dispose();

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                response = await SharedHttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"The WMS server returned {(int)response.StatusCode} {response.ReasonPhrase}. URL: {requestUrl}",
                        null,
                        response.StatusCode);
                }

                contentType = response.Content.Headers.ContentType?.MediaType ?? currentOptions.Format;
                if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var responseText = await response.Content.ReadAsStringAsync();
                var exceptionMessage = TryExtractServiceExceptionMessage(responseText);

                if (IsImageTooLargeException(exceptionMessage) &&
                    TryReduceImageSize(currentOptions, out var reducedOptions))
                {
                    currentOptions = reducedOptions;
                    continue;
                }

                throw new InvalidOperationException(
                    !string.IsNullOrWhiteSpace(exceptionMessage)
                        ? $"The WMS server returned a service exception instead of an image: {exceptionMessage}"
                        : $"The WMS server returned '{contentType}' instead of an image. Check the selected layer name, SRS, and bounding box.");
            }

            var fileExtension = ResolveFileExtension(options.Format, contentType);
            var fileName = $"{ComputeRequestHash(requestUrl)}{fileExtension}";
            var localFilePath = Path.Combine(ImageCacheDirectory, fileName);

            if (!File.Exists(localFilePath) || new FileInfo(localFilePath).Length == 0)
            {
                await using var responseStream = await response!.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(localFilePath);
                await responseStream.CopyToAsync(fileStream);
            }

            response?.Dispose();

            return new WmsImageResult(requestUrl, localFilePath, contentType);
        }

        private async Task<WmsCapabilitiesInfo> LoadCapabilitiesUncachedAsync(string normalizedBaseUrl)
        {
            var requestUrl = BuildGetCapabilitiesRequestUrl(normalizedBaseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            using var response = await SharedHttpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"The WMS server returned {(int)response.StatusCode} {response.ReasonPhrase}. URL: {requestUrl}",
                    null,
                    response.StatusCode);
            }

            return WmsCapabilitiesReader.ReadCapabilities(responseText);
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
