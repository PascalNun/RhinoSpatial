using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public class LoadTerrainComponent : GH_TaskCapableComponent<LoadTerrainComponent.SolveResults>
    {
        private const int InternalGridSize = 512;
        private const int DefaultMaxGridSizeLimit = 2048;
        private static readonly TimeSpan GlobalFallbackTimeout = TimeSpan.FromSeconds(12);

        private readonly WcsClient _wcsClient = new();
        private readonly TerrainAtomLoader _atom = new();
        private readonly GlobalSkadiTerrainClient _globalTerrainClient = new();

        public class SolveResults
        {
            public List<Mesh> TerrainMeshes { get; init; } = new();

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        public LoadTerrainComponent()
            : base("Load Terrain", "Load Terrain",
                "Load an aligned terrain mesh for the shared RhinoSpatial spatial context.",
                "RhinoSpatial", "Sources")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.LoadTerrain.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Terrain Service URL", "Terrain URL", "Base URL of a terrain WCS or supported ATOM download service, or path to a local GeoTIFF/ASCII Grid/XYZ/CSV DEM. Leave empty to use the built-in quick global terrain fallback for small study areas.", GH_ParamAccess.item);
            pManager.AddTextParameter("Coverage Id", "Coverage", "Optional coverage id. For local text-grid terrain, this can be an EPSG code such as EPSG:25832; otherwise leave empty to use the Spatial Context SRS.", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context from the Spatial Context component.", GH_ParamAccess.item);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = false;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Terrain", "Terrain", "Aligned terrain mesh for the selected spatial context.", GH_ParamAccess.list);
            pManager.AddTextParameter("Status", "Status", "Status or warning information from the terrain loader.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            try
            {
                var requestData = RequestTerrainData(dataAccess);
                if (requestData is null)
                {
                    return;
                }

                if (InPreSolve)
                {
                    var task = Task.Run(() => ComputeAsync(requestData, CancelToken), CancelToken);
                    TaskList.Add(task);
                    return;
                }

                if (!GetSolveResults(dataAccess, out var results))
                {
                    results = ComputeAsync(requestData, CancellationToken.None).GetAwaiter().GetResult();
                }

                Message = string.Empty;
                dataAccess.SetDataList(0, results.TerrainMeshes);
                dataAccess.SetData(1, results.Status);

                if (!string.IsNullOrWhiteSpace(results.Status) && results.MessageLevel.HasValue)
                {
                    AddRuntimeMessage(results.MessageLevel.Value, results.Status);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        private RequestData? RequestTerrainData(IGH_DataAccess dataAccess)
        {
            string? serviceUrl = null;
            string? coverageId = null;
            string? spatialContextText = null;

            dataAccess.GetData(0, ref serviceUrl);
            dataAccess.GetData(1, ref coverageId);
            dataAccess.GetData(2, ref spatialContextText);

            if (!RhinoSpatialInputParser.TryGetRequiredSpatialContext(spatialContextText, out var spatialContext, out var spatialContextError))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, spatialContextError);
                return null;
            }

            return new RequestData(
                RhinoSpatialSourceFallbacks.ResolveTerrainSources(serviceUrl, coverageId),
                spatialContext);
        }

        private async Task<SolveResults> ComputeAsync(RequestData requestData, CancellationToken cancellationToken)
        {
            var failures = new List<string>();
            foreach (var source in requestData.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await ComputeForSourceAsync(source, requestData.SpatialContext, cancellationToken);
                }
                catch (Exception ex)
                {
                    failures.Add($"{source.DisplayName}: {ex.Message}");
                }
            }

            return new SolveResults
            {
                TerrainMeshes = new List<Mesh>(),
                Status = failures.Count == 0
                    ? "No terrain source was available."
                    : $"No terrain source could load the selected area. {string.Join(" ", failures)}",
                MessageLevel = GH_RuntimeMessageLevel.Error
            };
        }

        private async Task<SolveResults> ComputeForSourceAsync(
            ResolvedTerrainSource source,
            SpatialContext2D spatialContext,
            CancellationToken cancellationToken)
        {
            if (source.Kind == TerrainSourceKind.GlobalSkadiTiles)
            {
                return await ComputeGlobalTerrainFallbackAsync(source, spatialContext, cancellationToken);
            }

            if (source.Kind == TerrainSourceKind.LocalGeoTiffDem)
            {
                return ComputeLocalGeoTiffTerrain(source, spatialContext);
            }

            if (source.Kind == TerrainSourceKind.LocalTextGridDem)
            {
                return ComputeLocalTextGridTerrain(source, spatialContext);
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(source.BaseUrl, @"[?&]service=wcs(?:&|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var progressActive = 1;
                TerrainAtomResult? atom;
                try
                {
                    atom = await _atom.TryLoadAsync(source.BaseUrl, spatialContext, progress =>
                        Eto.Forms.Application.Instance.AsyncInvoke(() =>
                        {
                            if (System.Threading.Volatile.Read(ref progressActive) != 0 && !cancellationToken.IsCancellationRequested && OnPingDocument() is not null)
                            { Message = progress; OnDisplayExpired(false); }
                        }), cancellationToken).ConfigureAwait(false);
                }
                finally { System.Threading.Volatile.Write(ref progressActive, 0); }
                if (atom is not null)
                {
                    var terrain = BuildTerrainMesh(atom.Raster, atom.Bounds, spatialContext, out var baseline);
                    SpatialTerrainCache.Store(spatialContext, atom.Raster.SrsName, atom.Bounds, atom.Raster, baseline);
                    return new SolveResults { TerrainMeshes = new List<Mesh> { terrain },
                        Status = atom.Status + " " + BuildTerrainAlignmentNote(spatialContext.UseAbsoluteCoordinates) + BuildMissingElevationNote(atom.Raster), MessageLevel = GH_RuntimeMessageLevel.Remark };
                }
            }

            try
            {
                var capabilities = await _wcsClient.LoadCapabilitiesAsync(source.BaseUrl);

                var coverageId = source.CoverageId;
                if (!capabilities.Coverages.Exists(coverage => coverage.CoverageId.Equals(coverageId, StringComparison.OrdinalIgnoreCase)))
                {
                    if (capabilities.Coverages.Count > 0)
                    {
                        coverageId = capabilities.Coverages[0].CoverageId;
                    }
                }

                var options = new WcsRequestOptions
                {
                    BaseUrl = source.BaseUrl,
                    CoverageId = coverageId,
                    Version = string.IsNullOrWhiteSpace(capabilities.ServiceVersion) ? "2.0.1" : capabilities.ServiceVersion,
                    Format = "image/tiff"
                };

                if (!string.IsNullOrWhiteSpace(capabilities.GetCoverageUrl))
                {
                    options.GetCoverageBaseUrl = capabilities.GetCoverageUrl;
                }

                if (!string.IsNullOrWhiteSpace(capabilities.DescribeCoverageUrl))
                {
                    options.DescribeCoverageBaseUrl = capabilities.DescribeCoverageUrl;
                }

                var description = await _wcsClient.LoadCoverageDescriptionAsync(options);

                var requestBoundingBox = ResolveRequestedBoundingBox(spatialContext, description);

                options.SrsName = description.NativeSrs;
                options.AxisXLabel = description.AxisXLabel;
                options.AxisYLabel = description.AxisYLabel;
                options.BoundingBox = requestBoundingBox;

                var coverage = await _wcsClient.DownloadCoverageAsync(options);

                var mesh = BuildTerrainMesh(
                    coverage.Raster,
                    requestBoundingBox,
                    spatialContext,
                    out var elevationBase);
                SpatialTerrainCache.Store(
                    spatialContext,
                    description.NativeSrs,
                    requestBoundingBox,
                    coverage.Raster,
                    elevationBase);
                var status = BuildStatusMessage(coverageId, spatialContext.UseAbsoluteCoordinates, coverage.UsedCachedFile)
                    + BuildMissingElevationNote(coverage.Raster);

                return new SolveResults
                {
                    TerrainMeshes = new List<Mesh> { mesh },
                    Status = $"{source.CreateStatusPrefix()}{status}"
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }
        }

        private SolveResults ComputeLocalGeoTiffTerrain(
            ResolvedTerrainSource source,
            SpatialContext2D spatialContext)
        {
            if (!File.Exists(source.BaseUrl))
            {
                throw new InvalidOperationException($"Local terrain GeoTIFF was not found: {source.BaseUrl}");
            }

            var rasterInfo = GeoTiffReader.ReadImageInfo(source.BaseUrl);
            var coverageId = string.IsNullOrWhiteSpace(source.CoverageId)
                ? Path.GetFileNameWithoutExtension(source.BaseUrl)
                : source.CoverageId;
            var raster = TerrainRasterReader.ReadRaster(source.BaseUrl, coverageId, rasterInfo.SrsName);

            if (RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(
                    spatialContext,
                    rasterInfo.SrsName,
                    out var contextBoundsInRasterSrs,
                    out _) &&
                !RhinoSpatialContextTools.DoBoundingBoxesIntersect(rasterInfo.BoundingBox, contextBoundsInRasterSrs))
            {
                return new SolveResults
                {
                    TerrainMeshes = new List<Mesh>(),
                    Status = $"Loaded local terrain GeoTIFF '{Path.GetFileName(source.BaseUrl)}' ({rasterInfo.Width}x{rasterInfo.Height}, {rasterInfo.SrsName}), but it does not overlap the selected Spatial Context area.",
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }

            var mesh = BuildTerrainMesh(raster, rasterInfo.BoundingBox, spatialContext, out var elevationBase);

            SpatialTerrainCache.Store(
                spatialContext,
                rasterInfo.SrsName,
                rasterInfo.BoundingBox,
                raster,
                elevationBase);

            return new SolveResults
            {
                TerrainMeshes = new List<Mesh> { mesh },
                Status = $"{source.CreateStatusPrefix()}Loaded local terrain GeoTIFF '{Path.GetFileName(source.BaseUrl)}' ({rasterInfo.Width}x{rasterInfo.Height}, {rasterInfo.SrsName}) {BuildTerrainAlignmentNote(spatialContext.UseAbsoluteCoordinates)}{BuildMissingElevationNote(raster)}",
                MessageLevel = GH_RuntimeMessageLevel.Remark
            };
        }

        private SolveResults ComputeLocalTextGridTerrain(
            ResolvedTerrainSource source,
            SpatialContext2D spatialContext)
        {
            if (!File.Exists(source.BaseUrl))
            {
                throw new InvalidOperationException($"Local terrain text grid was not found: {source.BaseUrl}");
            }

            var sourceSrs = !string.IsNullOrWhiteSpace(source.CoverageId) &&
                            source.CoverageId.Contains("EPSG", StringComparison.OrdinalIgnoreCase)
                ? source.CoverageId
                : spatialContext.ResolvedSrs;
            var coverageId = !string.IsNullOrWhiteSpace(source.CoverageId) &&
                             !source.CoverageId.Contains("EPSG", StringComparison.OrdinalIgnoreCase)
                ? source.CoverageId
                : Path.GetFileNameWithoutExtension(source.BaseUrl);
            var readResult = TerrainTextGridReader.ReadRaster(source.BaseUrl, coverageId, sourceSrs);

            if (RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(
                    spatialContext,
                    readResult.SourceSrs,
                    out var contextBoundsInRasterSrs,
                    out _) &&
                !RhinoSpatialContextTools.DoBoundingBoxesIntersect(readResult.BoundingBox, contextBoundsInRasterSrs))
            {
                return new SolveResults
                {
                    TerrainMeshes = new List<Mesh>(),
                    Status = $"Loaded local terrain {readResult.FormatLabel} '{Path.GetFileName(source.BaseUrl)}' ({readResult.Raster.Width}x{readResult.Raster.Height}, {readResult.SourceSrs}), but it does not overlap the selected Spatial Context area.",
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }

            var mesh = BuildTerrainMesh(readResult.Raster, readResult.BoundingBox, spatialContext, out var elevationBase);

            SpatialTerrainCache.Store(
                spatialContext,
                readResult.SourceSrs,
                readResult.BoundingBox,
                readResult.Raster,
                elevationBase);

            return new SolveResults
            {
                TerrainMeshes = new List<Mesh> { mesh },
                Status = $"{source.CreateStatusPrefix()}Loaded local terrain {readResult.FormatLabel} '{Path.GetFileName(source.BaseUrl)}' ({readResult.Raster.Width}x{readResult.Raster.Height}, {readResult.SourceSrs}) {BuildTerrainAlignmentNote(spatialContext.UseAbsoluteCoordinates)}{BuildMissingElevationNote(readResult.Raster)}",
                MessageLevel = GH_RuntimeMessageLevel.Remark
            };
        }

        private async Task<SolveResults> ComputeGlobalTerrainFallbackAsync(
            ResolvedTerrainSource source,
            SpatialContext2D spatialContext,
            CancellationToken cancellationToken)
        {
            if (spatialContext.Wgs84BoundingBox is null)
            {
                throw new InvalidOperationException("The global terrain fallback needs a WGS84 bounding box from the Spatial Context.");
            }

            TerrainRasterData raster;
            try
            {
                raster = await _globalTerrainClient
                    .LoadAsync(
                        spatialContext.Wgs84BoundingBox,
                        timeout: GlobalFallbackTimeout,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "The built-in global terrain fallback timed out after 12 seconds. "
                    + "Use a smaller Spatial Context or connect an explicit terrain source.",
                    ex);
            }

            var validElevationCount = CountValidElevations(raster);
            if (validElevationCount < 4)
            {
                throw new InvalidOperationException(
                    "The built-in global terrain fallback did not return enough usable elevation samples for this Spatial Context. "
                    + "Connect an explicit terrain source for this area.");
            }

            var mesh = BuildTerrainMesh(
                raster,
                spatialContext.Wgs84BoundingBox,
                spatialContext,
                out var elevationBase);
            SpatialTerrainCache.Store(
                spatialContext,
                raster.SrsName,
                spatialContext.Wgs84BoundingBox,
                raster,
                elevationBase);
            var status = BuildStatusMessage(raster.CoverageId, spatialContext.UseAbsoluteCoordinates, false)
                + BuildMissingElevationNote(raster)
                + " The built-in fallback is intended for quick context; use explicit terrain for project-grade elevation.";

            return new SolveResults
            {
                TerrainMeshes = new List<Mesh> { mesh },
                Status = $"{source.CreateStatusPrefix()}{status}"
            };
        }

        private static string BuildStatusMessage(string coverageId, bool useAbsoluteCoordinates, bool usedCachedFile)
        {
            var actionPrefix = usedCachedFile ? "Using cached terrain raster" : "Loaded terrain coverage";
            if (string.IsNullOrWhiteSpace(coverageId))
            {
                return actionPrefix + ".";
            }

            var alignmentNote = BuildTerrainAlignmentNote(useAbsoluteCoordinates);

            return $"{actionPrefix}: {coverageId} {alignmentNote}";
        }

        private static string BuildTerrainAlignmentNote(bool useAbsoluteCoordinates)
        {
            return useAbsoluteCoordinates
                ? "with absolute elevation."
                : "aligned to the shared local terrain/building elevation baseline.";
        }

        private static string BuildMissingElevationNote(TerrainRasterData raster)
        {
            var missing = raster.Elevations.Length - CountValidElevations(raster);
            return missing == 0
                ? string.Empty
                : $" The source contains {missing} missing elevation sample(s); affected areas are left open in the terrain surface.";
        }

        private static BoundingBox2D ResolveRequestedBoundingBox(
            SpatialContext2D spatialContext,
            WcsCoverageDescription description)
        {
            if (RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(spatialContext, description.NativeSrs, out var bbox, out _))
            {
                return ClampBoundingBox(bbox, description.NativeBoundingBox);
            }

            throw new InvalidOperationException($"Cannot resolve the selected area in terrain coordinate system '{description.NativeSrs}'.");
        }

        private static BoundingBox2D ClampBoundingBox(BoundingBox2D requested, BoundingBox2D nativeBounds)
        {
            var minX = Math.Max(requested.MinX, nativeBounds.MinX);
            var minY = Math.Max(requested.MinY, nativeBounds.MinY);
            var maxX = Math.Min(requested.MaxX, nativeBounds.MaxX);
            var maxY = Math.Min(requested.MaxY, nativeBounds.MaxY);

            if (maxX <= minX || maxY <= minY)
            {
                throw new InvalidOperationException("The terrain coverage does not overlap the selected Spatial Context area.");
            }

            return new BoundingBox2D(minX, minY, maxX, maxY);
        }

        private static Mesh BuildTerrainMesh(
            TerrainRasterData raster,
            BoundingBox2D sampleBounds,
            SpatialContext2D spatialContext,
            out double elevationBase)
        {
            var candidateBase = spatialContext.UseAbsoluteCoordinates ? 0.0 : ResolveElevationBase(raster);
            var data = TerrainMeshBuilder.Build(
                raster,
                sampleBounds,
                spatialContext,
                Math.Clamp(InternalGridSize, 64, DefaultMaxGridSizeLimit),
                candidateBase);
            elevationBase = spatialContext.UseAbsoluteCoordinates
                ? 0.0
                : SpatialElevationBaselineCache.ResolveOrStore(spatialContext, candidateBase);
            var elevationAdjustment = candidateBase - elevationBase;
            var mesh = new Mesh();
            foreach (var vertex in data.Vertices)
            {
                mesh.Vertices.Add(vertex.X, vertex.Y, vertex.Z + elevationAdjustment);
            }

            foreach (var face in data.Faces)
            {
                mesh.Faces.AddFace(face.A, face.B, face.C, face.D);
            }

            mesh.Vertices.CullUnused();
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        private static double ResolveElevationBase(TerrainRasterData raster)
        {
            var minZ = double.PositiveInfinity;

            foreach (var elevation in raster.Elevations)
            {
                if (TerrainMeshBuilder.IsNoData(raster, elevation))
                {
                    continue;
                }

                if (elevation < minZ)
                {
                    minZ = elevation;
                }
            }

            if (double.IsInfinity(minZ))
            {
                throw new InvalidOperationException("The terrain raster contains no usable elevation samples.");
            }

            return minZ;
        }

        private static int CountValidElevations(TerrainRasterData raster)
        {
            var validCount = 0;
            foreach (var elevation in raster.Elevations)
            {
                if (TerrainMeshBuilder.IsNoData(raster, elevation))
                {
                    continue;
                }

                validCount++;
            }

            return validCount;
        }

        private record RequestData(IReadOnlyList<ResolvedTerrainSource> Sources, SpatialContext2D SpatialContext);

        public override Guid ComponentGuid => new Guid("e6941d59-50c0-46f4-96fa-9546a0f54f9d");
    }
}
