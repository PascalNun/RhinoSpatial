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
        private readonly WfsClient _wfsClient = new();
        private readonly SourceDownloadCache _downloads = new(AtomDownloadKind.Vector);
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

            public System.Threading.CancellationToken CancellationToken { get; init; }

            public List<string> RequestedLayerNames { get; init; } = new();

            public int MaxFeatures { get; init; }

            public SpatialContext2D SpatialContext { get; init; } = null!;

            public VectorSourceKind SourceKind { get; init; }
        }

        private sealed class VectorOutputData
        {
            public GH_Structure<IGH_GeometricGoo> GeometryTree { get; init; } = new();

            public int GeometryItemCount { get; init; }
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
            pManager.AddTextParameter("WFS Service URL", "WFS URL", "WFS service, OGC API Features root/collection/items URL, direct GeoJSON or vector ZIP URL, or local Shapefile/GeoJSON/GeoPackage path. If left empty, RhinoSpatial will try to inherit it from the connected Layer input.", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "Layer", "One or more layer names or layer entries. Use List Item to choose one layer, or merge explicit selections if you want to load several layers. Optional for files and a single OGC API collection; choose a collection explicitly when an API root exposes several.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Max Features", "Max Features", "Per-layer feature cap. Use 0 to follow available pages within the selected area and safety limits; read Status for completeness diagnostics.", GH_ParamAccess.item, 0);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context. This is required so WFS, WMS, LoD2, terrain, GeoTIFF, and OSM outputs stay aligned.", GH_ParamAccess.item);
            pManager[1].Optional = true;
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

                dataAccess.SetDataTree(0, RhinoSpatialModelUnits.Tree(result.GeometryTree, RhinoSpatialModelUnits.OutputScale(requestData.SpatialContext)));

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

            dataAccess.GetDataList(1, layerSelections);

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

            var sourceInfo = VectorSourceResolver.Resolve(baseUrl);
            if (!sourceInfo.IsSupported)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"WFS URL must be a WFS service URL, an OGC API Features URL, a supported vector download URL, or a local Shapefile/GeoJSON/GeoPackage path. Source was not found or is unsupported: {baseUrl}");
                return false;
            }

            var requestedLayerNames = layerSelections
                .Where(layerName => !string.IsNullOrWhiteSpace(layerName))
                .Select(RhinoSpatialInputParser.ParseLayerName)
                .Where(layerName => !string.IsNullOrWhiteSpace(layerName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requestedLayerNames.Count == 0 && sourceInfo.CanUseDefaultLayerName && !string.IsNullOrWhiteSpace(sourceInfo.DefaultLayerName))
            {
                requestedLayerNames.Add(sourceInfo.DefaultLayerName);
            }

            if (maxFeatures < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Max Features must be zero or a positive number.");
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
                BaseUrl = sourceInfo.Source,
                RequestedLayerNames = requestedLayerNames,
                MaxFeatures = maxFeatures,
                SpatialContext = spatialContext,
                CancellationToken = CancelToken,
                SourceKind = sourceInfo.Kind
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            return requestData.SourceKind switch
            {
                VectorSourceKind.LocalGeoPackage => ComputeLocalGeoPackage(requestData),
                VectorSourceKind.LocalShapefile => ComputeLocalShapefile(requestData),
                VectorSourceKind.LocalGeoJson => ComputeLocalGeoJson(requestData),
                VectorSourceKind.RemoteDownload => ComputeRemoteVector(requestData),
                _ => ComputeRemoteService(requestData)
            };
        }

        private SolveResults ComputeWfs(RequestData requestData)
        {
            if (requestData.RequestedLayerNames.Count == 0) throw new InvalidOperationException("Select a WFS layer with List WFS Layers.");
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

                var layerResult = _wfsClient.LoadFeaturesWithStatusAsync(layerRequestOptions, requestData.CancellationToken).GetAwaiter().GetResult();
                var filterResult = SpatialFeatureFilter.FilterToBounds(
                    layerResult.Features,
                    requestData.SpatialContext.RequestBoundingBox);
                features.AddRange(filterResult.Features);

                if (!string.IsNullOrWhiteSpace(layerResult.StatusNote))
                {
                    statusNotes.Add(layerResult.StatusNote);
                }

                if (filterResult.OversizedEnclosingFeatureCount > 0)
                {
                    statusNotes.Add(BuildOversizedFilterNote(filterResult, "WFS feature"));
                }
            }

            var outputData = BuildVectorOutput(requestData, features);
            var resolvedSrsText = string.IsNullOrWhiteSpace(resolvedSrsName) ? "service default" : resolvedSrsName;
            var maxFeaturesText = FormatMaxFeatures(requestData.MaxFeatures);

            return BuildSolveResults(
                outputData,
                features.Count,
                BuildStatusMessage(
                    requestData,
                    features.Count,
                    outputData.GeometryItemCount,
                    maxFeaturesText,
                    resolvedSrsText,
                    statusNotes),
                ResolveMessageLevel(features.Count, statusNotes.Any(note => note.Contains("Using cached WFS", StringComparison.Ordinal) || note.StartsWith("Filtered ", StringComparison.Ordinal))),
                statusNotes.Any(note => note.Contains("Using cached WFS", StringComparison.Ordinal)));
        }

        private SolveResults ComputeRemoteService(RequestData requestData)
        {
            var collections = _ogcApiFeaturesClient.TryDiscoverAsync(requestData.BaseUrl, requestData.CancellationToken).GetAwaiter().GetResult();
            if (collections is null)
            {
                if (requestData.RequestedLayerNames.Count == 0) return ComputeRemoteVector(requestData);
                return ComputeWfs(requestData);
            }
            if (collections.Count == 0) throw new InvalidDataException("This OGC API exposes no feature collections.");
            var names = requestData.RequestedLayerNames;
            if (names.Count == 0 || (collections.Count == 1 && !names.Contains(collections[0].Id)))
                names = collections.Count == 1 ? new List<string> { collections[0].Id }
                    : throw new InvalidOperationException("This OGC API has several collections. Use List WFS Layers and select the collection(s) to load.");
            if (names.Any(name => !collections.Any(c => c.Id == name)))
                throw new InvalidOperationException("An OGC API collection selection was not found. Refresh List WFS Layers.");
            var resolved = CopyRequest(requestData, requestData.BaseUrl, VectorSourceKind.OgcApiFeatures, names);
            var features = new List<WfsFeature>();
            var notes = new List<string>();
            foreach (var name in names)
            {
                var result = _ogcApiFeaturesClient.LoadFeaturesWithStatusAsync(requestData.BaseUrl, name,
                    requestData.SpatialContext.Wgs84BoundingBox, requestData.MaxFeatures, requestData.CancellationToken).GetAwaiter().GetResult();
                var projected = WfsFeatureGeometryTransformer.TransformFeatures(result.Features, "EPSG:4326", requestData.SpatialContext.ResolvedSrs);
                var filtered = SpatialFeatureFilter.FilterToBounds(projected, requestData.SpatialContext.RequestBoundingBox);
                features.AddRange(filtered.Features);
                notes.Add(name + ": " + result.StatusNote);
                if (filtered.OversizedEnclosingFeatureCount > 0) notes.Add(BuildOversizedFilterNote(filtered, "feature"));
            }
            var output = BuildVectorOutput(resolved, features);
            return BuildSolveResults(output, features.Count,
                $"Loaded {features.Count} OGC API feature(s), {output.GeometryItemCount} geometry item(s), {FormatCoordinateMode(requestData.SpatialContext)}. {string.Join(" ", notes)}",
                features.Count == 0 ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark);
        }

        private static RequestData CopyRequest(RequestData source, string path, VectorSourceKind kind, List<string> layers) => new()
        {
            BaseUrl = path, SourceKind = kind, RequestedLayerNames = layers,
            MaxFeatures = source.MaxFeatures, SpatialContext = source.SpatialContext, CancellationToken = source.CancellationToken
        };

        private SolveResults ComputeRemoteVector(RequestData requestData)
        {
            var tile = new Lod2AtomLink(new Uri(requestData.BaseUrl), "Vector download", "", null);
            var downloaded = _downloads.DownloadAsync(tile, cancellationToken: requestData.CancellationToken).GetAwaiter().GetResult();
            using var files = SourceFilesLease.Open(downloaded.FilePath, requestData.CancellationToken);
            var candidates = files.Files.Where(path => Path.GetExtension(path).Equals(".shp", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".geojson", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)).ToList();
            var features = new List<WfsFeature>();
            var names = new List<string>();
            var notes = new List<string>();
            var failed = false;
            foreach (var file in candidates)
            {
                requestData.CancellationToken.ThrowIfCancellationRequested();
                var name = candidates.Count == 1 && requestData.RequestedLayerNames.Count == 1
                    ? requestData.RequestedLayerNames[0] : Path.GetFileNameWithoutExtension(file);
                if (requestData.RequestedLayerNames.Count > 0 && candidates.Count > 1 && !requestData.RequestedLayerNames.Contains(name)) continue;
                if (Path.GetExtension(file).Equals(".shp", StringComparison.OrdinalIgnoreCase))
                {
                    var metadata = ShapefileFeatureReader.ReadSourceMetadata(file);
                    if (string.IsNullOrWhiteSpace(metadata.SourceSrs)) throw new InvalidDataException("Downloaded Shapefile must have a resolvable source CRS in its .prj file.");
                    names.Add(name);
                    var read = ShapefileFeatureReader.ReadFeatures(file, name, requestData.SpatialContext, requestData.MaxFeatures);
                    features.AddRange(read.Features);
                    failed |= read.FailedFeatureCount > 0;
                    notes.Add($"{name}: scanned {read.SourceFeatureCount}, outside {read.SkippedOutsideContextCount}, failed {read.FailedFeatureCount}.");
                }
                else
                {
                    var json = File.ReadAllText(file);
                    using var document = System.Text.Json.JsonDocument.Parse(json);
                    if (!document.RootElement.TryGetProperty("type", out var type) || type.GetString() is not ("Feature" or "FeatureCollection")) continue;
                    names.Add(name);
                    var projected = WfsFeatureGeometryTransformer.TransformFeatures(GeoJsonReader.ReadFeatures(json, name),
                        GeoJsonReader.TryReadSourceSrs(json) ?? "EPSG:4326", requestData.SpatialContext.ResolvedSrs);
                    var filtered = SpatialFeatureFilter.FilterToBounds(projected, requestData.SpatialContext.RequestBoundingBox);
                    features.AddRange(requestData.MaxFeatures > 0 ? filtered.Features.Take(requestData.MaxFeatures) : filtered.Features);
                    if (filtered.OversizedEnclosingFeatureCount > 0) notes.Add(BuildOversizedFilterNote(filtered, "feature"));
                }
            }
            if (names.Count == 0) throw new InvalidDataException("The vector download contains no matching Shapefile or GeoJSON layer.");
            var resolved = CopyRequest(requestData, downloaded.FilePath, VectorSourceKind.RemoteDownload, names);
            var output = BuildVectorOutput(resolved, features);
            return BuildSolveResults(output, features.Count,
                $"Loaded {features.Count} feature(s) from a validated vector download ({(downloaded.UsedCache ? "cached" : "downloaded")}), {FormatCoordinateMode(requestData.SpatialContext)}. {string.Join(" ", notes)}",
                features.Count == 0 || failed ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark);
        }

        private SolveResults ComputeLocalGeoPackage(RequestData requestData)
        {
            using var deadline = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(
                requestData.CancellationToken
            );
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            var names = requestData.RequestedLayerNames;
            var layers = GeoPackageReader.ReadLayers(requestData.BaseUrl, deadline.Token);
            if (names.Count == 0)
            {
                if (layers.Count != 1)
                    throw new InvalidOperationException(
                        "Select a GeoPackage feature layer using List WFS Layers."
                    );
                names = new List<string> { layers[0].Name };
            }
            var resolved = CopyRequest(
                requestData,
                requestData.BaseUrl,
                VectorSourceKind.LocalGeoPackage,
                names
            );
            var features = new List<WfsFeature>();
            var notes = new List<string>();
            long pointCount = 0,
                attributeCharacters = 0;
            foreach (var name in names)
            {
                var read = GeoPackageReader.ReadFeatures(
                    requestData.BaseUrl,
                    name,
                    requestData.SpatialContext,
                    requestData.MaxFeatures,
                    deadline.Token
                );
                pointCount += read.Features.Sum(f =>
                    (long)f.Geometry.Points.Count
                    + f.Geometry.OuterRings.Sum(r => (long)r.Points.Count)
                    + f.Geometry.LineStrings.Sum(l => (long)l.Points.Count)
                );
                attributeCharacters += read.Features.Sum(f =>
                    f.Attributes.Values.Sum(v => (long)(v?.Length ?? 0))
                );
                if (
                    features.Count + read.Features.Count > 100000
                    || pointCount > 4000000
                    || attributeCharacters > 8 * 1024 * 1024
                )
                    throw new InvalidOperationException(
                        "GeoPackage selection exceeded its combined feature/coordinate/attribute budget; narrow the area or select fewer layers."
                    );
                features.AddRange(read.Features);
                notes.Add(
                    $"{name}: {read.Features.Count} feature(s), {read.ScannedCount} row(s), {(read.UsedSpatialIndex ? "RTree index" : "bounded scan")}{(read.ReachedFeatureLimit ? ", reached requested feature limit" : "")}."
                );
            }
            var output = BuildVectorOutput(resolved, features);
            return BuildSolveResults(
                output,
                features.Count,
                $"Loaded read-only GeoPackage: {string.Join(" ", notes)} {FormatCoordinateMode(requestData.SpatialContext)}",
                features.Count == 0 ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark
            );
        }

        private SolveResults ComputeLocalShapefile(RequestData requestData)
        {
            var layerName = requestData.RequestedLayerNames[0];
            var readResult = ShapefileFeatureReader.ReadFeatures(
                requestData.BaseUrl,
                layerName,
                requestData.SpatialContext,
                requestData.MaxFeatures);
            var outputData = BuildVectorOutput(requestData, readResult.Features);
            var maxFeaturesText = FormatMaxFeatures(requestData.MaxFeatures);
            var coordinateText = FormatCoordinateMode(requestData.SpatialContext);
            var status = readResult.Features.Count == 0
                ? $"No features were found in local Shapefile '{Path.GetFileName(requestData.BaseUrl)}' inside the current Spatial Context. Source SRS '{readResult.SourceSrs}'. Scanned {readResult.SourceFeatureCount} feature(s), skipped {readResult.SkippedOutsideContextCount} outside the context, failed {readResult.FailedFeatureCount}."
                : $"Loaded {readResult.Features.Count} feature(s) and {outputData.GeometryItemCount} geometry item(s) from local Shapefile '{Path.GetFileName(requestData.BaseUrl)}' with {maxFeaturesText} features, source SRS '{readResult.SourceSrs}', {coordinateText} Scanned {readResult.SourceFeatureCount} feature(s), skipped {readResult.SkippedOutsideContextCount} outside the context, failed {readResult.FailedFeatureCount}.";

            return BuildSolveResults(
                outputData,
                readResult.Features.Count,
                status,
                ResolveMessageLevel(readResult.Features.Count, readResult.FailedFeatureCount > 0));
        }

        private SolveResults ComputeLocalGeoJson(RequestData requestData)
        {
            var layerName = requestData.RequestedLayerNames[0];
            var geoJson = File.ReadAllText(requestData.BaseUrl);
            var sourceSrs = GeoJsonReader.TryReadSourceSrs(geoJson) ?? "EPSG:4326";
            var sourceFeatures = GeoJsonReader.ReadFeatures(geoJson, layerName);
            var projectedFeatures = WfsFeatureGeometryTransformer.TransformFeatures(
                sourceFeatures,
                sourceSrs,
                requestData.SpatialContext.ResolvedSrs);
            var filterResult = SpatialFeatureFilter.FilterToBounds(
                projectedFeatures,
                requestData.SpatialContext.RequestBoundingBox);
            var features = requestData.MaxFeatures > 0
                ? filterResult.Features.Take(requestData.MaxFeatures).ToList()
                : filterResult.Features;
            var outputData = BuildVectorOutput(requestData, features);
            var maxFeaturesText = FormatMaxFeatures(requestData.MaxFeatures);
            var coordinateText = FormatCoordinateMode(requestData.SpatialContext);
            var skippedOutsideContextCount = filterResult.OutsideContextCount;
            var filterNote = BuildOversizedFilterNote(filterResult, "feature");
            var filterSuffix = FormatOptionalStatusNote(filterNote);
            var status = features.Count == 0
                ? $"No features were found in local GeoJSON '{Path.GetFileName(requestData.BaseUrl)}' inside the current Spatial Context. Source SRS '{sourceSrs}'. Scanned {sourceFeatures.Count} feature(s), skipped {skippedOutsideContextCount} outside the context.{filterSuffix}"
                : $"Loaded {features.Count} feature(s) and {outputData.GeometryItemCount} geometry item(s) from local GeoJSON '{Path.GetFileName(requestData.BaseUrl)}' with {maxFeaturesText} features, source SRS '{sourceSrs}', {coordinateText} Scanned {sourceFeatures.Count} feature(s), skipped {skippedOutsideContextCount} outside the context.{filterSuffix}";

            return BuildSolveResults(
                outputData,
                features.Count,
                status,
                ResolveMessageLevel(features.Count, filterResult.OversizedEnclosingFeatureCount > 0));
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

        private static VectorOutputData BuildVectorOutput(RequestData requestData, IReadOnlyList<WfsFeature> features)
        {
            var appliedOffset = RhinoSpatialContextTools.ResolvePlacementOrigin(
                requestData.SpatialContext,
                requestData.SpatialContext.UseAbsoluteCoordinates,
                features);

            var geometryTree = RhinoSpatialOutputBuilder.BuildGeometryTree(
                features,
                requestData.RequestedLayerNames,
                appliedOffset.X,
                appliedOffset.Y);

            return new VectorOutputData
            {
                GeometryTree = geometryTree,
                GeometryItemCount = geometryTree.DataCount
            };
        }

        private static SolveResults BuildSolveResults(
            VectorOutputData outputData,
            int featureCount,
            string status,
            GH_RuntimeMessageLevel? messageLevel,
            bool usedCachedFallback = false)
        {
            return new SolveResults
            {
                GeometryTree = outputData.GeometryTree,
                FeatureCount = featureCount,
                GeometryItemCount = outputData.GeometryItemCount,
                Status = status,
                MessageLevel = messageLevel,
                UsedCachedFallback = usedCachedFallback
            };
        }

        private static string BuildStatusMessage(
            RequestData requestData,
            int featureCount,
            int geometryItemCount,
            string maxFeaturesText,
            string resolvedSrsText,
            IReadOnlyList<string> statusNotes)
        {
            var coordinateText = FormatCoordinateMode(requestData.SpatialContext);

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

        private static string BuildOversizedFilterNote(SpatialFeatureFilterResult filterResult, string featureLabel)
        {
            if (filterResult.OversizedEnclosingFeatureCount <= 0)
            {
                return string.Empty;
            }

            return $"Filtered {filterResult.OversizedEnclosingFeatureCount} oversized enclosing {featureLabel}(s) whose outlines were outside the Spatial Context.";
        }

        private static string FormatMaxFeatures(int maxFeatures)
        {
            return maxFeatures > 0 ? maxFeatures.ToString() : "all available";
        }

        private static string FormatOptionalStatusNote(string note)
        {
            return string.IsNullOrWhiteSpace(note) ? string.Empty : $" {note}";
        }

        private static string FormatCoordinateMode(SpatialContext2D spatialContext)
        {
            return spatialContext.UseAbsoluteCoordinates
                ? "using absolute coordinates."
                : "then localized the geometry near the Rhino origin.";
        }
    }
}
