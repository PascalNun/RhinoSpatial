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
            var holeAreaThreshold = ResolveRoadHoleAreaThreshold(representativeWidth, tolerance);
            var mergedGeometry = UnaryUnionOp.Union(roadRegions);
            mergedGeometry = CleanMergedRoadGeometry(mergedGeometry, representativeWidth, tolerance);
            mergedGeometry = SmoothMergedRoadGeometry(
                mergedGeometry,
                representativeWidth,
                tolerance);
            if (TryAppendRoadGeometry(tree, mergedGeometry, planZ, tolerance, holeAreaThreshold))
            {
                return tree;
            }

            for (var roadIndex = 0; roadIndex < roadRegions.Count; roadIndex++)
            {
                TryAppendRoadGeometry(tree, roadRegions[roadIndex], planZ, tolerance, holeAreaThreshold, roadIndex);
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
            var waterHoleRegions = new List<NtsGeometry>();

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

                foreach (var holeRing in area.InnerRings)
                {
                    if (!TryTransformRing(holeRing.Points, spatialContext, out var holePolyline, out _))
                    {
                        continue;
                    }

                    if (TryCreatePolygonRegion(holePolyline, out var holeRegion))
                    {
                        waterHoleRegions.Add(holeRegion);
                    }
                }
            }

            if (waterRegions.Count == 0)
            {
                return tree;
            }

            var mergedGeometry = UnaryUnionOp.Union(waterRegions);
            if (waterHoleRegions.Count > 0)
            {
                var mergedHoles = UnaryUnionOp.Union(waterHoleRegions);
                var clippedGeometry = mergedGeometry.Difference(mergedHoles);
                if (clippedGeometry is not null && !clippedGeometry.IsEmpty)
                {
                    mergedGeometry = clippedGeometry;
                }
            }

            if (TryAppendRoadGeometry(tree, mergedGeometry, planZ, tolerance))
            {
                return tree;
            }

            for (var regionIndex = 0; regionIndex < waterRegions.Count; regionIndex++)
            {
                TryAppendRoadGeometry(tree, waterRegions[regionIndex], planZ, tolerance, 0.0, regionIndex);
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
                QuadrantSegments = 18,
                MitreLimit = 2.0
            };

            var candidate = BufferOp.Buffer(lineString, width * 0.5, bufferParameters);
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
                QuadrantSegments = 16,
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
                QuadrantSegments = 36,
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
            double holeAreaThreshold = 0.0,
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
                    appendedAny |= TryAppendRoadPolygon(tree, polygon, z, tolerance, holeAreaThreshold, pathOffset);
                    break;
                case NtsMultiPolygon multiPolygon:
                    for (var index = 0; index < multiPolygon.NumGeometries; index++)
                    {
                        if (multiPolygon.GetGeometryN(index) is NtsPolygon childPolygon)
                        {
                            appendedAny |= TryAppendRoadPolygon(tree, childPolygon, z, tolerance, holeAreaThreshold, pathOffset + index);
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
                            holeAreaThreshold,
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
            double holeAreaThreshold,
            int pathIndex)
        {
            if (!TryCreateCurveFromLineString(polygon.ExteriorRing, z, out var exteriorCurve))
            {
                return false;
            }

            var boundaryCurves = new List<Curve> { exteriorCurve };
            for (var holeIndex = 0; holeIndex < polygon.NumInteriorRings; holeIndex++)
            {
                var interiorRing = polygon.GetInteriorRingN(holeIndex);
                if (!ShouldKeepRoadHole(interiorRing, holeAreaThreshold))
                {
                    continue;
                }

                if (TryCreateCurveFromLineString(interiorRing, z, out var holeCurve))
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

        private static double ResolveRoadCleanupDistance(double representativeWidth, double tolerance)
        {
            var unclampedDistance = representativeWidth * 0.24;
            return Math.Max(0.9, Math.Max(tolerance * 6.0, Math.Min(unclampedDistance, 6.0)));
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

        private static double ResolveRoadHoleAreaThreshold(double representativeWidth, double tolerance)
        {
            var unclampedArea = representativeWidth * representativeWidth * 1.5;
            return Math.Max(20.0, Math.Max(tolerance * tolerance * 100.0, Math.Min(unclampedArea, 180.0)));
        }

        private static bool ShouldKeepRoadHole(NtsLineString interiorRing, double holeAreaThreshold)
        {
            if (holeAreaThreshold <= 0.0)
            {
                return true;
            }

            if (interiorRing is not NtsLinearRing linearRing)
            {
                return true;
            }

            var holePolygon = new NtsPolygon((NtsLinearRing)linearRing.Copy());
            return !holePolygon.IsEmpty && Math.Abs(holePolygon.Area) >= holeAreaThreshold;
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
