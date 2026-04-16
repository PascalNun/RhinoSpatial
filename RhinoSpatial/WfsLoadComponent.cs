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
    public class WfsLoadComponent : GH_TaskCapableComponent<WfsLoadComponent.SolveResults>
    {
        private const int DirectLayerWarningThreshold = 8;
        private readonly WfsClient _wfsClient = new();

        public class SolveResults
        {
            public GH_Structure<IGH_GeometricGoo> GeometryTree { get; init; } = new();

            public int FeatureCount { get; init; }

            public int GeometryItemCount { get; init; }

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private class RequestData
        {
            public string BaseUrl { get; init; } = string.Empty;

            public List<string> RequestedLayerNames { get; init; } = new();

            public int MaxFeatures { get; init; }

            public SpatialContext2D SpatialContext { get; init; } = null!;
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
            pManager.AddTextParameter("WFS Service URL", "WFS URL", "Base URL of the WFS service. If left empty, RhinoSpatial will try to inherit it from the connected Layer input.", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "Layer", "One or more layer names or layer entries. Use List Item to choose one layer, or merge explicit selections if you want to load several layers.", GH_ParamAccess.list);
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

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "WFS Service URL is required, unless RhinoSpatial can inherit it from the connected Layer input.");
                return false;
            }

            var requestedLayerNames = layerSelections
                .Where(layerName => !string.IsNullOrWhiteSpace(layerName))
                .Select(RhinoSpatialInputParser.ParseLayerName)
                .Where(layerName => !string.IsNullOrWhiteSpace(layerName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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
                SpatialContext = spatialContext
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            var resolvedSrsName = requestData.SpatialContext.ResolvedSrs;
            var features = new List<WfsFeature>();

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

                var layerFeatures = _wfsClient.LoadFeaturesAsync(layerRequestOptions).GetAwaiter().GetResult();
                features.AddRange(layerFeatures);
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
                Status = BuildStatusMessage(requestData, features.Count, geometryItemCount, maxFeaturesText, resolvedSrsText),
                MessageLevel = ResolveMessageLevel(features.Count)
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

        private static string BuildStatusMessage(RequestData requestData, int featureCount, int geometryItemCount, string maxFeaturesText, string resolvedSrsText)
        {
            var coordinateText = requestData.SpatialContext.UseAbsoluteCoordinates
                ? "using absolute coordinates."
                : "then localized the geometry near the Rhino origin.";

            if (featureCount == 0)
            {
                return $"No features were found for the selected layer selection inside the current Spatial Context using shared-context SRS '{resolvedSrsText}'.";
            }

            return $"Loaded {featureCount} feature(s) and {geometryItemCount} geometry item(s) from {requestData.RequestedLayerNames.Count} layer(s) with {maxFeaturesText} features and shared-context SRS '{resolvedSrsText}', {coordinateText}";
        }

        private static GH_RuntimeMessageLevel? ResolveMessageLevel(int featureCount)
        {
            if (featureCount == 0)
            {
                return GH_RuntimeMessageLevel.Warning;
            }

            return null;
        }
    }
}
