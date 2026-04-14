using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public class Lod2LoadComponent : GH_TaskCapableComponent<Lod2LoadComponent.SolveResults>
    {
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
                BoundingBox = requestBoundingBox
            };

            var response = _wfsClient.LoadFeatureResponseAsync(requestOptions).GetAwaiter().GetResult();
            var buildings = Lod2GmlReader.ReadBuildings(response.ResponseText, resolvedLayer.LayerName);
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
                elevationBase);

            return new SolveResults
            {
                BrepTree = brepTree,
                BuildingCount = buildings.Count,
                Status = buildings.Count == 0
                    ? "No LoD2 buildings were returned inside the current Spatial Context."
                    : requestData.SpatialContext.UseAbsoluteCoordinates
                        ? $"Loaded {buildings.Count} LoD2 building Brep set(s) from layer '{resolvedLayer.LayerName}' using request SRS '{requestSrs}' with absolute elevation."
                        : $"Loaded {buildings.Count} LoD2 building Brep set(s) from layer '{resolvedLayer.LayerName}' using request SRS '{requestSrs}' and aligned them to the shared local terrain/building elevation baseline.",
                MessageLevel = buildings.Count == 0 ? GH_RuntimeMessageLevel.Warning : null
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
