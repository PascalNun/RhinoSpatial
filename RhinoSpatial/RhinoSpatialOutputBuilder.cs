using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using WfsCore;

namespace RhinoSpatial
{
    internal static class RhinoSpatialOutputBuilder
    {
        private const double VertexTolerance = 0.001;
        private const double AreaTolerance = 1e-9;

        public static Point3d CalculateLocalizingOffset(IReadOnlyList<WfsFeature> features)
        {
            double? minX = null;
            double? minY = null;

            foreach (var feature in features)
            {
                foreach (var ring in feature.Geometry.OuterRings)
                {
                    UpdateExtents(ring.Points, ref minX, ref minY);
                }

                foreach (var lineString in feature.Geometry.LineStrings)
                {
                    UpdateExtents(lineString.Points, ref minX, ref minY);
                }

                UpdateExtents(feature.Geometry.Points, ref minX, ref minY);
            }

            return new Point3d(minX ?? 0.0, minY ?? 0.0, 0.0);
        }

        public static GH_Structure<IGH_GeometricGoo> BuildGeometryTree(IReadOnlyList<WfsFeature> features, IReadOnlyList<string> layerOrder, double offsetX, double offsetY)
        {
            var geometryTree = new GH_Structure<IGH_GeometricGoo>();
            var featuresByLayer = features
                .GroupBy(feature => feature.SourceLayerName, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), System.StringComparer.OrdinalIgnoreCase);

            for (int layerIndex = 0; layerIndex < layerOrder.Count; layerIndex++)
            {
                var layerName = layerOrder[layerIndex];

                if (!featuresByLayer.TryGetValue(layerName, out var layerFeatures))
                {
                    continue;
                }

                for (int featureIndex = 0; featureIndex < layerFeatures.Count; featureIndex++)
                {
                    var feature = layerFeatures[featureIndex];
                    var path = new GH_Path(layerIndex, featureIndex);
                    geometryTree.EnsurePath(path);

                    foreach (var ring in feature.Geometry.OuterRings)
                    {
                        var curve = TryCreatePolylineCurve(ring.Points, closePolyline: true, offsetX, offsetY);

                        if (curve is null)
                        {
                            continue;
                        }

                        geometryTree.Append(new GH_Curve(curve), path);
                    }

                    foreach (var lineString in feature.Geometry.LineStrings)
                    {
                        var curve = TryCreatePolylineCurve(lineString.Points, closePolyline: false, offsetX, offsetY);

                        if (curve is null)
                        {
                            continue;
                        }

                        geometryTree.Append(new GH_Curve(curve), path);
                    }

                    foreach (var point in feature.Geometry.Points)
                    {
                        geometryTree.Append(new GH_Point(new Point3d(point.X - offsetX, point.Y - offsetY, 0.0)), path);
                    }
                }
            }

            return geometryTree;
        }

        private static void UpdateExtents(IReadOnlyList<Coordinate2D> points, ref double? minX, ref double? minY)
        {
            foreach (var point in points)
            {
                minX = !minX.HasValue || point.X < minX.Value ? point.X : minX;
                minY = !minY.HasValue || point.Y < minY.Value ? point.Y : minY;
            }
        }

        private static PolylineCurve? TryCreatePolylineCurve(IReadOnlyList<Coordinate2D> sourcePoints, bool closePolyline, double offsetX, double offsetY)
        {
            if (sourcePoints.Count < (closePolyline ? 3 : 2))
            {
                return null;
            }

            var polyline = new Polyline(sourcePoints.Count + (closePolyline ? 1 : 0));

            foreach (var point in sourcePoints)
            {
                polyline.Add(new Point3d(point.X - offsetX, point.Y - offsetY, 0.0));
            }

            polyline.RemoveNearlyEqualSubsequentPoints(VertexTolerance);
            polyline.DeleteShortSegments(VertexTolerance);

            if (polyline.Count < (closePolyline ? 3 : 2))
            {
                return null;
            }

            if (closePolyline)
            {
                var firstPoint = polyline[0];
                var lastPoint = polyline[polyline.Count - 1];

                if (firstPoint.DistanceTo(lastPoint) <= VertexTolerance)
                {
                    polyline[polyline.Count - 1] = firstPoint;
                }
                else
                {
                    polyline.Add(firstPoint);
                }

                if (polyline.Count < 4 || !polyline.IsClosed || !polyline.IsValid || !HasNonZeroArea(polyline))
                {
                    return null;
                }
            }

            return polyline.ToPolylineCurve();
        }

        private static bool HasNonZeroArea(Polyline polyline)
        {
            if (polyline.Count < 4)
            {
                return false;
            }

            var pointCount = polyline.IsClosed ? polyline.Count - 1 : polyline.Count;

            if (pointCount < 3)
            {
                return false;
            }

            double twiceArea = 0.0;

            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var currentPoint = polyline[pointIndex];
                var nextPoint = polyline[(pointIndex + 1) % pointCount];
                twiceArea += currentPoint.X * nextPoint.Y - nextPoint.X * currentPoint.Y;
            }

            return System.Math.Abs(twiceArea) > AreaTolerance;
        }
    }
}
