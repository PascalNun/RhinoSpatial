using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RhinoSpatial.Core
{
    public record TerrainTextGridReadResult(
        TerrainRasterData Raster,
        BoundingBox2D BoundingBox,
        string SourceSrs,
        string FormatLabel);

    public record TerrainTextGridSourceMetadata(
        BoundingBox2D BoundingBox,
        string SourceSrs,
        string FormatLabel);

    public static class TerrainTextGridReader
    {
        public static TerrainTextGridSourceMetadata ReadSourceMetadata(
            string filePath,
            string sourceSrs)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Terrain text grid was not found: {filePath}", filePath);
            }

            return Path.GetExtension(filePath).Equals(".asc", StringComparison.OrdinalIgnoreCase)
                ? ReadEsriAsciiGridMetadata(filePath, sourceSrs)
                : ReadXyzGridMetadata(filePath, sourceSrs);
        }

        public static TerrainTextGridReadResult ReadRaster(string filePath, string coverageId, string sourceSrs)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Terrain text grid was not found: {filePath}", filePath);
            }

            var extension = Path.GetExtension(filePath);
            if (extension.Equals(".asc", StringComparison.OrdinalIgnoreCase))
            {
                return ReadEsriAsciiGrid(filePath, coverageId, sourceSrs);
            }

            return ReadXyzGrid(filePath, coverageId, sourceSrs);
        }

        private static TerrainTextGridReadResult ReadEsriAsciiGrid(string filePath, string coverageId, string sourceSrs)
        {
            using var reader = new StreamReader(filePath);
            var header = ReadEsriAsciiHeader(reader, out var firstDataLine);
            var elevations = new float[GetCellCount(header.Width, header.Height)];
            var count = 0;
            var line = firstDataLine;
            while (line is not null)
            {
                foreach (var value in SplitFields(line))
                {
                    if (count >= elevations.Length)
                    {
                        throw new InvalidOperationException("The Esri ASCII Grid contains more cell values than ncols and nrows specify.");
                    }

                    elevations[count++] = ParseFloat(value);
                }

                line = reader.ReadLine();
            }

            if (count != elevations.Length)
            {
                throw new InvalidOperationException($"The Esri ASCII Grid contains {count} cell values; expected {elevations.Length}.");
            }

            // Terrain vertices represent cell centers; reference metadata covers the full cell footprint.
            var bounds = new BoundingBox2D(
                header.Footprint.MinX + header.CellSize * 0.5,
                header.Footprint.MinY + header.CellSize * 0.5,
                header.Footprint.MaxX - header.CellSize * 0.5,
                header.Footprint.MaxY - header.CellSize * 0.5);
            var raster = new TerrainRasterData(
                coverageId,
                sourceSrs,
                header.Width,
                header.Height,
                new Coordinate2D(bounds.MinX, bounds.MaxY),
                new Coordinate2D(header.CellSize, 0.0),
                new Coordinate2D(0.0, -header.CellSize),
                header.NoDataValue,
                elevations);

            return new TerrainTextGridReadResult(raster, bounds, sourceSrs, "Esri ASCII Grid");
        }

        private static TerrainTextGridSourceMetadata ReadEsriAsciiGridMetadata(
            string filePath,
            string sourceSrs)
        {
            using var reader = new StreamReader(filePath);
            var header = ReadEsriAsciiHeader(reader, out _);
            return new TerrainTextGridSourceMetadata(header.Footprint, sourceSrs, "Esri ASCII Grid");
        }

        private sealed record AsciiGridHeader(
            int Width,
            int Height,
            double CellSize,
            double NoDataValue,
            BoundingBox2D Footprint);

        private static AsciiGridHeader ReadEsriAsciiHeader(StreamReader reader, out string? firstDataLine)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            firstDataLine = null;
            while (reader.ReadLine() is { } line)
            {
                var parts = SplitFields(line);
                if (parts.Length == 0)
                {
                    continue;
                }

                var key = parts[0].ToLowerInvariant();
                if (key is not ("ncols" or "nrows" or "xllcorner" or "xllcenter" or "yllcorner" or "yllcenter" or "cellsize" or "nodata_value"))
                {
                    firstDataLine = line;
                    break;
                }

                if (parts.Length != 2 || !headers.TryAdd(key, parts[1]))
                {
                    throw new InvalidOperationException($"Invalid or duplicate Esri ASCII Grid header '{key}'.");
                }
            }

            var width = ReadRequiredInt(headers, "ncols");
            var height = ReadRequiredInt(headers, "nrows");
            GetCellCount(width, height);
            var cellSize = ReadRequiredDouble(headers, "cellsize");
            if (cellSize <= 0.0)
            {
                throw new InvalidOperationException("The terrain grid cellsize must be greater than zero.");
            }

            var usesCenters = headers.ContainsKey("xllcenter") && headers.ContainsKey("yllcenter");
            var usesCorners = headers.ContainsKey("xllcorner") && headers.ContainsKey("yllcorner");
            if ((!usesCenters && !usesCorners) ||
                (usesCenters && (headers.ContainsKey("xllcorner") || headers.ContainsKey("yllcorner"))) ||
                (usesCorners && (headers.ContainsKey("xllcenter") || headers.ContainsKey("yllcenter"))))
            {
                throw new InvalidOperationException("The terrain grid origin must use matching xllcorner/yllcorner or xllcenter/yllcenter headers.");
            }

            var xll = ReadRequiredDouble(headers, "xllcorner", "xllcenter");
            var yll = ReadRequiredDouble(headers, "yllcorner", "yllcenter");
            var minX = usesCenters ? xll - cellSize * 0.5 : xll;
            var minY = usesCenters ? yll - cellSize * 0.5 : yll;
            var maxX = minX + width * cellSize;
            var maxY = minY + height * cellSize;
            if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY))
            {
                throw new InvalidOperationException("The terrain grid extent must contain finite coordinates.");
            }

            var noData = TryReadDouble(headers, "NODATA_value");
            if (headers.ContainsKey("NODATA_value") && !noData.HasValue)
            {
                throw new InvalidOperationException("The terrain grid NODATA_value must be numeric.");
            }

            return new AsciiGridHeader(width, height, cellSize, noData ?? -9999.0, new BoundingBox2D(minX, minY, maxX, maxY));
        }

        private static int GetCellCount(int width, int height)
        {
            var count = (long)width * height;
            if (count > Array.MaxLength)
            {
                throw new InvalidOperationException("The terrain grid contains too many cells to load into memory.");
            }

            return (int)count;
        }

        private static TerrainTextGridSourceMetadata ReadXyzGridMetadata(
            string filePath,
            string sourceSrs)
        {
            double? minX = null;
            double? minY = null;
            double? maxX = null;
            double? maxY = null;
            foreach (var line in File.ReadLines(filePath))
            {
                var parts = SplitFields(line);
                if (parts.Length < 2 ||
                    !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    continue;
                }

                RequireFinitePosition(x, y);
                minX = !minX.HasValue ? x : Math.Min(minX.Value, x);
                minY = !minY.HasValue ? y : Math.Min(minY.Value, y);
                maxX = !maxX.HasValue ? x : Math.Max(maxX.Value, x);
                maxY = !maxY.HasValue ? y : Math.Max(maxY.Value, y);
            }

            if (!minX.HasValue || !minY.HasValue || !maxX.HasValue || !maxY.HasValue)
            {
                throw new InvalidOperationException("The XYZ/CSV terrain file did not contain readable x,y rows.");
            }

            return new TerrainTextGridSourceMetadata(
                new BoundingBox2D(minX.Value, minY.Value, maxX.Value, maxY.Value),
                sourceSrs,
                "XYZ/CSV terrain grid");
        }

        private static TerrainTextGridReadResult ReadXyzGrid(string filePath, string coverageId, string sourceSrs)
        {
            var samples = new List<(double X, double Y, float Z)>();
            foreach (var line in File.ReadLines(filePath))
            {
                var parts = SplitFields(line);
                if (parts.Length < 3 ||
                    !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                {
                    continue;
                }

                RequireFinitePosition(x, y);
                samples.Add((x, y, z));
            }

            if (samples.Count == 0)
            {
                throw new InvalidOperationException("The XYZ/CSV terrain grid did not contain readable x,y,z rows.");
            }

            var xs = samples.Select(sample => sample.X).Distinct().OrderBy(value => value).ToList();
            var ys = samples.Select(sample => sample.Y).Distinct().OrderByDescending(value => value).ToList();
            if (xs.Count < 2 || ys.Count < 2)
            {
                throw new InvalidOperationException("The XYZ/CSV terrain grid must contain at least a 2x2 regular grid.");
            }

            RequireRegularAxis(xs);
            RequireRegularAxis(ys);
            var width = xs.Count;
            var height = ys.Count;
            var xIndex = xs
                .Select((value, index) => (value, index))
                .ToDictionary(item => item.value, item => item.index);
            var yIndex = ys
                .Select((value, index) => (value, index))
                .ToDictionary(item => item.value, item => item.index);
            var elevations = Enumerable.Repeat(float.NaN, GetCellCount(width, height)).ToArray();

            foreach (var sample in samples)
            {
                elevations[(yIndex[sample.Y] * width) + xIndex[sample.X]] = sample.Z;
            }

            var minX = xs[0];
            var maxX = xs[^1];
            var minY = ys[^1];
            var maxY = ys[0];
            var raster = new TerrainRasterData(
                coverageId,
                sourceSrs,
                width,
                height,
                new Coordinate2D(minX, maxY),
                new Coordinate2D((maxX - minX) / (width - 1), 0.0),
                new Coordinate2D(0.0, -((maxY - minY) / (height - 1))),
                float.NaN,
                elevations);

            return new TerrainTextGridReadResult(
                raster,
                new BoundingBox2D(minX, minY, maxX, maxY),
                sourceSrs,
                "XYZ/CSV terrain grid");
        }

        private static void RequireFinitePosition(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                throw new InvalidOperationException("XYZ/CSV terrain positions must contain finite x,y coordinates.");
            }
        }

        private static void RequireRegularAxis(IReadOnlyList<double> coordinates)
        {
            var step = (coordinates[^1] - coordinates[0]) / (coordinates.Count - 1);
            var tolerance = Math.Max(Math.Abs(step) * 1e-6, 1e-9);
            for (var index = 1; index < coordinates.Count; index++)
            {
                if (!double.IsFinite(step) ||
                    Math.Abs(coordinates[index] - (coordinates[0] + index * step)) > tolerance)
                {
                    throw new InvalidOperationException("The XYZ/CSV terrain file must use a regular grid with evenly spaced x and y coordinates.");
                }
            }
        }

        private static string[] SplitFields(string line)
        {
            return line
                .Split(new[] { ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static int ReadRequiredInt(IReadOnlyDictionary<string, string> headers, string key)
        {
            if (!headers.TryGetValue(key, out var value) ||
                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                throw new InvalidOperationException($"The terrain grid header '{key}' must be an integer greater than zero.");
            }

            return parsed;
        }

        private static double ReadRequiredDouble(IReadOnlyDictionary<string, string> headers, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (headers.TryGetValue(key, out var value) &&
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
                {
                    return parsed;
                }
            }

            throw new InvalidOperationException($"The terrain grid header '{string.Join("' or '", keys)}' must contain a finite number.");
        }

        private static double? TryReadDouble(IReadOnlyDictionary<string, string> headers, string key)
        {
            return headers.TryGetValue(key, out var value) &&
                   double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static float ParseFloat(string value)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"The terrain grid cell value '{value}' is not numeric.");
        }
    }
}
