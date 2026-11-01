using System;
using System.Collections.Generic;

namespace RhinoSpatial.Core
{
    public record TerrainMeshData(
        List<Coordinate3D> Vertices,
        List<(int A, int B, int C, int D)> Faces);

    public static class TerrainMeshBuilder
    {
        public static TerrainMeshData Build(
            TerrainRasterData raster,
            BoundingBox2D sampleBounds,
            SpatialContext2D context,
            int maxGridSize,
            double elevationBase)
        {
            if (raster.Width < 2 || raster.Height < 2 ||
                (long)raster.Width * raster.Height != raster.Elevations.Length)
            {
                throw new InvalidOperationException("Terrain requires a raster of at least 2x2 cells with matching elevation values.");
            }

            if (maxGridSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(maxGridSize));
            }

            var xs = SampleIndices(raster.Width, maxGridSize);
            var ys = SampleIndices(raster.Height, maxGridSize);
            var vertices = new List<Coordinate3D>(xs.Count * ys.Count);
            var faces = new List<(int A, int B, int C, int D)>();
            var vertexIndices = new int[xs.Count * ys.Count];
            Array.Fill(vertexIndices, -1);
            var stepX = (sampleBounds.MaxX - sampleBounds.MinX) / (raster.Width - 1);
            var stepY = (sampleBounds.MaxY - sampleBounds.MinY) / (raster.Height - 1);
            var offsetX = context.UseAbsoluteCoordinates ? 0.0 : context.PlacementOrigin.X;
            var offsetY = context.UseAbsoluteCoordinates ? 0.0 : context.PlacementOrigin.Y;
            var offsetZ = context.UseAbsoluteCoordinates ? 0.0 : elevationBase;

            for (var row = 0; row < ys.Count; row++)
            {
                for (var col = 0; col < xs.Count; col++)
                {
                    var elevation = raster.Elevations[ys[row] * raster.Width + xs[col]];
                    if (IsNoData(raster, elevation))
                    {
                        continue;
                    }

                    var sourceX = sampleBounds.MinX + xs[col] * stepX;
                    var sourceY = sampleBounds.MaxY - ys[row] * stepY;
                    if (!SpatialReferenceTransform.TryTransformXY(
                            raster.SrsName, context.ResolvedSrs, sourceX, sourceY, out var x, out var y) ||
                        !double.IsFinite(x) || !double.IsFinite(y))
                    {
                        throw new InvalidOperationException($"Cannot transform terrain from '{raster.SrsName}' into Spatial Context '{context.ResolvedSrs}'.");
                    }

                    vertexIndices[row * xs.Count + col] = vertices.Count;
                    vertices.Add(new Coordinate3D(x - offsetX, y - offsetY, elevation - offsetZ));
                }
            }

            for (var row = 0; row < ys.Count - 1; row++)
            {
                for (var col = 0; col < xs.Count - 1; col++)
                {
                    var index = row * xs.Count + col;
                    var a = vertexIndices[index];
                    var b = vertexIndices[index + 1];
                    var c = vertexIndices[index + xs.Count + 1];
                    var d = vertexIndices[index + xs.Count];
                    if (a >= 0 && b >= 0 && c >= 0 && d >= 0 &&
                        HasCompleteCoverage(raster, xs[col], ys[row], xs[col + 1], ys[row + 1]))
                    {
                        faces.Add((a, b, c, d));
                    }
                }
            }

            if (faces.Count == 0)
            {
                throw new InvalidOperationException("The terrain raster has no connected usable elevation cells to form a surface.");
            }

            return new TerrainMeshData(vertices, faces);
        }

        public static bool IsNoData(TerrainRasterData raster, float elevation)
        {
            return !float.IsFinite(elevation) ||
                   (raster.NoDataValue.HasValue && Math.Abs(elevation - raster.NoDataValue.Value) < 1e-3);
        }

        private static bool HasCompleteCoverage(TerrainRasterData raster, int minX, int minY, int maxX, int maxY)
        {
            // Downsampling must not bridge a NoData hole hidden between retained vertices.
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    if (IsNoData(raster, raster.Elevations[y * raster.Width + x]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static List<int> SampleIndices(int count, int limit)
        {
            var stride = Math.Max(1, (int)Math.Ceiling((count - 1) / (double)(limit - 1)));
            var indices = new List<int>();
            for (var index = 0; index < count - 1; index += stride)
            {
                indices.Add(index);
            }

            indices.Add(count - 1);
            return indices;
        }
    }
}
