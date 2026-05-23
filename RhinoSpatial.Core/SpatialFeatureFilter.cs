using System;
using System.Collections.Generic;
using System.Linq;

namespace RhinoSpatial.Core
{
    public record SpatialFeatureFilterResult(
        List<WfsFeature> Features,
        int OversizedEnclosingFeatureCount,
        int OutsideContextCount);

    public static class SpatialFeatureFilter
    {
        private const double DefaultOversizedEnclosingFeatureRatio = 5.0;
        private const double BoundsToleranceRatio = 1e-9;

        public static SpatialFeatureFilterResult FilterToBounds(
            IReadOnlyList<WfsFeature> features,
            BoundingBox2D contextBounds,
            double oversizedEnclosingFeatureRatio = DefaultOversizedEnclosingFeatureRatio)
        {
            var filteredFeatures = new List<WfsFeature>(features.Count);
            var oversizedEnclosingFeatureCount = 0;
            var outsideContextCount = 0;
            var contextWidth = Math.Max(1e-9, contextBounds.MaxX - contextBounds.MinX);
            var contextHeight = Math.Max(1e-9, contextBounds.MaxY - contextBounds.MinY);
            var tolerance = Math.Max(contextWidth, contextHeight) * BoundsToleranceRatio;

            foreach (var feature in features)
            {
                if (!TryGetFeatureBounds(feature, out var featureBounds) ||
                    !DoBoundingBoxesIntersect(featureBounds, contextBounds))
                {
                    outsideContextCount++;
                    continue;
                }

                if (FeatureTouchesContext(feature, contextBounds, tolerance))
                {
                    filteredFeatures.Add(feature);
                    continue;
                }

                var featureWidth = Math.Max(0.0, featureBounds.MaxX - featureBounds.MinX);
                var featureHeight = Math.Max(0.0, featureBounds.MaxY - featureBounds.MinY);
                var isOversizedEnclosingFeature =
                    featureWidth > contextWidth * oversizedEnclosingFeatureRatio ||
                    featureHeight > contextHeight * oversizedEnclosingFeatureRatio;

                if (isOversizedEnclosingFeature)
                {
                    oversizedEnclosingFeatureCount++;
                    continue;
                }

                filteredFeatures.Add(feature);
            }

            return new SpatialFeatureFilterResult(
                filteredFeatures,
                oversizedEnclosingFeatureCount,
                outsideContextCount);
        }

        public static bool TryGetFeatureBounds(WfsFeature feature, out BoundingBox2D bounds)
        {
            double? minX = null;
            double? minY = null;
            double? maxX = null;
            double? maxY = null;

            foreach (var ring in feature.Geometry.OuterRings)
            {
                AccumulatePointBounds(ring.Points, ref minX, ref minY, ref maxX, ref maxY);
            }

            foreach (var lineString in feature.Geometry.LineStrings)
            {
                AccumulatePointBounds(lineString.Points, ref minX, ref minY, ref maxX, ref maxY);
            }

            AccumulatePointBounds(feature.Geometry.Points, ref minX, ref minY, ref maxX, ref maxY);

            if (!minX.HasValue || !minY.HasValue || !maxX.HasValue || !maxY.HasValue)
            {
                bounds = new BoundingBox2D(0.0, 0.0, 0.0, 0.0);
                return false;
            }

            bounds = new BoundingBox2D(minX.Value, minY.Value, maxX.Value, maxY.Value);
            return true;
        }

        private static bool FeatureTouchesContext(WfsFeature feature, BoundingBox2D contextBounds, double tolerance)
        {
            foreach (var ring in feature.Geometry.OuterRings)
            {
                if (PointsTouchBounds(ring.Points, contextBounds, tolerance, closePolyline: true))
                {
                    return true;
                }
            }

            foreach (var lineString in feature.Geometry.LineStrings)
            {
                if (PointsTouchBounds(lineString.Points, contextBounds, tolerance, closePolyline: false))
                {
                    return true;
                }
            }

            return feature.Geometry.Points.Any(point => IsPointInsideBounds(point, contextBounds, tolerance));
        }

        private static bool PointsTouchBounds(
            IReadOnlyList<Coordinate2D> points,
            BoundingBox2D contextBounds,
            double tolerance,
            bool closePolyline)
        {
            if (points.Count == 0)
            {
                return false;
            }

            if (points.Any(point => IsPointInsideBounds(point, contextBounds, tolerance)))
            {
                return true;
            }

            var segmentCount = closePolyline ? points.Count : points.Count - 1;
            for (var pointIndex = 0; pointIndex < segmentCount; pointIndex++)
            {
                var start = points[pointIndex];
                var end = points[(pointIndex + 1) % points.Count];
                if (SegmentTouchesBounds(start, end, contextBounds, tolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentTouchesBounds(Coordinate2D start, Coordinate2D end, BoundingBox2D bounds, double tolerance)
        {
            if (IsPointInsideBounds(start, bounds, tolerance) ||
                IsPointInsideBounds(end, bounds, tolerance))
            {
                return true;
            }

            var minX = Math.Min(start.X, end.X);
            var maxX = Math.Max(start.X, end.X);
            var minY = Math.Min(start.Y, end.Y);
            var maxY = Math.Max(start.Y, end.Y);
            if (maxX < bounds.MinX - tolerance ||
                minX > bounds.MaxX + tolerance ||
                maxY < bounds.MinY - tolerance ||
                minY > bounds.MaxY + tolerance)
            {
                return false;
            }

            return SegmentIntersectsSegment(start, end, new Coordinate2D(bounds.MinX, bounds.MinY), new Coordinate2D(bounds.MaxX, bounds.MinY), tolerance) ||
                   SegmentIntersectsSegment(start, end, new Coordinate2D(bounds.MaxX, bounds.MinY), new Coordinate2D(bounds.MaxX, bounds.MaxY), tolerance) ||
                   SegmentIntersectsSegment(start, end, new Coordinate2D(bounds.MaxX, bounds.MaxY), new Coordinate2D(bounds.MinX, bounds.MaxY), tolerance) ||
                   SegmentIntersectsSegment(start, end, new Coordinate2D(bounds.MinX, bounds.MaxY), new Coordinate2D(bounds.MinX, bounds.MinY), tolerance);
        }

        private static bool SegmentIntersectsSegment(
            Coordinate2D a,
            Coordinate2D b,
            Coordinate2D c,
            Coordinate2D d,
            double tolerance)
        {
            var orientation1 = Orientation(a, b, c);
            var orientation2 = Orientation(a, b, d);
            var orientation3 = Orientation(c, d, a);
            var orientation4 = Orientation(c, d, b);

            if (Math.Abs(orientation1) <= tolerance && IsPointOnSegment(c, a, b, tolerance))
            {
                return true;
            }

            if (Math.Abs(orientation2) <= tolerance && IsPointOnSegment(d, a, b, tolerance))
            {
                return true;
            }

            if (Math.Abs(orientation3) <= tolerance && IsPointOnSegment(a, c, d, tolerance))
            {
                return true;
            }

            if (Math.Abs(orientation4) <= tolerance && IsPointOnSegment(b, c, d, tolerance))
            {
                return true;
            }

            return (orientation1 > 0.0) != (orientation2 > 0.0) &&
                   (orientation3 > 0.0) != (orientation4 > 0.0);
        }

        private static double Orientation(Coordinate2D a, Coordinate2D b, Coordinate2D c)
        {
            return ((b.X - a.X) * (c.Y - a.Y)) -
                   ((b.Y - a.Y) * (c.X - a.X));
        }

        private static bool IsPointOnSegment(Coordinate2D point, Coordinate2D start, Coordinate2D end, double tolerance)
        {
            return point.X >= Math.Min(start.X, end.X) - tolerance &&
                   point.X <= Math.Max(start.X, end.X) + tolerance &&
                   point.Y >= Math.Min(start.Y, end.Y) - tolerance &&
                   point.Y <= Math.Max(start.Y, end.Y) + tolerance;
        }

        private static bool IsPointInsideBounds(Coordinate2D point, BoundingBox2D bounds, double tolerance)
        {
            return point.X >= bounds.MinX - tolerance &&
                   point.X <= bounds.MaxX + tolerance &&
                   point.Y >= bounds.MinY - tolerance &&
                   point.Y <= bounds.MaxY + tolerance;
        }

        private static bool DoBoundingBoxesIntersect(BoundingBox2D left, BoundingBox2D right)
        {
            return left.MinX <= right.MaxX &&
                   left.MaxX >= right.MinX &&
                   left.MinY <= right.MaxY &&
                   left.MaxY >= right.MinY;
        }

        private static void AccumulatePointBounds(
            IEnumerable<Coordinate2D> points,
            ref double? minX,
            ref double? minY,
            ref double? maxX,
            ref double? maxY)
        {
            foreach (var point in points)
            {
                minX = !minX.HasValue || point.X < minX.Value ? point.X : minX;
                minY = !minY.HasValue || point.Y < minY.Value ? point.Y : minY;
                maxX = !maxX.HasValue || point.X > maxX.Value ? point.X : maxX;
                maxY = !maxY.HasValue || point.Y > maxY.Value ? point.Y : maxY;
            }
        }
    }
}
