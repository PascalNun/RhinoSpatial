using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using RhinoSpatial.Core;

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
            bool useAbsoluteCoordinates,
            double elevationBase)
        {
            var brepTree = new GH_Structure<GH_Brep>();
            var buildingsByLayer = buildings
                .GroupBy(building => building.SourceLayerName, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), System.StringComparer.OrdinalIgnoreCase);

            var offsetX = useAbsoluteCoordinates ? 0.0 : placementOrigin.X;
            var offsetY = useAbsoluteCoordinates ? 0.0 : placementOrigin.Y;

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

                var surfaceKey = CreateSurfaceKey(polyline, VertexTolerance);
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

            var buildingCenter = GetBuildingCenter(buildingBreps);
            OrientBrepsOutward(buildingBreps, buildingCenter, tolerance);

            var cleanedBreps = JoinBuildingBreps(buildingBreps, tolerance);
            if (cleanedBreps.Count > 0)
            {
                var preservedBreps = PreserveMissingFaces(buildingBreps, cleanedBreps, tolerance);
                var finalizedBreps = FinalizeBuildingBreps(preservedBreps, tolerance);
                return finalizedBreps.Count > 0 ? finalizedBreps : preservedBreps;
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
                return IsUsableBrep(triangleBrep, tolerance)
                    ? new List<Brep> { triangleBrep! }
                    : new List<Brep>();
            }

            if (cornerPoints.Count == 4)
            {
                var quadBrep = Brep.CreateFromCornerPoints(cornerPoints[0], cornerPoints[1], cornerPoints[2], cornerPoints[3], tolerance);
                if (quadBrep is not null)
                {
                    return IsUsableBrep(quadBrep, tolerance)
                        ? new List<Brep> { quadBrep }
                        : new List<Brep>();
                }
            }

            var directBreps = TryCreatePlanarBreps(polyline, tolerance);
            if (directBreps.Count > 0)
            {
                return directBreps;
            }

            var simplifiedPolyline = SimplifyClosedPolyline(polyline, tolerance);
            if (simplifiedPolyline.Count >= 4)
            {
                var simplifiedBreps = TryCreatePlanarBreps(simplifiedPolyline, tolerance);
                if (simplifiedBreps.Count > 0)
                {
                    return simplifiedBreps;
                }
            }

            var fallbackBreps = TryCreateTriangulatedBreps(polyline, tolerance);
            if (fallbackBreps.Count > 0)
            {
                return fallbackBreps;
            }

            if (simplifiedPolyline.Count >= 4)
            {
                var simplifiedFallbackBreps = TryCreateTriangulatedBreps(simplifiedPolyline, tolerance);
                if (simplifiedFallbackBreps.Count > 0)
                {
                    return simplifiedFallbackBreps;
                }
            }

            return new List<Brep>();
        }

        private static List<Brep> TryCreatePlanarBreps(Polyline polyline, double tolerance)
        {
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

            return breps
                .Where(candidate => candidate is not null && IsUsableBrep(candidate, tolerance))
                .ToList()!;
        }

        private static List<Brep> TryCreateTriangulatedBreps(Polyline polyline, double tolerance)
        {
            var projectedPolyline = ProjectPolylineToBestFitPlane(polyline);
            if (projectedPolyline.Count < 4)
            {
                return new List<Brep>();
            }

            var boundaryCurve = projectedPolyline.ToNurbsCurve();
            var mesh = Mesh.CreateFromPlanarBoundary(boundaryCurve, MeshingParameters.FastRenderMesh, tolerance) ??
                       Mesh.CreateFromClosedPolyline(projectedPolyline);

            if (mesh is null || !mesh.IsValid || mesh.Faces.Count == 0)
            {
                return new List<Brep>();
            }

            var triangulatedBrep = Brep.CreateFromMesh(mesh, true);
            return IsUsableBrep(triangulatedBrep, tolerance)
                ? new List<Brep> { triangulatedBrep! }
                : new List<Brep>();
        }

        private static Polyline ProjectPolylineToBestFitPlane(Polyline polyline)
        {
            if (polyline.Count < 4)
            {
                return new Polyline();
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

            projected.RemoveNearlyEqualSubsequentPoints(VertexTolerance);
            projected.DeleteShortSegments(VertexTolerance);

            if (projected.Count > 0 && !projected[0].EpsilonEquals(projected[^1], VertexTolerance))
            {
                projected.Add(projected[0]);
            }

            return projected;
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
                    currentBreps = JoinBrepsPairwise(currentBreps, joinTolerance);
                    continue;
                }

                currentBreps = joinedBreps
                    .Where(candidate => candidate is not null && candidate.IsValid)
                    .ToList()!;

                currentBreps = JoinBrepsPairwise(currentBreps, joinTolerance);

                foreach (var brep in currentBreps)
                {
                    TryMergeCoplanarFaces(brep, joinTolerance);
                    brep.Compact();
                }

                currentBreps = currentBreps
                    .Where(candidate => IsUsableBrep(candidate, joinTolerance))
                    .ToList();
            }

            return currentBreps;
        }

        private static List<Brep> PreserveMissingFaces(List<Brep> sourceFaces, List<Brep> joinedBreps, double tolerance)
        {
            var result = joinedBreps
                .Where(candidate => candidate is not null && candidate.IsValid)
                .ToList();

            foreach (var sourceFace in sourceFaces)
            {
                if (!IsUsableBrep(sourceFace, tolerance))
                {
                    continue;
                }

                if (IsFaceRepresented(sourceFace, result, tolerance))
                {
                    continue;
                }

                result.Add(sourceFace.DuplicateBrep());
            }

            return result
                .Where(candidate => IsUsableBrep(candidate, tolerance))
                .ToList();
        }

        private static List<Brep> FinalizeBuildingBreps(List<Brep> breps, double tolerance)
        {
            if (breps.Count == 0)
            {
                return new List<Brep>();
            }

            var joinedBreps = JoinBuildingBreps(breps, tolerance);
            var working = joinedBreps.Count > 0 ? joinedBreps : breps
                .Where(candidate => candidate is not null && candidate.IsValid)
                .Select(candidate => candidate.DuplicateBrep())
                .ToList();

            var finalized = new List<Brep>();
            foreach (var brep in working)
            {
                if (brep is null || !brep.IsValid)
                {
                    continue;
                }

                var cappedBrep = TryRepairBuildingShell(brep, tolerance);

                if (IsUsableBrep(cappedBrep, tolerance))
                {
                    finalized.Add(cappedBrep);
                }
            }

            return finalized;
        }

        private static bool IsFaceRepresented(Brep sourceFace, IEnumerable<Brep> candidates, double tolerance)
        {
            if (!TryGetRepresentativeNormal(sourceFace, out var sourceCentroid, out var sourceNormal))
            {
                return false;
            }

            foreach (var candidate in candidates)
            {
                if (candidate is null || !candidate.IsValid)
                {
                    continue;
                }

                foreach (var face in candidate.Faces)
                {
                    if (!face.ClosestPoint(sourceCentroid, out var u, out var v))
                    {
                        continue;
                    }

                    var pointOnFace = face.PointAt(u, v);
                    if (pointOnFace.DistanceTo(sourceCentroid) > System.Math.Max(tolerance * 2.0, VertexTolerance * 10.0))
                    {
                        continue;
                    }

                    var pointRelation = face.IsPointOnFace(u, v, System.Math.Max(tolerance, VertexTolerance));
                    if (pointRelation == PointFaceRelation.Exterior)
                    {
                        continue;
                    }

                    var faceNormal = face.NormalAt(u, v);
                    if (face.OrientationIsReversed)
                    {
                        faceNormal.Reverse();
                    }

                    if (faceNormal.IsTiny())
                    {
                        continue;
                    }

                    faceNormal.Unitize();
                    if (System.Math.Abs(faceNormal * sourceNormal) >= 0.95)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static Brep TryRepairBuildingShell(Brep brep, double tolerance)
        {
            var working = brep.DuplicateBrep();

            TryJoinNakedEdges(working, tolerance);
            working = TryCapPlanarHoles(working, tolerance);
            TryJoinNakedEdges(working, tolerance);

            try
            {
                working.Repair(tolerance);
            }
            catch
            {
                // Keep the best valid shell we have so far.
            }

            TryMergeCoplanarFaces(working, tolerance);

            if (working.IsSolid && working.SolidOrientation == BrepSolidOrientation.Inward)
            {
                working.Flip();
            }

            working.Compact();
            return working;
        }

        private static Brep TryCapPlanarHoles(Brep brep, double tolerance)
        {
            try
            {
                var capped = brep.CapPlanarHoles(tolerance);
                return capped is not null && capped.IsValid ? capped : brep;
            }
            catch
            {
                return brep;
            }
        }

        private static void TryJoinNakedEdges(Brep brep, double tolerance)
        {
            try
            {
                brep.JoinNakedEdges(tolerance);
            }
            catch
            {
                // Not all open shells can be joined further.
            }
        }

        private static Point3d GetBuildingCenter(IEnumerable<Brep> breps)
        {
            var hasBounds = false;
            var buildingBounds = BoundingBox.Unset;

            foreach (var brep in breps)
            {
                if (brep is null || !brep.IsValid)
                {
                    continue;
                }

                var brepBounds = brep.GetBoundingBox(true);
                if (!brepBounds.IsValid)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    buildingBounds = brepBounds;
                    hasBounds = true;
                }
                else
                {
                    buildingBounds.Union(brepBounds);
                }
            }

            return hasBounds ? buildingBounds.Center : Point3d.Unset;
        }

        private static void OrientBrepsOutward(IEnumerable<Brep> breps, Point3d buildingCenter, double tolerance)
        {
            if (!buildingCenter.IsValid)
            {
                return;
            }

            foreach (var brep in breps)
            {
                OrientBrepOutward(brep, buildingCenter, tolerance);
            }
        }

        private static void OrientBrepOutward(Brep? brep, Point3d buildingCenter, double tolerance)
        {
            if (brep is null || !brep.IsValid || brep.Faces.Count == 0)
            {
                return;
            }

            if (!TryGetRepresentativeNormal(brep, out var faceCentroid, out var faceNormal))
            {
                return;
            }

            var outwardDirection = faceCentroid - buildingCenter;
            if (outwardDirection.IsTiny(tolerance))
            {
                return;
            }

            if (faceNormal * outwardDirection < 0.0)
            {
                brep.Flip();
            }
        }

        private static bool TryGetRepresentativeNormal(Brep brep, out Point3d faceCentroid, out Vector3d faceNormal)
        {
            faceCentroid = Point3d.Unset;
            faceNormal = Vector3d.Unset;

            foreach (var face in brep.Faces)
            {
                var properties = AreaMassProperties.Compute(face);
                faceCentroid = properties?.Centroid ?? face.GetBoundingBox(true).Center;
                if (!faceCentroid.IsValid)
                {
                    continue;
                }

                if (!face.ClosestPoint(faceCentroid, out var u, out var v))
                {
                    u = face.Domain(0).Mid;
                    v = face.Domain(1).Mid;
                }

                faceNormal = face.NormalAt(u, v);
                if (face.OrientationIsReversed)
                {
                    faceNormal.Reverse();
                }

                if (faceNormal.IsTiny())
                {
                    continue;
                }

                faceNormal.Unitize();
                return true;
            }

            return false;
        }

        private static List<Brep> JoinBrepsPairwise(List<Brep> breps, double tolerance)
        {
            var working = breps
                .Where(candidate => candidate is not null && candidate.IsValid)
                .Select(candidate => candidate.DuplicateBrep())
                .ToList();

            var changed = true;
            while (changed)
            {
                changed = false;

                for (var leftIndex = 0; leftIndex < working.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < working.Count; rightIndex++)
                    {
                        var joined = Brep.JoinBreps(new[] { working[leftIndex], working[rightIndex] }, tolerance);
                        if (joined is null || joined.Length != 1 || !joined[0].IsValid)
                        {
                            continue;
                        }

                        TryMergeCoplanarFaces(joined[0], tolerance);
                        joined[0].Compact();

                        working[leftIndex] = joined[0];
                        working.RemoveAt(rightIndex);
                        changed = true;
                        goto RestartJoinScan;
                    }
                }

RestartJoinScan:
                ;
            }

            return working
                .Where(candidate => IsUsableBrep(candidate, tolerance))
                .ToList();
        }

        internal static double CalculateElevationBase(IReadOnlyList<Lod2Building> buildings)
        {
            return ResolveElevationBase(buildings);
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

        private static bool IsUsableBrep(Brep? brep, double tolerance)
        {
            if (brep is null || !brep.IsValid)
            {
                return false;
            }

            var boundingBox = brep.GetBoundingBox(true);
            if (!boundingBox.IsValid || boundingBox.Diagonal.Length <= tolerance)
            {
                return false;
            }

            var areaProperties = AreaMassProperties.Compute(brep);
            return areaProperties is not null && areaProperties.Area > tolerance * tolerance;
        }

        private static string CreateSurfaceKey(Polyline polyline, double tolerance)
        {
            var cornerPoints = GetCornerPoints(polyline);
            if (cornerPoints.Count == 0)
            {
                return string.Empty;
            }

            var tokens = cornerPoints
                .Select(point => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Quantize(point.X, tolerance)}|{Quantize(point.Y, tolerance)}|{Quantize(point.Z, tolerance)}"))
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
