using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using WfsCore;

namespace RhinoSpatial
{
    public class SpatialContextComponent : GH_TaskCapableComponent<SpatialContextComponent.SolveResults>
    {
        private readonly WfsClient _wfsClient = new();
        private readonly WmsClient _wmsClient = new();
        private bool _lastOpenMapRequest;

        public class SolveResults
        {
            public string SpatialContextText { get; init; } = string.Empty;

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private class RequestData
        {
            public string BaseUrl { get; init; } = string.Empty;

            public string LayerSelection { get; init; } = string.Empty;

            public string RequestedSrs { get; init; } = string.Empty;

            public bool UseAbsoluteCoordinates { get; init; }
        }

        public SpatialContextComponent()
            : base("Spatial Context", "Spatial Context",
                "Open the map helper and output a shared spatial context for RhinoSpatial sources.",
                "RhinoSpatial", "Context")
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
            pManager.AddTextParameter("SRS", "SRS", "Optional manual SRS override. If you already know the projection, enter it here and Spatial Context can work without any reference inputs.", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Reference Service URL", "Ref URL", "Optional WFS or WMS service URL used only to auto-detect the SRS and help the map open near the right place.", GH_ParamAccess.item);
            pManager.AddTextParameter("Reference Layer Name", "Reference Layer", "Optional single WFS or WMS layer name used only to auto-detect the SRS and initial extent. If multiple layer entries are connected here, RhinoSpatial will ignore them and fall back to the manual or default SRS instead.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Open Map", "Open Map", "Open the integrated map helper in your browser. Connect a Button to trigger it.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Use Absolute Coordinates", "Use Absolute Coordinates", "If true, sources using this Spatial Context keep source coordinates. If false, RhinoSpatial localizes geometry and imagery near the Rhino origin.", GH_ParamAccess.item, false);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared spatial selection and placement context for RhinoSpatial sources.", GH_ParamAccess.item);
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

                if (!string.IsNullOrWhiteSpace(result.SpatialContextText))
                {
                    dataAccess.SetData(0, result.SpatialContextText);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.SpatialContext.png");

        public override Guid ComponentGuid => new Guid("2096d34f-eb85-4b18-8c9d-6b292b2f6e5e");

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? requestedSrs = null;
            var openMap = false;
            var useAbsoluteCoordinates = false;
            string? baseUrl = null;
            string? layerSelection = null;

            dataAccess.GetData(0, ref requestedSrs);
            dataAccess.GetData(1, ref baseUrl);
            dataAccess.GetData(2, ref layerSelection);
            dataAccess.GetData(3, ref openMap);
            dataAccess.GetData(4, ref useAbsoluteCoordinates);

            if (Params.Input.Count > 2 && Params.Input[2].VolatileDataCount > 1)
            {
                layerSelection = null;
            }

            HandleOpenMap(openMap, baseUrl, layerSelection, requestedSrs);

            requestData = new RequestData
            {
                BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim(),
                LayerSelection = string.IsNullOrWhiteSpace(layerSelection) ? string.Empty : layerSelection.Trim(),
                RequestedSrs = string.IsNullOrWhiteSpace(requestedSrs) ? string.Empty : requestedSrs.Trim(),
                UseAbsoluteCoordinates = useAbsoluteCoordinates
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
                    Status = "Open the map, draw a rectangle, then this component will output the current spatial context.",
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }

            var srsResolution = ResolveSrsName(requestData);
            var supportedSrs = NormalizeSupportedMapSrs(srsResolution.ResolvedSrsName);

            if (string.IsNullOrWhiteSpace(supportedSrs))
            {
                return new SolveResults
                {
                    Status = $"The Spatial Context map helper currently supports EPSG:25832, EPSG:25833, EPSG:27700, EPSG:3857, EPSG:4283, EPSG:7423, EPSG:7844, and EPSG:4326, but the resolved SRS was '{srsResolution.ResolvedSrsName}'. Enter one of those SRS values manually if needed.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var boundingBoxText = supportedSrs.Equals("EPSG:4326", StringComparison.OrdinalIgnoreCase)
                ? selection.BoundingBox4326
                : supportedSrs.Equals("EPSG:7423", StringComparison.OrdinalIgnoreCase)
                    ? selection.BoundingBox4326
                : supportedSrs.Equals("EPSG:7844", StringComparison.OrdinalIgnoreCase)
                    ? selection.BoundingBox7844
                : supportedSrs.Equals("EPSG:4283", StringComparison.OrdinalIgnoreCase)
                    ? selection.BoundingBox4283
                : supportedSrs.Equals("EPSG:27700", StringComparison.OrdinalIgnoreCase)
                    ? selection.BoundingBox27700
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

            if (!WfsComponentInputParser.TryParseBoundingBox(boundingBoxText, out var resolvedBoundingBox, out _))
            {
                return new SolveResults
                {
                    Status = "The selected Bounding Box could not be converted into a reusable spatial context.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var spatialContext = RhinoSpatialContextTools.CreateSpatialContext(
                supportedSrs,
                resolvedBoundingBox!,
                TryGetBoundingBox(selection.BoundingBox4326),
                BuildBoundingBoxesBySrs(selection),
                requestData.UseAbsoluteCoordinates);

            return new SolveResults
            {
                SpatialContextText = WfsComponentInputParser.SerializeSpatialContext(spatialContext),
                Status = srsResolution.Status,
                MessageLevel = srsResolution.MessageLevel
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
                    SpatialContextText = string.Empty,
                    Status = ex.Message,
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }
        }

        private static Dictionary<string, BoundingBox2D> BuildBoundingBoxesBySrs(BoundingBoxHelperHost.BoundingBoxSelection selection)
        {
            var boundingBoxes = new Dictionary<string, BoundingBox2D>(StringComparer.OrdinalIgnoreCase);
            TryAddBoundingBox(boundingBoxes, "EPSG:4326", selection.BoundingBox4326);
            TryAddBoundingBox(boundingBoxes, "EPSG:7423", selection.BoundingBox4326);
            TryAddBoundingBox(boundingBoxes, "EPSG:25832", selection.BoundingBox25832);
            TryAddBoundingBox(boundingBoxes, "EPSG:25833", selection.BoundingBox25833);
            TryAddBoundingBox(boundingBoxes, "EPSG:27700", selection.BoundingBox27700);
            TryAddBoundingBox(boundingBoxes, "EPSG:3857", selection.BoundingBox3857);
            TryAddBoundingBox(boundingBoxes, "EPSG:4283", selection.BoundingBox4283);
            TryAddBoundingBox(boundingBoxes, "EPSG:7844", selection.BoundingBox7844);
            return boundingBoxes;
        }

        private static void TryAddBoundingBox(Dictionary<string, BoundingBox2D> target, string srsName, string text)
        {
            if (WfsComponentInputParser.TryParseBoundingBox(text, out var boundingBox, out _) && boundingBox is not null)
            {
                target[srsName] = boundingBox;
            }
        }

        private static BoundingBox2D? TryGetBoundingBox(string text)
        {
            return WfsComponentInputParser.TryParseBoundingBox(text, out var boundingBox, out _) ? boundingBox : null;
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
                    string.Empty,
                    null);
            }

            var layerName = WfsComponentInputParser.ParseLayerName(requestData.LayerSelection);

            if (string.IsNullOrWhiteSpace(layerName))
            {
                return (
                    "EPSG:25832",
                    string.Empty,
                    null);
            }

            try
            {
                var wfsLayers = _wfsClient.LoadLayersAsync(requestData.BaseUrl).GetAwaiter().GetResult();
                var wfsLayerInfo = wfsLayers.FirstOrDefault(layer => string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase));

                if (wfsLayerInfo is not null)
                {
                    var supportedWfsLayerSrs = ResolveSupportedLayerSrs(wfsLayerInfo);

                    if (!string.IsNullOrWhiteSpace(supportedWfsLayerSrs))
                    {
                        return (supportedWfsLayerSrs, string.Empty, null);
                    }

                    return (
                        wfsLayerInfo.DefaultSrs?.Trim() ?? string.Empty,
                        string.Empty,
                        null);
                }
            }
            catch
            {
            }

            try
            {
                var wmsCapabilities = _wmsClient.LoadCapabilitiesAsync(requestData.BaseUrl).GetAwaiter().GetResult();
                var wmsLayerInfo = wmsCapabilities.Layers.FirstOrDefault(layer => string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase));

                if (wmsLayerInfo is not null)
                {
                    var supportedWmsLayerSrs = ResolveSupportedLayerSrs(wmsLayerInfo);

                    if (!string.IsNullOrWhiteSpace(supportedWmsLayerSrs))
                    {
                        return (supportedWmsLayerSrs, string.Empty, null);
                    }
                }
            }
            catch
            {
            }

            return (
                "EPSG:25832",
                "The reference service SRS could not be auto-detected, so the spatial context component used the default projected SRS EPSG:25832. Enter the SRS manually if needed.",
                GH_RuntimeMessageLevel.Warning);
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

            if (srsName.Contains("27700", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:27700";
            }

            if (srsName.Contains("4283", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4283";
            }

            if (srsName.Contains("7423", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7423";
            }

            if (srsName.Contains("7844", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7844";
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

        private static string ResolveSupportedLayerSrs(WfsLayerInfo layerInfo)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(layerInfo.DefaultSrs))
            {
                candidates.Add(layerInfo.DefaultSrs);
            }

            candidates.AddRange(layerInfo.OtherSrs);
            return ChoosePreferredSupportedSrs(candidates);
        }

        private static string ResolveSupportedLayerSrs(WmsLayerInfo layerInfo)
        {
            return ChoosePreferredSupportedSrs(layerInfo.SupportedSrs);
        }

        private static string ChoosePreferredSupportedSrs(IEnumerable<string> candidates)
        {
            string bestSrs = string.Empty;
            var bestRank = int.MinValue;

            foreach (var candidate in candidates)
            {
                var normalized = NormalizeSupportedMapSrs(candidate);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                var rank = GetSupportedSrsRank(normalized);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    bestSrs = normalized;
                }
            }

            return bestSrs;
        }

        private static int GetSupportedSrsRank(string normalizedSrs)
        {
            return normalizedSrs switch
            {
                "EPSG:25832" => 100,
                "EPSG:25833" => 95,
                "EPSG:27700" => 90,
                "EPSG:3857" => 70,
                "EPSG:7844" => 40,
                "EPSG:4283" => 35,
                "EPSG:4326" => 10,
                "EPSG:7423" => 5,
                _ => 0
            };
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

            if (!string.IsNullOrWhiteSpace(normalizedRequestedSrs))
            {
                var wfsLayerInfo = await TryResolveWfsLayerInfoAsync(baseUrl, layerName);
                if (wfsLayerInfo is not null)
                {
                    return (wfsLayerInfo.Wgs84BoundingBox, normalizedRequestedSrs);
                }

                var wmsLayerInfo = await TryResolveWmsLayerInfoAsync(baseUrl, layerName);
                return (wmsLayerInfo?.Wgs84BoundingBox, normalizedRequestedSrs);
            }

            var resolvedWfsLayerInfo = await TryResolveWfsLayerInfoAsync(baseUrl, layerName);
            if (resolvedWfsLayerInfo is not null)
            {
                return (
                    resolvedWfsLayerInfo.Wgs84BoundingBox,
                    ResolveSupportedLayerSrs(resolvedWfsLayerInfo));
            }

            var resolvedWmsLayerInfo = await TryResolveWmsLayerInfoAsync(baseUrl, layerName);
            return (
                resolvedWmsLayerInfo?.Wgs84BoundingBox,
                resolvedWmsLayerInfo is null ? string.Empty : ResolveSupportedLayerSrs(resolvedWmsLayerInfo));
        }

        private async Task<WfsLayerInfo?> TryResolveWfsLayerInfoAsync(string baseUrl, string layerName)
        {
            try
            {
                var layers = await _wfsClient.LoadLayersAsync(baseUrl);
                return layers.FirstOrDefault(layer => string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private async Task<WmsLayerInfo?> TryResolveWmsLayerInfoAsync(string baseUrl, string layerName)
        {
            try
            {
                var capabilities = await _wmsClient.LoadCapabilitiesAsync(baseUrl);
                return capabilities.Layers.FirstOrDefault(layer => string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }
    }
}
