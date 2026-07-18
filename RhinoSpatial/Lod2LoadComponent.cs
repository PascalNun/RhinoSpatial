using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
            public string Source { get; init; } = string.Empty;

            public string? LayerName { get; init; }

            public SpatialContext2D SpatialContext { get; init; } = null!;

            public Lod2SourceKind SourceKind { get; init; }
        }

        private sealed record LocalCityGmlCandidate(
            string DisplayName,
            string SourcePath,
            Func<string> ReadText,
            string SourceSrs,
            BoundingBox2D? SourceBoundingBox,
            long SourceByteLength);

        private sealed record LocalCityGmlSourceFile(
            string DisplayName,
            string SourcePath,
            Func<Stream> OpenRead,
            Func<string> ReadText,
            long ByteLength);

        private sealed class LocalCityGmlTimings
        {
            public TimeSpan CandidateScan { get; set; }

            public TimeSpan ParseAndBuildingFilter { get; set; }

            public TimeSpan SurfaceFilter { get; set; }

            public TimeSpan BrepOutput { get; set; }
        }

        private sealed class Lod2OutputData
        {
            public GH_Structure<GH_Brep> BrepTree { get; init; } = new();

            public RhinoSpatialLod2OutputBuilder.BuildReport BuildReport { get; init; } = null!;
        }

        private enum Lod2SourceKind
        {
            Url,
            File,
            Directory,
            Zip
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
            pManager.AddTextParameter("LoD2 Source", "LoD2 Source", "LoD2 source URL, local CityGML/GML/XML/CityJSON file, folder, or ZIP archive. If left empty, RhinoSpatial will try to inherit a WFS URL from the connected Layer input.", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "Layer", "Optional LoD2 building layer name for WFS sources, or local layer label for file/folder/ZIP sources.", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context. LoD2 requests use EPSG:7423/4326 internally, so any Spatial Context created from the map helper will align correctly.", GH_ParamAccess.item);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
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

            string? source = null;
            string? layerName = null;
            string? spatialContextText = null;

            dataAccess.GetData(0, ref source);
            dataAccess.GetData(1, ref layerName);
            dataAccess.GetData(2, ref spatialContextText);

            if (string.IsNullOrWhiteSpace(source))
            {
                WfsLayerInputResolver.TryResolveBaseUrlFromLayerInput(Params.Input[1], out var resolvedBaseUrl);
                if (!string.IsNullOrWhiteSpace(resolvedBaseUrl))
                {
                    source = resolvedBaseUrl;
                }
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "LoD2 Source is required. Connect a LoD2 WFS URL, local CityGML/GML/XML file, folder, ZIP archive, or a layer input that carries a service URL.");
                return false;
            }

            var trimmedSource = source.Trim();
            if (!IsUrlSource(trimmedSource) && !File.Exists(trimmedSource) && !Directory.Exists(trimmedSource))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"LoD2 Source was not found as a local file or folder: {trimmedSource}");
                return false;
            }

            var sourceKind = ResolveSourceKind(trimmedSource);

            if (!RhinoSpatialInputParser.TryGetRequiredSpatialContext(spatialContextText, out var spatialContext, out var spatialContextError))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, spatialContextError);
                return false;
            }

            requestData = new RequestData
            {
                Source = trimmedSource,
                LayerName = string.IsNullOrWhiteSpace(layerName) ? null : RhinoSpatialInputParser.ParseLayerName(layerName),
                SpatialContext = spatialContext,
                SourceKind = sourceKind
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            if (requestData.SourceKind != Lod2SourceKind.Url)
            {
                if (LocalSourceContainsCityJson(requestData.Source, requestData.SourceKind))
                {
                    return ComputeLocalCityJsonSource(requestData);
                }

                return ComputeLocalCityGmlSource(requestData);
            }

            var capabilities = _wfsClient.LoadCapabilitiesAsync(requestData.Source).GetAwaiter().GetResult();
            var resolvedLayer = ResolveLayer(capabilities, requestData.LayerName);
            var requestSrs = ResolveRequestSrs(resolvedLayer.LayerInfo, requestData.SpatialContext);

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
                BaseUrl = requestData.Source,
                TypeName = resolvedLayer.LayerName,
                MaxFeatures = 0,
                Version = "2.0.0",
                SrsName = requestSrs,
                OutputFormat = "application/gml+xml; version=3.2",
                BoundingBox = CreateBufferedBoundingBox(requestBoundingBox, requestSrs, DefaultRequestBufferMeters)
            };

            var response = _wfsClient.LoadFeatureResponseAsync(requestOptions).GetAwaiter().GetResult();
            var returnedBuildings = Lod2GmlReader.ReadBuildings(response.ResponseText, resolvedLayer.LayerName);
            var buildings = FilterBuildingSurfacesToRequestBounds(returnedBuildings, requestBoundingBox);
            var outputData = BuildLod2Output(
                requestData,
                buildings,
                new[] { resolvedLayer.LayerName },
                requestSrs,
                requestBoundingBox);

            return new SolveResults
            {
                BrepTree = outputData.BrepTree,
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
                    outputData.BuildReport),
                MessageLevel = buildings.Count == 0
                    ? GH_RuntimeMessageLevel.Warning
                    : HasLod2BuildWarnings(outputData.BuildReport)
                        ? GH_RuntimeMessageLevel.Remark
                    : string.IsNullOrWhiteSpace(response.StatusNote)
                        ? null
                        : GH_RuntimeMessageLevel.Remark
            };
        }

        private SolveResults ComputeLocalCityGmlSource(RequestData requestData)
        {
            var timings = new LocalCityGmlTimings();
            var stopwatch = Stopwatch.StartNew();
            var candidates = LoadLocalCityGmlCandidates(requestData, requestData.SourceKind, out var scannedFileCount, out var skippedByBoundsCount);
            timings.CandidateScan = stopwatch.Elapsed;

            if (candidates.Count == 0)
            {
                return new SolveResults
                {
                    Status = $"No CityGML/GML/XML building files intersecting the current Spatial Context were found in the LoD2 Source. Scanned {scannedFileCount} candidate file(s); skipped {skippedByBoundsCount} outside the Spatial Context using file bounds. Timings: source scan {FormatDuration(timings.CandidateScan)}.",
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }

            var candidateSrsValues = candidates
                .Select(candidate => candidate.SourceSrs)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidateSrsValues.Count > 1)
            {
                return new SolveResults
                {
                    Status = $"The local LoD2 source contains files with multiple source SRS values ({string.Join(", ", candidateSrsValues)}). Split the source into matching CRS groups for now.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var sourceSrs = candidateSrsValues[0];

            if (!RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(
                    requestData.SpatialContext,
                    sourceSrs,
                    out var requestBoundingBox,
                    out _))
            {
                var availableSrs = requestData.SpatialContext.BoundingBoxesBySrs.Count == 0
                    ? "none"
                    : string.Join(", ", requestData.SpatialContext.BoundingBoxesBySrs.Keys.OrderBy(key => key));

                return new SolveResults
                {
                    Status = $"The Spatial Context does not include a bounding box for the CityGML source SRS '{sourceSrs}'. Available SRS values: {availableSrs}. If the file uses a different CRS, set the Spatial Context SRS to match or redraw the context with a supported SRS.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var returnedBuildings = new List<Lod2Building>();
            var parsedFileCount = 0;
            var failedFileCount = 0;
            var failedFileNames = new List<string>();
            long totalByteCount = 0;

            stopwatch.Restart();
            foreach (var candidate in candidates)
            {
                try
                {
                    var gmlText = candidate.ReadText();
                    var candidateBuildings = Lod2GmlReader.ReadBuildings(gmlText, candidate.DisplayName, requestBoundingBox);
                    returnedBuildings.AddRange(candidateBuildings);
                    parsedFileCount++;
                    totalByteCount += candidate.SourceByteLength > 0
                        ? candidate.SourceByteLength
                        : Encoding.UTF8.GetByteCount(gmlText);
                }
                catch
                {
                    failedFileCount++;
                    if (failedFileNames.Count < 5)
                    {
                        failedFileNames.Add(candidate.DisplayName);
                    }
                }
            }
            timings.ParseAndBuildingFilter = stopwatch.Elapsed;

            stopwatch.Restart();
            var buildings = FilterBuildingSurfacesToRequestBounds(returnedBuildings, requestBoundingBox);
            timings.SurfaceFilter = stopwatch.Elapsed;
            var layerOrder = candidates
                .Select(candidate => candidate.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            stopwatch.Restart();
            var outputData = BuildLod2Output(
                requestData,
                buildings,
                layerOrder,
                sourceSrs,
                requestBoundingBox);
            timings.BrepOutput = stopwatch.Elapsed;
            var sourceLabel = BuildLocalSourceLabel(requestData.Source, requestData.SourceKind);

            return new SolveResults
            {
                BrepTree = outputData.BrepTree,
                BuildingCount = buildings.Count,
                Status = BuildLocalCityGmlStatusMessage(
                    buildings.Count,
                    sourceLabel,
                    sourceSrs,
                    requestData.SpatialContext.UseAbsoluteCoordinates,
                    requestData.SpatialContext,
                    requestBoundingBox,
                    returnedBuildings.Count,
                    totalByteCount,
                    requestData.Source,
                    requestData.SourceKind,
                    scannedFileCount,
                    candidates.Count,
                    parsedFileCount,
                    skippedByBoundsCount,
                    failedFileCount,
                    failedFileNames,
                    timings,
                    outputData.BuildReport),
                MessageLevel = buildings.Count == 0
                    ? GH_RuntimeMessageLevel.Warning
                    : HasLod2BuildWarnings(outputData.BuildReport)
                        ? GH_RuntimeMessageLevel.Remark
                        : null
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

        private SolveResults ComputeLocalCityJsonSource(RequestData requestData)
        {
            var sourceFiles = EnumerateLocalCityJsonSourceFiles(requestData.Source, requestData.SourceKind).ToList();
            if (sourceFiles.Count == 0)
            {
                return new SolveResults
                {
                    Status = "No CityJSON files were found in the local LoD2 source.",
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }

            var candidates = new List<(LocalCityGmlSourceFile File, string SourceSrs, CityJsonSourceMetadata Metadata)>();
            var skippedByBoundsCount = 0;
            var failedFileNames = new List<string>();

            foreach (var sourceFile in sourceFiles)
            {
                CityJsonSourceMetadata metadata;
                try
                {
                    var jsonText = sourceFile.ReadText();
                    metadata = CityJsonReader.ReadSourceMetadata(jsonText);
                }
                catch
                {
                    if (failedFileNames.Count < 5)
                    {
                        failedFileNames.Add(sourceFile.DisplayName);
                    }

                    continue;
                }

                var candidateSrs = ResolveLocalCityGmlSrs(metadata.SrsName, requestData.SpatialContext);

                if (metadata.BoundingBox is not null &&
                    RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(
                        requestData.SpatialContext,
                        candidateSrs,
                        out var contextBoundingBox,
                        out _) &&
                    !RhinoSpatialContextTools.DoBoundingBoxesIntersect(metadata.BoundingBox, contextBoundingBox))
                {
                    skippedByBoundsCount++;
                    continue;
                }

                candidates.Add((sourceFile, candidateSrs, metadata));
            }

            if (candidates.Count == 0)
            {
                return new SolveResults
                {
                    Status = $"No CityJSON building files intersecting the current Spatial Context were found in the LoD2 Source. Scanned {sourceFiles.Count} file(s); skipped {skippedByBoundsCount} outside the Spatial Context using file bounds.",
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }

            var candidateSrsValues = candidates
                .Select(candidate => candidate.SourceSrs)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidateSrsValues.Count > 1)
            {
                return new SolveResults
                {
                    Status = $"The local CityJSON source contains files with multiple source SRS values ({string.Join(", ", candidateSrsValues)}). Split the source into matching CRS groups for now.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var sourceSrs = candidateSrsValues[0];
            if (!RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(
                    requestData.SpatialContext,
                    sourceSrs,
                    out var requestBoundingBox,
                    out _))
            {
                var availableSrs = requestData.SpatialContext.BoundingBoxesBySrs.Count == 0
                    ? "none"
                    : string.Join(", ", requestData.SpatialContext.BoundingBoxesBySrs.Keys.OrderBy(key => key));

                return new SolveResults
                {
                    Status = $"The Spatial Context does not include a bounding box for the CityJSON source SRS '{sourceSrs}'. Available SRS values: {availableSrs}.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var returnedBuildings = new List<Lod2Building>();
            var parsedFileCount = 0;
            long totalByteCount = 0;

            foreach (var candidate in candidates)
            {
                try
                {
                    var jsonText = candidate.File.ReadText();
                    var displayName = string.IsNullOrWhiteSpace(requestData.LayerName)
                        ? candidate.File.DisplayName
                        : requestData.LayerName;
                    returnedBuildings.AddRange(CityJsonReader.ReadBuildings(jsonText, displayName, requestBoundingBox));
                    parsedFileCount++;
                    totalByteCount += candidate.File.ByteLength > 0
                        ? candidate.File.ByteLength
                        : Encoding.UTF8.GetByteCount(jsonText);
                }
                catch
                {
                    if (failedFileNames.Count < 5)
                    {
                        failedFileNames.Add(candidate.File.DisplayName);
                    }
                }
            }

            var buildings = FilterBuildingSurfacesToRequestBounds(returnedBuildings, requestBoundingBox);
            var layerOrder = candidates
                .Select(candidate => string.IsNullOrWhiteSpace(requestData.LayerName) ? candidate.File.DisplayName : requestData.LayerName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var outputData = BuildLod2Output(
                requestData,
                buildings,
                layerOrder,
                sourceSrs,
                requestBoundingBox);

            var alignmentNote = requestData.SpatialContext.UseAbsoluteCoordinates
                ? "with absolute elevation."
                : "and aligned them to the shared local terrain/building elevation baseline.";
            var failedNote = failedFileNames.Count == 0
                ? string.Empty
                : $" Example failed file(s): {string.Join(", ", failedFileNames)}.";

            return new SolveResults
            {
                BrepTree = outputData.BrepTree,
                BuildingCount = buildings.Count,
                Status = $"Loaded {buildings.Count} local CityJSON building Brep set(s) from {FormatLocalSourceKind(requestData.SourceKind)} '{BuildLocalSourceLabel(requestData.Source, requestData.SourceKind)}' using source SRS '{sourceSrs}' {alignmentNote} Parsed {outputData.BuildReport.ParsedBuildingCount} building(s), {outputData.BuildReport.ParsedSurfaceCount} source surface(s); created {outputData.BuildReport.OutputBrepCount} Brep object(s) from {outputData.BuildReport.ConstructedSurfaceCount} converted surface(s). Source files: scanned {sourceFiles.Count}, selected {candidates.Count}, parsed {parsedFileCount}, skipped {skippedByBoundsCount} outside the Spatial Context by file bounds, failed {failedFileNames.Count}. Parsed source size: {FormatByteLength(totalByteCount)}. Request bounds {sourceSrs}: {FormatBoundingBox2D(requestBoundingBox)}.{failedNote}",
                MessageLevel = buildings.Count == 0
                    ? GH_RuntimeMessageLevel.Warning
                    : HasLod2BuildWarnings(outputData.BuildReport) || failedFileNames.Count > 0
                        ? GH_RuntimeMessageLevel.Remark
                        : null
            };
        }

        private static Lod2OutputData BuildLod2Output(
            RequestData requestData,
            IReadOnlyList<Lod2Building> buildings,
            IReadOnlyList<string> layerOrder,
            string sourceSrs,
            BoundingBox2D requestBoundingBox)
        {
            var targetBoundingBox = requestData.SpatialContext.RequestBoundingBox;
            var placementPoint = new Point3d(
                requestData.SpatialContext.PlacementOrigin.X,
                requestData.SpatialContext.PlacementOrigin.Y,
                0.0);
            var elevationBase = requestData.SpatialContext.UseAbsoluteCoordinates
                ? 0.0
                : SpatialElevationBaselineCache.ResolveOrStore(
                    requestData.SpatialContext,
                    RhinoSpatialLod2OutputBuilder.CalculateElevationBase(buildings));
            var brepTree = RhinoSpatialLod2OutputBuilder.BuildBrepTree(
                buildings,
                layerOrder,
                sourceSrs,
                requestData.SpatialContext.ResolvedSrs,
                requestBoundingBox,
                targetBoundingBox,
                placementPoint,
                requestData.SpatialContext.UseAbsoluteCoordinates,
                elevationBase,
                out var buildReport,
                skipBuildingShellPostProcessing: true);

            return new Lod2OutputData
            {
                BrepTree = brepTree,
                BuildReport = buildReport
            };
        }

        private static bool HasLod2BuildWarnings(RhinoSpatialLod2OutputBuilder.BuildReport buildReport)
        {
            return buildReport.FailedSurfaceBrepCount > 0 ||
                   buildReport.MalformedLoopSurfaceCount > 0 ||
                   buildReport.BuildingsWithoutOutputCount > 0;
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

        private static string BuildLocalCityGmlStatusMessage(
            int buildingCount,
            string sourceLabel,
            string sourceSrs,
            bool useAbsoluteCoordinates,
            SpatialContext2D spatialContext,
            BoundingBox2D requestBoundingBox,
            int returnedBuildingCount,
            long parsedByteCount,
            string sourcePath,
            Lod2SourceKind sourceKind,
            int scannedFileCount,
            int candidateFileCount,
            int parsedFileCount,
            int skippedByBoundsCount,
            int failedFileCount,
            IReadOnlyList<string> failedFileNames,
            LocalCityGmlTimings timings,
            RhinoSpatialLod2OutputBuilder.BuildReport buildReport)
        {
            if (buildingCount == 0)
            {
                var emptyStatus = returnedBuildingCount > 0
                    ? $"The local LoD2 source contained {returnedBuildingCount} building(s), but none intersected the current Spatial Context. File bounds were evaluated in '{sourceSrs}' against {FormatBoundingBox2D(requestBoundingBox)}."
                    : "No LoD2/CityGML buildings were found in the local source.";

                return $"{emptyStatus} Scanned {scannedFileCount} file(s), selected {candidateFileCount}, parsed {parsedFileCount}, skipped {skippedByBoundsCount} outside the Spatial Context by file bounds, failed {failedFileCount}. Timings: {FormatLocalTimings(timings)}.";
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
                diagnosticNote += $" Local source contained {returnedBuildingCount} building(s); kept {buildReport.ParsedBuildingCount} intersecting the Spatial Context.";
            }

            diagnosticNote += $" Source files: scanned {scannedFileCount}, selected {candidateFileCount}, parsed {parsedFileCount}, skipped {skippedByBoundsCount} outside the Spatial Context by file bounds, failed {failedFileCount}.";
            diagnosticNote += " Building- and surface-level bounds prefiltering was applied before CityGML surface conversion.";
            diagnosticNote += $" Timings: {FormatLocalTimings(timings)}.";
            diagnosticNote += $" Parsed source size: {FormatByteLength(parsedByteCount)}.";
            diagnosticNote += $" Source bounds {sourceSrs}: {FormatBoundingBox2D(requestBoundingBox)}.";
            diagnosticNote += $" Context local XY bounds: {FormatLocalContextBounds(spatialContext)}.";
            diagnosticNote += buildReport.TransformedSurfaceBounds.HasValue
                ? $" Returned CityGML local bounds: {FormatBoundingBox(buildReport.TransformedSurfaceBounds.Value)}."
                : " Returned CityGML local bounds: none.";
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

            if (buildReport.FastSurfaceOutputBuildingCount > 0)
            {
                diagnosticNote += $" Used fast local CityGML surface output for {buildReport.FastSurfaceOutputBuildingCount} building(s) / {buildReport.FastSurfaceOutputBrepCount} Brep face(s), skipping expensive shell join/repair.";
            }

            if (failedFileNames.Count > 0)
            {
                diagnosticNote += $" Example failed file(s): {string.Join(", ", failedFileNames)}.";
            }

            return $"Loaded {buildingCount} local CityGML building Brep set(s) from {FormatLocalSourceKind(sourceKind)} '{BuildLocalSourceLabel(sourcePath, sourceKind)}' as '{sourceLabel}' using source SRS '{sourceSrs}' {alignmentNote}{diagnosticNote}";
        }

        private static string ResolveLocalCityGmlSrs(string fileSrs, SpatialContext2D spatialContext)
        {
            var normalizedFileSrs = RhinoSpatialContextTools.NormalizeSrsKey(fileSrs);

            return string.IsNullOrWhiteSpace(normalizedFileSrs)
                ? RhinoSpatialContextTools.NormalizeSrsKey(spatialContext.ResolvedSrs)
                : normalizedFileSrs;
        }

        private static List<LocalCityGmlCandidate> LoadLocalCityGmlCandidates(
            RequestData requestData,
            Lod2SourceKind sourceKind,
            out int scannedFileCount,
            out int skippedByBoundsCount)
        {
            scannedFileCount = 0;
            skippedByBoundsCount = 0;
            var candidates = new List<LocalCityGmlCandidate>();

            foreach (var sourceFile in EnumerateLocalSourceFiles(requestData.Source, sourceKind))
            {
                scannedFileCount++;

                CityGmlSourceMetadata sourceMetadata;
                using (var sourceStream = sourceFile.OpenRead())
                {
                    sourceMetadata = Lod2GmlReader.ReadSourceMetadata(sourceStream);
                }

                var sourceSrs = ResolveLocalCityGmlSrs(sourceMetadata.SrsName, requestData.SpatialContext);
                var sourceBoundingBox = sourceMetadata.BoundingBox;

                if (sourceBoundingBox is not null &&
                    RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(
                        requestData.SpatialContext,
                        sourceSrs,
                        out var contextBoundingBox,
                        out _) &&
                    !RhinoSpatialContextTools.DoBoundingBoxesIntersect(sourceBoundingBox, contextBoundingBox))
                {
                    skippedByBoundsCount++;
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(requestData.LayerName)
                    ? sourceFile.DisplayName
                    : requestData.LayerName;
                candidates.Add(new LocalCityGmlCandidate(
                    displayName,
                    sourceFile.SourcePath,
                    sourceFile.ReadText,
                    sourceSrs,
                    sourceBoundingBox,
                    sourceFile.ByteLength));
            }

            return candidates;
        }

        private static IEnumerable<LocalCityGmlSourceFile> EnumerateLocalSourceFiles(string source, Lod2SourceKind sourceKind)
        {
            if (sourceKind == Lod2SourceKind.File)
            {
                if (IsCityGmlFileName(source))
                {
                    yield return new LocalCityGmlSourceFile(
                        Path.GetFileNameWithoutExtension(source),
                        source,
                        () => File.OpenRead(source),
                        () => File.ReadAllText(source),
                        new FileInfo(source).Length);
                }

                yield break;
            }

            if (sourceKind == Lod2SourceKind.Directory)
            {
                foreach (var filePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                             .Where(IsCityGmlFileName)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new LocalCityGmlSourceFile(
                        Path.GetFileNameWithoutExtension(filePath),
                        filePath,
                        () => File.OpenRead(filePath),
                        () => File.ReadAllText(filePath),
                        new FileInfo(filePath).Length);
                }

                yield break;
            }

            if (sourceKind == Lod2SourceKind.Zip)
            {
                using var archive = ZipFile.OpenRead(source);
                foreach (var entry in archive.Entries
                             .Where(entry => IsCityGmlFileName(entry.FullName))
                             .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    var entryName = entry.FullName;
                    yield return new LocalCityGmlSourceFile(
                        Path.GetFileNameWithoutExtension(entry.FullName),
                        $"{source}!{entry.FullName}",
                        () => OpenZipEntryReadStream(source, entryName),
                        () => ReadZipEntryText(source, entryName),
                        entry.Length);
                }
            }
        }

        private static Stream OpenZipEntryReadStream(string zipPath, string entryName)
        {
            var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(entryName);
            if (entry is null)
            {
                archive.Dispose();
                throw new FileNotFoundException($"ZIP entry was not found: {entryName}", entryName);
            }

            return new ZipEntryReadStream(archive, entry.Open());
        }

        private static string ReadZipEntryText(string zipPath, string entryName)
        {
            using var stream = OpenZipEntryReadStream(zipPath, entryName);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private sealed class ZipEntryReadStream : Stream
        {
            private readonly ZipArchive _archive;
            private readonly Stream _innerStream;

            public ZipEntryReadStream(ZipArchive archive, Stream innerStream)
            {
                _archive = archive;
                _innerStream = innerStream;
            }

            public override bool CanRead => _innerStream.CanRead;

            public override bool CanSeek => _innerStream.CanSeek;

            public override bool CanWrite => false;

            public override long Length => _innerStream.Length;

            public override long Position
            {
                get => _innerStream.Position;
                set => _innerStream.Position = value;
            }

            public override void Flush()
            {
                _innerStream.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _innerStream.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _innerStream.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                _innerStream.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _innerStream.Dispose();
                    _archive.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        private static bool IsCityGmlFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return extension.Equals(".gml", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".citygml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCityJsonFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".cityjson", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LocalSourceContainsCityJson(string source, Lod2SourceKind sourceKind)
        {
            return EnumerateLocalCityJsonSourceFiles(source, sourceKind).Any();
        }

        private static IEnumerable<LocalCityGmlSourceFile> EnumerateLocalCityJsonSourceFiles(string source, Lod2SourceKind sourceKind)
        {
            if (sourceKind == Lod2SourceKind.File)
            {
                if (IsCityJsonFileName(source))
                {
                    yield return new LocalCityGmlSourceFile(
                        Path.GetFileNameWithoutExtension(source),
                        source,
                        () => File.OpenRead(source),
                        () => File.ReadAllText(source),
                        new FileInfo(source).Length);
                }

                yield break;
            }

            if (sourceKind == Lod2SourceKind.Directory)
            {
                foreach (var filePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                             .Where(IsCityJsonFileName)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new LocalCityGmlSourceFile(
                        Path.GetFileNameWithoutExtension(filePath),
                        filePath,
                        () => File.OpenRead(filePath),
                        () => File.ReadAllText(filePath),
                        new FileInfo(filePath).Length);
                }

                yield break;
            }

            if (sourceKind == Lod2SourceKind.Zip)
            {
                using var archive = ZipFile.OpenRead(source);
                foreach (var entry in archive.Entries
                             .Where(entry => IsCityJsonFileName(entry.FullName))
                             .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    var entryName = entry.FullName;
                    yield return new LocalCityGmlSourceFile(
                        Path.GetFileNameWithoutExtension(entry.FullName),
                        $"{source}!{entry.FullName}",
                        () => OpenZipEntryReadStream(source, entryName),
                        () => ReadZipEntryText(source, entryName),
                        entry.Length);
                }
            }
        }

        private static Lod2SourceKind ResolveSourceKind(string source)
        {
            if (IsUrlSource(source))
            {
                return Lod2SourceKind.Url;
            }

            if (Directory.Exists(source))
            {
                return Lod2SourceKind.Directory;
            }

            if (Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Lod2SourceKind.Zip;
            }

            return Lod2SourceKind.File;
        }

        private static bool IsUrlSource(string source)
        {
            return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                   (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildLocalSourceLabel(string source, Lod2SourceKind sourceKind)
        {
            return sourceKind == Lod2SourceKind.Directory
                ? new DirectoryInfo(source).Name
                : Path.GetFileName(source);
        }

        private static string FormatLocalSourceKind(Lod2SourceKind sourceKind)
        {
            return sourceKind switch
            {
                Lod2SourceKind.Directory => "folder",
                Lod2SourceKind.Zip => "ZIP",
                _ => "file"
            };
        }

        private static BoundingBox2D CreateBufferedBoundingBox(BoundingBox2D boundingBox, string srsName, double bufferMeters)
        {
            if (bufferMeters <= 0.0)
            {
                return boundingBox;
            }

            var normalizedSrs = RhinoSpatialContextTools.NormalizeSrsKey(srsName);
            if (normalizedSrs == "EPSG:4326" || normalizedSrs == "EPSG:4258" || normalizedSrs == "EPSG:7423" || normalizedSrs == "EPSG:4283" || normalizedSrs == "EPSG:7844")
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

        private static List<Lod2Building> FilterBuildingSurfacesToRequestBounds(IReadOnlyList<Lod2Building> buildings, BoundingBox2D requestBoundingBox)
        {
            var filteredBuildings = new List<Lod2Building>();

            foreach (var building in buildings)
            {
                var filteredSurfaces = building.Surfaces
                    .Where(surface => SurfaceIntersectsBounds(surface, requestBoundingBox))
                    .ToList();

                if (filteredSurfaces.Count == 0)
                {
                    continue;
                }

                filteredBuildings.Add(new Lod2Building(
                    building.Id,
                    building.SourceLayerName,
                    filteredSurfaces,
                    building.Attributes));
            }

            return filteredBuildings;
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

        private static string FormatByteLength(long byteLength)
        {
            return byteLength >= 1024 * 1024
                ? $"{(byteLength / 1024.0 / 1024.0).ToString("0.##", CultureInfo.InvariantCulture)} MB"
                : $"{(byteLength / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} KB";
        }

        private static string FormatLocalTimings(LocalCityGmlTimings timings)
        {
            return $"source scan {FormatDuration(timings.CandidateScan)}, " +
                   $"parse/building filter {FormatDuration(timings.ParseAndBuildingFilter)}, " +
                   $"surface filter {FormatDuration(timings.SurfaceFilter)}, " +
                   $"Brep output {FormatDuration(timings.BrepOutput)}";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalMinutes >= 1.0)
            {
                return $"{duration.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture)} min";
            }

            if (duration.TotalSeconds >= 1.0)
            {
                return $"{duration.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture)} s";
            }

            return $"{duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms";
        }

        private void NormalizeComponentLayout()
        {
            var changed = false;

            if (Params.Input.Count > 0)
            {
                changed |= SetParameterMetadata(
                    Params.Input[0],
                    "LoD2 Source",
                    "LoD2 Source",
                    "LoD2 source URL, local CityGML/GML/XML/CityJSON file, folder, or ZIP archive. If left empty, RhinoSpatial will try to inherit a WFS URL from the connected Layer input.");
                Params.Input[0].Optional = true;
            }

            if (Params.Input.Count > 1)
            {
                changed |= SetParameterMetadata(
                    Params.Input[1],
                    "Layer",
                    "Layer",
                    "Optional LoD2 building layer name for WFS sources, or local layer label for file/folder/ZIP sources.");
                Params.Input[1].Optional = true;
            }

            if (Params.Input.Count > 2)
            {
                changed |= SetParameterMetadata(
                    Params.Input[2],
                    "Spatial Context",
                    "Spatial Context",
                    "Shared RhinoSpatial spatial context. LoD2 requests use EPSG:7423/4326 internally, so any Spatial Context created from the map helper will align correctly.");
            }

            while (Params.Input.Count > 3)
            {
                Params.UnregisterInputParameter(Params.Input[3], true);
                changed = true;
            }

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

        private static string ResolveRequestSrs(WfsLayerInfo layerInfo, SpatialContext2D spatialContext)
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
                if (spatialContext.BoundingBoxesBySrs.ContainsKey(normalized) &&
                    normalized != "EPSG:4326")
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
