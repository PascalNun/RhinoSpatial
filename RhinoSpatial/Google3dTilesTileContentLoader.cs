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

        public int ClippedOversizedTriangleCount { get; init; }

        public int FallbackPrimitiveCount { get; init; }

        public int DegenerateTriangleCount { get; init; }

        public int InvalidMeshCount { get; init; }

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
        private static readonly string TextureCacheDirectory = Path.Combine(Path.GetTempPath(), "RhinoSpatial", "google-3d-tiles");
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
            var clippedOversizedTriangleCount = 0;
            var fallbackPrimitiveCount = 0;
            var degenerateTriangleCount = 0;
            var invalidMeshCount = 0;
            var lastError = string.Empty;
            const int maxTilesToDecode = 12;

            foreach (var tile in tiles
                .Where(static tile => tile is not null && !string.IsNullOrWhiteSpace(tile.Url))
                .GroupBy(static tile => tile.Url, StringComparer.Ordinal)
                .Select(static group => group.First())
                .Take(maxTilesToDecode))
            {
                var tileUrl = tile.Url;
                if (!tileUrl.Contains(".glb", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                attemptedTileCount++;
                Google3dTilesDecodedTile decodedTile;
                try
                {
                    decodedTile = await GetOrLoadDecodedTileAsync(tileUrl, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    decodeFailureCount++;
                    lastError = exception.Message;
                    continue;
                }

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

                skippedDecodedPrimitiveCount += decodedTile.SkippedPrimitiveCount;
                if (!string.IsNullOrWhiteSpace(decodedTile.LastError))
                {
                    lastError = decodedTile.LastError;
                }

                var decodedPrimitives = decodedTile.Primitives;
                decodedPrimitiveCount += decodedPrimitives.Count;
                foreach (var decodedPrimitive in decodedPrimitives)
                {
                    var displayPrimitive = CreateDisplayPrimitive(tile, decodedPrimitive, spatialContext, out var buildReport);
                    totalDecodedTriangleCount += buildReport.TotalTriangleCount;
                    rejectedOutOfBoundsTriangleCount += buildReport.RejectedOutOfBoundsTriangleCount;
                    rejectedOversizedTriangleCount += buildReport.RejectedOversizedTriangleCount;
                    clippedOversizedTriangleCount += buildReport.ClippedOversizedTriangleCount;
                    degenerateTriangleCount += buildReport.DegenerateTriangleCount;
                    invalidMeshCount += buildReport.InvalidMeshCount;
                    if (displayPrimitive?.IsClippedFallback == true)
                    {
                        fallbackPrimitiveCount++;
                    }

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
                ClippedOversizedTriangleCount = clippedOversizedTriangleCount,
                FallbackPrimitiveCount = fallbackPrimitiveCount,
                DegenerateTriangleCount = degenerateTriangleCount,
                InvalidMeshCount = invalidMeshCount,
                LastError = lastError
            };
        }

        private static async Task<Google3dTilesDecodedTile> GetOrLoadDecodedTileAsync(string tileUrl, CancellationToken cancellationToken)
        {
            lock (CacheSyncRoot)
            {
                if (DecodedTileCache.TryGetValue(tileUrl, out var cached))
                {
                    return cached;
                }
            }

            var glbBytes = await GetTileBytesAsync(tileUrl, cancellationToken).ConfigureAwait(false);
            var decodedTile = Google3dTilesGlbDecoder.Decode(glbBytes);

            lock (CacheSyncRoot)
            {
                DecodedTileCache[tileUrl] = decodedTile;
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
            out PrimitiveBuildReport buildReport)
        {
            buildReport = new PrimitiveBuildReport
            {
                TotalTriangleCount = decodedPrimitive.TriangleIndices.Count / 3
            };

            if (!TryChooseProjectedVertices(tile, decodedPrimitive, spatialContext, out var projectedVertices, out var candidateElevationBaseline, out var clipBounds, out var maxTriangleEdgeLength))
            {
                return null;
            }

            var mesh = new Mesh();
            var usedVertexMap = new Dictionary<int, int>();
            var elevationBaseline = ResolveElevationBaseline(spatialContext);
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
                    spatialContext.UseAbsoluteCoordinates ? sourcePoint.Z : sourcePoint.Z - elevationBaseline);

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
                buildReport.UnclippedTriangleCount++;
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
                    IsClippedFallback = buildReport.UnclippedTriangleCount == 0 && buildReport.ClippedOversizedTriangleCount > 0
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
                IsClippedFallback = buildReport.UnclippedTriangleCount == 0 && buildReport.ClippedOversizedTriangleCount > 0
            };
        }

        private static double ResolveElevationBaseline(SpatialContext2D spatialContext)
        {
            if (spatialContext.UseAbsoluteCoordinates)
            {
                return 0.0;
            }

            return SpatialElevationBaselineCache.TryGet(spatialContext, out var elevationBaseline)
                ? elevationBaseline
                : 0.0;
        }

        private sealed class PrimitiveBuildReport
        {
            public int TotalTriangleCount { get; init; }

            public int RejectedOutOfBoundsTriangleCount { get; set; }

            public int RejectedOversizedTriangleCount { get; set; }

            public int ClippedOversizedTriangleCount { get; set; }

            public int UnclippedTriangleCount { get; set; }

            public int DegenerateTriangleCount { get; set; }

            public int InvalidMeshCount { get; set; }
        }

        private static bool TryChooseProjectedVertices(
            Google3dTilesTileDescriptor tile,
            Google3dTilesDecodedPrimitive decodedPrimitive,
            SpatialContext2D spatialContext,
            out List<Point3d> projectedVertices,
            out double minimumHeight,
            out BoundingBox2D clipBounds,
            out double maxTriangleEdgeLength)
        {
            var placedBounds = CreatePlacedBounds(spatialContext);
            var scoringBounds = CreateExpandedPlacedBounds(placedBounds);
            clipBounds = ExpandPlacedBounds(placedBounds, ReferenceMeshBoundsPaddingRatio);
            maxTriangleEdgeLength = Math.Max(MinimumTriangleEdgeLimit, MeasureBoundsDiagonal(clipBounds) * TriangleEdgeLimitMultiplier);
            var candidates = new[]
            {
                ProjectVertices(tile, decodedPrimitive, spatialContext, ProjectionMode.TileTransform),
                ProjectVertices(tile, decodedPrimitive, spatialContext, ProjectionMode.YUpToZUpThenTileTransform),
                ProjectVertices(tile, decodedPrimitive, spatialContext, ProjectionMode.InverseYUpToZUpThenTileTransform),
                ProjectVertices(tile, decodedPrimitive, spatialContext, ProjectionMode.RawEcef)
            };
            var selected = candidates[0];
            var selectedScore = ScoreCandidate(selected.Points, decodedPrimitive.TriangleIndices, scoringBounds, maxTriangleEdgeLength);
            for (var candidateIndex = 1; candidateIndex < candidates.Length; candidateIndex++)
            {
                var candidate = candidates[candidateIndex];
                var score = ScoreCandidate(candidate.Points, decodedPrimitive.TriangleIndices, scoringBounds, maxTriangleEdgeLength);
                if (score > selectedScore)
                {
                    selected = candidate;
                    selectedScore = score;
                }
            }

            if (selected.Points.Count == 0)
            {
                projectedVertices = new List<Point3d>();
                minimumHeight = 0.0;
                return false;
            }

            projectedVertices = selected.Points;
            minimumHeight = selected.MinimumHeight;
            return true;
        }

        private static (List<Point3d> Points, double MinimumHeight) ProjectVertices(
            Google3dTilesTileDescriptor tile,
            Google3dTilesDecodedPrimitive decodedPrimitive,
            SpatialContext2D spatialContext,
            ProjectionMode projectionMode)
        {
            var points = new List<Point3d>(decodedPrimitive.EcefVertices.Count);
            var minimumHeight = double.PositiveInfinity;
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
                    return (new List<Point3d>(), 0.0);
                }

                if (!IsFinite(projectedPoint.X) ||
                    !IsFinite(projectedPoint.Y) ||
                    !IsFinite(projectedPoint.Z))
                {
                    return (new List<Point3d>(), 0.0);
                }

                points.Add(new Point3d(
                    projectedPoint.X - offsetX,
                    projectedPoint.Y - offsetY,
                    projectedPoint.Z));
                minimumHeight = Math.Min(minimumHeight, projectedPoint.Z);
            }

            if (double.IsPositiveInfinity(minimumHeight))
            {
                minimumHeight = 0.0;
            }

            return (points, minimumHeight);
        }

        private static double ScoreCandidate(
            IReadOnlyList<Point3d> points,
            IReadOnlyList<int> triangleIndices,
            BoundingBox2D expandedBounds,
            double maxTriangleEdgeLength)
        {
            if (points.Count == 0)
            {
                return double.NegativeInfinity;
            }

            var insideCount = 0;
            foreach (var point in points)
            {
                if (IsInsideBounds(point, expandedBounds))
                {
                    insideCount++;
                }
            }

            var usableTriangleCount = 0;
            var oversizedTriangleCount = 0;
            var outOfBoundsTriangleCount = 0;
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

                if (!TriangleBoundingBoxTouchesBounds(points[a], points[b], points[c], expandedBounds))
                {
                    outOfBoundsTriangleCount++;
                }
                else if (IsOversizedTriangle(points[a], points[b], points[c], maxTriangleEdgeLength))
                {
                    oversizedTriangleCount++;
                }
                else
                {
                    usableTriangleCount++;
                }
            }

            var candidateBounds = BoundingBox.Empty;
            foreach (var point in points)
            {
                candidateBounds.Union(point);
            }

            if (!candidateBounds.IsValid)
            {
                return insideCount;
            }

            var studyCenterX = (expandedBounds.MinX + expandedBounds.MaxX) * 0.5;
            var studyCenterY = (expandedBounds.MinY + expandedBounds.MaxY) * 0.5;
            var candidateCenter = candidateBounds.Center;
            var distance = Math.Sqrt(
                Math.Pow(candidateCenter.X - studyCenterX, 2.0) +
                Math.Pow(candidateCenter.Y - studyCenterY, 2.0));
            var width = Math.Max(1.0, expandedBounds.MaxX - expandedBounds.MinX);
            var height = Math.Max(1.0, expandedBounds.MaxY - expandedBounds.MinY);
            var studyDiagonal = Math.Sqrt((width * width) + (height * height));

            var contextTouchingTriangleCount = usableTriangleCount + oversizedTriangleCount;
            return (contextTouchingTriangleCount * 100000.0) +
                   (insideCount * 1000.0) -
                   (oversizedTriangleCount * 10.0) -
                   (outOfBoundsTriangleCount * 10.0) -
                   (distance / Math.Max(1.0, studyDiagonal));
        }

        private static BoundingBox2D CreatePlacedBounds(SpatialContext2D spatialContext)
        {
            return RhinoSpatialContextTools.CreatePlacedBoundingBox(
                spatialContext.PlacementBoundingBox,
                spatialContext.PlacementOrigin,
                spatialContext.UseAbsoluteCoordinates);
        }

        private static BoundingBox2D CreateExpandedPlacedBounds(BoundingBox2D placedBounds)
        {
            return ExpandPlacedBounds(placedBounds, 3.0);
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

    }
}
