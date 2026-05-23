using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public class WfsLoadComponent : GH_TaskCapableComponent<WfsLoadComponent.SolveResults>
    {
        private const int DirectLayerWarningThreshold = 8;
        private const double OversizedEnclosingFeatureRatio = 5.0;
        private const double BoundsToleranceRatio = 1e-9;
        private readonly WfsClient _wfsClient = new();
        private readonly OgcApiFeaturesClient _ogcApiFeaturesClient = new();

        public class SolveResults
        {
            public GH_Structure<IGH_GeometricGoo> GeometryTree { get; init; } = new();

            public int FeatureCount { get; init; }

            public int GeometryItemCount { get; init; }

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }

            public bool UsedCachedFallback { get; init; }
        }

        private class RequestData
        {
            public string BaseUrl { get; init; } = string.Empty;

            public List<string> RequestedLayerNames { get; init; } = new();

            public int MaxFeatures { get; init; }

            public SpatialContext2D SpatialContext { get; init; } = null!;

            public bool IsLocalShapefile { get; init; }

            public bool IsOgcApiFeatures { get; init; }
        }

        public WfsLoadComponent()
            : base("Load WFS", "Load WFS",
                "Load aligned WFS geometry for the shared RhinoSpatial spatial context.",
                "RhinoSpatial", "Sources")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.last;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("WFS Service URL", "WFS URL", "Base URL of the WFS service, OGC API Features collection/items URL, or path to a local Shapefile (.shp). If left empty, RhinoSpatial will try to inherit it from the connected Layer input.", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "Layer", "One or more layer names or layer entries. Use List Item to choose one layer, or merge explicit selections if you want to load several layers. Optional for local Shapefile and OGC API Features sources.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Max Features", "Max Features", "Maximum number of features to request. Use 0 to request all available features.", GH_ParamAccess.item, 0);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context. This is required so WFS, WMS, LoD2, terrain, GeoTIFF, and OSM outputs stay aligned.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "Geometry", "Geometry grouped by layer and feature. RhinoSpatial currently outputs curves for polygon and line features, and points for point features.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            try
            {
                if (!TryGetRequestData(dataAccess, out var requestData))
                {
                    return;
                }

                if (InPreSolve)
                {
                    Task<SolveResults> task = Task.Run(() => ComputeSafe(requestData), CancelToken);
                    TaskList.Add(task);
                    return;
                }

                if (!GetSolveResults(dataAccess, out SolveResults result))
                {
                    result = ComputeSafe(requestData);
                }

                if (!string.IsNullOrWhiteSpace(result.Status) && result.MessageLevel.HasValue)
                {
                    AddRuntimeMessage(result.MessageLevel.Value, result.Status);
                }

                dataAccess.SetDataTree(0, result.GeometryTree);

                if (result.FeatureCount == 0 && !result.MessageLevel.HasValue)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, result.Status);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.LoadWfs.png");

        public override Guid ComponentGuid => new Guid("eb3a719e-b4a1-4044-9c81-50fc2c4930ba");

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? baseUrl = null;
            string? spatialContextText = null;
            var layerSelections = new List<string>();
            var maxFeatures = 0;

            dataAccess.GetData(0, ref baseUrl);

            if (!dataAccess.GetDataList(1, layerSelections))
            {
                return false;
            }

            dataAccess.GetData(2, ref maxFeatures);
            dataAccess.GetData(3, ref spatialContextText);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                WfsLayerInputResolver.TryResolveBaseUrlFromLayerInput(Params.Input[1], out var resolvedBaseUrl);

                if (!string.IsNullOrWhiteSpace(resolvedBaseUrl))
                {
                    baseUrl = resolvedBaseUrl;
                }
            }

            var isLocalShapefile = IsLocalShapefileSource(baseUrl);
            var isOgcApiFeatures = OgcApiFeaturesClient.LooksLikeOgcApiFeaturesUrl(baseUrl);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "WFS Service URL is required, unless RhinoSpatial can inherit it from the connected Layer input.");
                return false;
            }

            if (!IsUrlSource(baseUrl) && !isLocalShapefile)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"WFS URL must be a WFS service URL, an OGC API Features collection/items URL, or a local .shp file path. Source was not found or is unsupported: {baseUrl}");
                return false;
            }

            var requestedLayerNames = layerSelections
                .Where(layerName => !string.IsNullOrWhiteSpace(layerName))
                .Select(RhinoSpatialInputParser.ParseLayerName)
                .Where(layerName => !string.IsNullOrWhiteSpace(layerName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requestedLayerNames.Count == 0 && isLocalShapefile)
            {
                requestedLayerNames.Add(Path.GetFileNameWithoutExtension(baseUrl));
            }

            if (requestedLayerNames.Count == 0 && isOgcApiFeatures)
            {
                requestedLayerNames.Add(ResolveOgcApiFeaturesLayerName(baseUrl));
            }

            if (requestedLayerNames.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one valid layer selection is required.");
                return false;
            }

            if (requestedLayerNames.Count > 1 &&
                WfsLayerInputResolver.IsConnectedDirectlyToWfsLayersOutput(Params.Input[1]))
            {
                if (requestedLayerNames.Count > DirectLayerWarningThreshold)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Remark,
                        $"Loading {requestedLayerNames.Count} WFS layers directly from the full layer list. RhinoSpatial will place them in separate top-level branches by layer. Use List Item if you only want one layer.");
                }
            }

            if (!RhinoSpatialInputParser.TryGetRequiredSpatialContext(spatialContextText, out var spatialContext, out var spatialContextError))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, spatialContextError);
                return false;
            }

            requestData = new RequestData
            {
                BaseUrl = baseUrl,
                RequestedLayerNames = requestedLayerNames,
                MaxFeatures = maxFeatures,
                SpatialContext = spatialContext,
                IsLocalShapefile = isLocalShapefile,
                IsOgcApiFeatures = isOgcApiFeatures
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            if (requestData.IsLocalShapefile)
            {
                return ComputeLocalShapefile(requestData);
            }

            if (requestData.IsOgcApiFeatures)
            {
                return ComputeOgcApiFeatures(requestData);
            }

            var resolvedSrsName = requestData.SpatialContext.ResolvedSrs;
            var features = new List<WfsFeature>();
            var statusNotes = new List<string>();

            foreach (var requestedLayerName in requestData.RequestedLayerNames)
            {
                var layerRequestOptions = new WfsRequestOptions
                {
                    BaseUrl = requestData.BaseUrl,
                    TypeName = requestedLayerName,
                    MaxFeatures = requestData.MaxFeatures,
                    SrsName = resolvedSrsName,
                    BoundingBox = requestData.SpatialContext.RequestBoundingBox
                };

                var layerResult = _wfsClient.LoadFeaturesWithStatusAsync(layerRequestOptions).GetAwaiter().GetResult();
                var filteredLayerFeatures = FilterFeaturesToSpatialContext(
                    layerResult.Features,
                    requestData.SpatialContext.RequestBoundingBox,
                    out var oversizedFeatureCount);
                features.AddRange(filteredLayerFeatures);

                if (!string.IsNullOrWhiteSpace(layerResult.StatusNote))
                {
                    statusNotes.Add(layerResult.StatusNote);
                }

                if (oversizedFeatureCount > 0)
                {
                    statusNotes.Add($"Filtered {oversizedFeatureCount} oversized enclosing WFS feature(s) whose outlines were outside the Spatial Context.");
                }
            }

            var appliedOffset = RhinoSpatialContextTools.ResolvePlacementOrigin(
                requestData.SpatialContext,
                requestData.SpatialContext.UseAbsoluteCoordinates,
                features);

            var geometryTree = RhinoSpatialOutputBuilder.BuildGeometryTree(features, requestData.RequestedLayerNames, appliedOffset.X, appliedOffset.Y);
            var geometryItemCount = geometryTree.DataCount;
            var resolvedSrsText = string.IsNullOrWhiteSpace(resolvedSrsName) ? "service default" : resolvedSrsName;
            var maxFeaturesText = requestData.MaxFeatures > 0 ? requestData.MaxFeatures.ToString() : "all available";

            return new SolveResults
            {
                GeometryTree = geometryTree,
                FeatureCount = features.Count,
                GeometryItemCount = geometryItemCount,
                Status = BuildStatusMessage(
                    requestData,
                    features.Count,
                    geometryItemCount,
                    maxFeaturesText,
                    resolvedSrsText,
                    statusNotes),
                MessageLevel = ResolveMessageLevel(features.Count, statusNotes.Count > 0),
                UsedCachedFallback = statusNotes.Count > 0
            };
        }

        private SolveResults ComputeOgcApiFeatures(RequestData requestData)
        {
            var layerName = requestData.RequestedLayerNames[0];
            var sourceFeatures = _ogcApiFeaturesClient.LoadFeaturesAsync(
                    requestData.BaseUrl,
                    layerName,
                    requestData.SpatialContext.Wgs84BoundingBox,
                    requestData.MaxFeatures)
                .GetAwaiter()
                .GetResult();
            var projectedFeatures = WfsFeatureGeometryTransformer.TransformFeatures(
                sourceFeatures,
                "EPSG:4326",
                requestData.SpatialContext.ResolvedSrs);
            var features = FilterFeaturesToSpatialContext(
                projectedFeatures,
                requestData.SpatialContext.RequestBoundingBox,
                out var oversizedFeatureCount);
            var appliedOffset = RhinoSpatialContextTools.ResolvePlacementOrigin(
                requestData.SpatialContext,
                requestData.SpatialContext.UseAbsoluteCoordinates,
                features);
            var geometryTree = RhinoSpatialOutputBuilder.BuildGeometryTree(
                features,
                requestData.RequestedLayerNames,
                appliedOffset.X,
                appliedOffset.Y);
            var geometryItemCount = geometryTree.DataCount;
            var maxFeaturesText = requestData.MaxFeatures > 0 ? requestData.MaxFeatures.ToString() : "all available";
            var coordinateText = requestData.SpatialContext.UseAbsoluteCoordinates
                ? "using absolute coordinates."
                : "then localized the geometry near the Rhino origin.";
            var filterNote = oversizedFeatureCount > 0
                ? $" Filtered {oversizedFeatureCount} oversized enclosing feature(s) whose outlines were outside the Spatial Context."
                : string.Empty;
            var status = features.Count == 0
                ? $"No features were found for OGC API Features source '{Path.GetFileName(requestData.BaseUrl)}' inside the current Spatial Context.{filterNote}"
                : $"Loaded {features.Count} feature(s) and {geometryItemCount} geometry item(s) from OGC API Features source '{layerName}' with {maxFeaturesText} features, assumed GeoJSON CRS EPSG:4326, {coordinateText}{filterNote}";

            return new SolveResults
            {
                GeometryTree = geometryTree,
                FeatureCount = features.Count,
                GeometryItemCount = geometryItemCount,
                Status = status,
                MessageLevel = ResolveMessageLevel(features.Count, oversizedFeatureCount > 0)
            };
        }

        private SolveResults ComputeLocalShapefile(RequestData requestData)
        {
            var layerName = requestData.RequestedLayerNames[0];
            var readResult = ShapefileFeatureReader.ReadFeatures(
                requestData.BaseUrl,
                layerName,
                requestData.SpatialContext,
                requestData.MaxFeatures);
            var appliedOffset = RhinoSpatialContextTools.ResolvePlacementOrigin(
                requestData.SpatialContext,
                requestData.SpatialContext.UseAbsoluteCoordinates,
                readResult.Features);
            var geometryTree = RhinoSpatialOutputBuilder.BuildGeometryTree(
                readResult.Features,
                requestData.RequestedLayerNames,
                appliedOffset.X,
                appliedOffset.Y);
            var geometryItemCount = geometryTree.DataCount;
            var maxFeaturesText = requestData.MaxFeatures > 0 ? requestData.MaxFeatures.ToString() : "all available";
            var coordinateText = requestData.SpatialContext.UseAbsoluteCoordinates
                ? "using absolute coordinates."
                : "then localized the geometry near the Rhino origin.";
            var status = readResult.Features.Count == 0
                ? $"No features were found in local Shapefile '{Path.GetFileName(requestData.BaseUrl)}' inside the current Spatial Context. Source SRS '{readResult.SourceSrs}'. Scanned {readResult.SourceFeatureCount} feature(s), skipped {readResult.SkippedOutsideContextCount} outside the context, failed {readResult.FailedFeatureCount}."
                : $"Loaded {readResult.Features.Count} feature(s) and {geometryItemCount} geometry item(s) from local Shapefile '{Path.GetFileName(requestData.BaseUrl)}' with {maxFeaturesText} features, source SRS '{readResult.SourceSrs}', {coordinateText} Scanned {readResult.SourceFeatureCount} feature(s), skipped {readResult.SkippedOutsideContextCount} outside the context, failed {readResult.FailedFeatureCount}.";

            return new SolveResults
            {
                GeometryTree = geometryTree,
                FeatureCount = readResult.Features.Count,
                GeometryItemCount = geometryItemCount,
                Status = status,
                MessageLevel = ResolveMessageLevel(readResult.Features.Count, readResult.FailedFeatureCount > 0)
            };
        }

        private SolveResults ComputeSafe(RequestData requestData)
        {
            try
            {
                return Compute(requestData);
            }
            catch (Exception ex)
            {
                return new SolveResults
                {
                    GeometryTree = new GH_Structure<IGH_GeometricGoo>(),
                    FeatureCount = 0,
                    GeometryItemCount = 0,
                    Status = ex.Message,
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }
        }

        private static string BuildStatusMessage(
            RequestData requestData,
            int featureCount,
            int geometryItemCount,
            string maxFeaturesText,
            string resolvedSrsText,
            IReadOnlyList<string> statusNotes)
        {
            var coordinateText = requestData.SpatialContext.UseAbsoluteCoordinates
                ? "using absolute coordinates."
                : "then localized the geometry near the Rhino origin.";

            var statusSuffix = statusNotes.Count == 0
                ? string.Empty
                : $" {string.Join(" ", statusNotes.Distinct(StringComparer.Ordinal))}";

            if (featureCount == 0)
            {
                return $"No features were found for the selected layer selection inside the current Spatial Context using shared-context SRS '{resolvedSrsText}'.{statusSuffix}";
            }

            return $"Loaded {featureCount} feature(s) and {geometryItemCount} geometry item(s) from {requestData.RequestedLayerNames.Count} layer(s) with {maxFeaturesText} features and shared-context SRS '{resolvedSrsText}', {coordinateText}{statusSuffix}";
        }

        private static GH_RuntimeMessageLevel? ResolveMessageLevel(int featureCount, bool usedCachedFallback)
        {
            if (featureCount == 0)
            {
                return GH_RuntimeMessageLevel.Warning;
            }

            if (usedCachedFallback)
            {
                return GH_RuntimeMessageLevel.Remark;
            }

            return null;
        }

        private static List<WfsFeature> FilterFeaturesToSpatialContext(
            IReadOnlyList<WfsFeature> features,
            BoundingBox2D contextBounds,
            out int oversizedEnclosingFeatureCount)
        {
            oversizedEnclosingFeatureCount = 0;
            var filteredFeatures = new List<WfsFeature>(features.Count);
            var contextWidth = Math.Max(1e-9, contextBounds.MaxX - contextBounds.MinX);
            var contextHeight = Math.Max(1e-9, contextBounds.MaxY - contextBounds.MinY);
            var tolerance = Math.Max(contextWidth, contextHeight) * BoundsToleranceRatio;

            foreach (var feature in features)
            {
                if (!TryGetFeatureBounds(feature, out var featureBounds) ||
                    !RhinoSpatialContextTools.DoBoundingBoxesIntersect(featureBounds, contextBounds))
                {
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
                    featureWidth > contextWidth * OversizedEnclosingFeatureRatio ||
                    featureHeight > contextHeight * OversizedEnclosingFeatureRatio;

                if (isOversizedEnclosingFeature)
                {
                    oversizedEnclosingFeatureCount++;
                    continue;
                }

                filteredFeatures.Add(feature);
            }

            return filteredFeatures;
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

        private static bool TryGetFeatureBounds(WfsFeature feature, out BoundingBox2D bounds)
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

        private static bool IsLocalShapefileSource(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            return File.Exists(source.Trim()) &&
                   Path.GetExtension(source.Trim()).Equals(".shp", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveOgcApiFeaturesLayerName(string sourceUrl)
        {
            if (!Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return "OGC API Features";
            }

            var pathParts = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            var collectionIndex = pathParts.FindIndex(part => string.Equals(part, "collections", StringComparison.OrdinalIgnoreCase));
            if (collectionIndex >= 0 && collectionIndex + 1 < pathParts.Count)
            {
                return Uri.UnescapeDataString(pathParts[collectionIndex + 1]);
            }

            return string.IsNullOrWhiteSpace(pathParts.LastOrDefault())
                ? "OGC API Features"
                : Uri.UnescapeDataString(pathParts.Last());
        }

        private static bool IsUrlSource(string source)
        {
            return Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri) &&
                   (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }
    }
}
