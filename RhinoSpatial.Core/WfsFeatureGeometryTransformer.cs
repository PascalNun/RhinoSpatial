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
            if (!SpatialReferenceTransform.TryCreateXYTransform(sourceSrs, targetSrs, out var transform))
                throw new InvalidOperationException($"Could not transform vector coordinates from '{sourceSrs}' to '{targetSrs}': unsupported CRS.");
            if (SpatialReferenceTransform.NormalizeSrs(sourceSrs) == SpatialReferenceTransform.NormalizeSrs(targetSrs))
            {
                foreach (var feature in features)
                {
                    var points = feature.Geometry.OuterRings.SelectMany(r => r.Points)
                        .Concat(feature.Geometry.LineStrings.SelectMany(l => l.Points)).Concat(feature.Geometry.Points);
                    if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
                        throw new InvalidOperationException("Could not transform vector coordinates: invalid coordinate.");
                }
                return features.ToList();
            }

            var transformedFeatures = new List<WfsFeature>(features.Count);
            foreach (var feature in features)
            {
                transformedFeatures.Add(feature with
                {
                    Geometry = TransformGeometry(feature.Geometry, transform, sourceSrs, targetSrs)
                });
            }

            return transformedFeatures;
        }

        private static WfsGeometry TransformGeometry(WfsGeometry geometry, SpatialReferenceTransform.XYTransform transform, string sourceSrs, string targetSrs)
        {
            return new WfsGeometry
            {
                Type = geometry.Type,
                OuterRings = geometry.OuterRings
                    .Select(ring => new LinearRing(TransformPoints(ring.Points, transform, sourceSrs, targetSrs)))
                    .ToList(),
                LineStrings = geometry.LineStrings
                    .Select(lineString => new LineString(TransformPoints(lineString.Points, transform, sourceSrs, targetSrs)))
                    .ToList(),
                Points = TransformPoints(geometry.Points, transform, sourceSrs, targetSrs)
            };
        }

        private static List<Coordinate2D> TransformPoints(
            IReadOnlyList<Coordinate2D> points,
            SpatialReferenceTransform.XYTransform transform,
            string sourceSrs,
            string targetSrs)
        {
            var transformedPoints = new List<Coordinate2D>(points.Count);
            foreach (var point in points)
            {
                if (!transform(
                        point.X,
                        point.Y,
                        out var x,
                        out var y))
                {
                    throw new InvalidOperationException($"Could not transform vector coordinates from '{sourceSrs}' to '{targetSrs}': invalid coordinate.");
                }

                transformedPoints.Add(new Coordinate2D(x, y));
            }

            return transformedPoints;
        }
    }
}
