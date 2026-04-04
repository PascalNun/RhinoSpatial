using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using WfsCore;

namespace RhinoWFS
{
    internal static class RhinoWFSOutputBuilder
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
                    foreach (var point in ring.Points)
                    {
                        minX = !minX.HasValue || point.X < minX.Value ? point.X : minX;
                        minY = !minY.HasValue || point.Y < minY.Value ? point.Y : minY;
                    }
                }
            }

            return new Point3d(minX ?? 0.0, minY ?? 0.0, 0.0);
        }

        public static GH_Structure<GH_Curve> BuildGeometryTree(IReadOnlyList<WfsFeature> features, IReadOnlyList<string> layerOrder, double offsetX, double offsetY)
        {
            var geometryTree = new GH_Structure<GH_Curve>();
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
                        var curve = TryCreatePolylineCurve(ring, offsetX, offsetY);

                        if (curve is null)
                        {
                            continue;
                        }

                        geometryTree.Append(new GH_Curve(curve), path);
                    }
                }
            }

            return geometryTree;
        }
        private static PolylineCurve? TryCreatePolylineCurve(LinearRing ring, double offsetX, double offsetY)
        {
            if (ring.Points.Count < 3)
            {
                return null;
            }

            var polyline = new Polyline(ring.Points.Count + 1);

            foreach (var point in ring.Points)
            {
                polyline.Add(new Point3d(point.X - offsetX, point.Y - offsetY, 0.0));
            }

            polyline.RemoveNearlyEqualSubsequentPoints(VertexTolerance);
            polyline.DeleteShortSegments(VertexTolerance);

            if (polyline.Count < 3)
            {
                return null;
            }

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
