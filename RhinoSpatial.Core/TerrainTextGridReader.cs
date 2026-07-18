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
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (headers.Count < 6 && reader.ReadLine() is { } headerLine)
            {
                var parts = SplitFields(headerLine);
                if (parts.Length < 2)
                {
                    continue;
                }

                headers[parts[0]] = parts[1];
            }

            var width = ReadRequiredInt(headers, "ncols");
            var height = ReadRequiredInt(headers, "nrows");
            var xll = ReadRequiredDouble(headers, "xllcorner", "xllcenter");
            var yll = ReadRequiredDouble(headers, "yllcorner", "yllcenter");
            var cellSize = ReadRequiredDouble(headers, "cellsize");
            var usesCellCenter =
                headers.ContainsKey("xllcenter") ||
                headers.ContainsKey("yllcenter");
            var noData = TryReadDouble(headers, "NODATA_value");
            var elevations = new float[width * height];
            var row = 0;

            while (row < height && reader.ReadLine() is { } line)
            {
                var parts = SplitFields(line);
                if (parts.Length == 0)
                {
                    continue;
                }

                for (var col = 0; col < Math.Min(width, parts.Length); col++)
                {
                    elevations[row * width + col] = ParseFloat(parts[col]);
                }

                row++;
            }

            if (row < height)
            {
                throw new InvalidOperationException("The Esri ASCII Grid ended before all rows were read.");
            }

            var minX = usesCellCenter ? xll - (cellSize * 0.5) : xll;
            var minY = usesCellCenter ? yll - (cellSize * 0.5) : yll;
            var maxX = minX + ((width - 1) * cellSize);
            var maxY = minY + ((height - 1) * cellSize);
            var raster = new TerrainRasterData(
                coverageId,
                sourceSrs,
                width,
                height,
                new Coordinate2D(minX, maxY),
                new Coordinate2D(cellSize, 0.0),
                new Coordinate2D(0.0, -cellSize),
                noData,
                elevations);

            return new TerrainTextGridReadResult(
                raster,
                new BoundingBox2D(minX, minY, maxX, maxY),
                sourceSrs,
                "Esri ASCII Grid");
        }

        private static TerrainTextGridSourceMetadata ReadEsriAsciiGridMetadata(
            string filePath,
            string sourceSrs)
        {
            using var reader = new StreamReader(filePath);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (headers.Count < 6 && reader.ReadLine() is { } headerLine)
            {
                var parts = SplitFields(headerLine);
                if (parts.Length >= 2)
                {
                    headers[parts[0]] = parts[1];
                }
            }

            var width = ReadRequiredInt(headers, "ncols");
            var height = ReadRequiredInt(headers, "nrows");
            var xll = ReadRequiredDouble(headers, "xllcorner", "xllcenter");
            var yll = ReadRequiredDouble(headers, "yllcorner", "yllcenter");
            var cellSize = ReadRequiredDouble(headers, "cellsize");
            var usesCellCenter = headers.ContainsKey("xllcenter") || headers.ContainsKey("yllcenter");
            var minX = usesCellCenter ? xll - (cellSize * 0.5) : xll;
            var minY = usesCellCenter ? yll - (cellSize * 0.5) : yll;
            return new TerrainTextGridSourceMetadata(
                new BoundingBox2D(
                    minX,
                    minY,
                    minX + ((width - 1) * cellSize),
                    minY + ((height - 1) * cellSize)),
                sourceSrs,
                "Esri ASCII Grid");
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

            var width = xs.Count;
            var height = ys.Count;
            var xIndex = xs
                .Select((value, index) => (value, index))
                .ToDictionary(item => item.value, item => item.index);
            var yIndex = ys
                .Select((value, index) => (value, index))
                .ToDictionary(item => item.value, item => item.index);
            var elevations = Enumerable.Repeat(float.NaN, width * height).ToArray();

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

        private static string[] SplitFields(string line)
        {
            return line
                .Split(new[] { ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static int ReadRequiredInt(IReadOnlyDictionary<string, string> headers, string key)
        {
            if (!headers.TryGetValue(key, out var value) ||
                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new InvalidOperationException($"The terrain grid header is missing '{key}'.");
            }

            return parsed;
        }

        private static double ReadRequiredDouble(IReadOnlyDictionary<string, string> headers, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (headers.TryGetValue(key, out var value) &&
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            throw new InvalidOperationException($"The terrain grid header is missing '{string.Join("' or '", keys)}'.");
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
                : float.NaN;
        }
    }
}
