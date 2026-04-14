using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using RhinoSpatial.Core;

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
            var angleTolerance = RhinoDoc.ActiveDoc?.ModelAngleToleranceRadians ?? RhinoMath.ToRadians(1.0);
            var planZ = ResolveSharedLinearPlanElevation(roads, spatialContext);
            var outlineCurves = new List<Curve>();

            for (var featureIndex = 0; featureIndex < roads.Count; featureIndex++)
            {
                var road = roads[featureIndex];
                if (!TryTransformLineAtZ(road.CenterLine.Points, spatialContext, planZ, out var centerLine))
                {
                    continue;
                }

                var width = ResolveRoadWidth(road.Tags);
                if (TryCreateRibbonOutlineCurve(centerLine, width, out var outlineCurve))
                {
                    outlineCurves.Add(
                        TryFilletRoadOutlineCurve(outlineCurve, width, tolerance, angleTolerance) ?? outlineCurve);
                }
            }

            if (outlineCurves.Count == 0)
            {
                return tree;
            }

            var mergedCurves = Curve.CreateBooleanUnion(outlineCurves, tolerance);
            var resultCurves = mergedCurves is { Length: > 0 }
                ? mergedCurves
                : outlineCurves.ToArray();

            for (var curveIndex = 0; curveIndex < resultCurves.Length; curveIndex++)
            {
                var path = new GH_Path(curveIndex);
                tree.EnsurePath(path);

                var planarBreps = CreatePlanarBreps(resultCurves[curveIndex], tolerance);
                if (planarBreps.Count > 0)
                {
                    foreach (var brep in planarBreps)
                    {
                        tree.Append(new GH_Brep(brep), path);
                    }

                    continue;
                }

                tree.Append(new GH_Curve(resultCurves[curveIndex]), path);
            }

            return tree;
        }

        public static GH_Structure<IGH_GeometricGoo> BuildWaterTree(
            IReadOnlyList<OsmAreaFeature> waterAreas,
            IReadOnlyList<OsmLinearFeature> waterLines,
            SpatialContext2D spatialContext)
        {
            var tree = BuildAreaTree(waterAreas, spatialContext);
            AppendRibbonFeatures(tree, waterLines, spatialContext, ResolveWaterWidth, waterAreas.Count);
            return tree;
        }

        public static GH_Structure<IGH_GeometricGoo> BuildGreenTree(IReadOnlyList<OsmAreaFeature> greenAreas, SpatialContext2D spatialContext)
        {
            return BuildAreaTree(greenAreas, spatialContext);
        }

        public static GH_Structure<IGH_GeometricGoo> BuildRailTree(IReadOnlyList<OsmLinearFeature> rails, SpatialContext2D spatialContext)
        {
            return BuildRibbonTree(rails, spatialContext, ResolveRailWidth);
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
            polyline = new Polyline();
            baseZ = 0.0;

            if (sourcePoints.Count < 4)
            {
                return false;
            }

            baseZ = ResolveAverageElevation(sourcePoints, spatialContext);
            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;
            var transformedPoints = new List<Point3d>(sourcePoints.Count);

            foreach (var sourcePoint in sourcePoints)
            {
                if (!SpatialReferenceTransform.TryTransformXY("EPSG:4326", spatialContext.ResolvedSrs, sourcePoint.X, sourcePoint.Y, out var x, out var y))
                {
                    return false;
                }

                transformedPoints.Add(new Point3d(x - offsetX, y - offsetY, baseZ));
            }

            polyline = new Polyline(transformedPoints);
            polyline.RemoveNearlyEqualSubsequentPoints(VertexTolerance);
            polyline.DeleteShortSegments(VertexTolerance);

            if (polyline.Count < 3)
            {
                return false;
            }

            if (!polyline[0].EpsilonEquals(polyline[^1], VertexTolerance))
            {
                polyline.Add(polyline[0]);
            }

            return polyline.Count >= 4 && polyline.IsClosed;
        }

        private static bool TryTransformLine(
            IReadOnlyList<Coordinate2D> sourcePoints,
            SpatialContext2D spatialContext,
            bool useTerrainAverage,
            out Polyline polyline)
        {
            polyline = new Polyline();

            if (sourcePoints.Count < 2)
            {
                return false;
            }

            var baseZ = useTerrainAverage ? ResolveAverageElevation(sourcePoints, spatialContext) : 0.0;
            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;
            var transformedPoints = new List<Point3d>(sourcePoints.Count);

            foreach (var sourcePoint in sourcePoints)
            {
                if (!SpatialReferenceTransform.TryTransformXY("EPSG:4326", spatialContext.ResolvedSrs, sourcePoint.X, sourcePoint.Y, out var x, out var y))
                {
                    return false;
                }

                transformedPoints.Add(new Point3d(x - offsetX, y - offsetY, baseZ));
            }

            polyline = new Polyline(transformedPoints);
            polyline.RemoveNearlyEqualSubsequentPoints(VertexTolerance);
            polyline.DeleteShortSegments(VertexTolerance);
            return polyline.Count >= 2;
        }

        private static bool TryTransformLineAtZ(
            IReadOnlyList<Coordinate2D> sourcePoints,
            SpatialContext2D spatialContext,
            double z,
            out Polyline polyline)
        {
            polyline = new Polyline();

            if (sourcePoints.Count < 2)
            {
                return false;
            }

            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;
            var transformedPoints = new List<Point3d>(sourcePoints.Count);

            foreach (var sourcePoint in sourcePoints)
            {
                if (!SpatialReferenceTransform.TryTransformXY("EPSG:4326", spatialContext.ResolvedSrs, sourcePoint.X, sourcePoint.Y, out var x, out var y))
                {
                    return false;
                }

                transformedPoints.Add(new Point3d(x - offsetX, y - offsetY, z));
            }

            polyline = new Polyline(transformedPoints);
            polyline.RemoveNearlyEqualSubsequentPoints(VertexTolerance);
            polyline.DeleteShortSegments(VertexTolerance);
            return polyline.Count >= 2;
        }

        private static double ResolveAverageElevation(IReadOnlyList<Coordinate2D> sourcePoints, SpatialContext2D spatialContext)
        {
            var sampledElevations = new List<double>(sourcePoints.Count);

            foreach (var point in sourcePoints)
            {
                if (SpatialTerrainCache.TrySamplePlacedElevation(spatialContext, "EPSG:4326", point.X, point.Y, out var sampledElevation))
                {
                    sampledElevations.Add(sampledElevation);
                }
            }

            if (sampledElevations.Count == 0)
            {
                return 0.0;
            }

            return sampledElevations.Average();
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

            return ResolveAverageElevation(sourcePoints, spatialContext);
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

        private static Curve? TryFilletRoadOutlineCurve(Curve outlineCurve, double width, double tolerance, double angleTolerance)
        {
            if (outlineCurve is null || !outlineCurve.IsClosed)
            {
                return null;
            }

            var radius = ResolveRoadCornerRadius(width, tolerance);
            if (radius <= tolerance)
            {
                return null;
            }

            var filletedCurve = Curve.CreateFilletCornersCurve(outlineCurve, radius, tolerance, angleTolerance);
            return filletedCurve is not null && filletedCurve.IsClosed ? filletedCurve : null;
        }

        private static double ResolveRoadCornerRadius(double width, double tolerance)
        {
            var unclampedRadius = width * 0.35;
            return Math.Max(tolerance * 2.0, Math.Min(unclampedRadius, 8.0));
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

        private static double ResolveWaterWidth(IReadOnlyDictionary<string, string?> tags)
        {
            if (TryGetTagLength(tags, "width", out var widthMeters))
            {
                return Math.Max(widthMeters, 2.0);
            }

            if (!tags.TryGetValue("waterway", out var waterwayType) || string.IsNullOrWhiteSpace(waterwayType))
            {
                return 6.0;
            }

            return waterwayType switch
            {
                "river" => 18.0,
                "canal" => 10.0,
                "stream" => 4.0,
                "drain" => 3.0,
                "ditch" => 2.0,
                _ => 6.0
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
