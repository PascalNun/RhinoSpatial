using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Union;
using Rhino;
using Rhino.Geometry;
using RhinoSpatial.Core;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsGeometryCollection = NetTopologySuite.Geometries.GeometryCollection;
using NtsLineString = NetTopologySuite.Geometries.LineString;
using NtsLinearRing = NetTopologySuite.Geometries.LinearRing;
using NtsMultiPolygon = NetTopologySuite.Geometries.MultiPolygon;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;

namespace RhinoSpatial
{
    internal static class RhinoSpatialOsmOutputBuilder
    {
        private const double VertexTolerance = 0.001;
        private const double DefaultBuildingHeight = 4.0;
        private const double DefaultRoadLaneWidth = 3.25;

        public static GH_Structure<GH_Brep> BuildBuildingTree(IReadOnlyList<OsmAreaFeature> buildings, SpatialContext2D spatialContext)
        {
            var tree = new GH_Structure<GH_Brep>();
            var tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01;

            for (var featureIndex = 0; featureIndex < buildings.Count; featureIndex++)
            {
                var building = buildings[featureIndex];
                var path = new GH_Path(featureIndex);
                tree.EnsurePath(path);

                var minHeight = ResolveBuildingMinHeight(building.Tags);
                var totalHeight = ResolveBuildingHeight(building.Tags);
                var extrusionHeight = Math.Max(tolerance, totalHeight - minHeight);

                foreach (var ring in building.OuterRings)
                {
                    if (!TryTransformRing(ring.Points, spatialContext, out var polyline, out var baseZ))
                    {
                        continue;
                    }

                    var basePlaneZ = baseZ + minHeight;
                    var footprintCurve = CreatePolylineCurveAtZ(polyline, basePlaneZ);
                    if (footprintCurve is null)
                    {
                        continue;
                    }

                    var planarBreps = Brep.CreatePlanarBreps(footprintCurve, tolerance)?
                        .Where(brep => brep is not null && brep.IsValid)
                        .ToList() ?? new List<Brep>();
                    if (planarBreps.Count == 0)
                    {
                        continue;
                    }

                    if (extrusionHeight > tolerance)
                    {
                        var extrusionSurface = Surface.CreateExtrusion(footprintCurve, new Vector3d(0.0, 0.0, extrusionHeight));
                        if (extrusionSurface is not null)
                        {
                            var brep = extrusionSurface.ToBrep();
                            var cappedBrep = brep?.CapPlanarHoles(tolerance) ?? brep;

                            if (cappedBrep is not null && cappedBrep.IsValid)
                            {
                                tree.Append(new GH_Brep(cappedBrep), path);
                                continue;
                            }
                        }
                    }

                    foreach (var footprint in planarBreps)
                    {
                        if (footprint.IsValid)
                        {
                            tree.Append(new GH_Brep(footprint), path);
                        }
                    }
                }
            }

            return tree;
        }

        public static GH_Structure<IGH_GeometricGoo> BuildRoadTree(IReadOnlyList<OsmLinearFeature> roads, SpatialContext2D spatialContext)
        {
            var tree = new GH_Structure<IGH_GeometricGoo>();
            var tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01;
            var planZ = ResolveSharedLinearPlanElevation(roads, spatialContext);
            var roadRegions = new List<NtsGeometry>();
            var collectedWidths = new List<double>();

            for (var featureIndex = 0; featureIndex < roads.Count; featureIndex++)
            {
                var road = roads[featureIndex];
                if (!TryTransformLineAtZ(road.CenterLine.Points, spatialContext, planZ, out var centerLine))
                {
                    continue;
                }

                var width = ResolveRoadWidth(road.Tags);
                if (TryCreateBufferedLineRegion(centerLine, width, tolerance, out var roadRegion))
                {
                    roadRegions.Add(roadRegion);
                    collectedWidths.Add(width);
                }
            }

            if (roadRegions.Count == 0)
            {
                return tree;
            }

            var representativeWidth = ResolveRepresentativeRoadWidth(collectedWidths, tolerance);
            var mergedGeometry = UnaryUnionOp.Union(roadRegions);
            mergedGeometry = CleanMergedRoadGeometry(mergedGeometry, representativeWidth, tolerance);
            mergedGeometry = SmoothMergedRoadGeometry(
                mergedGeometry,
                representativeWidth,
                tolerance);
            if (TryAppendRoadGeometry(tree, mergedGeometry, planZ, tolerance))
            {
                return tree;
            }

            for (var roadIndex = 0; roadIndex < roadRegions.Count; roadIndex++)
            {
                TryAppendRoadGeometry(tree, roadRegions[roadIndex], planZ, tolerance, roadIndex);
            }

            return tree;
        }

        public static GH_Structure<IGH_GeometricGoo> BuildWaterTree(
            IReadOnlyList<OsmAreaFeature> waterAreas,
            SpatialContext2D spatialContext)
        {
            var tree = new GH_Structure<IGH_GeometricGoo>();
            var tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01;
            var planZ = ResolveSharedWaterPlanElevation(waterAreas, spatialContext);
            var waterRegions = new List<NtsGeometry>();

            for (var areaIndex = 0; areaIndex < waterAreas.Count; areaIndex++)
            {
                var area = waterAreas[areaIndex];
                foreach (var ring in area.OuterRings)
                {
                    if (!TryTransformRing(ring.Points, spatialContext, out var polyline, out _))
                    {
                        continue;
                    }

                    if (TryCreatePolygonRegion(polyline, out var polygonRegion))
                    {
                        waterRegions.Add(polygonRegion);
                    }
                }
            }

            if (waterRegions.Count == 0)
            {
                return tree;
            }

            var mergedGeometry = UnaryUnionOp.Union(waterRegions);
            if (TryAppendRoadGeometry(tree, mergedGeometry, planZ, tolerance))
            {
                return tree;
            }

            for (var regionIndex = 0; regionIndex < waterRegions.Count; regionIndex++)
            {
                TryAppendRoadGeometry(tree, waterRegions[regionIndex], planZ, tolerance, regionIndex);
            }

            return tree;
        }

        public static GH_Structure<IGH_GeometricGoo> BuildGreenTree(IReadOnlyList<OsmAreaFeature> greenAreas, SpatialContext2D spatialContext)
        {
            return BuildAreaTree(greenAreas, spatialContext);
        }

        public static GH_Structure<IGH_GeometricGoo> BuildRailTree(IReadOnlyList<OsmLinearFeature> rails, SpatialContext2D spatialContext)
        {
            var tree = new GH_Structure<IGH_GeometricGoo>();

            for (var featureIndex = 0; featureIndex < rails.Count; featureIndex++)
            {
                var rail = rails[featureIndex];
                var path = new GH_Path(featureIndex);
                tree.EnsurePath(path);

                if (!TryTransformLine(rail.CenterLine.Points, spatialContext, useTerrainAverage: false, out var centerLine))
                {
                    continue;
                }

                tree.Append(new GH_Curve(new PolylineCurve(centerLine)), path);
            }

            return tree;
        }

        private static GH_Structure<IGH_GeometricGoo> BuildAreaTree(IReadOnlyList<OsmAreaFeature> areas, SpatialContext2D spatialContext)
        {
            var tree = new GH_Structure<IGH_GeometricGoo>();
            var tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01;

            for (var featureIndex = 0; featureIndex < areas.Count; featureIndex++)
            {
                var area = areas[featureIndex];
                var path = new GH_Path(featureIndex);
                tree.EnsurePath(path);

                foreach (var ring in area.OuterRings)
                {
                    if (!TryTransformRing(ring.Points, spatialContext, out var polyline, out var baseZ))
                    {
                        continue;
                    }

                    var planarBreps = CreatePlanarBreps(polyline, baseZ, tolerance);
                    foreach (var brep in planarBreps.Where(candidate => candidate is not null && candidate.IsValid))
                    {
                        tree.Append(new GH_Brep(brep), path);
                    }
                }
            }

            return tree;
        }

        private static GH_Structure<IGH_GeometricGoo> BuildRibbonTree(
            IReadOnlyList<OsmLinearFeature> features,
            SpatialContext2D spatialContext,
            Func<IReadOnlyDictionary<string, string?>, double> widthResolver)
        {
            var tree = new GH_Structure<IGH_GeometricGoo>();
            AppendRibbonFeatures(tree, features, spatialContext, widthResolver, 0);
            return tree;
        }

        private static void AppendRibbonFeatures(
            GH_Structure<IGH_GeometricGoo> tree,
            IReadOnlyList<OsmLinearFeature> features,
            SpatialContext2D spatialContext,
            Func<IReadOnlyDictionary<string, string?>, double> widthResolver,
            int pathOffset)
        {
            var tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01;

            for (var featureIndex = 0; featureIndex < features.Count; featureIndex++)
            {
                var feature = features[featureIndex];
                var path = new GH_Path(pathOffset + featureIndex);
                tree.EnsurePath(path);

                if (!TryTransformLine(feature.CenterLine.Points, spatialContext, useTerrainAverage: true, out var centerLine))
                {
                    continue;
                }

                var width = widthResolver(feature.Tags);
                if (TryCreateRibbonBrep(centerLine, width, tolerance, out var ribbonBrep))
                {
                    tree.Append(new GH_Brep(ribbonBrep), path);
                    continue;
                }

                var curve = centerLine.ToPolylineCurve();
                if (curve is not null)
                {
                    tree.Append(new GH_Curve(curve), path);
                }
            }
        }

        private static bool TryTransformRing(
            IReadOnlyList<Coordinate2D> sourcePoints,
            SpatialContext2D spatialContext,
            out Polyline polyline,
            out double baseZ)
        {
            baseZ = RhinoSpatialContextTools.ResolveAveragePlacedElevation(spatialContext, sourcePoints);
            return RhinoSpatialContextTools.TryTransformPolyline(
                sourcePoints,
                spatialContext,
                "EPSG:4326",
                baseZ,
                closePolyline: true,
                out polyline);
        }

        private static bool TryTransformLine(
            IReadOnlyList<Coordinate2D> sourcePoints,
            SpatialContext2D spatialContext,
            bool useTerrainAverage,
            out Polyline polyline)
        {
            var baseZ = useTerrainAverage
                ? RhinoSpatialContextTools.ResolveAveragePlacedElevation(spatialContext, sourcePoints)
                : 0.0;

            return RhinoSpatialContextTools.TryTransformPolyline(
                sourcePoints,
                spatialContext,
                "EPSG:4326",
                baseZ,
                closePolyline: false,
                out polyline);
        }

        private static bool TryTransformLineAtZ(
            IReadOnlyList<Coordinate2D> sourcePoints,
            SpatialContext2D spatialContext,
            double z,
            out Polyline polyline)
        {
            return RhinoSpatialContextTools.TryTransformPolyline(
                sourcePoints,
                spatialContext,
                "EPSG:4326",
                z,
                closePolyline: false,
                out polyline);
        }

        private static double ResolveSharedLinearPlanElevation(IReadOnlyList<OsmLinearFeature> features, SpatialContext2D spatialContext)
        {
            if (!spatialContext.UseAbsoluteCoordinates)
            {
                return 0.0;
            }

            var sourcePoints = features
                .SelectMany(feature => feature.CenterLine.Points)
                .ToList();

            if (sourcePoints.Count == 0)
            {
                return 0.0;
            }

            return RhinoSpatialContextTools.ResolveAveragePlacedElevation(spatialContext, sourcePoints);
        }

        private static double ResolveSharedWaterPlanElevation(
            IReadOnlyList<OsmAreaFeature> waterAreas,
            SpatialContext2D spatialContext)
        {
            if (!spatialContext.UseAbsoluteCoordinates)
            {
                return 0.0;
            }

            var sourcePoints = waterAreas
                .SelectMany(area => area.OuterRings)
                .SelectMany(ring => ring.Points)
                .ToList();

            if (sourcePoints.Count == 0)
            {
                return 0.0;
            }

            return RhinoSpatialContextTools.ResolveAveragePlacedElevation(spatialContext, sourcePoints);
        }

        private static List<Brep> CreatePlanarBreps(Polyline polyline, double z, double tolerance)
        {
            var curve = CreatePolylineCurveAtZ(polyline, z);
            if (curve is null)
            {
                return new List<Brep>();
            }

            var planarBreps = Brep.CreatePlanarBreps(curve, tolerance);
            return planarBreps?.Where(brep => brep is not null && brep.IsValid).ToList() ?? new List<Brep>();
        }

        private static List<Brep> CreatePlanarBreps(Curve curve, double tolerance)
        {
            var planarBreps = Brep.CreatePlanarBreps(curve, tolerance);
            return planarBreps?.Where(brep => brep is not null && brep.IsValid).ToList() ?? new List<Brep>();
        }

        private static PolylineCurve? CreatePolylineCurveAtZ(Polyline polyline, double z)
        {
            var translatedPolyline = new Polyline(polyline.Select(point => new Point3d(point.X, point.Y, z)));
            return translatedPolyline.ToPolylineCurve();
        }

        private static bool TryCreateRibbonBrep(Polyline centerLine, double width, double tolerance, out Brep ribbonBrep)
        {
            ribbonBrep = null!;

            if (!TryCreateRibbonOutlineCurve(centerLine, width, out var outlineCurve))
            {
                return false;
            }

            var planarBreps = Brep.CreatePlanarBreps(outlineCurve, tolerance);
            var candidate = planarBreps?.FirstOrDefault(brep => brep is not null && brep.IsValid);
            if (candidate is null)
            {
                return false;
            }

            ribbonBrep = candidate;
            return true;
        }

        private static bool TryCreateRibbonOutlineCurve(Polyline centerLine, double width, out Curve outlineCurve)
        {
            outlineCurve = null!;

            if (centerLine.Count < 2 || width <= VertexTolerance)
            {
                return false;
            }

            var preparedCenterLine = ExtendOpenPolyline(centerLine, ResolveRoadOverlapDistance(width));
            var preparedCurve = preparedCenterLine.ToPolylineCurve();
            if (preparedCurve is not { IsValid: true })
            {
                return TryCreateRibbonOutlineCurveFallback(centerLine, width, out outlineCurve);
            }

            var halfWidth = width * 0.5;
            var leftOffset = SelectBestOffsetCurve(
                preparedCurve.Offset(Plane.WorldXY, halfWidth, VertexTolerance, CurveOffsetCornerStyle.Round));
            var rightOffset = SelectBestOffsetCurve(
                preparedCurve.Offset(Plane.WorldXY, -halfWidth, VertexTolerance, CurveOffsetCornerStyle.Round));

            if (leftOffset is null || rightOffset is null)
            {
                return TryCreateRibbonOutlineCurveFallback(centerLine, width, out outlineCurve);
            }

            var reversedRightOffset = rightOffset.DuplicateCurve();
            reversedRightOffset.Reverse();

            var joinedCurves = Curve.JoinCurves(
                new Curve[]
                {
                    leftOffset,
                    new LineCurve(leftOffset.PointAtEnd, reversedRightOffset.PointAtStart),
                    reversedRightOffset,
                    new LineCurve(reversedRightOffset.PointAtEnd, leftOffset.PointAtStart)
                },
                VertexTolerance);

            var closedOutline = joinedCurves
                .Where(curve => curve is not null && curve.IsClosed && curve.IsValid)
                .OrderByDescending(GetCurveLengthSafe)
                .FirstOrDefault();

            if (closedOutline is not null)
            {
                outlineCurve = closedOutline;
                return true;
            }

            return TryCreateRibbonOutlineCurveFallback(centerLine, width, out outlineCurve);
        }

        private static bool TryCreateBufferedLineRegion(Polyline centerLine, double width, double tolerance, out NtsGeometry roadRegion)
        {
            roadRegion = null!;

            if (!TryCreateLineString(centerLine, out var lineString))
            {
                return false;
            }

            var bufferParameters = new BufferParameters
            {
                EndCapStyle = EndCapStyle.Flat,
                JoinStyle = JoinStyle.Round,
                QuadrantSegments = 6,
                MitreLimit = 2.0
            };

            var mergeAllowance = ResolveRoadMergeAllowance(width, tolerance);
            var candidate = BufferOp.Buffer(lineString, (width * 0.5) + mergeAllowance, bufferParameters);
            if (candidate is null || candidate.IsEmpty)
            {
                return false;
            }

            roadRegion = candidate;
            return true;
        }

        private static bool TryCreatePolygonRegion(Polyline polyline, out NtsGeometry polygonRegion)
        {
            polygonRegion = null!;

            if (polyline.Count < 4)
            {
                return false;
            }

            var coordinates = polyline
                .Select(point => new Coordinate(point.X, point.Y))
                .ToArray();

            if (coordinates.Length < 4)
            {
                return false;
            }

            if (!coordinates[0].Equals2D(coordinates[^1]))
            {
                Array.Resize(ref coordinates, coordinates.Length + 1);
                coordinates[^1] = new Coordinate(coordinates[0]);
            }

            var shell = new NtsLinearRing(coordinates);
            if (!shell.IsValid || shell.IsEmpty)
            {
                return false;
            }

            var polygon = new NtsPolygon(shell);
            if (!polygon.IsValid || polygon.IsEmpty)
            {
                return false;
            }

            polygonRegion = polygon;
            return true;
        }

        private static NtsGeometry CleanMergedRoadGeometry(NtsGeometry geometry, double representativeWidth, double tolerance)
        {
            if (geometry is null || geometry.IsEmpty)
            {
                return geometry!;
            }

            var cleanupDistance = ResolveRoadCleanupDistance(representativeWidth, tolerance);
            if (cleanupDistance <= tolerance)
            {
                return geometry;
            }

            var bufferParameters = new BufferParameters
            {
                EndCapStyle = EndCapStyle.Round,
                JoinStyle = JoinStyle.Round,
                QuadrantSegments = 6,
                MitreLimit = 2.0
            };

            var expanded = BufferOp.Buffer(geometry, cleanupDistance, bufferParameters);
            if (expanded is null || expanded.IsEmpty)
            {
                return geometry;
            }

            var contracted = BufferOp.Buffer(expanded, -cleanupDistance, bufferParameters);
            return contracted is null || contracted.IsEmpty
                ? geometry
                : contracted;
        }

        private static NtsGeometry SmoothMergedRoadGeometry(NtsGeometry geometry, double representativeWidth, double tolerance)
        {
            if (geometry is null || geometry.IsEmpty)
            {
                return geometry!;
            }

            var smoothingRadius = ResolveRoadSmoothingDistance(representativeWidth, tolerance);
            if (smoothingRadius <= tolerance)
            {
                return geometry;
            }

            var bufferParameters = new BufferParameters
            {
                EndCapStyle = EndCapStyle.Round,
                JoinStyle = JoinStyle.Round,
                QuadrantSegments = 12,
                MitreLimit = 2.0
            };

            var eroded = BufferOp.Buffer(geometry, -smoothingRadius, bufferParameters);
            if (eroded is null || eroded.IsEmpty)
            {
                return geometry;
            }

            var restored = BufferOp.Buffer(eroded, smoothingRadius, bufferParameters);
            return restored is null || restored.IsEmpty
                ? geometry
                : restored;
        }

        private static bool TryCreateRibbonOutlineCurveFallback(Polyline centerLine, double width, out Curve outlineCurve)
        {
            outlineCurve = null!;

            var halfWidth = width * 0.5;
            var left = new List<Point3d>(centerLine.Count);
            var right = new List<Point3d>(centerLine.Count);

            for (var index = 0; index < centerLine.Count; index++)
            {
                var point = centerLine[index];
                var tangent = ResolveTangent(centerLine, index);
                if (!tangent.Unitize())
                {
                    return false;
                }

                var perpendicular = new Vector3d(-tangent.Y, tangent.X, 0.0);
                if (!perpendicular.Unitize())
                {
                    return false;
                }

                left.Add(point + perpendicular * halfWidth);
                right.Add(point - perpendicular * halfWidth);
            }

            var outline = new Polyline(left);
            for (var index = right.Count - 1; index >= 0; index--)
            {
                outline.Add(right[index]);
            }

            outline.RemoveNearlyEqualSubsequentPoints(VertexTolerance);
            outline.DeleteShortSegments(VertexTolerance);

            if (outline.Count < 4)
            {
                return false;
            }

            if (!outline[0].EpsilonEquals(outline[^1], VertexTolerance))
            {
                outline.Add(outline[0]);
            }

            if (!outline.IsClosed)
            {
                return false;
            }

            outlineCurve = outline.ToPolylineCurve();
            return true;
        }

        private static bool TryCreateLineString(Polyline polyline, out NtsLineString lineString)
        {
            lineString = null!;

            if (polyline.Count < 2)
            {
                return false;
            }

            var coordinates = polyline
                .Select(point => new Coordinate(point.X, point.Y))
                .ToArray();

            if (coordinates.Length < 2)
            {
                return false;
            }

            lineString = new NtsLineString(coordinates);
            return lineString.IsValid && !lineString.IsEmpty;
        }

        private static bool TryAppendRoadGeometry(
            GH_Structure<IGH_GeometricGoo> tree,
            NtsGeometry geometry,
            double z,
            double tolerance,
            int pathOffset = 0)
        {
            if (geometry is null || geometry.IsEmpty)
            {
                return false;
            }

            var appendedAny = false;

            switch (geometry)
            {
                case NtsPolygon polygon:
                    appendedAny |= TryAppendRoadPolygon(tree, polygon, z, tolerance, pathOffset);
                    break;
                case NtsMultiPolygon multiPolygon:
                    for (var index = 0; index < multiPolygon.NumGeometries; index++)
                    {
                        if (multiPolygon.GetGeometryN(index) is NtsPolygon childPolygon)
                        {
                            appendedAny |= TryAppendRoadPolygon(tree, childPolygon, z, tolerance, pathOffset + index);
                        }
                    }
                    break;
                case NtsGeometryCollection geometryCollection:
                    for (var index = 0; index < geometryCollection.NumGeometries; index++)
                    {
                        appendedAny |= TryAppendRoadGeometry(
                            tree,
                            geometryCollection.GetGeometryN(index),
                            z,
                            tolerance,
                            pathOffset + index);
                    }
                    break;
            }

            return appendedAny;
        }

        private static bool TryAppendRoadPolygon(
            GH_Structure<IGH_GeometricGoo> tree,
            NtsPolygon polygon,
            double z,
            double tolerance,
            int pathIndex)
        {
            if (!TryCreateCurveFromLineString(polygon.ExteriorRing, z, out var exteriorCurve))
            {
                return false;
            }

            var boundaryCurves = new List<Curve> { exteriorCurve };
            for (var holeIndex = 0; holeIndex < polygon.NumInteriorRings; holeIndex++)
            {
                if (TryCreateCurveFromLineString(polygon.GetInteriorRingN(holeIndex), z, out var holeCurve))
                {
                    boundaryCurves.Add(holeCurve);
                }
            }

            var planarBreps = Brep.CreatePlanarBreps(boundaryCurves, tolerance);
            if (planarBreps is not { Length: > 0 })
            {
                return false;
            }

            var path = new GH_Path(pathIndex);
            tree.EnsurePath(path);

            foreach (var brep in planarBreps.Where(candidate => candidate is not null && candidate.IsValid))
            {
                tree.Append(new GH_Brep(brep), path);
            }

            return tree.get_Branch(path).Count > 0;
        }

        private static bool TryCreateCurveFromLineString(NtsLineString lineString, double z, out Curve curve)
        {
            curve = null!;

            var coordinates = lineString.Coordinates;
            if (coordinates.Length < 4)
            {
                return false;
            }

            var polyline = new Polyline(
                coordinates.Select(coordinate => new Point3d(coordinate.X, coordinate.Y, z)));

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

            curve = polyline.ToPolylineCurve();
            return curve is not null && curve.IsClosed && curve.IsValid;
        }

        private static Polyline ExtendOpenPolyline(Polyline polyline, double distance)
        {
            if (polyline.Count < 2 || distance <= VertexTolerance)
            {
                return new Polyline(polyline);
            }

            var extendedPoints = polyline.ToList();
            var startDirection = extendedPoints[0] - extendedPoints[1];
            if (startDirection.Unitize())
            {
                extendedPoints[0] += startDirection * distance;
            }

            var endIndex = extendedPoints.Count - 1;
            var endDirection = extendedPoints[endIndex] - extendedPoints[endIndex - 1];
            if (endDirection.Unitize())
            {
                extendedPoints[endIndex] += endDirection * distance;
            }

            var extendedPolyline = new Polyline(extendedPoints);
            extendedPolyline.RemoveNearlyEqualSubsequentPoints(VertexTolerance);
            extendedPolyline.DeleteShortSegments(VertexTolerance);
            return extendedPolyline;
        }

        private static Curve? SelectBestOffsetCurve(IEnumerable<Curve>? candidateCurves)
        {
            if (candidateCurves is null)
            {
                return null;
            }

            return candidateCurves
                .Where(curve => curve is not null && curve.IsValid)
                .OrderByDescending(GetCurveLengthSafe)
                .FirstOrDefault();
        }

        private static double ResolveRoadOverlapDistance(double width)
        {
            return Math.Max(width * 0.75, VertexTolerance * 20.0);
        }

        private static double GetCurveLengthSafe(Curve curve)
        {
            return curve.GetLength();
        }

        private static double ResolveRoadMergeAllowance(double width, double tolerance)
        {
            return Math.Max(tolerance * 2.0, width * 0.08);
        }

        private static double ResolveRoadCleanupDistance(double representativeWidth, double tolerance)
        {
            var unclampedDistance = representativeWidth * 0.18;
            return Math.Max(0.5, Math.Max(tolerance * 4.0, Math.Min(unclampedDistance, 4.0)));
        }

        private static double ResolveRepresentativeRoadWidth(IReadOnlyList<double> widths, double tolerance)
        {
            if (widths.Count == 0)
            {
                return Math.Max(6.0, tolerance * 10.0);
            }

            var sortedWidths = widths
                .Where(width => width > tolerance)
                .OrderBy(width => width)
                .ToArray();

            if (sortedWidths.Length == 0)
            {
                return Math.Max(6.0, tolerance * 10.0);
            }

            return sortedWidths[sortedWidths.Length / 2];
        }

        private static double ResolveRoadSmoothingDistance(double representativeWidth, double tolerance)
        {
            var unclampedDistance = representativeWidth * 0.35;
            return Math.Max(2.0, Math.Max(tolerance * 4.0, Math.Min(unclampedDistance, 10.0)));
        }

        private static Vector3d ResolveTangent(Polyline polyline, int index)
        {
            if (polyline.Count < 2)
            {
                return Vector3d.Unset;
            }

            if (index <= 0)
            {
                return polyline[1] - polyline[0];
            }

            if (index >= polyline.Count - 1)
            {
                return polyline[^1] - polyline[^2];
            }

            var previous = polyline[index] - polyline[index - 1];
            var next = polyline[index + 1] - polyline[index];

            if (!previous.Unitize())
            {
                return next;
            }

            if (!next.Unitize())
            {
                return previous;
            }

            var tangent = previous + next;
            if (tangent.IsTiny())
            {
                tangent = next;
            }

            return tangent;
        }

        private static double ResolveBuildingHeight(IReadOnlyDictionary<string, string?> tags)
        {
            if (TryGetTagLength(tags, "height", out var heightMeters))
            {
                return Math.Max(heightMeters, DefaultBuildingHeight);
            }

            if (TryGetTagLength(tags, "building:height", out var buildingHeightMeters))
            {
                return Math.Max(buildingHeightMeters, DefaultBuildingHeight);
            }

            if (TryGetTagDouble(tags, "building:levels", out var levels))
            {
                return Math.Max(levels * 3.0, DefaultBuildingHeight);
            }

            return DefaultBuildingHeight;
        }

        private static double ResolveBuildingMinHeight(IReadOnlyDictionary<string, string?> tags)
        {
            if (TryGetTagLength(tags, "min_height", out var minHeightMeters))
            {
                return Math.Max(0.0, minHeightMeters);
            }

            if (TryGetTagDouble(tags, "min_level", out var genericMinLevels))
            {
                return Math.Max(0.0, genericMinLevels * 3.0);
            }

            return 0.0;
        }

        private static double ResolveRoadWidth(IReadOnlyDictionary<string, string?> tags)
        {
            if (TryGetTagLength(tags, "width", out var widthMeters))
            {
                return Math.Max(widthMeters, 4.0);
            }

            if (TryGetTagDouble(tags, "lanes", out var lanes))
            {
                return Math.Max(lanes * DefaultRoadLaneWidth, 6.0);
            }

            if (!tags.TryGetValue("highway", out var highwayType) || string.IsNullOrWhiteSpace(highwayType))
            {
                return 8.0;
            }

            return highwayType switch
            {
                "motorway" => 18.0,
                "trunk" => 16.0,
                "primary" => 14.0,
                "secondary" => 12.0,
                "tertiary" => 10.0,
                "motorway_link" => 10.0,
                "trunk_link" => 9.0,
                "primary_link" => 8.0,
                "secondary_link" => 7.0,
                "tertiary_link" => 6.0,
                "residential" => 7.0,
                "service" => 5.0,
                "unclassified" => 6.5,
                _ => 8.0
            };
        }

        private static double ResolveRailWidth(IReadOnlyDictionary<string, string?> tags)
        {
            if (TryGetTagLength(tags, "width", out var widthMeters))
            {
                return Math.Max(widthMeters, 3.0);
            }

            if (!tags.TryGetValue("railway", out var railwayType) || string.IsNullOrWhiteSpace(railwayType))
            {
                return 4.0;
            }

            return railwayType switch
            {
                "rail" => 5.0,
                "light_rail" => 4.0,
                "tram" => 3.5,
                "subway" => 4.5,
                "narrow_gauge" => 3.5,
                _ => 4.0
            };
        }

        private static bool TryGetTagLength(IReadOnlyDictionary<string, string?> tags, string key, out double meters)
        {
            meters = 0.0;

            if (!tags.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant();

            if (normalized.Contains('\''))
            {
                var feetMatch = Regex.Match(normalized, @"(?<feet>\d+(\.\d+)?)\s*'");
                if (feetMatch.Success &&
                    double.TryParse(feetMatch.Groups["feet"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var feet))
                {
                    meters = feet * 0.3048;
                    return true;
                }
            }

            var match = Regex.Match(normalized, @"-?\d+([.,]\d+)?");
            if (!match.Success)
            {
                return false;
            }

            var numericText = match.Value.Replace(',', '.');
            if (!double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                return false;
            }

            if (normalized.Contains("ft"))
            {
                meters = parsedValue * 0.3048;
                return true;
            }

            meters = parsedValue;
            return true;
        }

        private static bool TryGetTagDouble(IReadOnlyDictionary<string, string?> tags, string key, out double value)
        {
            value = 0.0;

            if (!tags.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            return double.TryParse(rawValue.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
