using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Rhino.Display;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesContentLoadResult
    {
        public List<Google3dTilesDisplayPrimitive> Primitives { get; init; } = new();

        public int AttemptedTileCount { get; init; }

        public int DecodeFailureCount { get; init; }

        public int DecodedPrimitiveCount { get; init; }

        public int EmptyPrimitiveCount { get; init; }

        public int EmptyTileCount { get; init; }

        public int DracoCompressedTileCount { get; init; }

        public int DracoRequiredTileCount { get; init; }

        public int SkippedDecodedPrimitiveCount { get; init; }

        public int TotalDecodedTriangleCount { get; init; }

        public int RejectedOutOfBoundsTriangleCount { get; init; }

        public int RejectedOversizedTriangleCount { get; init; }

        public int DegenerateTriangleCount { get; init; }

        public int InvalidMeshCount { get; init; }

        public int TileTransformProjectionCount { get; init; }

        public int YUpProjectionCount { get; init; }

        public int InverseYUpProjectionCount { get; init; }

        public int RawEcefProjectionCount { get; init; }

        public double ClosestProjectedCenterDistance { get; init; } = double.PositiveInfinity;

        public double ClosestTileOriginDistance { get; init; } = double.PositiveInfinity;

        public double MinimumProjectedBoundsDiagonal { get; init; } = double.PositiveInfinity;

        public double AppliedElevationBaseline { get; init; }

        public bool UsedSharedElevationBaseline { get; init; }

        public bool EstablishedElevationBaseline { get; init; }

        public List<string> Copyrights { get; init; } = new();

        public string LastError { get; init; } = string.Empty;
    }

    internal static class Google3dTilesTileContentLoader
    {
        private enum ProjectionMode
        {
            TileTransform,
            YUpToZUpThenTileTransform,
            InverseYUpToZUpThenTileTransform,
            RawEcef
        }

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        private static readonly object CacheSyncRoot = new();
        private static readonly Dictionary<string, Google3dTilesDecodedTile> DecodedTileCache = new(StringComparer.Ordinal);
        private static readonly Queue<string> DecodedTileCacheOrder = new();
        private static readonly string TextureCacheDirectory = Path.Combine(Path.GetTempPath(), "RhinoSpatial", "google-3d-tiles");
        private const int MaxDecodedTileCacheEntries = 96;
        private const double TriangleEdgeLimitMultiplier = 12.0;
        private const double MinimumTriangleEdgeLimit = 5000.0;
        private const double MeshPointTolerance = 1e-6;
        private const double ReferenceMeshBoundsPaddingRatio = 0.25;

        public static async Task<Google3dTilesContentLoadResult> LoadDisplayPrimitivesAsync(
            IEnumerable<Google3dTilesTileDescriptor> tiles,
            SpatialContext2D spatialContext,
            CancellationToken cancellationToken = default)
        {
            var displayPrimitives = new List<Google3dTilesDisplayPrimitive>();
            var attemptedTileCount = 0;
            var decodeFailureCount = 0;
            var decodedPrimitiveCount = 0;
            var emptyPrimitiveCount = 0;
            var emptyTileCount = 0;
            var dracoCompressedTileCount = 0;
            var dracoRequiredTileCount = 0;
            var skippedDecodedPrimitiveCount = 0;
            var totalDecodedTriangleCount = 0;
            var rejectedOutOfBoundsTriangleCount = 0;
            var rejectedOversizedTriangleCount = 0;
            var degenerateTriangleCount = 0;
            var invalidMeshCount = 0;
            var tileTransformProjectionCount = 0;
            var yUpProjectionCount = 0;
            var inverseYUpProjectionCount = 0;
            var rawEcefProjectionCount = 0;
            var closestProjectedCenterDistance = double.PositiveInfinity;
            var closestTileOriginDistance = double.PositiveInfinity;
            var minimumProjectedBoundsDiagonal = double.PositiveInfinity;
            var copyrights = new HashSet<string>(StringComparer.Ordinal);
            var lastError = string.Empty;
            const int maxTilesToDecode = 12;
            const int maxConcurrentTileLoads = 6;

            var selectedTiles = tiles
                .Where(static tile => tile is not null && !string.IsNullOrWhiteSpace(tile.Url))
                .GroupBy(static tile => string.IsNullOrWhiteSpace(tile.Key) ? tile.Url : tile.Key, StringComparer.Ordinal)
                .Select(static group => group.First())
                .Take(maxTilesToDecode)
                .ToList();

            foreach (var tileBatch in selectedTiles.Chunk(maxConcurrentTileLoads))
            {
                var loadAttempts = await Task.WhenAll(
                        tileBatch.Select(tile => LoadDecodedTileSafeAsync(tile, cancellationToken)))
                    .ConfigureAwait(false);
                foreach (var loadAttempt in loadAttempts)
                {
                    attemptedTileCount++;
                    if (loadAttempt.DecodedTile is null)
                    {
                        decodeFailureCount++;
                        lastError = loadAttempt.ErrorMessage;
                        continue;
                    }

                    var decodedTile = loadAttempt.DecodedTile;
                    if (decodedTile.UsesDracoCompression)
                    {
                        dracoCompressedTileCount++;
                    }

                    if (decodedTile.RequiresDracoCompression)
                    {
                        dracoRequiredTileCount++;
                    }

                    if (decodedTile.Primitives.Count == 0)
                    {
                        emptyTileCount++;
                    }

                    if (!string.IsNullOrWhiteSpace(decodedTile.Copyright))
                    {
                        copyrights.Add(decodedTile.Copyright.Trim());
                    }

                    skippedDecodedPrimitiveCount += decodedTile.SkippedPrimitiveCount;
                    if (!string.IsNullOrWhiteSpace(decodedTile.LastError))
                    {
                        lastError = decodedTile.LastError;
                    }

                    var decodedPrimitives = decodedTile.Primitives;
                    decodedPrimitiveCount += decodedPrimitives.Count;
                    var projectionMode = ChooseProjectionMode(
                        loadAttempt.Tile,
                        decodedPrimitives,
                        spatialContext);
                    switch (projectionMode)
                    {
                        case ProjectionMode.TileTransform:
                            tileTransformProjectionCount++;
                            break;
                        case ProjectionMode.YUpToZUpThenTileTransform:
                            yUpProjectionCount++;
                            break;
                        case ProjectionMode.InverseYUpToZUpThenTileTransform:
                            inverseYUpProjectionCount++;
                            break;
                        case ProjectionMode.RawEcef:
                            rawEcefProjectionCount++;
                            break;
                    }

                    MeasureProjectionDiagnostics(
                        loadAttempt.Tile,
                        decodedPrimitives,
                        spatialContext,
                        projectionMode,
                        out var projectedCenterDistance,
                        out var tileOriginDistance,
                        out var projectedBoundsDiagonal);
                    closestProjectedCenterDistance = Math.Min(
                        closestProjectedCenterDistance,
                        projectedCenterDistance);
                    closestTileOriginDistance = Math.Min(
                        closestTileOriginDistance,
                        tileOriginDistance);
                    minimumProjectedBoundsDiagonal = Math.Min(
                        minimumProjectedBoundsDiagonal,
                        projectedBoundsDiagonal);

                    foreach (var decodedPrimitive in decodedPrimitives)
                    {
                        var displayPrimitive = CreateDisplayPrimitive(
                            loadAttempt.Tile,
                            decodedPrimitive,
                            spatialContext,
                            projectionMode,
                            out var buildReport);
                        totalDecodedTriangleCount += buildReport.TotalTriangleCount;
                        rejectedOutOfBoundsTriangleCount += buildReport.RejectedOutOfBoundsTriangleCount;
                        rejectedOversizedTriangleCount += buildReport.RejectedOversizedTriangleCount;
                        degenerateTriangleCount += buildReport.DegenerateTriangleCount;
                        invalidMeshCount += buildReport.InvalidMeshCount;

                        if (displayPrimitive is not null)
                        {
                            displayPrimitives.Add(displayPrimitive);
                        }
                        else
                        {
                            emptyPrimitiveCount++;
                        }
                    }
                }
            }

            var baselineResult = ApplySharedElevationBaseline(displayPrimitives, spatialContext);

            return new Google3dTilesContentLoadResult
            {
                Primitives = displayPrimitives,
                AttemptedTileCount = attemptedTileCount,
                DecodeFailureCount = decodeFailureCount,
                DecodedPrimitiveCount = decodedPrimitiveCount,
                EmptyPrimitiveCount = emptyPrimitiveCount,
                EmptyTileCount = emptyTileCount,
                DracoCompressedTileCount = dracoCompressedTileCount,
                DracoRequiredTileCount = dracoRequiredTileCount,
                SkippedDecodedPrimitiveCount = skippedDecodedPrimitiveCount,
                TotalDecodedTriangleCount = totalDecodedTriangleCount,
                RejectedOutOfBoundsTriangleCount = rejectedOutOfBoundsTriangleCount,
                RejectedOversizedTriangleCount = rejectedOversizedTriangleCount,
                DegenerateTriangleCount = degenerateTriangleCount,
                InvalidMeshCount = invalidMeshCount,
                TileTransformProjectionCount = tileTransformProjectionCount,
                YUpProjectionCount = yUpProjectionCount,
                InverseYUpProjectionCount = inverseYUpProjectionCount,
                RawEcefProjectionCount = rawEcefProjectionCount,
                ClosestProjectedCenterDistance = closestProjectedCenterDistance,
                ClosestTileOriginDistance = closestTileOriginDistance,
                MinimumProjectedBoundsDiagonal = minimumProjectedBoundsDiagonal,
                AppliedElevationBaseline = baselineResult.ElevationBaseline,
                UsedSharedElevationBaseline = baselineResult.UsedSharedBaseline,
                EstablishedElevationBaseline = baselineResult.EstablishedBaseline,
                Copyrights = copyrights.OrderBy(static value => value, StringComparer.Ordinal).ToList(),
                LastError = lastError
            };
        }

        private static async Task<TileLoadAttempt> LoadDecodedTileSafeAsync(
            Google3dTilesTileDescriptor tile,
            CancellationToken cancellationToken)
        {
            try
            {
                var decodedTile = await GetOrLoadDecodedTileAsync(tile.Url, cancellationToken).ConfigureAwait(false);
                return new TileLoadAttempt(tile, decodedTile, string.Empty);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new TileLoadAttempt(tile, null, exception.Message);
            }
        }

        private static async Task<Google3dTilesDecodedTile> GetOrLoadDecodedTileAsync(string tileUrl, CancellationToken cancellationToken)
        {
            var cacheKey = CreateDecodedTileCacheKey(tileUrl);
            lock (CacheSyncRoot)
            {
                if (DecodedTileCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }
            }

            var glbBytes = await GetTileBytesAsync(tileUrl, cancellationToken).ConfigureAwait(false);
            var decodedTile = Google3dTilesGlbDecoder.Decode(glbBytes);

            lock (CacheSyncRoot)
            {
                if (!DecodedTileCache.ContainsKey(cacheKey))
                {
                    while (DecodedTileCache.Count >= MaxDecodedTileCacheEntries &&
                           DecodedTileCacheOrder.Count > 0)
                    {
                        DecodedTileCache.Remove(DecodedTileCacheOrder.Dequeue());
                    }

                    DecodedTileCache[cacheKey] = decodedTile;
                    DecodedTileCacheOrder.Enqueue(cacheKey);
                }
            }

            return decodedTile;
        }

        private static async Task<byte[]> GetTileBytesAsync(string tileUrl, CancellationToken cancellationToken)
        {
            using var response = await HttpClient
                .GetAsync(tileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Google 3D Tiles GLB request failed ({(int)response.StatusCode}) for {TrimUrlForStatus(tileUrl)}: {TrimStatusBody(body)}");
        }

        private static Google3dTilesDisplayPrimitive? CreateDisplayPrimitive(
            Google3dTilesTileDescriptor tile,
            Google3dTilesDecodedPrimitive decodedPrimitive,
            SpatialContext2D spatialContext,
            ProjectionMode projectionMode,
            out PrimitiveBuildReport buildReport)
        {
            buildReport = new PrimitiveBuildReport
            {
                TotalTriangleCount = decodedPrimitive.TriangleIndices.Count / 3
            };

            if (!TryProjectVertices(
                    tile,
                    decodedPrimitive,
                    spatialContext,
                    projectionMode,
                    out var projectedVertices,
                    out var clipBounds,
                    out var maxTriangleEdgeLength))
            {
                return null;
            }

            var mesh = new Mesh();
            var usedVertexMap = new Dictionary<int, int>();
            var minimumTriangleArea = Math.Max(1e-8, Math.Pow(MeasureBoundsDiagonal(clipBounds), 2.0) * 1e-12);

            int GetOrCreateVertexIndex(int sourceIndex)
            {
                if (usedVertexMap.TryGetValue(sourceIndex, out var existingIndex))
                {
                    return existingIndex;
                }

                var sourcePoint = projectedVertices[sourceIndex];
                var newIndex = mesh.Vertices.Count;
                mesh.Vertices.Add(
                    sourcePoint.X,
                    sourcePoint.Y,
                    sourcePoint.Z);

                if (decodedPrimitive.TextureCoordinates.Count > sourceIndex)
                {
                    var uv = decodedPrimitive.TextureCoordinates[sourceIndex];
                    mesh.TextureCoordinates.Add(uv.X, 1.0f - uv.Y);
                }
                else
                {
                    mesh.TextureCoordinates.Add(0.0f, 0.0f);
                }

                usedVertexMap[sourceIndex] = newIndex;
                return newIndex;
            }

            for (var triangleIndex = 0; triangleIndex < decodedPrimitive.TriangleIndices.Count; triangleIndex += 3)
            {
                var a = decodedPrimitive.TriangleIndices[triangleIndex];
                var b = decodedPrimitive.TriangleIndices[triangleIndex + 1];
                var c = decodedPrimitive.TriangleIndices[triangleIndex + 2];

                if (a < 0 || b < 0 || c < 0)
                {
                    continue;
                }

                if (a >= projectedVertices.Count || b >= projectedVertices.Count || c >= projectedVertices.Count)
                {
                    continue;
                }

                if (a == b || b == c || a == c)
                {
                    continue;
                }

                var pa = projectedVertices[a];
                var pb = projectedVertices[b];
                var pc = projectedVertices[c];
                if (!TriangleBoundingBoxTouchesBounds(pa, pb, pc, clipBounds))
                {
                    buildReport.RejectedOutOfBoundsTriangleCount++;
                    continue;
                }

                var oversized = IsOversizedTriangle(pa, pb, pc, maxTriangleEdgeLength);
                if (oversized)
                {
                    buildReport.RejectedOversizedTriangleCount++;
                    continue;
                }

                if (!IsUsableTriangle(pa, pb, pc, minimumTriangleArea))
                {
                    buildReport.DegenerateTriangleCount++;
                    continue;
                }

                mesh.Faces.AddFace(
                    GetOrCreateVertexIndex(a),
                    GetOrCreateVertexIndex(b),
                    GetOrCreateVertexIndex(c));
            }

            if (mesh.Faces.Count == 0)
            {
                return null;
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            if (!mesh.IsValid)
            {
                buildReport.InvalidMeshCount++;
                return null;
            }

            DisplayMaterial? material = null;
            if (decodedPrimitive.BaseColorTextureBytes is not null &&
                decodedPrimitive.BaseColorTextureBytes.Length > 0)
            {
                var texturePath = EnsureTextureFile(decodedPrimitive.BaseColorTextureBytes, decodedPrimitive.BaseColorTextureMimeType);
                material = RhinoSpatialRasterDisplayTools.CreateDisplayMaterial(texturePath);
                return new Google3dTilesDisplayPrimitive
                {
                    Mesh = mesh,
                    Material = material,
                    TextureFilePath = texturePath,
                    SourceUrl = tile.Url,
                    SourceKey = tile.Key
                };
            }

            material ??= new DisplayMaterial(System.Drawing.Color.White)
            {
                IsTwoSided = true,
                Shine = decodedPrimitive.IsUnlit ? 0.0 : 0.2
            };

            return new Google3dTilesDisplayPrimitive
            {
                Mesh = mesh,
                Material = material,
                SourceUrl = tile.Url,
                SourceKey = tile.Key
            };
        }

        private static string CreateDecodedTileCacheKey(string tileUrl)
        {
            if (Uri.TryCreate(tileUrl, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Host, "tile.googleapis.com", StringComparison.OrdinalIgnoreCase))
            {
                return uri.GetLeftPart(UriPartial.Path);
            }

            return tileUrl;
        }

        private static ElevationBaselineResult ApplySharedElevationBaseline(
            IReadOnlyList<Google3dTilesDisplayPrimitive> displayPrimitives,
            SpatialContext2D spatialContext)
        {
            if (spatialContext.UseAbsoluteCoordinates || displayPrimitives.Count == 0)
            {
                return new ElevationBaselineResult(0.0, false, false);
            }

            var establishedBaseline = false;
            double elevationBaseline;
            if (!SpatialElevationBaselineCache.TryGet(spatialContext, out elevationBaseline))
            {
                var elevations = displayPrimitives.SelectMany(static primitive =>
                {
                    var mesh = primitive.Mesh;
                    var values = new double[mesh.Vertices.Count];
                    for (var vertexIndex = 0; vertexIndex < mesh.Vertices.Count; vertexIndex++)
                    {
                        values[vertexIndex] = mesh.Vertices[vertexIndex].Z;
                    }

                    return values;
                });
                if (!Google3dTilesElevationBaseline.TryResolveCandidate(elevations, out var candidateBaseline))
                {
                    return new ElevationBaselineResult(0.0, false, false);
                }

                elevationBaseline = SpatialElevationBaselineCache.ResolveOrStore(
                    spatialContext,
                    candidateBaseline,
                    out establishedBaseline);
            }

            if (Math.Abs(elevationBaseline) > 1e-9)
            {
                var translation = Transform.Translation(0.0, 0.0, -elevationBaseline);
                foreach (var displayPrimitive in displayPrimitives)
                {
                    displayPrimitive.Mesh.Transform(translation);
                }
            }

            return new ElevationBaselineResult(elevationBaseline, true, establishedBaseline);
        }

        private readonly record struct ElevationBaselineResult(
            double ElevationBaseline,
            bool UsedSharedBaseline,
            bool EstablishedBaseline);

        private sealed class PrimitiveBuildReport
        {
            public int TotalTriangleCount { get; init; }

            public int RejectedOutOfBoundsTriangleCount { get; set; }

            public int RejectedOversizedTriangleCount { get; set; }

            public int DegenerateTriangleCount { get; set; }

            public int InvalidMeshCount { get; set; }
        }

        private static ProjectionMode ChooseProjectionMode(
            Google3dTilesTileDescriptor tile,
            IReadOnlyList<Google3dTilesDecodedPrimitive> decodedPrimitives,
            SpatialContext2D spatialContext)
        {
            var placedBounds = CreatePlacedBounds(spatialContext);
            var scoringBounds = ExpandPlacedBounds(placedBounds, 3.0);
            var clipBounds = ExpandPlacedBounds(placedBounds, ReferenceMeshBoundsPaddingRatio);
            var maxTriangleEdgeLength = Math.Max(
                MinimumTriangleEdgeLimit,
                MeasureBoundsDiagonal(clipBounds) * TriangleEdgeLimitMultiplier);
            var modes = new[]
            {
                ProjectionMode.TileTransform,
                ProjectionMode.YUpToZUpThenTileTransform,
                ProjectionMode.InverseYUpToZUpThenTileTransform,
                ProjectionMode.RawEcef
            };
            var selectedMode = modes[0];
            var selectedScore = double.NegativeInfinity;

            foreach (var mode in modes)
            {
                var score = 0.0;
                var scoredPrimitiveCount = 0;
                foreach (var primitive in decodedPrimitives)
                {
                    var points = ProjectVertices(tile, primitive, spatialContext, mode);
                    var primitiveScore = ScoreProjectionCandidate(
                        points,
                        primitive.TriangleIndices,
                        scoringBounds,
                        maxTriangleEdgeLength);
                    if (double.IsNegativeInfinity(primitiveScore))
                    {
                        continue;
                    }

                    score += primitiveScore;
                    scoredPrimitiveCount++;
                }

                if (scoredPrimitiveCount > 0 && score > selectedScore)
                {
                    selectedMode = mode;
                    selectedScore = score;
                }
            }

            return selectedMode;
        }

        private static void MeasureProjectionDiagnostics(
            Google3dTilesTileDescriptor tile,
            IReadOnlyList<Google3dTilesDecodedPrimitive> decodedPrimitives,
            SpatialContext2D spatialContext,
            ProjectionMode projectionMode,
            out double projectedCenterDistance,
            out double tileOriginDistance,
            out double projectedBoundsDiagonal)
        {
            projectedCenterDistance = double.PositiveInfinity;
            tileOriginDistance = double.PositiveInfinity;
            projectedBoundsDiagonal = double.PositiveInfinity;
            var projectedBounds = BoundingBox.Empty;
            foreach (var primitive in decodedPrimitives)
            {
                foreach (var point in ProjectVertices(tile, primitive, spatialContext, projectionMode))
                {
                    projectedBounds.Union(point);
                }
            }

            var placedBounds = CreatePlacedBounds(spatialContext);
            var studyCenter = new Point3d(
                (placedBounds.MinX + placedBounds.MaxX) * 0.5,
                (placedBounds.MinY + placedBounds.MaxY) * 0.5,
                0.0);
            if (projectedBounds.IsValid)
            {
                projectedCenterDistance = Distance2D(projectedBounds.Center, studyCenter);
                var width = projectedBounds.Max.X - projectedBounds.Min.X;
                var height = projectedBounds.Max.Y - projectedBounds.Min.Y;
                projectedBoundsDiagonal = Math.Sqrt((width * width) + (height * height));
            }

            var transformedOrigin = ApplyTileTransform(Point3d.Origin, tile.Transform);
            if (!TryConvertEcefToProjected(transformedOrigin, spatialContext, out var projectedOrigin))
            {
                return;
            }

            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;
            tileOriginDistance = Distance2D(
                new Point3d(projectedOrigin.X - offsetX, projectedOrigin.Y - offsetY, 0.0),
                studyCenter);
        }

        private static double ScoreProjectionCandidate(
            IReadOnlyList<Point3d> points,
            IReadOnlyList<int> triangleIndices,
            BoundingBox2D scoringBounds,
            double maxTriangleEdgeLength)
        {
            if (points.Count == 0)
            {
                return double.NegativeInfinity;
            }

            var insidePointCount = points.Count(point => IsInsideBounds(point, scoringBounds));
            var contextTriangleCount = 0;
            var oversizedTriangleCount = 0;
            var outOfContextTriangleCount = 0;
            for (var triangleIndex = 0; triangleIndex + 2 < triangleIndices.Count; triangleIndex += 3)
            {
                var a = triangleIndices[triangleIndex];
                var b = triangleIndices[triangleIndex + 1];
                var c = triangleIndices[triangleIndex + 2];
                if (a < 0 || b < 0 || c < 0 ||
                    a >= points.Count || b >= points.Count || c >= points.Count ||
                    a == b || b == c || a == c)
                {
                    continue;
                }

                if (!TriangleBoundingBoxTouchesBounds(points[a], points[b], points[c], scoringBounds))
                {
                    outOfContextTriangleCount++;
                    continue;
                }

                contextTriangleCount++;
                if (IsOversizedTriangle(points[a], points[b], points[c], maxTriangleEdgeLength))
                {
                    oversizedTriangleCount++;
                }
            }

            var candidateBounds = BoundingBox.Empty;
            foreach (var point in points)
            {
                candidateBounds.Union(point);
            }

            var normalizedCenterDistance = 0.0;
            if (candidateBounds.IsValid)
            {
                var studyCenterX = (scoringBounds.MinX + scoringBounds.MaxX) * 0.5;
                var studyCenterY = (scoringBounds.MinY + scoringBounds.MaxY) * 0.5;
                var center = candidateBounds.Center;
                var distance = Math.Sqrt(
                    Math.Pow(center.X - studyCenterX, 2.0) +
                    Math.Pow(center.Y - studyCenterY, 2.0));
                normalizedCenterDistance = distance / Math.Max(1.0, MeasureBoundsDiagonal(scoringBounds));
            }

            return (contextTriangleCount * 100000.0) +
                   (insidePointCount * 1000.0) -
                   (oversizedTriangleCount * 10.0) -
                   (outOfContextTriangleCount * 10.0) -
                   normalizedCenterDistance;
        }

        private static bool TryProjectVertices(
            Google3dTilesTileDescriptor tile,
            Google3dTilesDecodedPrimitive decodedPrimitive,
            SpatialContext2D spatialContext,
            ProjectionMode projectionMode,
            out List<Point3d> projectedVertices,
            out BoundingBox2D clipBounds,
            out double maxTriangleEdgeLength)
        {
            var placedBounds = CreatePlacedBounds(spatialContext);
            clipBounds = ExpandPlacedBounds(placedBounds, ReferenceMeshBoundsPaddingRatio);
            maxTriangleEdgeLength = Math.Max(MinimumTriangleEdgeLimit, MeasureBoundsDiagonal(clipBounds) * TriangleEdgeLimitMultiplier);
            projectedVertices = ProjectVertices(tile, decodedPrimitive, spatialContext, projectionMode);

            if (projectedVertices.Count == 0)
            {
                return false;
            }

            return true;
        }

        private static List<Point3d> ProjectVertices(
            Google3dTilesTileDescriptor tile,
            Google3dTilesDecodedPrimitive decodedPrimitive,
            SpatialContext2D spatialContext,
            ProjectionMode projectionMode)
        {
            var points = new List<Point3d>(decodedPrimitive.EcefVertices.Count);
            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;

            foreach (var vertex in decodedPrimitive.EcefVertices)
            {
                var transformedVertex = projectionMode switch
                {
                    ProjectionMode.TileTransform => ApplyTileTransform(vertex, tile.Transform),
                    ProjectionMode.YUpToZUpThenTileTransform => ApplyTileTransform(ConvertGltfYUpToZUp(vertex), tile.Transform),
                    ProjectionMode.InverseYUpToZUpThenTileTransform => ApplyTileTransform(ConvertInverseGltfYUpToZUp(vertex), tile.Transform),
                    _ => vertex
                };

                if (!TryConvertEcefToProjected(transformedVertex, spatialContext, out var projectedPoint))
                {
                    return new List<Point3d>();
                }

                if (!IsFinite(projectedPoint.X) ||
                    !IsFinite(projectedPoint.Y) ||
                    !IsFinite(projectedPoint.Z))
                {
                    return new List<Point3d>();
                }

                points.Add(new Point3d(
                    projectedPoint.X - offsetX,
                    projectedPoint.Y - offsetY,
                    projectedPoint.Z));
            }

            return points;
        }

        private static BoundingBox2D CreatePlacedBounds(SpatialContext2D spatialContext)
        {
            return RhinoSpatialContextTools.CreatePlacedBoundingBox(
                spatialContext.PlacementBoundingBox,
                spatialContext.PlacementOrigin,
                spatialContext.UseAbsoluteCoordinates);
        }

        private static BoundingBox2D ExpandPlacedBounds(BoundingBox2D placedBounds, double paddingRatio)
        {
            if (paddingRatio <= 0.0)
            {
                return placedBounds;
            }

            var width = Math.Max(1.0, placedBounds.MaxX - placedBounds.MinX);
            var height = Math.Max(1.0, placedBounds.MaxY - placedBounds.MinY);
            var paddingX = width * paddingRatio;
            var paddingY = height * paddingRatio;

            return new BoundingBox2D(
                placedBounds.MinX - paddingX,
                placedBounds.MinY - paddingY,
                placedBounds.MaxX + paddingX,
                placedBounds.MaxY + paddingY);
        }

        private static double MeasureBoundsDiagonal(BoundingBox2D bounds)
        {
            var width = Math.Max(1.0, bounds.MaxX - bounds.MinX);
            var height = Math.Max(1.0, bounds.MaxY - bounds.MinY);
            return Math.Sqrt((width * width) + (height * height));
        }

        private static bool IsUsableTriangle(Point3d a, Point3d b, Point3d c, double minimumArea)
        {
            if (!IsValidPoint(a) || !IsValidPoint(b) || !IsValidPoint(c))
            {
                return false;
            }

            if (Distance3D(a, b) <= MeshPointTolerance ||
                Distance3D(b, c) <= MeshPointTolerance ||
                Distance3D(c, a) <= MeshPointTolerance)
            {
                return false;
            }

            var ab = b - a;
            var ac = c - a;
            var area = Vector3d.CrossProduct(ab, ac).Length * 0.5;
            return IsFinite(area) && area > minimumArea;
        }

        private static bool TriangleBoundingBoxTouchesBounds(Point3d a, Point3d b, Point3d c, BoundingBox2D bounds)
        {
            if (IsInsideBounds(a, bounds) || IsInsideBounds(b, bounds) || IsInsideBounds(c, bounds))
            {
                return true;
            }

            var minX = Math.Min(a.X, Math.Min(b.X, c.X));
            var maxX = Math.Max(a.X, Math.Max(b.X, c.X));
            var minY = Math.Min(a.Y, Math.Min(b.Y, c.Y));
            var maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));

            return minX <= bounds.MaxX &&
                   maxX >= bounds.MinX &&
                   minY <= bounds.MaxY &&
                   maxY >= bounds.MinY;
        }

        private static bool IsOversizedTriangle(Point3d a, Point3d b, Point3d c, double maxEdgeLength)
        {
            return Distance2D(a, b) > maxEdgeLength ||
                   Distance2D(b, c) > maxEdgeLength ||
                   Distance2D(c, a) > maxEdgeLength;
        }

        private static double Distance2D(Point3d a, Point3d b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static double Distance3D(Point3d a, Point3d b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static bool IsValidPoint(Point3d point)
        {
            return IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsInsideBounds(Point3d point, BoundingBox2D bounds)
        {
            return point.X >= bounds.MinX &&
                   point.X <= bounds.MaxX &&
                   point.Y >= bounds.MinY &&
                   point.Y <= bounds.MaxY;
        }

        private static Point3d ConvertGltfYUpToZUp(Point3d point)
        {
            return new Point3d(point.X, -point.Z, point.Y);
        }

        private static Point3d ConvertInverseGltfYUpToZUp(Point3d point)
        {
            return new Point3d(point.X, point.Z, -point.Y);
        }

        private static Point3d ApplyTileTransform(Point3d point, IReadOnlyList<double> matrixValues)
        {
            if (matrixValues is null || matrixValues.Count != 16)
            {
                return point;
            }

            var x = point.X;
            var y = point.Y;
            var z = point.Z;

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

            return new Point3d(transformedX, transformedY, transformedZ);
        }

        private static string EnsureTextureFile(byte[] imageBytes, string? mimeType)
        {
            Directory.CreateDirectory(TextureCacheDirectory);
            var extension = string.Equals(mimeType, "image/png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            using var sha1 = SHA1.Create();
            var hash = Convert.ToHexString(sha1.ComputeHash(imageBytes)).ToLowerInvariant();
            var path = Path.Combine(TextureCacheDirectory, $"{hash}{extension}");

            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, imageBytes);
            }

            return path;
        }

        private static bool TryConvertEcefToProjected(Point3d ecefPoint, SpatialContext2D spatialContext, out Point3d projectedPoint)
        {
            projectedPoint = Point3d.Unset;

            if (!Google3dTilesCoordinateConverter.TryConvertEcefToGeodetic(
                    ecefPoint.X,
                    ecefPoint.Y,
                    ecefPoint.Z,
                    out var latitudeDegrees,
                    out var longitudeDegrees,
                    out var height))
            {
                return false;
            }

            if (!SpatialReferenceTransform.TryTransformXY(
                    "EPSG:4326",
                    spatialContext.ResolvedSrs,
                    longitudeDegrees,
                    latitudeDegrees,
                    out var projectedX,
                    out var projectedY))
            {
                return false;
            }

            var spatialHeight = SpatialVerticalDatumTransform.ConvertWgs84EllipsoidHeightToSpatialHeight(
                latitudeDegrees,
                longitudeDegrees,
                height);

            projectedPoint = new Point3d(projectedX, projectedY, spatialHeight.HeightMeters);
            return true;
        }

        private static string TrimUrlForStatus(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.AbsolutePath;
            }

            return url.Length <= 140 ? url : url[..140] + "...";
        }

        private static string TrimStatusBody(string body)
        {
            var trimmed = body.Trim();
            if (trimmed.Length <= 240)
            {
                return trimmed;
            }

            return trimmed[..240] + "...";
        }

        private sealed record TileLoadAttempt(
            Google3dTilesTileDescriptor Tile,
            Google3dTilesDecodedTile? DecodedTile,
            string ErrorMessage);
    }
}
