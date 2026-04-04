using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using WfsCore;

namespace RhinoWFS
{
    public class WfsLoadComponent : GH_TaskCapableComponent<WfsLoadComponent.SolveResults>
    {
        private readonly WfsClient _wfsClient = new();

        public class SolveResults
        {
            public GH_Structure<GH_Curve> GeometryTree { get; init; } = new();

            public int FeatureCount { get; init; }

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private class RequestData
        {
            public string BaseUrl { get; init; } = string.Empty;

            public List<string> RequestedLayerNames { get; init; } = new();

            public int MaxFeatures { get; init; }

            public string SrsName { get; init; } = string.Empty;

            public BoundingBox2D? BoundingBox { get; init; }

            public bool UseAbsoluteCoordinates { get; init; }
        }

        public WfsLoadComponent()
            : base("Load WFS", "Load WFS",
                "Load WFS data into Grasshopper.",
                "RhinoWFS", "WFS")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.last;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("WFS URL", "WFS URL", "Base URL of the WFS service. If left empty, RhinoWFS will try to inherit it from the connected Layer input.", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "Layer", "One or more layer names or layer entries. You can plug in the 'Layer' output from List WFS Layers directly.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Max Features", "Max Features", "Maximum number of features to request. Use 0 to request all available features.", GH_ParamAccess.item, 0);
            pManager.AddTextParameter("Bounding Box", "Bounding Box", "Optional bounding box in the format minX,minY,maxX,maxY using the same SRS.", GH_ParamAccess.item);
            pManager.AddTextParameter("SRS", "SRS", "Optional coordinate system for the request and returned geometry. Leave empty to auto-detect the layer default SRS from GetCapabilities.", GH_ParamAccess.item, string.Empty);
            pManager.AddBooleanParameter("Use Absolute Coordinates", "Use Absolute Coordinates", "If true, keep the original WFS coordinates. If false, move the geometry near the Rhino origin automatically.", GH_ParamAccess.item, false);

            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Geometry", "Geometry", "Closed boundary curves grouped by feature.", GH_ParamAccess.tree);
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

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoWFS.Resources.LoadWfs.png");

        public override Guid ComponentGuid => new Guid("eb3a719e-b4a1-4044-9c81-50fc2c4930ba");

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? baseUrl = null;
            string? srsName = null;
            string? boundingBoxText = null;
            var layerSelections = new List<string>();
            var maxFeatures = 0;
            var useAbsoluteCoordinates = false;

            dataAccess.GetData(0, ref baseUrl);

            if (!dataAccess.GetDataList(1, layerSelections))
            {
                return false;
            }

            dataAccess.GetData(2, ref maxFeatures);
            dataAccess.GetData(3, ref boundingBoxText);
            dataAccess.GetData(4, ref srsName);
            dataAccess.GetData(5, ref useAbsoluteCoordinates);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                WfsUpstreamContextResolver.TryResolveBaseUrlFromLayerInput(Params.Input[1], out var resolvedBaseUrl);

                if (!string.IsNullOrWhiteSpace(resolvedBaseUrl))
                {
                    baseUrl = resolvedBaseUrl;
                }
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "WFS URL is required, unless RhinoWFS can inherit it from the connected Layer input.");
                return false;
            }

            var requestedLayerNames = layerSelections
                .Where(layerName => !string.IsNullOrWhiteSpace(layerName))
                .Select(WfsComponentInputParser.ParseLayerName)
                .Where(layerName => !string.IsNullOrWhiteSpace(layerName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requestedLayerNames.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one valid layer selection is required.");
                return false;
            }

            if (!WfsComponentInputParser.TryParseBoundingBox(boundingBoxText, out var boundingBox, out var boundingBoxError))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, boundingBoxError);
                return false;
            }

            requestData = new RequestData
            {
                BaseUrl = baseUrl,
                RequestedLayerNames = requestedLayerNames,
                MaxFeatures = maxFeatures,
                SrsName = string.IsNullOrWhiteSpace(srsName) ? string.Empty : srsName.Trim(),
                BoundingBox = boundingBox,
                UseAbsoluteCoordinates = useAbsoluteCoordinates
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            var (resolvedSrsName, usedAutoDetectedSrs) = ResolveSrsName(requestData);
            var features = new List<WfsFeature>();

            foreach (var requestedLayerName in requestData.RequestedLayerNames)
            {
                var layerRequestOptions = new WfsRequestOptions
                {
                    BaseUrl = requestData.BaseUrl,
                    TypeName = requestedLayerName,
                    MaxFeatures = requestData.MaxFeatures,
                    SrsName = resolvedSrsName,
                    BoundingBox = requestData.BoundingBox
                };

                var layerFeatures = _wfsClient.LoadFeaturesAsync(layerRequestOptions).GetAwaiter().GetResult();
                features.AddRange(layerFeatures);
            }

            var appliedOffset = requestData.UseAbsoluteCoordinates
                ? Point3d.Origin
                : RhinoWFSOutputBuilder.CalculateLocalizingOffset(features);

            var geometryTree = RhinoWFSOutputBuilder.BuildGeometryTree(features, requestData.RequestedLayerNames, appliedOffset.X, appliedOffset.Y);
            var curveCount = geometryTree.DataCount;
            var resolvedSrsText = string.IsNullOrWhiteSpace(resolvedSrsName) ? "service default" : resolvedSrsName;
            var maxFeaturesText = requestData.MaxFeatures > 0 ? requestData.MaxFeatures.ToString() : "all available";
            var srsSourceText = usedAutoDetectedSrs ? "auto-detected" : "requested";

            return new SolveResults
            {
                GeometryTree = geometryTree,
                FeatureCount = features.Count,
                Status = requestData.UseAbsoluteCoordinates
                    ? $"Loaded {features.Count} feature(s) and {curveCount} curve(s) from {requestData.RequestedLayerNames.Count} layer(s) with {maxFeaturesText} features and {srsSourceText} SRS '{resolvedSrsText}' using absolute coordinates."
                    : $"Loaded {features.Count} feature(s) and {curveCount} curve(s) from {requestData.RequestedLayerNames.Count} layer(s) with {maxFeaturesText} features and {srsSourceText} SRS '{resolvedSrsText}', then localized the geometry near the Rhino origin."
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
                    GeometryTree = new GH_Structure<GH_Curve>(),
                    FeatureCount = 0,
                    Status = ex.Message,
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }
        }

        private (string ResolvedSrsName, bool UsedAutoDetectedSrs) ResolveSrsName(RequestData requestData)
        {
            if (!string.IsNullOrWhiteSpace(requestData.SrsName))
            {
                return (requestData.SrsName, false);
            }

            var layers = _wfsClient.LoadLayersAsync(requestData.BaseUrl).GetAwaiter().GetResult();
            var layerByName = new Dictionary<string, WfsLayerInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var layer in layers)
            {
                layerByName[layer.Name] = layer;
            }

            var resolvedDefaultSrs = string.Empty;

            foreach (var requestedLayerName in requestData.RequestedLayerNames)
            {
                if (!layerByName.TryGetValue(requestedLayerName, out var layerInfo))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(layerInfo.DefaultSrs))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(resolvedDefaultSrs))
                {
                    resolvedDefaultSrs = layerInfo.DefaultSrs;
                    continue;
                }

                if (!string.Equals(resolvedDefaultSrs, layerInfo.DefaultSrs, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The selected layers use different default SRS values. Please enter the SRS manually.");
                }
            }

            return (resolvedDefaultSrs, !string.IsNullOrWhiteSpace(resolvedDefaultSrs));
        }
    }
}
