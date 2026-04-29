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
        public List<Google3dTilesDisplayPrimitive> Primitives { get; init; } = new();

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
        private const int MaxVisitedTiles = 12000;
        private const int MaxExternalTilesets = 120;
        private const int MaxContentTiles = 12;
        private const int MaxDecodeCandidateTiles = 96;
        private const int MaxQueueSize = 20000;

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
                return Google3dTilesDirectLoadResult.Failed("Google 3D Tiles viewer needs a user-managed Google Maps API key.");
            }

            var rootUri = BuildGoogleTilesUri("/v1/3dtiles/root.json", apiKey, null, null);
            reportProgress?.Invoke("Google 3D Tiles viewer is requesting the root tileset from Google...");
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
                   visitedTiles < MaxVisitedTiles)
            {
                var visit = queue.Dequeue();
                visitedTiles++;

                var tileTransform = CombineMatrices(
                    visit.Transform,
                    TryReadTransform(visit.Tile, out var localTransform) ? localTransform : IdentityMatrix());

                if (!TileIntersectsStudyArea(visit.Tile, boundingBox4326, tileTransform))
                {
                    continue;
                }

                var contentUriTexts = ReadContentUris(visit.Tile).ToList();
                var hasIntersectingRefinement = false;

                foreach (var contentUriText in contentUriTexts)
                {
                    var contentUri = BuildContentUri(contentUriText, visit.BaseUri, apiKey, session);
                    var contentPath = contentUri.AbsolutePath;

                    if (contentPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                        loadedExternalTilesets < MaxExternalTilesets)
                    {
                        loadedExternalTilesets++;
                        hasIntersectingRefinement = true;
                        try
                        {
                            reportProgress?.Invoke($"Google 3D Tiles viewer is resolving child tileset {loadedExternalTilesets}...");
                            using var externalDocument = await LoadJsonDocumentAsync(contentUri, cancellationToken).ConfigureAwait(false);
                            var externalRootElement = externalDocument.RootElement;
                            session ??= TryGetSessionFromJson(externalRootElement);
                            if (TryGetProperty(externalRootElement, "root", out var externalRoot))
                            {
                                var externalTransform = CombineMatrices(
                                    tileTransform,
                                    TryReadTransform(externalRoot, out var externalLocalTransform)
                                        ? externalLocalTransform
                                        : IdentityMatrix());
                                if (TileIntersectsStudyArea(externalRoot, boundingBox4326, externalTransform) &&
                                    queue.Count < MaxQueueSize)
                                {
                                    queue.Enqueue(new TileVisit(externalRoot.Clone(), contentUri, tileTransform, visit.Depth + 1));
                                }
                            }
                        }
                        catch
                        {
                            // A missing child tileset should not prevent nearby visible tiles from loading.
                        }

                        continue;
                    }
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

                        var childTransform = CombineMatrices(
                            tileTransform,
                            TryReadTransform(child, out var childLocalTransform)
                                ? childLocalTransform
                                : IdentityMatrix());
                        if (!TileIntersectsStudyArea(child, boundingBox4326, childTransform))
                        {
                            continue;
                        }

                        hasIntersectingRefinement = true;
                        queue.Enqueue(new TileVisit(child.Clone(), visit.BaseUri, tileTransform, visit.Depth + 1));
                    }
                }

                if (hasIntersectingRefinement)
                {
                    continue;
                }

                foreach (var contentUriText in contentUriTexts)
                {
                    var contentUri = BuildContentUri(contentUriText, visit.BaseUri, apiKey, session);
                    var contentPath = contentUri.AbsolutePath;

                    if (!contentPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    descriptors.Add(new Google3dTilesTileDescriptor
                    {
                        Url = contentUri.ToString(),
                        Transform = tileTransform,
                        Depth = visit.Depth,
                        GeometricError = TryReadGeometricError(visit.Tile)
                    });
                    if (descriptors.Count <= MaxContentTiles ||
                        descriptors.Count % 25 == 0)
                    {
                        reportProgress?.Invoke($"Google 3D Tiles viewer found {descriptors.Count} candidate GLB tile content URL(s)...");
                    }
                }
            }

            if (descriptors.Count == 0)
            {
                return Google3dTilesDirectLoadResult.Failed(
                    $"Google 3D Tiles viewer did not find GLB tile content intersecting the selected area after visiting {visitedTiles} tile node(s).");
            }

            var selectedDescriptors = SelectBestContentDescriptors(descriptors, MaxDecodeCandidateTiles).ToList();
            var decodedPrimitives = new List<Google3dTilesDisplayPrimitive>();
            var aggregateContentResult = new Google3dTilesContentLoadResult();
            var decodedDescriptorCount = 0;

            foreach (var descriptorBatch in selectedDescriptors.Chunk(MaxContentTiles))
            {
                var batch = descriptorBatch.ToList();
                decodedDescriptorCount += batch.Count;
                reportProgress?.Invoke($"Google 3D Tiles viewer is decoding tile content URL(s) {decodedDescriptorCount - batch.Count + 1}-{decodedDescriptorCount} from {selectedDescriptors.Count} selected fine candidate(s)...");
                var contentResult = await Google3dTilesTileContentLoader
                    .LoadDisplayPrimitivesAsync(batch, spatialContext, cancellationToken)
                    .ConfigureAwait(false);
                aggregateContentResult = CombineContentResults(aggregateContentResult, contentResult);
                decodedPrimitives.AddRange(contentResult.Primitives);
            }

            var primitives = decodedPrimitives
                .Where(static primitive => !primitive.IsClippedFallback)
                .ToList();
            var suppressedFallbackCount = decodedPrimitives.Count - primitives.Count;
            var triangleCount = primitives.Sum(static primitive => primitive.Mesh.Faces.TriangleCount);

            return new Google3dTilesDirectLoadResult
            {
                Primitives = primitives,
                Status = primitives.Count > 0
                    ? BuildSuccessStatus(primitives.Count, triangleCount, decodedDescriptorCount, descriptors.Count, suppressedFallbackCount, aggregateContentResult)
                    : BuildEmptyContentStatus(decodedDescriptorCount, descriptors.Count, aggregateContentResult, suppressedFallbackCount)
            };
        }

        private static string BuildSuccessStatus(
            int primitiveCount,
            int triangleCount,
            int decodedDescriptorCount,
            int totalCandidateCount,
            int suppressedFallbackCount,
            Google3dTilesContentLoadResult contentResult)
        {
            var fallbackNote = suppressedFallbackCount > 0
                ? $" Suppressed {suppressedFallbackCount} coarse fallback mesh(es)."
                : string.Empty;
            var validityNote = contentResult.InvalidMeshCount > 0 || contentResult.DegenerateTriangleCount > 0
                ? $" Dropped {contentResult.InvalidMeshCount} invalid mesh(es) and {contentResult.DegenerateTriangleCount} degenerate triangle(s)."
                : string.Empty;

            return $"Google 3D Tiles viewer decoded {primitiveCount} preview mesh(es) from {decodedDescriptorCount} selected tile content URL(s) ({totalCandidateCount} candidate URL(s)), {triangleCount} triangles. Vertical correction: EGM96 geoid grid.{fallbackNote}{validityNote}";
        }

        private static Google3dTilesContentLoadResult CombineContentResults(
            Google3dTilesContentLoadResult left,
            Google3dTilesContentLoadResult right)
        {
            var primitives = new List<Google3dTilesDisplayPrimitive>(left.Primitives.Count + right.Primitives.Count);
            primitives.AddRange(left.Primitives);
            primitives.AddRange(right.Primitives);

            return new Google3dTilesContentLoadResult
            {
                Primitives = primitives,
                AttemptedTileCount = left.AttemptedTileCount + right.AttemptedTileCount,
                DecodeFailureCount = left.DecodeFailureCount + right.DecodeFailureCount,
                DecodedPrimitiveCount = left.DecodedPrimitiveCount + right.DecodedPrimitiveCount,
                EmptyPrimitiveCount = left.EmptyPrimitiveCount + right.EmptyPrimitiveCount,
                EmptyTileCount = left.EmptyTileCount + right.EmptyTileCount,
                DracoCompressedTileCount = left.DracoCompressedTileCount + right.DracoCompressedTileCount,
                DracoRequiredTileCount = left.DracoRequiredTileCount + right.DracoRequiredTileCount,
                SkippedDecodedPrimitiveCount = left.SkippedDecodedPrimitiveCount + right.SkippedDecodedPrimitiveCount,
                TotalDecodedTriangleCount = left.TotalDecodedTriangleCount + right.TotalDecodedTriangleCount,
                RejectedOutOfBoundsTriangleCount = left.RejectedOutOfBoundsTriangleCount + right.RejectedOutOfBoundsTriangleCount,
                RejectedOversizedTriangleCount = left.RejectedOversizedTriangleCount + right.RejectedOversizedTriangleCount,
                ClippedOversizedTriangleCount = left.ClippedOversizedTriangleCount + right.ClippedOversizedTriangleCount,
                FallbackPrimitiveCount = left.FallbackPrimitiveCount + right.FallbackPrimitiveCount,
                DegenerateTriangleCount = left.DegenerateTriangleCount + right.DegenerateTriangleCount,
                InvalidMeshCount = left.InvalidMeshCount + right.InvalidMeshCount,
                LastError = string.IsNullOrWhiteSpace(right.LastError) ? left.LastError : right.LastError
            };
        }

        private static IEnumerable<Google3dTilesTileDescriptor> SelectBestContentDescriptors(
            IEnumerable<Google3dTilesTileDescriptor> descriptors,
            int maxCount)
        {
            return descriptors
                .Where(static descriptor => !string.IsNullOrWhiteSpace(descriptor.Url))
                .GroupBy(static descriptor => descriptor.Url, StringComparer.Ordinal)
                .Select(static group => group
                    .OrderBy(static descriptor => NormalizeGeometricError(descriptor.GeometricError))
                    .ThenByDescending(static descriptor => descriptor.Depth)
                    .First())
                .OrderBy(static descriptor => NormalizeGeometricError(descriptor.GeometricError))
                .ThenByDescending(static descriptor => descriptor.Depth)
                .Take(maxCount);
        }

        private static double NormalizeGeometricError(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? double.MaxValue
                : Math.Max(0.0, value);
        }

        private static string BuildEmptyContentStatus(
            int decodedDescriptorCount,
            int totalCandidateCount,
            Google3dTilesContentLoadResult contentResult,
            int suppressedFallbackCount)
        {
            if (contentResult.DecodeFailureCount > 0 && contentResult.DecodeFailureCount == contentResult.AttemptedTileCount)
            {
                var suffix = string.IsNullOrWhiteSpace(contentResult.LastError)
                    ? string.Empty
                    : $" Last decode error: {contentResult.LastError}";
                return $"Google 3D Tiles viewer found {totalCandidateCount} candidate tile content URL(s), but all {contentResult.DecodeFailureCount} attempted GLB decode(s) failed.{suffix}";
            }

            var dracoNote = contentResult.DracoCompressedTileCount > 0
                ? $" {contentResult.DracoCompressedTileCount} attempted GLB(s) use Draco compression ({contentResult.DracoRequiredTileCount} required)."
                : string.Empty;
            var skippedNote = contentResult.SkippedDecodedPrimitiveCount > 0
                ? $" Skipped {contentResult.SkippedDecodedPrimitiveCount} primitive(s) without directly readable POSITION/index data."
                : string.Empty;
            var rejectedNote = contentResult.TotalDecodedTriangleCount > 0
                ? $" Rejected {contentResult.RejectedOversizedTriangleCount} oversized and {contentResult.RejectedOutOfBoundsTriangleCount} out-of-context triangle(s) from {contentResult.TotalDecodedTriangleCount} decoded triangle(s)."
                : string.Empty;
            var fallbackNote = suppressedFallbackCount > 0 || contentResult.FallbackPrimitiveCount > 0
                ? $" Suppressed {Math.Max(suppressedFallbackCount, contentResult.FallbackPrimitiveCount)} coarse fallback mesh(es) because they are not usable local 3D tile geometry."
                : string.Empty;
            var validityNote = contentResult.InvalidMeshCount > 0 || contentResult.DegenerateTriangleCount > 0
                ? $" Dropped {contentResult.InvalidMeshCount} invalid mesh(es) and {contentResult.DegenerateTriangleCount} degenerate triangle(s)."
                : string.Empty;
            var errorNote = string.IsNullOrWhiteSpace(contentResult.LastError)
                ? string.Empty
                : $" Last decode/build error: {contentResult.LastError}";

            return $"Google 3D Tiles viewer found {totalCandidateCount} candidate tile content URL(s), attempted {contentResult.AttemptedTileCount} GLB load(s) from {decodedDescriptorCount} selected candidate(s), decoded {contentResult.DecodedPrimitiveCount} primitive(s), but produced no usable preview mesh faces.{dracoNote}{skippedNote}{rejectedNote}{fallbackNote}{validityNote}{errorNote}";
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
            var inheritedQuery = ParseQuery(baseUri.Query);
            if (contentUriText.StartsWith("/v1/3dtiles/", StringComparison.OrdinalIgnoreCase))
            {
                return BuildGoogleTilesUri(
                    contentUriText,
                    apiKey,
                    session ?? TryGetSession(baseUri),
                    inheritedQuery);
            }

            var uri = Uri.TryCreate(contentUriText, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(baseUri, contentUriText);

            if (!string.Equals(uri.Host, "tile.googleapis.com", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            return BuildGoogleTilesUri(
                uri.PathAndQuery,
                apiKey,
                session ?? TryGetSession(uri) ?? TryGetSession(baseUri),
                inheritedQuery);
        }

        private static Uri BuildGoogleTilesUri(
            string pathAndQuery,
            string apiKey,
            string? session,
            IReadOnlyDictionary<string, string>? inheritedQuery)
        {
            var builder = new UriBuilder(
                pathAndQuery.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(pathAndQuery)
                    : new Uri(new Uri("https://tile.googleapis.com"), pathAndQuery));
            var query = ParseQuery(builder.Query);

            if (inheritedQuery is not null)
            {
                foreach (var entry in inheritedQuery)
                {
                    if (!query.ContainsKey(entry.Key))
                    {
                        query[entry.Key] = entry.Value;
                    }
                }
            }

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

        private static bool TileIntersectsStudyArea(JsonElement tile, BoundingBox2D studyArea4326, IReadOnlyList<double> tileTransform)
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
                if (values.Length >= 4)
                {
                    var center = ApplyMatrixToPoint(values[0], values[1], values[2], tileTransform);
                    if (!Google3dTilesCoordinateConverter.TryConvertEcefToGeodetic(
                        center.X,
                        center.Y,
                        center.Z,
                        out var latitude,
                        out var longitude,
                        out _))
                    {
                        return true;
                    }

                    var radiusDegrees = Math.Max((values[3] / 111_000.0) * 1.5, 0.00001);
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
                if (values.Length >= 3)
                {
                    var center = ApplyMatrixToPoint(values[0], values[1], values[2], tileTransform);
                    if (!Google3dTilesCoordinateConverter.TryConvertEcefToGeodetic(
                        center.X,
                        center.Y,
                        center.Z,
                        out var latitude,
                        out var longitude,
                        out _))
                    {
                        return true;
                    }

                    var radiusDegrees = values.Length >= 12
                        ? EstimateBoxRadiusDegrees(values)
                        : 0.05;
                    var tileBox = new BoundingBox2D(
                        longitude - radiusDegrees,
                        latitude - radiusDegrees,
                        longitude + radiusDegrees,
                        latitude + radiusDegrees);
                    return RhinoSpatialContextTools.DoBoundingBoxesIntersect(tileBox, studyArea4326);
                }
            }

            return true;
        }

        private static (double X, double Y, double Z) ApplyMatrixToPoint(
            double x,
            double y,
            double z,
            IReadOnlyList<double> matrixValues)
        {
            if (matrixValues.Count != 16)
            {
                return (x, y, z);
            }

            var transformedX = (matrixValues[0] * x) + (matrixValues[4] * y) + (matrixValues[8] * z) + matrixValues[12];
            var transformedY = (matrixValues[1] * x) + (matrixValues[5] * y) + (matrixValues[9] * z) + matrixValues[13];
            var transformedZ = (matrixValues[2] * x) + (matrixValues[6] * y) + (matrixValues[10] * z) + matrixValues[14];
            var transformedW = (matrixValues[3] * x) + (matrixValues[7] * y) + (matrixValues[11] * z) + matrixValues[15];

            if (Math.Abs(transformedW) > 1e-9 && Math.Abs(transformedW - 1.0) > 1e-9)
            {
                transformedX /= transformedW;
                transformedY /= transformedW;
                transformedZ /= transformedW;
            }

            return (transformedX, transformedY, transformedZ);
        }

        private static double EstimateBoxRadiusDegrees(IReadOnlyList<double> boxValues)
        {
            var halfAxisX = VectorLength(boxValues[3], boxValues[4], boxValues[5]);
            var halfAxisY = VectorLength(boxValues[6], boxValues[7], boxValues[8]);
            var halfAxisZ = VectorLength(boxValues[9], boxValues[10], boxValues[11]);
            var radiusMeters = Math.Sqrt(
                (halfAxisX * halfAxisX) +
                (halfAxisY * halfAxisY) +
                (halfAxisZ * halfAxisZ));

            return Math.Max((radiusMeters / 111_000.0) * 1.5, 0.00001);
        }

        private static double VectorLength(double x, double y, double z)
        {
            return Math.Sqrt((x * x) + (y * y) + (z * z));
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

        private static double TryReadGeometricError(JsonElement tile)
        {
            if (TryGetProperty(tile, "geometricError", out var geometricError) &&
                geometricError.ValueKind == JsonValueKind.Number &&
                geometricError.TryGetDouble(out var value))
            {
                return value;
            }

            return double.PositiveInfinity;
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
