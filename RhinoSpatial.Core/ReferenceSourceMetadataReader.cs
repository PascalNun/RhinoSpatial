using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace RhinoSpatial.Core
{
    public record ReferenceSourceMetadata(
        string SourceKind,
        string SrsName,
        BoundingBox2D? NativeBoundingBox,
        BoundingBox2D? Wgs84BoundingBox,
        int SourceItemCount);

    public static class ReferenceSourceMetadataReader
    {
        private const int MaximumDirectoryFileCount = 256;
        private static readonly string[] SupportedFileExtensions =
        {
            ".tif", ".tiff", ".shp", ".geojson", ".json", ".cityjson",
            ".gml", ".xml", ".asc", ".xyz", ".csv", ".zip"
        };

        public static bool IsLocalSource(string? source)
        {
            var path = source?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(path) &&
                   (File.Exists(path) || Directory.Exists(path));
        }

        public static ReferenceSourceMetadata Read(
            string source,
            string fallbackSrs = "")
        {
            var path = source.Trim();
            if (Directory.Exists(path))
            {
                var files = Directory
                    .EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                    .Where(IsSupportedFile)
                    .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumDirectoryFileCount)
                    .ToList();
                if (files.Count == 0)
                {
                    throw new InvalidOperationException("The reference folder does not contain a supported geospatial file.");
                }

                var metadata = new List<ReferenceSourceMetadata>(files.Count);
                foreach (var file in files)
                {
                    try
                    {
                        metadata.Add(ReadFile(file, fallbackSrs));
                    }
                    catch
                    {
                        // A mixed project folder can contain stale or malformed files.
                        // Keep any metadata that can still establish a useful map view.
                    }
                }

                return Combine(metadata, "folder");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Reference source was not found: {path}", path);
            }

            return ReadFile(path, fallbackSrs);
        }

        public static bool TryTransformBoundsToWgs84(
            string sourceSrs,
            BoundingBox2D sourceBounds,
            out BoundingBox2D wgs84Bounds)
        {
            var corners = new[]
            {
                new Coordinate2D(sourceBounds.MinX, sourceBounds.MinY),
                new Coordinate2D(sourceBounds.MinX, sourceBounds.MaxY),
                new Coordinate2D(sourceBounds.MaxX, sourceBounds.MinY),
                new Coordinate2D(sourceBounds.MaxX, sourceBounds.MaxY)
            };
            var transformed = new List<Coordinate2D>(corners.Length);
            foreach (var corner in corners)
            {
                if (!SpatialReferenceTransform.TryTransformXY(
                        sourceSrs,
                        "EPSG:4326",
                        corner.X,
                        corner.Y,
                        out var longitude,
                        out var latitude))
                {
                    wgs84Bounds = new BoundingBox2D(0.0, 0.0, 0.0, 0.0);
                    return false;
                }

                transformed.Add(new Coordinate2D(longitude, latitude));
            }

            wgs84Bounds = new BoundingBox2D(
                transformed.Min(static point => point.X),
                transformed.Min(static point => point.Y),
                transformed.Max(static point => point.X),
                transformed.Max(static point => point.Y));
            return true;
        }

        private static ReferenceSourceMetadata ReadFile(
            string filePath,
            string fallbackSrs)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".tif" or ".tiff" => ReadGeoTiff(filePath),
                ".shp" => ReadShapefile(filePath, fallbackSrs),
                ".geojson" => ReadJson(File.ReadAllText(filePath), "GeoJSON", fallbackSrs),
                ".json" or ".cityjson" => ReadJson(File.ReadAllText(filePath), "JSON", fallbackSrs),
                ".gml" or ".xml" => ReadGml(filePath, fallbackSrs),
                ".asc" or ".xyz" or ".csv" => ReadTerrainText(filePath, fallbackSrs),
                ".zip" => ReadZip(filePath, fallbackSrs),
                _ => throw new InvalidOperationException($"Unsupported reference source file type: {extension}")
            };
        }

        private static ReferenceSourceMetadata ReadGeoTiff(string filePath)
        {
            var raster = GeoTiffInfoCache.GetOrRead(filePath);
            return Create("GeoTIFF", raster.SrsName, raster.BoundingBox, 1);
        }

        private static ReferenceSourceMetadata ReadShapefile(
            string filePath,
            string fallbackSrs)
        {
            var metadata = ShapefileFeatureReader.ReadSourceMetadata(filePath, fallbackSrs);
            return Create("Shapefile", metadata.SourceSrs, metadata.BoundingBox, 1);
        }

        private static ReferenceSourceMetadata ReadGml(
            string filePath,
            string fallbackSrs)
        {
            using var stream = File.OpenRead(filePath);
            var metadata = Lod2GmlReader.ReadSourceMetadata(stream);
            var sourceSrs = string.IsNullOrWhiteSpace(metadata.SrsName)
                ? fallbackSrs
                : metadata.SrsName;
            return Create("GML/XML", sourceSrs, metadata.BoundingBox, 1);
        }

        private static ReferenceSourceMetadata ReadTerrainText(
            string filePath,
            string fallbackSrs)
        {
            var metadata = TerrainTextGridReader.ReadSourceMetadata(filePath, fallbackSrs);
            return Create(metadata.FormatLabel, metadata.SourceSrs, metadata.BoundingBox, 1);
        }

        private static ReferenceSourceMetadata ReadJson(
            string json,
            string fallbackKind,
            string fallbackSrs)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) &&
                       typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString() ?? string.Empty
                : string.Empty;
            if (string.Equals(type, "CityJSON", StringComparison.OrdinalIgnoreCase) ||
                root.TryGetProperty("CityObjects", out _))
            {
                var cityMetadata = CityJsonReader.ReadSourceMetadata(json);
                var citySrs = string.IsNullOrWhiteSpace(cityMetadata.SrsName)
                    ? fallbackSrs
                    : cityMetadata.SrsName;
                return Create("CityJSON", citySrs, cityMetadata.BoundingBox, 1);
            }

            if (!string.Equals(type, "FeatureCollection", StringComparison.OrdinalIgnoreCase) &&
                !root.TryGetProperty("features", out _))
            {
                throw new InvalidOperationException($"The {fallbackKind} reference file is neither CityJSON nor a GeoJSON FeatureCollection.");
            }

            var geoJsonSrs = GeoJsonReader.TryReadSourceSrs(json);
            var sourceSrs = string.IsNullOrWhiteSpace(geoJsonSrs)
                ? "EPSG:4326"
                : geoJsonSrs!;
            var bounds = TryReadGeoJsonBoundingBox(root) ??
                         CombineBoundingBoxes(
                             GeoJsonReader
                                 .ReadFeatures(json, "Reference")
                                 .Select(feature => SpatialFeatureFilter.TryGetFeatureBounds(feature, out var featureBounds)
                                     ? featureBounds
                                     : null));
            return Create("GeoJSON", sourceSrs, bounds, 1);
        }

        private static ReferenceSourceMetadata ReadZip(
            string filePath,
            string fallbackSrs)
        {
            using var archive = ZipFile.OpenRead(filePath);
            var entries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Where(entry =>
                {
                    var extension = Path.GetExtension(entry.Name);
                    return extension.Equals(".gml", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".cityjson", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".geojson", StringComparison.OrdinalIgnoreCase);
                })
                .Take(MaximumDirectoryFileCount)
                .ToList();
            if (entries.Count == 0)
            {
                throw new InvalidOperationException("The reference ZIP does not contain supported GML, CityJSON, or GeoJSON metadata.");
            }

            var metadata = new List<ReferenceSourceMetadata>(entries.Count);
            foreach (var entry in entries)
            {
                try
                {
                    var extension = Path.GetExtension(entry.Name);
                    using var stream = entry.Open();
                    if (extension.Equals(".gml", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        var gmlMetadata = Lod2GmlReader.ReadSourceMetadata(stream);
                        var srs = string.IsNullOrWhiteSpace(gmlMetadata.SrsName) ? fallbackSrs : gmlMetadata.SrsName;
                        metadata.Add(Create("GML/XML", srs, gmlMetadata.BoundingBox, 1));
                        continue;
                    }

                    using var reader = new StreamReader(stream);
                    metadata.Add(ReadJson(reader.ReadToEnd(), "JSON", fallbackSrs));
                }
                catch
                {
                    // LoD archives can mix metadata, schemas, and partial exports.
                    // Skip unreadable entries and combine the usable spatial entries.
                }
            }

            return Combine(metadata, "ZIP archive");
        }

        private static ReferenceSourceMetadata Create(
            string sourceKind,
            string srsName,
            BoundingBox2D? nativeBounds,
            int sourceItemCount)
        {
            BoundingBox2D? wgs84Bounds = null;
            if (nativeBounds is not null &&
                !string.IsNullOrWhiteSpace(srsName) &&
                TryTransformBoundsToWgs84(srsName, nativeBounds, out var transformedBounds))
            {
                wgs84Bounds = transformedBounds;
            }

            return new ReferenceSourceMetadata(
                sourceKind,
                srsName,
                nativeBounds,
                wgs84Bounds,
                sourceItemCount);
        }

        private static ReferenceSourceMetadata Combine(
            IEnumerable<ReferenceSourceMetadata> metadata,
            string sourceKind)
        {
            var items = metadata.ToList();
            if (items.Count == 0)
            {
                throw new InvalidOperationException("The reference source did not expose usable metadata.");
            }

            var distinctSrs = items
                .Select(static item => item.SrsName)
                .Where(static srs => !string.IsNullOrWhiteSpace(srs))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var commonSrs = distinctSrs.Count == 1 ? distinctSrs[0] : string.Empty;
            var nativeBounds = string.IsNullOrWhiteSpace(commonSrs)
                ? null
                : CombineBoundingBoxes(items
                    .Where(item => string.Equals(item.SrsName, commonSrs, StringComparison.OrdinalIgnoreCase))
                    .Select(static item => item.NativeBoundingBox));
            return new ReferenceSourceMetadata(
                sourceKind,
                commonSrs,
                nativeBounds,
                CombineBoundingBoxes(items.Select(static item => item.Wgs84BoundingBox)),
                items.Sum(static item => item.SourceItemCount));
        }

        private static BoundingBox2D? TryReadGeoJsonBoundingBox(JsonElement root)
        {
            if (!root.TryGetProperty("bbox", out var bbox) || bbox.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var values = bbox.EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.Number)
                .Select(static value => value.GetDouble())
                .ToArray();
            return values.Length switch
            {
                >= 6 => new BoundingBox2D(values[0], values[1], values[3], values[4]),
                >= 4 => new BoundingBox2D(values[0], values[1], values[2], values[3]),
                _ => null
            };
        }

        private static BoundingBox2D? CombineBoundingBoxes(
            IEnumerable<BoundingBox2D?> boundingBoxes)
        {
            BoundingBox2D? combined = null;
            foreach (var bounds in boundingBoxes)
            {
                if (bounds is null)
                {
                    continue;
                }

                combined = combined is null
                    ? bounds
                    : new BoundingBox2D(
                        Math.Min(combined.MinX, bounds.MinX),
                        Math.Min(combined.MinY, bounds.MinY),
                        Math.Max(combined.MaxX, bounds.MaxX),
                        Math.Max(combined.MaxY, bounds.MaxY));
            }

            return combined;
        }

        private static bool IsSupportedFile(string filePath)
        {
            return SupportedFileExtensions.Contains(
                Path.GetExtension(filePath),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
