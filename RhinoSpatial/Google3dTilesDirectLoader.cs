using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesDirectLoadResult
    {
        public List<Google3dTilesReferenceSession.DisplayPrimitive> Primitives { get; init; } = new();

        public string Status { get; init; } = string.Empty;

        public static Google3dTilesDirectLoadResult Failed(string status)
        {
            return new Google3dTilesDirectLoadResult
            {
                Status = status
            };
        }
    }

    internal static class Google3dTilesDirectLoader
    {
        private const int MaxVisitedTiles = 1800;
        private const int MaxExternalTilesets = 18;
        private const int MaxContentTiles = 12;
        private const int MaxQueueSize = 3000;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public static async Task<Google3dTilesDirectLoadResult> LoadAsync(
            string apiKey,
            BoundingBox2D boundingBox4326,
            SpatialContext2D spatialContext,
            Action<string>? reportProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Google3dTilesDirectLoadResult.Failed("Google 3D Tiles direct loader needs a user-managed Google Maps API key.");
            }

            var rootUri = BuildGoogleTilesUri("/v1/3dtiles/root.json", apiKey, null);
            reportProgress?.Invoke("Google 3D Tiles direct loader is requesting the root tileset from Google...");
            using var rootDocument = await LoadJsonDocumentAsync(rootUri, cancellationToken).ConfigureAwait(false);
            reportProgress?.Invoke("Google 3D Tiles root tileset received. Traversing intersecting tile nodes...");
            var rootElement = rootDocument.RootElement;
            var session = TryGetSession(rootUri) ?? TryGetSessionFromJson(rootElement);

            var rootTile = TryGetProperty(rootElement, "root", out var root)
                ? root.Clone()
                : rootElement.Clone();
            var queue = new Queue<TileVisit>();
            queue.Enqueue(new TileVisit(rootTile, rootUri, IdentityMatrix(), 0));

            var descriptors = new List<Google3dTilesTileDescriptor>();
            var visitedTiles = 0;
            var loadedExternalTilesets = 0;

            while (queue.Count > 0 &&
                   descriptors.Count < MaxContentTiles &&
                   visitedTiles < MaxVisitedTiles)
            {
                var visit = queue.Dequeue();
                visitedTiles++;

                var tileTransform = CombineMatrices(
                    visit.Transform,
                    TryReadTransform(visit.Tile, out var localTransform) ? localTransform : IdentityMatrix());

                if (!TileIntersectsStudyArea(visit.Tile, boundingBox4326))
                {
                    continue;
                }

                foreach (var contentUriText in ReadContentUris(visit.Tile))
                {
                    var contentUri = BuildContentUri(contentUriText, visit.BaseUri, apiKey, session);
                    var contentPath = contentUri.AbsolutePath;

                    if (contentPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                        loadedExternalTilesets < MaxExternalTilesets)
                    {
                        loadedExternalTilesets++;
                        try
                        {
                            reportProgress?.Invoke($"Google 3D Tiles direct loader is resolving child tileset {loadedExternalTilesets}...");
                            using var externalDocument = await LoadJsonDocumentAsync(contentUri, cancellationToken).ConfigureAwait(false);
                            var externalRootElement = externalDocument.RootElement;
                            session ??= TryGetSessionFromJson(externalRootElement);
                            if (TryGetProperty(externalRootElement, "root", out var externalRoot))
                            {
                                queue.Enqueue(new TileVisit(externalRoot.Clone(), contentUri, tileTransform, visit.Depth + 1));
                            }
                        }
                        catch
                        {
                            // A missing child tileset should not prevent nearby visible tiles from loading.
                        }

                        continue;
                    }

                    if (!contentPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    descriptors.Add(new Google3dTilesTileDescriptor
                    {
                        Url = contentUri.ToString(),
                        Transform = tileTransform
                    });
                    reportProgress?.Invoke($"Google 3D Tiles direct loader found {descriptors.Count} candidate GLB tile content URL(s)...");

                    if (descriptors.Count >= MaxContentTiles)
                    {
                        break;
                    }
                }

                if (descriptors.Count >= MaxContentTiles)
                {
                    break;
                }

                if (TryGetProperty(visit.Tile, "children", out var children) &&
                    children.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in children.EnumerateArray())
                    {
                        if (queue.Count >= MaxQueueSize)
                        {
                            break;
                        }

                        queue.Enqueue(new TileVisit(child.Clone(), visit.BaseUri, tileTransform, visit.Depth + 1));
                    }
                }
            }

            if (descriptors.Count == 0)
            {
                return Google3dTilesDirectLoadResult.Failed(
                    $"Google 3D Tiles direct loader did not find GLB tile content intersecting the selected area after visiting {visitedTiles} tile node(s).");
            }

            reportProgress?.Invoke($"Google 3D Tiles direct loader is decoding {descriptors.Count} tile content URL(s) for Rhino preview...");
            var primitives = await Google3dTilesTileContentLoader
                .LoadDisplayPrimitivesAsync(descriptors, spatialContext, cancellationToken)
                .ConfigureAwait(false);
            var triangleCount = primitives.Sum(static primitive => primitive.Mesh.Faces.TriangleCount);

            return new Google3dTilesDirectLoadResult
            {
                Primitives = primitives,
                Status = primitives.Count > 0
                    ? $"Google 3D Tiles direct loader decoded {primitives.Count} reference mesh(es) from {descriptors.Count} tile content URL(s), {triangleCount} triangles."
                    : $"Google 3D Tiles direct loader found {descriptors.Count} tile content URL(s), but none produced viewport geometry inside the selected area."
            };
        }

        private static async Task<JsonDocument> LoadJsonDocumentAsync(Uri uri, CancellationToken cancellationToken)
        {
            using var response = await HttpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Google 3D Tiles request failed ({(int)response.StatusCode}) for {uri.AbsolutePath}: {TrimStatusBody(body)}");
            }

            return JsonDocument.Parse(body);
        }

        private static Uri BuildContentUri(string contentUriText, Uri baseUri, string apiKey, string? session)
        {
            if (contentUriText.StartsWith("/v1/3dtiles/", StringComparison.OrdinalIgnoreCase))
            {
                return BuildGoogleTilesUri(contentUriText, apiKey, session);
            }

            var uri = Uri.TryCreate(contentUriText, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(baseUri, contentUriText);

            if (!string.Equals(uri.Host, "tile.googleapis.com", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            return BuildGoogleTilesUri(uri.PathAndQuery, apiKey, session);
        }

        private static Uri BuildGoogleTilesUri(string pathAndQuery, string apiKey, string? session)
        {
            var builder = new UriBuilder(
                pathAndQuery.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(pathAndQuery)
                    : new Uri(new Uri("https://tile.googleapis.com"), pathAndQuery));
            var query = ParseQuery(builder.Query);

            if (!query.ContainsKey("key"))
            {
                query["key"] = apiKey;
            }

            if (!string.IsNullOrWhiteSpace(session) &&
                !query.ContainsKey("session") &&
                builder.Path.StartsWith("/v1/3dtiles/datasets/", StringComparison.OrdinalIgnoreCase))
            {
                query["session"] = session!;
            }

            builder.Query = string.Join(
                "&",
                query.Select(entry => $"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value)}"));
            return builder.Uri;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var trimmed = query.TrimStart('?');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return result;
            }

            foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var keyValue = part.Split('=', 2);
                var key = Uri.UnescapeDataString(keyValue[0]);
                var value = keyValue.Length == 2 ? Uri.UnescapeDataString(keyValue[1]) : string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = value;
                }
            }

            return result;
        }

        private static IEnumerable<string> ReadContentUris(JsonElement tile)
        {
            if (TryGetProperty(tile, "content", out var content) &&
                TryReadUri(content, out var uri))
            {
                yield return uri;
            }

            if (!TryGetProperty(tile, "contents", out var contents) ||
                contents.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var entry in contents.EnumerateArray())
            {
                if (TryReadUri(entry, out var contentUri))
                {
                    yield return contentUri;
                }
            }
        }

        private static bool TryReadUri(JsonElement content, out string uri)
        {
            uri = string.Empty;
            foreach (var propertyName in new[] { "uri", "url" })
            {
                if (TryGetProperty(content, propertyName, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                {
                    uri = property.GetString() ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(uri);
                }
            }

            return false;
        }

        private static bool TileIntersectsStudyArea(JsonElement tile, BoundingBox2D studyArea4326)
        {
            if (!TryGetProperty(tile, "boundingVolume", out var boundingVolume))
            {
                return true;
            }

            if (TryGetProperty(boundingVolume, "region", out var region) &&
                region.ValueKind == JsonValueKind.Array)
            {
                var values = region.EnumerateArray()
                    .Select(static value => value.GetDouble())
                    .ToArray();
                if (values.Length >= 4)
                {
                    var tileBox = new BoundingBox2D(
                        RadiansToDegrees(values[0]),
                        RadiansToDegrees(values[1]),
                        RadiansToDegrees(values[2]),
                        RadiansToDegrees(values[3]));
                    return RhinoSpatialContextTools.DoBoundingBoxesIntersect(tileBox, studyArea4326);
                }
            }

            if (TryGetProperty(boundingVolume, "sphere", out var sphere) &&
                sphere.ValueKind == JsonValueKind.Array)
            {
                var values = sphere.EnumerateArray()
                    .Select(static value => value.GetDouble())
                    .ToArray();
                if (values.Length >= 4 &&
                    Google3dTilesCoordinateConverter.TryConvertEcefToGeodetic(
                        values[0],
                        values[1],
                        values[2],
                        out var latitude,
                        out var longitude,
                        out _))
                {
                    var radiusDegrees = Math.Max(values[3] / 111_000.0, 0.00001);
                    var tileBox = new BoundingBox2D(
                        longitude - radiusDegrees,
                        latitude - radiusDegrees,
                        longitude + radiusDegrees,
                        latitude + radiusDegrees);
                    return RhinoSpatialContextTools.DoBoundingBoxesIntersect(tileBox, studyArea4326);
                }
            }

            if (TryGetProperty(boundingVolume, "box", out var box) &&
                box.ValueKind == JsonValueKind.Array)
            {
                var values = box.EnumerateArray()
                    .Select(static value => value.GetDouble())
                    .ToArray();
                if (values.Length >= 3 &&
                    Google3dTilesCoordinateConverter.TryConvertEcefToGeodetic(
                        values[0],
                        values[1],
                        values[2],
                        out var latitude,
                        out var longitude,
                        out _))
                {
                    var tileBox = new BoundingBox2D(longitude - 0.05, latitude - 0.05, longitude + 0.05, latitude + 0.05);
                    return RhinoSpatialContextTools.DoBoundingBoxesIntersect(tileBox, studyArea4326);
                }
            }

            return true;
        }

        private static bool TryReadTransform(JsonElement tile, out List<double> transform)
        {
            transform = new List<double>();
            if (!TryGetProperty(tile, "transform", out var transformElement) ||
                transformElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            transform = transformElement.EnumerateArray()
                .Select(static value => value.GetDouble())
                .ToList();
            return transform.Count == 16;
        }

        private static List<double> IdentityMatrix()
        {
            return new List<double>
            {
                1.0, 0.0, 0.0, 0.0,
                0.0, 1.0, 0.0, 0.0,
                0.0, 0.0, 1.0, 0.0,
                0.0, 0.0, 0.0, 1.0
            };
        }

        private static List<double> CombineMatrices(IReadOnlyList<double> parent, IReadOnlyList<double> local)
        {
            if (parent.Count != 16)
            {
                return local.ToList();
            }

            if (local.Count != 16)
            {
                return parent.ToList();
            }

            var result = new double[16];
            for (var column = 0; column < 4; column++)
            {
                for (var row = 0; row < 4; row++)
                {
                    var sum = 0.0;
                    for (var k = 0; k < 4; k++)
                    {
                        sum += parent[(k * 4) + row] * local[(column * 4) + k];
                    }

                    result[(column * 4) + row] = sum;
                }
            }

            return result.ToList();
        }

        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        private static string? TryGetSession(Uri uri)
        {
            var query = ParseQuery(uri.Query);
            return query.TryGetValue("session", out var session) ? session : null;
        }

        private static string? TryGetSessionFromJson(JsonElement root)
        {
            foreach (var propertyName in new[] { "session", "sessionId" })
            {
                if (TryGetProperty(root, propertyName, out var sessionProperty) &&
                    sessionProperty.ValueKind == JsonValueKind.String)
                {
                    var session = sessionProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(session))
                    {
                        return session;
                    }
                }
            }

            return null;
        }

        private static double RadiansToDegrees(double value)
        {
            return value * (180.0 / Math.PI);
        }

        private static string TrimStatusBody(string body)
        {
            var trimmed = body.Trim();
            if (trimmed.Length <= 220)
            {
                return trimmed;
            }

            return trimmed[..220] + "...";
        }

        private sealed record TileVisit(JsonElement Tile, Uri BaseUri, List<double> Transform, int Depth);
    }
}
