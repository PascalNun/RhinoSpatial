using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal static class SpatialContextHelperHost
    {
        internal sealed record SpatialContextSelection(string BoundingBox4326, string BoundingBox25832, string BoundingBox25833, string BoundingBox27700, string BoundingBox3857, string BoundingBox4283, string BoundingBox7844)
        {
            public bool HasSelection =>
                !string.IsNullOrWhiteSpace(BoundingBox4326) ||
                !string.IsNullOrWhiteSpace(BoundingBox25832) ||
                !string.IsNullOrWhiteSpace(BoundingBox25833) ||
                !string.IsNullOrWhiteSpace(BoundingBox27700) ||
                !string.IsNullOrWhiteSpace(BoundingBox3857) ||
                !string.IsNullOrWhiteSpace(BoundingBox4283) ||
                !string.IsNullOrWhiteSpace(BoundingBox7844);
        }

        private const string HtmlResourceName = "RhinoSpatial.Resources.SpatialContextHelper.html";
        private static readonly object SyncRoot = new();
        private static TcpListener? _listener;
        private static CancellationTokenSource? _cancellationTokenSource;
        private static Task? _serverTask;
        private static int _port;
        private static string? _htmlDocument;
        private static SpatialContextSelection _latestSelection = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        public static event EventHandler? SelectionChanged;

        public static string EnsureStarted()
        {
            lock (SyncRoot)
            {
                if (IsRunning())
                {
                    return BuildUrl(_port);
                }

                _cancellationTokenSource?.Cancel();
                _listener?.Stop();

                _cancellationTokenSource = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _serverTask = Task.Run(() => RunServerAsync(_listener, _cancellationTokenSource.Token));

                return BuildUrl(_port);
            }
        }

        public static void OpenInBrowser()
        {
            OpenInBrowser(null, null);
        }

        public static void OpenInBrowser(BoundingBox2D? initialViewBoundingBox4326, string? preferredSrs)
        {
            OpenInBrowser(initialViewBoundingBox4326, preferredSrs, null);
        }

        public static void OpenInBrowser(BoundingBox2D? initialViewBoundingBox4326, string? preferredSrs, SpatialContextSelection? persistedSelection)
        {
            var url = EnsureStarted();
            var queryParts = new List<string>();

            if (initialViewBoundingBox4326 is not null)
            {
                var viewText = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{initialViewBoundingBox4326.MinX},{initialViewBoundingBox4326.MinY},{initialViewBoundingBox4326.MaxX},{initialViewBoundingBox4326.MaxY}");
                queryParts.Add($"view4326={Uri.EscapeDataString(viewText)}");
            }

            if (!string.IsNullOrWhiteSpace(preferredSrs))
            {
                queryParts.Add($"preferredSrs={Uri.EscapeDataString(preferredSrs)}");
            }

            if (persistedSelection is not null && !string.IsNullOrWhiteSpace(persistedSelection.BoundingBox4326))
            {
                queryParts.Add($"selected4326={Uri.EscapeDataString(persistedSelection.BoundingBox4326)}");
            }

            if (queryParts.Count > 0)
            {
                var queryPrefix = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                url = $"{url}{queryPrefix}{string.Join("&", queryParts)}";
            }

            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = url,
                    UseShellExecute = false
                });

                return;
            }

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = url,
                UseShellExecute = false
            });
        }

        public static SpatialContextSelection GetLatestSelection()
        {
            lock (SyncRoot)
            {
                return _latestSelection;
            }
        }

        public static void RestoreSelection(SpatialContextSelection selection)
        {
            if (!selection.HasSelection)
            {
                return;
            }

            lock (SyncRoot)
            {
                _latestSelection = selection;
            }
        }

        private static bool IsRunning()
        {
            return _listener is not null &&
                   _serverTask is not null &&
                   !_serverTask.IsCanceled &&
                   !_serverTask.IsCompleted &&
                   !_serverTask.IsFaulted;
        }

        private static string BuildUrl(int port)
        {
            return $"http://127.0.0.1:{port}/";
        }

        private static async Task RunServerAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClient client;

                    try
                    {
                        client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using var _ = client;
            using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            string? headerLine;

            do
            {
                headerLine = await reader.ReadLineAsync().ConfigureAwait(false);
            }
            while (!string.IsNullOrEmpty(headerLine));

            var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var requestMethod = requestParts.Length >= 1 ? requestParts[0] : "GET";
            var requestTarget = requestParts.Length >= 2 ? requestParts[1] : "/";
            var requestPath = requestTarget;
            var queryParameters = ParseQueryParameters(requestTarget);
            var querySeparatorIndex = requestPath.IndexOf('?');

            if (querySeparatorIndex >= 0)
            {
                requestPath = requestPath[..querySeparatorIndex];
            }

            if (requestPath == "/" || requestPath.StartsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                var htmlBytes = Encoding.UTF8.GetBytes(GetHtmlDocument());
                await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", htmlBytes, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(requestMethod, "GET", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(requestPath, "/api/selection", StringComparison.OrdinalIgnoreCase))
            {
                queryParameters.TryGetValue("bbox4326", out var boundingBox4326);
                queryParameters.TryGetValue("bbox25832", out var boundingBox25832);
                queryParameters.TryGetValue("bbox25833", out var boundingBox25833);
                queryParameters.TryGetValue("bbox27700", out var boundingBox27700);
                queryParameters.TryGetValue("bbox3857", out var boundingBox3857);
                queryParameters.TryGetValue("bbox4283", out var boundingBox4283);
                queryParameters.TryGetValue("bbox7844", out var boundingBox7844);
                UpdateLatestSelection(
                    boundingBox4326 ?? string.Empty,
                    boundingBox25832 ?? string.Empty,
                    boundingBox25833 ?? string.Empty,
                    boundingBox27700 ?? string.Empty,
                    boundingBox3857 ?? string.Empty,
                    boundingBox4283 ?? string.Empty,
                    boundingBox7844 ?? string.Empty);

                var responseBody = Encoding.UTF8.GetBytes("{\"ok\":true}");
                await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", responseBody, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (requestPath.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(stream, "204 No Content", "text/plain; charset=utf-8", Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
                return;
            }

            var body = Encoding.UTF8.GetBytes("Not Found");
            await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", body, cancellationToken).ConfigureAwait(false);
        }

        private static async Task WriteResponseAsync(Stream stream, string status, string contentType, byte[] body, CancellationToken cancellationToken)
        {
            var headerBuilder = new StringBuilder();
            headerBuilder.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            headerBuilder.Append("Content-Type: ").Append(contentType).Append("\r\n");
            headerBuilder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            headerBuilder.Append("Cache-Control: no-store\r\n");
            headerBuilder.Append("Connection: close\r\n");
            headerBuilder.Append("\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(headerBuilder.ToString());
            await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

            if (body.Length > 0)
            {
                await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void UpdateLatestSelection(string boundingBox4326, string boundingBox25832, string boundingBox25833, string boundingBox27700, string boundingBox3857, string boundingBox4283, string boundingBox7844)
        {
            var normalized4326 = boundingBox4326.Trim();
            var normalized25832 = boundingBox25832.Trim();
            var normalized25833 = boundingBox25833.Trim();
            var normalized27700 = boundingBox27700.Trim();
            var normalized3857 = boundingBox3857.Trim();
            var normalized4283 = boundingBox4283.Trim();
            var normalized7844 = boundingBox7844.Trim();
            var shouldRaiseEvent = false;

            lock (SyncRoot)
            {
                if (string.Equals(_latestSelection.BoundingBox4326, normalized4326, StringComparison.Ordinal) &&
                    string.Equals(_latestSelection.BoundingBox25832, normalized25832, StringComparison.Ordinal) &&
                    string.Equals(_latestSelection.BoundingBox25833, normalized25833, StringComparison.Ordinal) &&
                    string.Equals(_latestSelection.BoundingBox27700, normalized27700, StringComparison.Ordinal) &&
                    string.Equals(_latestSelection.BoundingBox3857, normalized3857, StringComparison.Ordinal) &&
                    string.Equals(_latestSelection.BoundingBox4283, normalized4283, StringComparison.Ordinal) &&
                    string.Equals(_latestSelection.BoundingBox7844, normalized7844, StringComparison.Ordinal))
                {
                    return;
                }

                _latestSelection = new SpatialContextSelection(normalized4326, normalized25832, normalized25833, normalized27700, normalized3857, normalized4283, normalized7844);
                shouldRaiseEvent = true;
            }

            if (shouldRaiseEvent)
            {
                SelectionChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        private static Dictionary<string, string> ParseQueryParameters(string requestTarget)
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var querySeparatorIndex = requestTarget.IndexOf('?');

            if (querySeparatorIndex < 0 || querySeparatorIndex >= requestTarget.Length - 1)
            {
                return parameters;
            }

            var query = requestTarget[(querySeparatorIndex + 1)..];
            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in pairs)
            {
                var equalsIndex = pair.IndexOf('=');
                var rawKey = equalsIndex >= 0 ? pair[..equalsIndex] : pair;
                var rawValue = equalsIndex >= 0 ? pair[(equalsIndex + 1)..] : string.Empty;
                var key = Uri.UnescapeDataString(rawKey.Replace('+', ' '));
                var value = Uri.UnescapeDataString(rawValue.Replace('+', ' '));

                if (!string.IsNullOrWhiteSpace(key))
                {
                    parameters[key] = value;
                }
            }

            return parameters;
        }

        private static string GetHtmlDocument()
        {
            if (_htmlDocument is not null)
            {
                return _htmlDocument;
            }

            using Stream? resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(HtmlResourceName);

            if (resourceStream is null)
            {
                throw new InvalidOperationException("The embedded Spatial Context helper page could not be found.");
            }

            using var reader = new StreamReader(resourceStream, Encoding.UTF8);
            _htmlDocument = reader.ReadToEnd();

            return _htmlDocument;
        }
    }
}
