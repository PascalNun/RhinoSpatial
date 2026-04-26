using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal static class Google3dTilesViewerHost
    {
        internal sealed record ViewerConfiguration(
            Guid OwnerId,
            string ApiKey,
            string BoundingBox4326,
            string StatusNote)
        {
            public bool IsUsable =>
                OwnerId != Guid.Empty &&
                !string.IsNullOrWhiteSpace(ApiKey) &&
                !string.IsNullOrWhiteSpace(BoundingBox4326);
        }

        private const string HtmlResourceName = "RhinoSpatial.Resources.Google3dTilesViewer.html";
        private static readonly object SyncRoot = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static TcpListener? _listener;
        private static CancellationTokenSource? _cancellationTokenSource;
        private static Task? _serverTask;
        private static int _port;
        private static string? _htmlDocument;
        private static ViewerConfiguration _currentConfiguration = new(Guid.Empty, string.Empty, string.Empty, string.Empty);

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

        public static string UpdateConfiguration(Guid ownerId, string apiKey, BoundingBox2D boundingBox4326, string statusNote)
        {
            var bboxText = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{boundingBox4326.MinX},{boundingBox4326.MinY},{boundingBox4326.MaxX},{boundingBox4326.MaxY}");

            lock (SyncRoot)
            {
                _currentConfiguration = new ViewerConfiguration(
                    ownerId,
                    apiKey?.Trim() ?? string.Empty,
                    bboxText,
                    statusNote?.Trim() ?? string.Empty);
            }

            return EnsureStarted();
        }

        public static string GetCurrentUrl()
        {
            return EnsureStarted();
        }

        public static void EnsureBackgroundRuntime(Guid ownerId, string apiKey, BoundingBox2D boundingBox4326, string statusNote)
        {
            var url = UpdateConfiguration(ownerId, apiKey, boundingBox4326, statusNote);
            Google3dTilesViewerWindow.ShowOrUpdate(url, false);
        }

        public static void OpenInBrowser(Guid ownerId, string apiKey, BoundingBox2D boundingBox4326, string statusNote)
        {
            var url = UpdateConfiguration(ownerId, apiKey, boundingBox4326, statusNote);
            Google3dTilesViewerWindow.ShowOrUpdate(url, true);
        }

        public static void OpenCurrentInBrowser()
        {
            Google3dTilesViewerWindow.ShowOrUpdate(EnsureStarted(), true);
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
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? headerLine;

            do
            {
                headerLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(headerLine))
                {
                    continue;
                }

                var separatorIndex = headerLine.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var headerName = headerLine[..separatorIndex].Trim();
                var headerValue = headerLine[(separatorIndex + 1)..].Trim();
                headers[headerName] = headerValue;
            }
            while (!string.IsNullOrEmpty(headerLine));

            var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var method = requestParts.Length >= 1 ? requestParts[0] : "GET";
            var requestTarget = requestParts.Length >= 2 ? requestParts[1] : "/";
            var requestPath = requestTarget;
            var querySeparatorIndex = requestPath.IndexOf('?');

            if (querySeparatorIndex >= 0)
            {
                requestPath = requestPath[..querySeparatorIndex];
            }

            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                requestPath.Equals("/api/mesh-batch", StringComparison.OrdinalIgnoreCase))
            {
                await HandleMeshBatchRequestAsync(stream, reader, headers, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                requestPath.Equals("/api/runtime-status", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRuntimeStatusRequestAsync(stream, reader, headers, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                requestPath.Equals("/api/tile-batch", StringComparison.OrdinalIgnoreCase))
            {
                await HandleTileBatchRequestAsync(stream, reader, headers, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (requestPath == "/" || requestPath.StartsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                var htmlBytes = Encoding.UTF8.GetBytes(GetHtmlDocument());
                await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", htmlBytes, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(requestPath, "/api/config", StringComparison.OrdinalIgnoreCase))
            {
                ViewerConfiguration configuration;

                lock (SyncRoot)
                {
                    configuration = _currentConfiguration;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    ownerId = configuration.OwnerId.ToString(),
                    apiKey = configuration.ApiKey,
                    boundingBox4326 = configuration.BoundingBox4326,
                    statusNote = configuration.StatusNote,
                    attributionText = "Google Maps",
                    usageNote = "Reference only. RhinoSpatial does not import, bake, or export Google Photorealistic 3D Tiles content."
                });
                var body = Encoding.UTF8.GetBytes(payload);
                await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", body, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (requestPath.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(stream, "204 No Content", "text/plain; charset=utf-8", Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
                return;
            }

            var notFound = Encoding.UTF8.GetBytes("Not Found");
            await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", notFound, cancellationToken).ConfigureAwait(false);
        }

        private static async Task HandleMeshBatchRequestAsync(
            Stream stream,
            StreamReader reader,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken)
        {
            if (!headers.TryGetValue("Content-Length", out var contentLengthText) ||
                !int.TryParse(contentLengthText, out var contentLength) ||
                contentLength <= 0)
            {
                var error = Encoding.UTF8.GetBytes("Mesh batch requests require a valid Content-Length.");
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            var bodyBuffer = new char[contentLength];
            var totalRead = 0;

            while (totalRead < contentLength)
            {
                var read = await reader.ReadAsync(bodyBuffer, totalRead, contentLength - totalRead).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            var bodyText = new string(bodyBuffer, 0, totalRead);

            Google3dTilesMeshBatchPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<Google3dTilesMeshBatchPayload>(bodyText, JsonOptions);
            }
            catch (JsonException)
            {
                var error = Encoding.UTF8.GetBytes("Mesh batch JSON could not be parsed.");
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (payload is null || !Guid.TryParse(payload.OwnerId, out var ownerId))
            {
                var error = Encoding.UTF8.GetBytes("Mesh batch payload is missing a valid ownerId.");
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!Google3dTilesReferenceManager.ApplyRuntimeBatch(ownerId, payload, out var statusMessage))
            {
                var error = Encoding.UTF8.GetBytes(statusMessage);
                await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            var responseBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                ok = true,
                status = statusMessage
            }));
            await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", responseBody, cancellationToken).ConfigureAwait(false);
        }

        private static async Task HandleRuntimeStatusRequestAsync(
            Stream stream,
            StreamReader reader,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken)
        {
            if (!headers.TryGetValue("Content-Length", out var contentLengthText) ||
                !int.TryParse(contentLengthText, out var contentLength) ||
                contentLength <= 0)
            {
                var error = Encoding.UTF8.GetBytes("Runtime status requests require a valid Content-Length.");
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            var bodyBuffer = new char[contentLength];
            var totalRead = 0;

            while (totalRead < contentLength)
            {
                var read = await reader.ReadAsync(bodyBuffer, totalRead, contentLength - totalRead).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            var bodyText = new string(bodyBuffer, 0, totalRead);
            Google3dTilesRuntimeStatusPayload? payload;

            try
            {
                payload = JsonSerializer.Deserialize<Google3dTilesRuntimeStatusPayload>(bodyText, JsonOptions);
            }
            catch (JsonException)
            {
                var error = Encoding.UTF8.GetBytes("Runtime status JSON could not be parsed.");
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (payload is null || !Guid.TryParse(payload.OwnerId, out var ownerId))
            {
                var error = Encoding.UTF8.GetBytes("Runtime status payload is missing a valid ownerId.");
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            var updated = Google3dTilesReferenceManager.UpdateRuntimeStatus(ownerId, payload.Status);
            var responseBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = updated }));
            await WriteResponseAsync(stream, updated ? "200 OK" : "404 Not Found", "application/json; charset=utf-8", responseBody, cancellationToken).ConfigureAwait(false);
        }

        private static async Task HandleTileBatchRequestAsync(
            Stream stream,
            StreamReader reader,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken)
        {
            if (!headers.TryGetValue("Content-Length", out var contentLengthText) ||
                !int.TryParse(contentLengthText, out var contentLength) ||
                contentLength <= 0)
            {
                var error = Encoding.UTF8.GetBytes("Tile batch requests require a valid Content-Length.");
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            var bodyBuffer = new char[contentLength];
            var totalRead = 0;

            while (totalRead < contentLength)
            {
                var read = await reader.ReadAsync(bodyBuffer, totalRead, contentLength - totalRead).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            var bodyText = new string(bodyBuffer, 0, totalRead);
            Google3dTilesTileBatchPayload? payload;

            try
            {
                payload = JsonSerializer.Deserialize<Google3dTilesTileBatchPayload>(bodyText, JsonOptions);
            }
            catch (JsonException)
            {
                var error = Encoding.UTF8.GetBytes("Tile batch JSON could not be parsed.");
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (payload is null || !Guid.TryParse(payload.OwnerId, out var ownerId))
            {
                var error = Encoding.UTF8.GetBytes("Tile batch payload is missing a valid ownerId.");
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await Google3dTilesReferenceManager.ApplyTileBatchAsync(ownerId, payload).ConfigureAwait(false);
            if (!result.Success)
            {
                var error = Encoding.UTF8.GetBytes(result.StatusMessage);
                await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            var responseBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                ok = true,
                status = result.StatusMessage
            }));
            await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", responseBody, cancellationToken).ConfigureAwait(false);
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

        private static string GetHtmlDocument()
        {
            if (_htmlDocument is not null)
            {
                return _htmlDocument;
            }

            using Stream? resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(HtmlResourceName);

            if (resourceStream is null)
            {
                throw new InvalidOperationException("The embedded Google 3D Tiles viewer page could not be found.");
            }

            using var reader = new StreamReader(resourceStream, Encoding.UTF8);
            _htmlDocument = reader.ReadToEnd();
            return _htmlDocument;
        }
    }
}
