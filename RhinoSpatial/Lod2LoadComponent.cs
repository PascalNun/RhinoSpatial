using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public class Lod2LoadComponent : GH_TaskCapableComponent<Lod2LoadComponent.SolveResults>
    {
        private const double DefaultRequestBufferMeters = 25.0;
        private readonly WfsClient _wfsClient = new();

        public class SolveResults
        {
            public GH_Structure<GH_Brep> BrepTree { get; init; } = new();

            public int BuildingCount { get; init; }

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private sealed class RequestData
        {
            public string BaseUrl { get; init; } = string.Empty;

            public string? LayerName { get; init; }

            public SpatialContext2D SpatialContext { get; init; } = null!;
        }

        private sealed class ResolvedLayer
        {
            public string LayerName { get; init; } = string.Empty;

            public WfsLayerInfo LayerInfo { get; init; } = null!;
        }

        public Lod2LoadComponent()
            : base("Load LoD2 Buildings", "Load LoD2",
                "Load aligned LoD2 building geometry for the shared RhinoSpatial spatial context.",
                "RhinoSpatial", "Sources")
        {
            NormalizeComponentLayout();
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("LoD2 Service URL", "LoD2 URL", "Base URL of the LoD2 service. If left empty, RhinoSpatial will try to inherit it from the connected Layer input.", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "Layer", "Optional LoD2 building layer name. Leave empty if the service only exposes one building layer.", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context. LoD2 requests use EPSG:7423/4326 internally, so any Spatial Context created from the map helper will align correctly.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Buildings", "Buildings", "LoD2 building Breps grouped by layer and building.", GH_ParamAccess.tree);
            pManager.AddTextParameter("Status", "Status", "Status and diagnostic information from the LoD2 loader.", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            NormalizeComponentLayout();
        }

        public override bool Read(GH_IReader reader)
        {
            var result = base.Read(reader);
            NormalizeComponentLayout();
            return result;
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

                dataAccess.SetDataTree(0, result.BrepTree);
                dataAccess.SetData(1, result.Status);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.LoadLod2.png");

        public override Guid ComponentGuid => new Guid("0f8c618c-0b9e-4a90-9acb-6835bea721bb");

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? baseUrl = null;
            string? layerName = null;
            string? spatialContextText = null;

            dataAccess.GetData(0, ref baseUrl);
            dataAccess.GetData(1, ref layerName);
            dataAccess.GetData(2, ref spatialContextText);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                WfsLayerInputResolver.TryResolveBaseUrlFromLayerInput(Params.Input[1], out var resolvedBaseUrl);
                if (!string.IsNullOrWhiteSpace(resolvedBaseUrl))
                {
                    baseUrl = resolvedBaseUrl;
                }
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "LoD2 Service URL is required, unless RhinoSpatial can inherit it from the connected Layer input.");
                return false;
            }

            if (!RhinoSpatialInputParser.TryGetRequiredSpatialContext(spatialContextText, out var spatialContext, out var spatialContextError))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, spatialContextError);
                return false;
            }

            requestData = new RequestData
            {
                BaseUrl = baseUrl.Trim(),
                LayerName = string.IsNullOrWhiteSpace(layerName) ? null : RhinoSpatialInputParser.ParseLayerName(layerName),
                SpatialContext = spatialContext
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            var capabilities = _wfsClient.LoadCapabilitiesAsync(requestData.BaseUrl).GetAwaiter().GetResult();
            var resolvedLayer = ResolveLayer(capabilities, requestData.LayerName);
            var requestSrs = ResolveRequestSrs(resolvedLayer.LayerInfo);

            if (!RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(
                    requestData.SpatialContext,
                    requestSrs,
                    out var requestBoundingBox,
                    out var placementOrigin))
            {
                var availableSrs = requestData.SpatialContext.BoundingBoxesBySrs.Count == 0
                    ? "none"
                    : string.Join(", ", requestData.SpatialContext.BoundingBoxesBySrs.Keys.OrderBy(key => key));

                return new SolveResults
                {
                    Status = $"The Spatial Context does not include a bounding box for '{requestSrs}'. Available SRS values: {availableSrs}. Open the Spatial Context map helper and draw a rectangle again.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var requestOptions = new WfsRequestOptions
            {
                BaseUrl = requestData.BaseUrl,
                TypeName = resolvedLayer.LayerName,
                MaxFeatures = 0,
                Version = "2.0.0",
                SrsName = requestSrs,
                OutputFormat = "application/gml+xml; version=3.2",
                BoundingBox = CreateBufferedBoundingBox(requestBoundingBox, requestSrs, DefaultRequestBufferMeters)
            };

            var response = _wfsClient.LoadFeatureResponseAsync(requestOptions).GetAwaiter().GetResult();
            var returnedBuildings = Lod2GmlReader.ReadBuildings(response.ResponseText, resolvedLayer.LayerName);
            var buildings = FilterBuildingsToRequestBounds(returnedBuildings, requestBoundingBox);
            var targetBoundingBox = requestData.SpatialContext.RequestBoundingBox;
            var placementPoint = new Point3d(requestData.SpatialContext.PlacementOrigin.X, requestData.SpatialContext.PlacementOrigin.Y, 0.0);
            var elevationBase = requestData.SpatialContext.UseAbsoluteCoordinates
                ? 0.0
                : SpatialElevationBaselineCache.ResolveOrStore(
                    requestData.SpatialContext,
                    RhinoSpatialLod2OutputBuilder.CalculateElevationBase(buildings));
            var brepTree = RhinoSpatialLod2OutputBuilder.BuildBrepTree(
                buildings,
                new[] { resolvedLayer.LayerName },
                requestSrs,
                requestData.SpatialContext.ResolvedSrs,
                requestBoundingBox,
                targetBoundingBox,
                placementPoint,
                requestData.SpatialContext.UseAbsoluteCoordinates,
                elevationBase,
                out var buildReport);

            return new SolveResults
            {
                BrepTree = brepTree,
                BuildingCount = buildings.Count,
                Status = BuildStatusMessage(
                    buildings.Count,
                    resolvedLayer.LayerName,
                    requestSrs,
                    requestData.SpatialContext.UseAbsoluteCoordinates,
                    requestData.SpatialContext,
                    requestBoundingBox,
                    requestOptions.BoundingBox,
                    DefaultRequestBufferMeters,
                    returnedBuildings.Count,
                    response.ResponseText.Length,
                    response.StatusNote,
                    buildReport),
                MessageLevel = buildings.Count == 0
                    ? GH_RuntimeMessageLevel.Warning
                    : buildReport.FailedSurfaceBrepCount > 0 || buildReport.MalformedLoopSurfaceCount > 0 || buildReport.BuildingsWithoutOutputCount > 0
                        ? GH_RuntimeMessageLevel.Remark
                    : string.IsNullOrWhiteSpace(response.StatusNote)
                        ? null
                        : GH_RuntimeMessageLevel.Remark
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
                    Status = ex.Message,
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }
        }

        private static string BuildStatusSuffix(string statusNote)
        {
            return string.IsNullOrWhiteSpace(statusNote)
                ? string.Empty
                : $" {statusNote}";
        }

        private static string BuildStatusMessage(
            int buildingCount,
            string layerName,
            string requestSrs,
            bool useAbsoluteCoordinates,
            SpatialContext2D spatialContext,
            BoundingBox2D requestBoundingBox,
            BoundingBox2D? queryBoundingBox,
            double requestBufferMeters,
            int returnedBuildingCount,
            int responseLengthBytes,
            string responseStatusNote,
            RhinoSpatialLod2OutputBuilder.BuildReport buildReport)
        {
            if (buildingCount == 0)
            {
                var noDataStatus = returnedBuildingCount > 0
                    ? $"The buffered LoD2 WFS request returned {returnedBuildingCount} building(s), but none intersected the current Spatial Context."
                    : "No LoD2 buildings were returned inside the current Spatial Context.";
                if (queryBoundingBox is not null)
                {
                    noDataStatus += $" WFS query used a {FormatNumber(requestBufferMeters)} m buffer: {FormatBoundingBox2D(queryBoundingBox)}.";
                }

                return string.IsNullOrWhiteSpace(responseStatusNote)
                    ? noDataStatus
                    : $"{noDataStatus} {responseStatusNote}";
            }

            var alignmentNote = useAbsoluteCoordinates
                ? "with absolute elevation."
                : "and aligned them to the shared local terrain/building elevation baseline.";
            var skippedSurfaceCount = buildReport.MalformedLoopSurfaceCount +
                                      buildReport.DuplicateSurfaceCount +
                                      buildReport.FailedSurfaceBrepCount;
            var diagnosticNote =
                $" Parsed {buildReport.ParsedBuildingCount} building(s), {buildReport.ParsedSurfaceCount} source surface(s); " +
                $"created {buildReport.OutputBrepCount} Brep object(s) from {buildReport.ConstructedSurfaceCount} converted surface(s).";
            if (returnedBuildingCount != buildReport.ParsedBuildingCount)
            {
                diagnosticNote += $" WFS returned {returnedBuildingCount} building(s); kept {buildReport.ParsedBuildingCount} intersecting the Spatial Context.";
            }

            if (queryBoundingBox is not null)
            {
                diagnosticNote += $" WFS query used a {FormatNumber(requestBufferMeters)} m buffer: {FormatBoundingBox2D(queryBoundingBox)}.";
            }

            diagnosticNote += $" Response size: {FormatByteLength(responseLengthBytes)}.";
            diagnosticNote += $" Request bounds {requestSrs}: {FormatBoundingBox2D(requestBoundingBox)}.";
            diagnosticNote += $" Context local XY bounds: {FormatLocalContextBounds(spatialContext)}.";
            diagnosticNote += buildReport.TransformedSurfaceBounds.HasValue
                ? $" Returned LoD2 local bounds: {FormatBoundingBox(buildReport.TransformedSurfaceBounds.Value)}."
                : " Returned LoD2 local bounds: none.";
            diagnosticNote += buildReport.OutputBrepBounds.HasValue
                ? $" Output Brep bounds: {FormatBoundingBox(buildReport.OutputBrepBounds.Value)}."
                : " Output Brep bounds: none.";

            if (buildReport.BuildingsWithoutOutputCount > 0)
            {
                diagnosticNote += $" {buildReport.BuildingsWithoutOutputCount} returned building(s) produced no usable Breps.";
                if (buildReport.BuildingsWithoutOutputIds.Count > 0)
                {
                    diagnosticNote += $" Example no-output building id(s): {string.Join(", ", buildReport.BuildingsWithoutOutputIds)}.";
                }
            }

            if (skippedSurfaceCount > 0)
            {
                diagnosticNote +=
                    $" Skipped {skippedSurfaceCount} surface(s): {buildReport.MalformedLoopSurfaceCount} malformed loop(s), " +
                    $"{buildReport.DuplicateSurfaceCount} duplicate(s), {buildReport.FailedSurfaceBrepCount} conversion failure(s).";
            }

            if (buildReport.InvalidOutputBrepCount > 0)
            {
                diagnosticNote += $" Dropped {buildReport.InvalidOutputBrepCount} invalid output Brep(s).";
            }

            if (buildReport.InnerLoopOuterFallbackCount > 0)
            {
                diagnosticNote += $" Used outer-face fallback for {buildReport.InnerLoopOuterFallbackCount} surface(s) with problematic inner loops.";
            }

            return $"Loaded {buildingCount} LoD2 building Brep set(s) from layer '{layerName}' using request SRS '{requestSrs}' {alignmentNote}{diagnosticNote}{BuildStatusSuffix(responseStatusNote)}";
        }

        private static BoundingBox2D CreateBufferedBoundingBox(BoundingBox2D boundingBox, string srsName, double bufferMeters)
        {
            if (bufferMeters <= 0.0)
            {
                return boundingBox;
            }

            var normalizedSrs = RhinoSpatialContextTools.NormalizeSrsKey(srsName);
            if (normalizedSrs == "EPSG:4326" || normalizedSrs == "EPSG:7423" || normalizedSrs == "EPSG:4283" || normalizedSrs == "EPSG:7844")
            {
                var centerLatitude = (boundingBox.MinY + boundingBox.MaxY) * 0.5;
                var latitudeBuffer = bufferMeters / 111_320.0;
                var longitudeMeters = Math.Max(1_000.0, 111_320.0 * Math.Cos(centerLatitude * Math.PI / 180.0));
                var longitudeBuffer = bufferMeters / longitudeMeters;

                return new BoundingBox2D(
                    boundingBox.MinX - longitudeBuffer,
                    boundingBox.MinY - latitudeBuffer,
                    boundingBox.MaxX + longitudeBuffer,
                    boundingBox.MaxY + latitudeBuffer);
            }

            return new BoundingBox2D(
                boundingBox.MinX - bufferMeters,
                boundingBox.MinY - bufferMeters,
                boundingBox.MaxX + bufferMeters,
                boundingBox.MaxY + bufferMeters);
        }

        private static List<Lod2Building> FilterBuildingsToRequestBounds(IReadOnlyList<Lod2Building> buildings, BoundingBox2D requestBoundingBox)
        {
            return buildings
                .Where(building => BuildingIntersectsBounds(building, requestBoundingBox))
                .ToList();
        }

        private static bool BuildingIntersectsBounds(Lod2Building building, BoundingBox2D requestBoundingBox)
        {
            foreach (var surface in building.Surfaces)
            {
                if (SurfaceIntersectsBounds(surface, requestBoundingBox))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SurfaceIntersectsBounds(SurfacePolygon3D surface, BoundingBox2D requestBoundingBox)
        {
            return PointsIntersectBounds(surface.OuterPoints, requestBoundingBox) ||
                   surface.InnerRings.Any(points => PointsIntersectBounds(points, requestBoundingBox));
        }

        private static bool PointsIntersectBounds(IReadOnlyList<Coordinate3D> points, BoundingBox2D requestBoundingBox)
        {
            if (points.Count == 0)
            {
                return false;
            }

            var minX = points.Min(static point => point.X);
            var minY = points.Min(static point => point.Y);
            var maxX = points.Max(static point => point.X);
            var maxY = points.Max(static point => point.Y);
            var pointBounds = new BoundingBox2D(minX, minY, maxX, maxY);
            return RhinoSpatialContextTools.DoBoundingBoxesIntersect(pointBounds, requestBoundingBox);
        }

        private static string FormatBoundingBox2D(BoundingBox2D bounds)
        {
            return $"X {FormatNumber(bounds.MinX)}..{FormatNumber(bounds.MaxX)}, Y {FormatNumber(bounds.MinY)}..{FormatNumber(bounds.MaxY)}";
        }

        private static string FormatLocalContextBounds(SpatialContext2D spatialContext)
        {
            if (spatialContext.UseAbsoluteCoordinates)
            {
                return FormatBoundingBox2D(spatialContext.PlacementBoundingBox);
            }

            var origin = spatialContext.PlacementOrigin;
            return $"X {FormatNumber(spatialContext.PlacementBoundingBox.MinX - origin.X)}..{FormatNumber(spatialContext.PlacementBoundingBox.MaxX - origin.X)}, " +
                   $"Y {FormatNumber(spatialContext.PlacementBoundingBox.MinY - origin.Y)}..{FormatNumber(spatialContext.PlacementBoundingBox.MaxY - origin.Y)}";
        }

        private static string FormatBoundingBox(BoundingBox bounds)
        {
            return $"X {FormatNumber(bounds.Min.X)}..{FormatNumber(bounds.Max.X)}, " +
                   $"Y {FormatNumber(bounds.Min.Y)}..{FormatNumber(bounds.Max.Y)}, " +
                   $"Z {FormatNumber(bounds.Min.Z)}..{FormatNumber(bounds.Max.Z)}";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatByteLength(int byteLength)
        {
            return byteLength >= 1024 * 1024
                ? $"{(byteLength / 1024.0 / 1024.0).ToString("0.##", CultureInfo.InvariantCulture)} MB"
                : $"{(byteLength / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} KB";
        }

        private void NormalizeComponentLayout()
        {
            var changed = false;

            if (Params.Output.Count > 0)
            {
                changed |= SetParameterMetadata(
                    Params.Output[0],
                    "Buildings",
                    "Buildings",
                    "LoD2 building Breps grouped by layer and building.");
            }

            if (Params.Output.Count < 2)
            {
                Params.RegisterOutputParam(new Grasshopper.Kernel.Parameters.Param_String
                {
                    Name = "Status",
                    NickName = "Status",
                    Description = "Status and diagnostic information from the LoD2 loader.",
                    Access = GH_ParamAccess.item
                });
                changed = true;
            }
            else
            {
                changed |= SetParameterMetadata(
                    Params.Output[1],
                    "Status",
                    "Status",
                    "Status and diagnostic information from the LoD2 loader.");
            }

            if (changed)
            {
                Params.OnParametersChanged();
            }
        }

        private static bool SetParameterMetadata(IGH_Param parameter, string name, string nickName, string description)
        {
            var changed = false;
            if (!string.Equals(parameter.Name, name, StringComparison.Ordinal))
            {
                parameter.Name = name;
                changed = true;
            }

            if (!string.Equals(parameter.NickName, nickName, StringComparison.Ordinal))
            {
                parameter.NickName = nickName;
                changed = true;
            }

            if (!string.Equals(parameter.Description, description, StringComparison.Ordinal))
            {
                parameter.Description = description;
                changed = true;
            }

            return changed;
        }

        private static ResolvedLayer ResolveLayer(WfsCapabilitiesInfo capabilities, string? requestedLayerName)
        {
            if (!string.IsNullOrWhiteSpace(requestedLayerName))
            {
                var explicitLayer = capabilities.Layers.FirstOrDefault(layer =>
                    string.Equals(layer.Name, requestedLayerName, StringComparison.OrdinalIgnoreCase));

                if (explicitLayer is null)
                {
                    throw new InvalidOperationException($"The LoD2 layer '{requestedLayerName}' was not found in the WFS service.");
                }

                return new ResolvedLayer
                {
                    LayerName = explicitLayer.Name,
                    LayerInfo = explicitLayer
                };
            }

            if (capabilities.Layers.Count == 1)
            {
                return new ResolvedLayer
                {
                    LayerName = capabilities.Layers[0].Name,
                    LayerInfo = capabilities.Layers[0]
                };
            }

            var preferredLayer = capabilities.Layers
                .OrderByDescending(layer => ScoreLayer(layer))
                .ThenBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (preferredLayer is null)
            {
                throw new InvalidOperationException("No usable LoD2 layer could be resolved from the WFS service.");
            }

            return new ResolvedLayer
            {
                LayerName = preferredLayer.Name,
                LayerInfo = preferredLayer
            };
        }

        private static int ScoreLayer(WfsLayerInfo layer)
        {
            var text = $"{layer.Name} {layer.Title}";
            var score = 0;

            if (text.Contains("building", StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }

            if (text.Contains("lod2", StringComparison.OrdinalIgnoreCase))
            {
                score += 80;
            }

            if (text.Contains("bu-core3d", StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }

            return score;
        }

        private static string ResolveRequestSrs(WfsLayerInfo layerInfo)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(layerInfo.DefaultSrs))
            {
                candidates.Add(layerInfo.DefaultSrs);
            }

            candidates.AddRange(layerInfo.OtherSrs);

            foreach (var candidate in candidates)
            {
                var normalized = RhinoSpatialContextTools.NormalizeSrsKey(candidate);

                if (normalized == "EPSG:7423")
                {
                    return normalized;
                }
            }

            foreach (var candidate in candidates)
            {
                var normalized = RhinoSpatialContextTools.NormalizeSrsKey(candidate);

                if (normalized == "EPSG:4326")
                {
                    return normalized;
                }
            }

            return RhinoSpatialContextTools.NormalizeSrsKey(layerInfo.DefaultSrs);
        }

    }
}
