using System;
using System.Collections.Concurrent;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal static class SpatialTerrainCache
    {
        private sealed record Entry(
            string TerrainSrs,
            BoundingBox2D BoundingBox,
            TerrainRasterData Raster,
            double ElevationBase);

        private static readonly ConcurrentDictionary<string, Entry> EntriesByContext = new();

        public static void Store(
            SpatialContext2D spatialContext,
            string terrainSrs,
            BoundingBox2D boundingBox,
            TerrainRasterData raster,
            double elevationBase)
        {
            var key = RhinoSpatialContextTools.CreateSpatialContextKey(spatialContext);
            EntriesByContext[key] = new Entry(terrainSrs, boundingBox, raster, elevationBase);
        }

        public static bool TrySamplePlacedElevation(
            SpatialContext2D spatialContext,
            string sourceSrs,
            double sourceX,
            double sourceY,
            out double elevation)
        {
            elevation = 0.0;

            var key = RhinoSpatialContextTools.CreateSpatialContextKey(spatialContext);
            if (!EntriesByContext.TryGetValue(key, out var entry))
            {
                return false;
            }

            if (!SpatialReferenceTransform.TryTransformXY(sourceSrs, entry.TerrainSrs, sourceX, sourceY, out var terrainX, out var terrainY))
            {
                return false;
            }

            if (!TrySampleRaster(entry, terrainX, terrainY, out var rawElevation))
            {
                return false;
            }

            elevation = spatialContext.UseAbsoluteCoordinates
                ? rawElevation
                : rawElevation - entry.ElevationBase;

            return true;
        }

        private static bool TrySampleRaster(Entry entry, double x, double y, out double elevation)
        {
            elevation = 0.0;

            var raster = entry.Raster;
            if (raster.Width < 2 || raster.Height < 2)
            {
                return false;
            }

            if (x < entry.BoundingBox.MinX || x > entry.BoundingBox.MaxX || y < entry.BoundingBox.MinY || y > entry.BoundingBox.MaxY)
            {
                return false;
            }

            var spanX = entry.BoundingBox.MaxX - entry.BoundingBox.MinX;
            var spanY = entry.BoundingBox.MaxY - entry.BoundingBox.MinY;
            if (spanX <= 0.0 || spanY <= 0.0)
            {
                return false;
            }

            var gridX = (x - entry.BoundingBox.MinX) / spanX * (raster.Width - 1);
            var gridY = (entry.BoundingBox.MaxY - y) / spanY * (raster.Height - 1);

            var x0 = Math.Clamp((int)Math.Floor(gridX), 0, raster.Width - 1);
            var y0 = Math.Clamp((int)Math.Floor(gridY), 0, raster.Height - 1);
            var x1 = Math.Min(x0 + 1, raster.Width - 1);
            var y1 = Math.Min(y0 + 1, raster.Height - 1);

            var fx = Math.Clamp(gridX - x0, 0.0, 1.0);
            var fy = Math.Clamp(gridY - y0, 0.0, 1.0);

            var z00 = ReadRasterElevation(raster, x0, y0);
            var z10 = ReadRasterElevation(raster, x1, y0);
            var z01 = ReadRasterElevation(raster, x0, y1);
            var z11 = ReadRasterElevation(raster, x1, y1);

            if (!z00.HasValue || !z10.HasValue || !z01.HasValue || !z11.HasValue)
            {
                return false;
            }

            var top = Lerp(z00.Value, z10.Value, fx);
            var bottom = Lerp(z01.Value, z11.Value, fx);
            elevation = Lerp(top, bottom, fy);
            return true;
        }

        private static double? ReadRasterElevation(TerrainRasterData raster, int x, int y)
        {
            var index = y * raster.Width + x;
            if (index < 0 || index >= raster.Elevations.Length)
            {
                return null;
            }

            var elevation = raster.Elevations[index];
            if (raster.NoDataValue.HasValue && Math.Abs(elevation - raster.NoDataValue.Value) < 1e-3)
            {
                return null;
            }

            return elevation;
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }
    }
}
