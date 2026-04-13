using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using WfsCore;

namespace RhinoSpatial
{
    internal static class RhinoSpatialLod2OutputBuilder
    {
        private const double VertexTolerance = 0.001;

        public static GH_Structure<GH_Brep> BuildBrepTree(
            IReadOnlyList<Lod2Building> buildings,
            IReadOnlyList<string> layerOrder,
            string sourceSrs,
            string targetSrs,
            BoundingBox2D sourceBoundingBox,
            BoundingBox2D targetBoundingBox,
            Point3d placementOrigin,
            bool useAbsoluteCoordinates)
        {
            var brepTree = new GH_Structure<GH_Brep>();
            var buildingsByLayer = buildings
                .GroupBy(building => building.SourceLayerName, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), System.StringComparer.OrdinalIgnoreCase);

            var offsetX = useAbsoluteCoordinates ? 0.0 : placementOrigin.X;
            var offsetY = useAbsoluteCoordinates ? 0.0 : placementOrigin.Y;
            var elevationBase = useAbsoluteCoordinates ? 0.0 : ResolveElevationBase(buildings);

            for (int layerIndex = 0; layerIndex < layerOrder.Count; layerIndex++)
            {
                if (!buildingsByLayer.TryGetValue(layerOrder[layerIndex], out var layerBuildings))
                {
                    continue;
                }

                for (int buildingIndex = 0; buildingIndex < layerBuildings.Count; buildingIndex++)
                {
                    var buildingBreps = BuildBuildingBreps(layerBuildings[buildingIndex], sourceSrs, targetSrs, sourceBoundingBox, targetBoundingBox, offsetX, offsetY, elevationBase);
                    if (buildingBreps.Count == 0)
                    {
                        continue;
                    }

                    var path = new GH_Path(layerIndex, buildingIndex);
                    foreach (var brep in buildingBreps.Where(candidate => candidate is not null && candidate.IsValid))
                    {
                        brepTree.Append(new GH_Brep(brep), path);
                    }
                }
            }

            return brepTree;
        }

        private static List<Brep> BuildBuildingBreps(
            Lod2Building building,
            string sourceSrs,
            string targetSrs,
            BoundingBox2D sourceBoundingBox,
            BoundingBox2D targetBoundingBox,
            double offsetX,
            double offsetY,
            double elevationBase)
        {
            var buildingBreps = new List<Brep>();
            var surfaceKeys = new HashSet<string>(System.StringComparer.Ordinal);
            var tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01;
            var snapTolerance = System.Math.Max(VertexTolerance, tolerance);
            var snappedPoints = new Dictionary<string, Point3d>(System.StringComparer.Ordinal);

            foreach (var surface in building.Surfaces)
            {
                if (!TryCreateTransformedClosedPolyline(
                        surface,
                        sourceSrs,
                        targetSrs,
                        sourceBoundingBox,
                        targetBoundingBox,
                        offsetX,
                        offsetY,
                        elevationBase,
                        snappedPoints,
                        snapTolerance,
                        out var polyline))
                {
                    continue;
                }

                var surfaceKey = CreateSurfaceKey(polyline);
                if (!surfaceKeys.Add(surfaceKey))
                {
                    continue;
                }

                var surfaceBreps = CreateSurfaceBrepsFromPolyline(polyline, tolerance);
                if (surfaceBreps.Count == 0)
                {
                    continue;
                }

                buildingBreps.AddRange(surfaceBreps);
            }

            if (buildingBreps.Count == 0)
            {
                return new List<Brep>();
            }

            var cleanedBreps = JoinBuildingBreps(buildingBreps, tolerance);
            if (cleanedBreps.Count > 0)
            {
                return cleanedBreps;
            }

            return buildingBreps;
        }

        private static bool TryCreateTransformedClosedPolyline(
            SurfaceRing3D surface,
            string sourceSrs,
            string targetSrs,
            BoundingBox2D sourceBoundingBox,
            BoundingBox2D targetBoundingBox,
            double offsetX,
            double offsetY,
            double elevationBase,
            Dictionary<string, Point3d> snappedPoints,
            double snapTolerance,
            out Polyline polyline)
        {
            polyline = new Polyline();

            if (surface.Points.Count < 4)
            {
                return false;
            }

            polyline = new Polyline(surface.Points.Count);

            foreach (var point in surface.Points)
            {
                var mappedPoint = TransformPoint(point, sourceSrs, targetSrs, sourceBoundingBox, targetBoundingBox);
                var candidatePoint = new Point3d(mappedPoint.X - offsetX, mappedPoint.Y - offsetY, mappedPoint.Z - elevationBase);
                polyline.Add(GetSnappedPoint(candidatePoint, snappedPoints, snapTolerance));
            }

            polyline.RemoveNearlyEqualSubsequentPoints(VertexTolerance);
            polyline.DeleteShortSegments(VertexTolerance);
            polyline = SimplifyClosedPolyline(polyline, snapTolerance);

            if (polyline.Count < 4)
            {
                return false;
            }

            if (!polyline[0].EpsilonEquals(polyline[^1], VertexTolerance))
            {
                polyline.Add(polyline[0]);
            }

            if (!polyline.IsClosed || polyline.Count < 4)
            {
                return false;
            }

            return true;
        }

        private static Polyline SimplifyClosedPolyline(Polyline polyline, double tolerance)
        {
            var simplified = polyline.ToList();
            if (simplified.Count >= 2 && simplified[0].EpsilonEquals(simplified[^1], tolerance))
            {
                simplified.RemoveAt(simplified.Count - 1);
            }

            var changed = true;
            while (changed && simplified.Count >= 3)
            {
                changed = false;

                for (int index = 0; index < simplified.Count; index++)
                {
                    var previous = simplified[(index - 1 + simplified.Count) % simplified.Count];
                    var current = simplified[index];
                    var next = simplified[(index + 1) % simplified.Count];

                    if (current.DistanceTo(previous) <= tolerance || current.DistanceTo(next) <= tolerance)
                    {
                        simplified.RemoveAt(index);
                        changed = true;
                        break;
                    }

                    if (previous.DistanceTo(next) <= tolerance)
                    {
                        simplified.RemoveAt(index);
                        changed = true;
                        break;
                    }

                    var connector = new Line(previous, next);
                    if (connector.IsValid && connector.DistanceTo(current, true) <= tolerance)
                    {
                        simplified.RemoveAt(index);
                        changed = true;
                        break;
                    }
                }
            }

            var result = new Polyline(simplified);
            if (result.Count > 0 && !result[0].EpsilonEquals(result[^1], tolerance))
            {
                result.Add(result[0]);
            }

            return result;
        }

        private static Point3d GetSnappedPoint(Point3d point, Dictionary<string, Point3d> snappedPoints, double tolerance)
        {
            var baseX = Quantize(point.X, tolerance);
            var baseY = Quantize(point.Y, tolerance);
            var baseZ = Quantize(point.Z, tolerance);

            for (var deltaX = -1; deltaX <= 1; deltaX++)
            {
                for (var deltaY = -1; deltaY <= 1; deltaY++)
                {
                    for (var deltaZ = -1; deltaZ <= 1; deltaZ++)
                    {
                        var neighborKey = CreatePointKey(baseX + deltaX, baseY + deltaY, baseZ + deltaZ);
                        if (snappedPoints.TryGetValue(neighborKey, out var existingPoint) &&
                            existingPoint.DistanceTo(point) <= tolerance)
                        {
                            return existingPoint;
                        }
                    }
                }
            }

            var key = CreatePointKey(baseX, baseY, baseZ);
            snappedPoints[key] = point;
            return point;
        }

        private static List<Brep> CreateSurfaceBrepsFromPolyline(Polyline polyline, double tolerance)
        {
            if (polyline.Count < 4)
            {
                return new List<Brep>();
            }

            var cornerPoints = GetCornerPoints(polyline);

            if (cornerPoints.Count == 3)
            {
                var triangleBrep = Brep.CreateFromCornerPoints(cornerPoints[0], cornerPoints[1], cornerPoints[2], tolerance);
                return triangleBrep is null ? new List<Brep>() : new List<Brep> { triangleBrep };
            }

            if (cornerPoints.Count == 4)
            {
                var quadBrep = Brep.CreateFromCornerPoints(cornerPoints[0], cornerPoints[1], cornerPoints[2], cornerPoints[3], tolerance);
                if (quadBrep is not null)
                {
                    return new List<Brep> { quadBrep };
                }
            }

            var fitResult = Plane.FitPlaneToPoints(polyline, out var fittedPlane);
            if (fitResult != PlaneFitResult.Success)
            {
                fittedPlane = new Plane(polyline[0], polyline[1], polyline[2]);
            }

            var projected = new Polyline(polyline.Count);
            foreach (var point in polyline)
            {
                projected.Add(fittedPlane.ClosestPoint(point));
            }

            var curve = projected.ToNurbsCurve();
            var breps = Brep.CreatePlanarBreps(curve, tolerance);
            if (breps is null || breps.Length == 0)
            {
                return new List<Brep>();
            }

            return breps.Where(candidate => candidate is not null).ToList()!;
        }

        private static Coordinate3D TransformPoint(
            Coordinate3D point,
            string sourceSrs,
            string targetSrs,
            BoundingBox2D sourceBoundingBox,
            BoundingBox2D targetBoundingBox)
        {
            if (SpatialReferenceTransform.TryTransformXY(sourceSrs, targetSrs, point.X, point.Y, out var transformedX, out var transformedY))
            {
                return new Coordinate3D(transformedX, transformedY, point.Z);
            }

            var sourceSpanX = sourceBoundingBox.MaxX - sourceBoundingBox.MinX;
            var sourceSpanY = sourceBoundingBox.MaxY - sourceBoundingBox.MinY;

            if (System.Math.Abs(sourceSpanX) < 1e-12 || System.Math.Abs(sourceSpanY) < 1e-12)
            {
                return point;
            }

            var normalizedX = (point.X - sourceBoundingBox.MinX) / sourceSpanX;
            var normalizedY = (point.Y - sourceBoundingBox.MinY) / sourceSpanY;

            var mappedX = targetBoundingBox.MinX + normalizedX * (targetBoundingBox.MaxX - targetBoundingBox.MinX);
            var mappedY = targetBoundingBox.MinY + normalizedY * (targetBoundingBox.MaxY - targetBoundingBox.MinY);

            return new Coordinate3D(mappedX, mappedY, point.Z);
        }

        private static List<Point3d> GetCornerPoints(Polyline polyline)
        {
            var uniquePoints = polyline.ToList();
            if (uniquePoints.Count >= 2 && uniquePoints[0].EpsilonEquals(uniquePoints[^1], VertexTolerance))
            {
                uniquePoints.RemoveAt(uniquePoints.Count - 1);
            }

            return uniquePoints;
        }

        private static List<Brep> JoinBuildingBreps(List<Brep> buildingBreps, double tolerance)
        {
            var currentBreps = buildingBreps
                .Where(candidate => candidate is not null && candidate.IsValid)
                .Select(candidate => candidate.DuplicateBrep())
                .ToList();

            foreach (var joinTolerance in new[]
                     {
                         tolerance,
                         tolerance * 2.0,
                         tolerance * 5.0,
                         tolerance * 10.0,
                         System.Math.Max(tolerance * 25.0, 0.01),
                         System.Math.Max(tolerance * 50.0, 0.05)
                     })
            {
                var joinedBreps = Brep.JoinBreps(currentBreps, joinTolerance);
                if (joinedBreps is null || joinedBreps.Length == 0)
                {
                    continue;
                }

                currentBreps = joinedBreps
                    .Where(candidate => candidate is not null && candidate.IsValid)
                    .ToList()!;

                foreach (var brep in currentBreps)
                {
                    TryMergeCoplanarFaces(brep, joinTolerance);
                    brep.Compact();
                }
            }

            return currentBreps;
        }

        private static void TryMergeCoplanarFaces(Brep brep, double tolerance)
        {
            try
            {
                brep.MergeCoplanarFaces(tolerance);
            }
            catch
            {
                // Some Breps still cannot be simplified further. Keep the valid result.
            }
        }

        private static double ResolveElevationBase(IReadOnlyList<Lod2Building> buildings)
        {
            var minZ = double.PositiveInfinity;

            foreach (var point in buildings
                         .SelectMany(building => building.Surfaces)
                         .SelectMany(surface => surface.Points))
            {
                if (point.Z < minZ)
                {
                    minZ = point.Z;
                }
            }

            return double.IsInfinity(minZ) ? 0.0 : minZ;
        }

        private static string CreateSurfaceKey(Polyline polyline)
        {
            var cornerPoints = GetCornerPoints(polyline);
            if (cornerPoints.Count == 0)
            {
                return string.Empty;
            }

            var tokens = cornerPoints
                .Select(point => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Quantize(point.X, VertexTolerance)}|{Quantize(point.Y, VertexTolerance)}|{Quantize(point.Z, VertexTolerance)}"))
                .ToList();

            var forwardKey = CreateCanonicalCyclicKey(tokens);
            tokens.Reverse();
            var reverseKey = CreateCanonicalCyclicKey(tokens);

            return string.CompareOrdinal(forwardKey, reverseKey) <= 0 ? forwardKey : reverseKey;
        }

        private static string CreateCanonicalCyclicKey(List<string> tokens)
        {
            if (tokens.Count == 0)
            {
                return string.Empty;
            }

            string? best = null;

            for (int startIndex = 0; startIndex < tokens.Count; startIndex++)
            {
                var rotated = new string[tokens.Count];

                for (int offset = 0; offset < tokens.Count; offset++)
                {
                    rotated[offset] = tokens[(startIndex + offset) % tokens.Count];
                }

                var candidate = string.Join(";", rotated);
                if (best is null || string.CompareOrdinal(candidate, best) < 0)
                {
                    best = candidate;
                }
            }

            return best ?? string.Empty;
        }

        private static string CreatePointKey(long quantizedX, long quantizedY, long quantizedZ)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{quantizedX}|{quantizedY}|{quantizedZ}");
        }

        private static long Quantize(double value, double tolerance)
        {
            return (long)System.Math.Round(value / tolerance);
        }
    }
}
