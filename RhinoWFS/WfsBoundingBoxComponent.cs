using System;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using WfsCore;

namespace RhinoWFS
{
    public class WfsBoundingBoxComponent : GH_TaskCapableComponent<WfsBoundingBoxComponent.SolveResults>
    {
        private readonly WfsClient _wfsClient = new();
        private bool _lastOpenMapRequest;

        public class SolveResults
        {
            public string BoundingBoxText { get; init; } = string.Empty;

            public string ResolvedSrs { get; init; } = string.Empty;

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private class RequestData
        {
            public string BaseUrl { get; init; } = string.Empty;

            public string LayerSelection { get; init; } = string.Empty;

            public string RequestedSrs { get; init; } = string.Empty;
        }

        public WfsBoundingBoxComponent()
            : base("WFS Bounding Box", "WFS Bounding Box",
                "Open the Bounding Box map helper and output a bounding box plus matching SRS for Load WFS.",
                "RhinoWFS", "WFS")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            BoundingBoxHelperHost.SelectionChanged += HandleSelectionChanged;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            BoundingBoxHelperHost.SelectionChanged -= HandleSelectionChanged;
            base.RemovedFromDocument(document);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("WFS URL", "WFS URL", "Optional WFS URL used to auto-detect the layer SRS. You can pass this through later into Load WFS.", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "Layer", "Optional selected layer entry. Connect one chosen layer from List WFS Layers to auto-detect the SRS.", GH_ParamAccess.item);
            pManager.AddTextParameter("SRS", "SRS", "Optional manual SRS override. Leave empty to auto-detect the layer SRS. If no layer data is connected, the helper falls back to EPSG:25832.", GH_ParamAccess.item, string.Empty);
            pManager.AddBooleanParameter("Open Map", "Open Map", "Open the integrated Bounding Box helper in your browser. Connect a Button to trigger it.", GH_ParamAccess.item, false);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Bounding Box", "Bounding Box", "Bounding box text in the format minX,minY,maxX,maxY.", GH_ParamAccess.item);
            pManager.AddTextParameter("SRS", "SRS", "The SRS that matches the Bounding Box output.", GH_ParamAccess.item);
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
                    Task<SolveResults> task = Task.Run(() => Compute(requestData), CancelToken);
                    TaskList.Add(task);
                    return;
                }

                if (!GetSolveResults(dataAccess, out SolveResults result))
                {
                    result = Compute(requestData);
                }

                if (!string.IsNullOrWhiteSpace(result.Status) && result.MessageLevel.HasValue)
                {
                    AddRuntimeMessage(result.MessageLevel.Value, result.Status);
                }

                if (!string.IsNullOrWhiteSpace(result.BoundingBoxText))
                {
                    dataAccess.SetData(0, result.BoundingBoxText);
                }

                if (!string.IsNullOrWhiteSpace(result.ResolvedSrs))
                {
                    dataAccess.SetData(1, result.ResolvedSrs);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoWFS.Resources.BoundingBoxWfs.png");

        public override Guid ComponentGuid => new Guid("2096d34f-eb85-4b18-8c9d-6b292b2f6e5e");

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? baseUrl = null;
            string? layerSelection = null;
            string? requestedSrs = null;
            var openMap = false;

            dataAccess.GetData(0, ref baseUrl);
            dataAccess.GetData(1, ref layerSelection);
            dataAccess.GetData(2, ref requestedSrs);
            dataAccess.GetData(3, ref openMap);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                WfsUpstreamContextResolver.TryResolveBaseUrlFromLayerInput(Params.Input[1], out var resolvedBaseUrl);

                if (!string.IsNullOrWhiteSpace(resolvedBaseUrl))
                {
                    baseUrl = resolvedBaseUrl;
                }
            }

            HandleOpenMap(openMap, baseUrl, layerSelection, requestedSrs);

            requestData = new RequestData
            {
                BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim(),
                LayerSelection = string.IsNullOrWhiteSpace(layerSelection) ? string.Empty : layerSelection.Trim(),
                RequestedSrs = string.IsNullOrWhiteSpace(requestedSrs) ? string.Empty : requestedSrs.Trim()
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            var selection = BoundingBoxHelperHost.GetLatestSelection();

            if (!selection.HasSelection)
            {
                return new SolveResults
                {
                    Status = "Open the map, draw a rectangle, then this component will output the current Bounding Box.",
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }

            var srsResolution = ResolveSrsName(requestData);
            var supportedSrs = NormalizeSupportedMapSrs(srsResolution.ResolvedSrsName);

            if (string.IsNullOrWhiteSpace(supportedSrs))
            {
                return new SolveResults
                {
                    Status = $"The Bounding Box helper currently supports EPSG:25832, EPSG:25833, EPSG:3857, and EPSG:4326, but the resolved SRS was '{srsResolution.ResolvedSrsName}'. Enter one of those SRS values manually if needed.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var boundingBoxText = supportedSrs.Equals("EPSG:4326", StringComparison.OrdinalIgnoreCase)
                ? selection.BoundingBox4326
                : supportedSrs.Equals("EPSG:3857", StringComparison.OrdinalIgnoreCase)
                    ? selection.BoundingBox3857
                : supportedSrs.Equals("EPSG:25833", StringComparison.OrdinalIgnoreCase)
                    ? selection.BoundingBox25833
                    : selection.BoundingBox25832;

            if (string.IsNullOrWhiteSpace(boundingBoxText))
            {
                return new SolveResults
                {
                    Status = "The map selection could not be converted to the requested SRS.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            return new SolveResults
            {
                BoundingBoxText = boundingBoxText,
                ResolvedSrs = supportedSrs,
                Status = srsResolution.Status,
                MessageLevel = srsResolution.MessageLevel
            };
        }

        private (string ResolvedSrsName, string Status, GH_RuntimeMessageLevel? MessageLevel) ResolveSrsName(RequestData requestData)
        {
            if (!string.IsNullOrWhiteSpace(requestData.RequestedSrs))
            {
                return (requestData.RequestedSrs, string.Empty, null);
            }

            if (string.IsNullOrWhiteSpace(requestData.BaseUrl) || string.IsNullOrWhiteSpace(requestData.LayerSelection))
            {
                return (
                    "EPSG:25832",
                    "No WFS URL and layer were connected, so the Bounding Box helper used the default projected SRS EPSG:25832.",
                    GH_RuntimeMessageLevel.Warning);
            }

            var layerName = WfsComponentInputParser.ParseLayerName(requestData.LayerSelection);

            if (string.IsNullOrWhiteSpace(layerName))
            {
                return (
                    "EPSG:25832",
                    "The connected layer entry could not be read, so the Bounding Box helper used the default projected SRS EPSG:25832.",
                    GH_RuntimeMessageLevel.Warning);
            }

            var layers = _wfsClient.LoadLayersAsync(requestData.BaseUrl).GetAwaiter().GetResult();
            var layerInfo = layers.FirstOrDefault(layer => string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase));

            if (layerInfo is null || string.IsNullOrWhiteSpace(layerInfo.DefaultSrs))
            {
                return (
                    "EPSG:25832",
                    "The layer SRS could not be auto-detected, so the Bounding Box helper used the default projected SRS EPSG:25832.",
                    GH_RuntimeMessageLevel.Warning);
            }

            return (layerInfo.DefaultSrs.Trim(), string.Empty, null);
        }

        private static string NormalizeSupportedMapSrs(string srsName)
        {
            if (string.IsNullOrWhiteSpace(srsName))
            {
                return string.Empty;
            }

            if (srsName.Contains("25832", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25832";
            }

            if (srsName.Contains("25833", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25833";
            }

            if (srsName.Contains("3857", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:3857";
            }

            if (srsName.Contains("4326", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4326";
            }

            return string.Empty;
        }

        private void HandleOpenMap(bool openMap, string? baseUrl, string? layerSelection, string? requestedSrs)
        {
            if (!openMap)
            {
                _lastOpenMapRequest = false;
                return;
            }

            if (_lastOpenMapRequest)
            {
                return;
            }

            _lastOpenMapRequest = true;
            _ = Task.Run(() => OpenMapAsync(baseUrl, layerSelection, requestedSrs));
        }

        private void HandleSelectionChanged(object? sender, EventArgs e)
        {
            var document = OnPingDocument();

            if (document is null)
            {
                return;
            }

            document.ScheduleSolution(1, _ => ExpireSolution(false));
        }

        private async Task OpenMapAsync(string? baseUrl, string? layerSelection, string? requestedSrs)
        {
            try
            {
                var (initialViewBoundingBox, preferredSrs) = await ResolveInitialMapContextAsync(baseUrl, layerSelection, requestedSrs);
                BoundingBoxHelperHost.OpenInBrowser(initialViewBoundingBox, preferredSrs);
            }
            catch
            {
                BoundingBoxHelperHost.OpenInBrowser();
            }
        }

        private async Task<(BoundingBox2D? InitialViewBoundingBox4326, string PreferredSrs)> ResolveInitialMapContextAsync(string? baseUrl, string? layerSelection, string? requestedSrs)
        {
            var normalizedRequestedSrs = NormalizeSupportedMapSrs(requestedSrs ?? string.Empty);
            var layerName = WfsComponentInputParser.ParseLayerName(layerSelection ?? string.Empty);

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(layerName))
            {
                return (null, normalizedRequestedSrs);
            }

            var layers = await _wfsClient.LoadLayersAsync(baseUrl);
            var layerInfo = layers.FirstOrDefault(layer => string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(normalizedRequestedSrs))
            {
                return (layerInfo?.Wgs84BoundingBox, normalizedRequestedSrs);
            }

            return (layerInfo?.Wgs84BoundingBox, NormalizeSupportedMapSrs(layerInfo?.DefaultSrs ?? string.Empty));
        }
    }
}
