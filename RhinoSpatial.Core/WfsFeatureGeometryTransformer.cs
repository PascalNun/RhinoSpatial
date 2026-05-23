using System.Collections.Generic;
using System.Linq;

namespace RhinoSpatial.Core
{
    public static class WfsFeatureGeometryTransformer
    {
        public static List<WfsFeature> TransformFeatures(
            IReadOnlyList<WfsFeature> features,
            string sourceSrs,
            string targetSrs)
        {
            if (NormalizeSrs(sourceSrs) == NormalizeSrs(targetSrs))
            {
                return features.ToList();
            }

            var transformedFeatures = new List<WfsFeature>(features.Count);
            foreach (var feature in features)
            {
                transformedFeatures.Add(feature with
                {
                    Geometry = TransformGeometry(feature.Geometry, sourceSrs, targetSrs)
                });
            }

            return transformedFeatures;
        }

        private static WfsGeometry TransformGeometry(WfsGeometry geometry, string sourceSrs, string targetSrs)
        {
            return new WfsGeometry
            {
                Type = geometry.Type,
                OuterRings = geometry.OuterRings
                    .Select(ring => new LinearRing(TransformPoints(ring.Points, sourceSrs, targetSrs)))
                    .ToList(),
                LineStrings = geometry.LineStrings
                    .Select(lineString => new LineString(TransformPoints(lineString.Points, sourceSrs, targetSrs)))
                    .ToList(),
                Points = TransformPoints(geometry.Points, sourceSrs, targetSrs)
            };
        }

        private static List<Coordinate2D> TransformPoints(
            IEnumerable<Coordinate2D> points,
            string sourceSrs,
            string targetSrs)
        {
            var transformedPoints = new List<Coordinate2D>();
            foreach (var point in points)
            {
                if (!SpatialReferenceTransform.TryTransformXY(
                        sourceSrs,
                        targetSrs,
                        point.X,
                        point.Y,
                        out var x,
                        out var y))
                {
                    continue;
                }

                transformedPoints.Add(new Coordinate2D(x, y));
            }

            return transformedPoints;
        }

        private static string NormalizeSrs(string? srs)
        {
            return string.IsNullOrWhiteSpace(srs)
                ? string.Empty
                : srs.Trim().ToUpperInvariant();
        }
    }
}
