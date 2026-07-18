using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsLineString = NetTopologySuite.Geometries.LineString;

namespace RhinoSpatial.Core
{
    public record ShapefileReadResult(
        List<WfsFeature> Features,
        string SourceSrs,
        BoundingBox2D? SourceBoundingBox,
        int SourceFeatureCount,
        int SkippedOutsideContextCount,
        int FailedFeatureCount);

    public record ShapefileSourceMetadata(
        string SourceSrs,
        BoundingBox2D? BoundingBox);

    public static class ShapefileFeatureReader
    {
        public static ShapefileSourceMetadata ReadSourceMetadata(
            string shapefilePath,
            string fallbackSrs = "")
        {
            if (!File.Exists(shapefilePath))
            {
                throw new FileNotFoundException($"Shapefile was not found: {shapefilePath}", shapefilePath);
            }

            return new ShapefileSourceMetadata(
                ResolveSourceSrs(shapefilePath, fallbackSrs),
                TryReadBoundingBox(shapefilePath));
        }

        public static ShapefileReadResult ReadFeatures(
            string shapefilePath,
            string sourceLayerName,
            SpatialContext2D spatialContext,
            int maxFeatures)
        {
            if (!File.Exists(shapefilePath))
            {
                throw new FileNotFoundException($"Shapefile was not found: {shapefilePath}", shapefilePath);
            }

            var sourceSrs = ResolveSourceSrs(shapefilePath, spatialContext.ResolvedSrs);
            if (string.IsNullOrWhiteSpace(sourceSrs))
            {
                sourceSrs = spatialContext.ResolvedSrs;
            }

            var sourceBoundingBox = TryReadBoundingBox(shapefilePath);
            var contextBoundsInSourceSrs = RhinoSpatialContextToolsAdapter.TryResolveBoundingBoxForSrs(
                spatialContext,
                sourceSrs,
                out var bounds)
                ? bounds
                : null;

            var features = new List<WfsFeature>();
            var skippedOutsideContextCount = 0;
            var failedFeatureCount = 0;
            var sourceFeatureCount = 0;

            foreach (var feature in Shapefile.ReadAllFeatures(shapefilePath))
            {
                sourceFeatureCount++;
                if (maxFeatures > 0 && features.Count >= maxFeatures)
                {
                    break;
                }

                try
                {
                    var geometry = feature.Geometry;
                    if (geometry is null || geometry.IsEmpty)
                    {
                        continue;
                    }

                    if (contextBoundsInSourceSrs is not null &&
                        !EnvelopeIntersectsBounds(geometry.EnvelopeInternal, contextBoundsInSourceSrs))
                    {
                        skippedOutsideContextCount++;
                        continue;
                    }

                    var transformedGeometry = TransformGeometry(geometry, sourceSrs, spatialContext.ResolvedSrs);
                    var wfsGeometry = ConvertGeometry(transformedGeometry);
                    if (wfsGeometry.OuterRings.Count == 0 &&
                        wfsGeometry.LineStrings.Count == 0 &&
                        wfsGeometry.Points.Count == 0)
                    {
                        continue;
                    }

                    features.Add(new WfsFeature(
                        sourceFeatureCount.ToString(CultureInfo.InvariantCulture),
                        sourceLayerName,
                        wfsGeometry,
                        ReadAttributes(feature.Attributes)));
                }
                catch
                {
                    failedFeatureCount++;
                }
            }

            return new ShapefileReadResult(
                features,
                sourceSrs,
                sourceBoundingBox,
                sourceFeatureCount,
                skippedOutsideContextCount,
                failedFeatureCount);
        }

        private static string ResolveSourceSrs(string shapefilePath, string fallbackSrs)
        {
            var prjPath = Path.ChangeExtension(shapefilePath, ".prj");
            if (!File.Exists(prjPath))
            {
                return fallbackSrs;
            }

            var projectionText = File.ReadAllText(prjPath);
            var normalized = NormalizeKnownProjection(projectionText);
            return string.IsNullOrWhiteSpace(normalized) ? fallbackSrs : normalized;
        }

        private static string NormalizeKnownProjection(string projectionText)
        {
            var text = projectionText ?? string.Empty;
            var authorityMatch = Regex.Match(
                text,
                "AUTHORITY\\[\\s*\"EPSG\"\\s*,\\s*\"(?<epsg>\\d+)\"\\s*\\]",
                RegexOptions.IgnoreCase);
            if (authorityMatch.Success)
            {
                return $"EPSG:{authorityMatch.Groups["epsg"].Value}";
            }

            if (text.Contains("ETRS_1989_UTM_Zone_32N", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ETRS89 / UTM zone 32N", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25832";
            }

            if (text.Contains("ETRS_1989_UTM_Zone_33N", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ETRS89 / UTM zone 33N", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25833";
            }

            if (text.Contains("WGS_1984_Web_Mercator", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("WGS 84 / Pseudo-Mercator", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:3857";
            }

            if (text.Contains("WGS_1984", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("WGS 84", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4326";
            }

            return string.Empty;
        }

        private static BoundingBox2D? TryReadBoundingBox(string shapefilePath)
        {
            try
            {
                using var reader = Shapefile.OpenRead(shapefilePath);
                var envelope = reader.BoundingBox;
                return new BoundingBox2D(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
            }
            catch
            {
                return null;
            }
        }

        private static NtsGeometry TransformGeometry(NtsGeometry geometry, string sourceSrs, string targetSrs)
        {
            var normalizedSource = NormalizeSrs(sourceSrs);
            var normalizedTarget = NormalizeSrs(targetSrs);
            if (normalizedSource == normalizedTarget)
            {
                return geometry;
            }

            var clone = geometry.Copy();
            clone.Apply(new CoordinateSequenceTransformFilter(sourceSrs, targetSrs));
            clone.GeometryChanged();
            return clone;
        }

        private static WfsGeometry ConvertGeometry(NtsGeometry geometry)
        {
            var output = new WfsGeometry
            {
                Type = geometry.GeometryType
            };
            AppendGeometry(geometry, output);
            return output;
        }

        private static void AppendGeometry(NtsGeometry geometry, WfsGeometry output)
        {
            switch (geometry)
            {
                case Polygon polygon:
                    output.OuterRings.Add(ReadRing(polygon.ExteriorRing));
                    break;
                case MultiPolygon multiPolygon:
                    for (var index = 0; index < multiPolygon.NumGeometries; index++)
                    {
                        AppendGeometry(multiPolygon.GetGeometryN(index), output);
                    }

                    break;
                case NtsLineString lineString:
                    output.LineStrings.Add(ReadLineString(lineString));
                    break;
                case MultiLineString multiLineString:
                    for (var index = 0; index < multiLineString.NumGeometries; index++)
                    {
                        AppendGeometry(multiLineString.GetGeometryN(index), output);
                    }

                    break;
                case Point point:
                    output.Points.Add(new Coordinate2D(point.X, point.Y));
                    break;
                case MultiPoint multiPoint:
                    for (var index = 0; index < multiPoint.NumGeometries; index++)
                    {
                        AppendGeometry(multiPoint.GetGeometryN(index), output);
                    }

                    break;
                case GeometryCollection collection:
                    for (var index = 0; index < collection.NumGeometries; index++)
                    {
                        AppendGeometry(collection.GetGeometryN(index), output);
                    }

                    break;
            }
        }

        private static LinearRing ReadRing(NtsLineString ring)
        {
            return new LinearRing(ReadCoordinates(ring.Coordinates));
        }

        private static LineString ReadLineString(NtsLineString lineString)
        {
            return new LineString(ReadCoordinates(lineString.Coordinates));
        }

        private static List<Coordinate2D> ReadCoordinates(IEnumerable<Coordinate> coordinates)
        {
            return coordinates
                .Select(coordinate => new Coordinate2D(coordinate.X, coordinate.Y))
                .ToList();
        }

        private static Dictionary<string, string?> ReadAttributes(IAttributesTable attributes)
        {
            var output = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in attributes.GetNames())
            {
                output[name] = attributes[name]?.ToString();
            }

            return output;
        }

        private static bool EnvelopeIntersectsBounds(Envelope envelope, BoundingBox2D bounds)
        {
            return envelope.MinX <= bounds.MaxX &&
                   envelope.MaxX >= bounds.MinX &&
                   envelope.MinY <= bounds.MaxY &&
                   envelope.MaxY >= bounds.MinY;
        }

        private static string NormalizeSrs(string? srs)
        {
            if (string.IsNullOrWhiteSpace(srs))
            {
                return string.Empty;
            }

            if (srs.Contains("25832", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25832";
            }

            if (srs.Contains("25833", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25833";
            }

            if (srs.Contains("3857", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:3857";
            }

            if (srs.Contains("4326", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4326";
            }

            return srs.Trim().ToUpperInvariant();
        }

        private sealed class CoordinateSequenceTransformFilter : ICoordinateSequenceFilter
        {
            private readonly string _sourceSrs;
            private readonly string _targetSrs;

            public CoordinateSequenceTransformFilter(string sourceSrs, string targetSrs)
            {
                _sourceSrs = sourceSrs;
                _targetSrs = targetSrs;
            }

            public bool Done => false;

            public bool GeometryChanged => true;

            public void Filter(CoordinateSequence seq, int i)
            {
                if (!SpatialReferenceTransform.TryTransformXY(
                        _sourceSrs,
                        _targetSrs,
                        seq.GetX(i),
                        seq.GetY(i),
                        out var x,
                        out var y))
                {
                    throw new InvalidOperationException($"Could not transform Shapefile coordinates from '{_sourceSrs}' to '{_targetSrs}'.");
                }

                seq.SetX(i, x);
                seq.SetY(i, y);
            }
        }

        private static class RhinoSpatialContextToolsAdapter
        {
            public static bool TryResolveBoundingBoxForSrs(SpatialContext2D spatialContext, string requestedSrs, out BoundingBox2D boundingBox)
            {
                boundingBox = spatialContext.RequestBoundingBox;
                var normalizedRequestedSrs = NormalizeSrs(requestedSrs);
                var normalizedContextSrs = NormalizeSrs(spatialContext.ResolvedSrs);

                if (spatialContext.BoundingBoxesBySrs.TryGetValue(normalizedRequestedSrs, out var matchingBoundingBox))
                {
                    boundingBox = matchingBoundingBox;
                    return true;
                }

                if (normalizedRequestedSrs == normalizedContextSrs)
                {
                    return true;
                }

                if ((normalizedRequestedSrs == "EPSG:4326" || normalizedRequestedSrs == "EPSG:7423") &&
                    spatialContext.Wgs84BoundingBox is not null)
                {
                    boundingBox = spatialContext.Wgs84BoundingBox;
                    return true;
                }

                return false;
            }
        }
    }
}
