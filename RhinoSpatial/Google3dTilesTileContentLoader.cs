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
    internal static class Google3dTilesTileContentLoader
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        private static readonly object CacheSyncRoot = new();
        private static readonly Dictionary<string, List<Google3dTilesDecodedPrimitive>> DecodedTileCache = new(StringComparer.Ordinal);
        private static readonly string TextureCacheDirectory = Path.Combine(Path.GetTempPath(), "RhinoSpatial", "google-3d-tiles");

        public static async Task<List<Google3dTilesReferenceSession.DisplayPrimitive>> LoadDisplayPrimitivesAsync(
            IEnumerable<Google3dTilesTileDescriptor> tiles,
            SpatialContext2D spatialContext,
            CancellationToken cancellationToken = default)
        {
            var displayPrimitives = new List<Google3dTilesReferenceSession.DisplayPrimitive>();
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

                List<Google3dTilesDecodedPrimitive> decodedPrimitives;
                try
                {
                    decodedPrimitives = await GetOrLoadDecodedTileAsync(tileUrl, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                foreach (var decodedPrimitive in decodedPrimitives)
                {
                    var displayPrimitive = CreateDisplayPrimitive(tile, decodedPrimitive, spatialContext);
                    if (displayPrimitive is not null)
                    {
                        displayPrimitives.Add(displayPrimitive);
                    }
                }
            }

            return displayPrimitives;
        }

        private static async Task<List<Google3dTilesDecodedPrimitive>> GetOrLoadDecodedTileAsync(string tileUrl, CancellationToken cancellationToken)
        {
            lock (CacheSyncRoot)
            {
                if (DecodedTileCache.TryGetValue(tileUrl, out var cached))
                {
                    return cached;
                }
            }

            var glbBytes = await HttpClient.GetByteArrayAsync(tileUrl, cancellationToken).ConfigureAwait(false);
            var decodedPrimitives = Google3dTilesGlbDecoder.Decode(glbBytes);

            lock (CacheSyncRoot)
            {
                DecodedTileCache[tileUrl] = decodedPrimitives;
            }

            return decodedPrimitives;
        }

        private static Google3dTilesReferenceSession.DisplayPrimitive? CreateDisplayPrimitive(
            Google3dTilesTileDescriptor tile,
            Google3dTilesDecodedPrimitive decodedPrimitive,
            SpatialContext2D spatialContext)
        {
            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;
            var placedBounds = RhinoSpatialContextTools.CreatePlacedBoundingBox(
                spatialContext.PlacementBoundingBox,
                spatialContext.PlacementOrigin,
                spatialContext.UseAbsoluteCoordinates);
            var boundsWidth = Math.Max(1.0, placedBounds.MaxX - placedBounds.MinX);
            var boundsHeight = Math.Max(1.0, placedBounds.MaxY - placedBounds.MinY);
            var boundsDiagonal = Math.Sqrt((boundsWidth * boundsWidth) + (boundsHeight * boundsHeight));
            var expandedBounds = new BoundingBox2D(
                placedBounds.MinX - (boundsWidth * 0.45),
                placedBounds.MinY - (boundsHeight * 0.45),
                placedBounds.MaxX + (boundsWidth * 0.45),
                placedBounds.MaxY + (boundsHeight * 0.45));

            var projectedVertices = new List<Point3d>(decodedPrimitive.EcefVertices.Count);
            var minimumHeight = double.PositiveInfinity;

            foreach (var vertex in decodedPrimitive.EcefVertices)
            {
                var transformedVertex = ApplyTileTransform(vertex, tile.Transform);

                if (!TryConvertEcefToProjected(transformedVertex, spatialContext, out var projectedPoint))
                {
                    return null;
                }

                projectedVertices.Add(new Point3d(
                    projectedPoint.X - offsetX,
                    projectedPoint.Y - offsetY,
                    projectedPoint.Z));
                minimumHeight = Math.Min(minimumHeight, projectedPoint.Z);
            }

            if (projectedVertices.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh();
            var usedVertexMap = new Dictionary<int, int>();

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
                    spatialContext.UseAbsoluteCoordinates ? sourcePoint.Z : sourcePoint.Z - minimumHeight);

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

                if (!ShouldKeepTriangle(
                        projectedVertices[a],
                        projectedVertices[b],
                        projectedVertices[c],
                        placedBounds,
                        expandedBounds,
                        boundsDiagonal))
                {
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

            DisplayMaterial? material = null;
            if (decodedPrimitive.BaseColorTextureBytes is not null &&
                decodedPrimitive.BaseColorTextureBytes.Length > 0)
            {
                var texturePath = EnsureTextureFile(decodedPrimitive.BaseColorTextureBytes, decodedPrimitive.BaseColorTextureMimeType);
                material = RhinoSpatialRasterDisplayTools.CreateDisplayMaterial(texturePath);
            }

            material ??= new DisplayMaterial(System.Drawing.Color.White)
            {
                IsTwoSided = true,
                Shine = decodedPrimitive.IsUnlit ? 0.0 : 0.2
            };

            return new Google3dTilesReferenceSession.DisplayPrimitive
            {
                Mesh = mesh,
                Material = material,
                SourceUrl = tile.Url
            };
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

            projectedPoint = new Point3d(projectedX, projectedY, height);
            return true;
        }

        private static bool DoesTriangleIntersectBounds(Point3d a, Point3d b, Point3d c, BoundingBox2D bounds)
        {
            var triangleBounds = new BoundingBox2D(
                Math.Min(a.X, Math.Min(b.X, c.X)),
                Math.Min(a.Y, Math.Min(b.Y, c.Y)),
                Math.Max(a.X, Math.Max(b.X, c.X)),
                Math.Max(a.Y, Math.Max(b.Y, c.Y)));

            return RhinoSpatialContextTools.DoBoundingBoxesIntersect(bounds, triangleBounds);
        }

        private static bool ShouldKeepTriangle(
            Point3d a,
            Point3d b,
            Point3d c,
            BoundingBox2D bounds,
            BoundingBox2D expandedBounds,
            double boundsDiagonal)
        {
            if (!DoesTriangleIntersectBounds(a, b, c, bounds))
            {
                return false;
            }

            var edgeLimit = boundsDiagonal * 6.0;
            var edgeAB = Distance2D(a, b);
            var edgeBC = Distance2D(b, c);
            var edgeCA = Distance2D(c, a);
            if (edgeAB > edgeLimit || edgeBC > edgeLimit || edgeCA > edgeLimit)
            {
                return false;
            }

            var insideCount = 0;
            if (IsPointInsideBounds(a, expandedBounds)) insideCount++;
            if (IsPointInsideBounds(b, expandedBounds)) insideCount++;
            if (IsPointInsideBounds(c, expandedBounds)) insideCount++;

            if (insideCount >= 1)
            {
                return true;
            }

            var centroid = new Point3d(
                (a.X + b.X + c.X) / 3.0,
                (a.Y + b.Y + c.Y) / 3.0,
                (a.Z + b.Z + c.Z) / 3.0);

            return IsPointInsideBounds(centroid, expandedBounds);
        }

        private static bool IsPointInsideBounds(Point3d point, BoundingBox2D bounds)
        {
            return point.X >= bounds.MinX &&
                   point.X <= bounds.MaxX &&
                   point.Y >= bounds.MinY &&
                   point.Y <= bounds.MaxY;
        }

        private static double Distance2D(Point3d a, Point3d b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }
    }
}
