using System;
using System.Collections.Generic;
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
            pManager.AddTextParameter("Terrain Service URL", "Terrain URL", "Base URL of the terrain (WCS) service. Leave empty to use the built-in quick global terrain fallback for small study areas.", GH_ParamAccess.item);
            pManager.AddTextParameter("Coverage Id", "Coverage", "Optional coverage id. Leave empty to use the default coverage of the selected terrain source.", GH_ParamAccess.item, string.Empty);
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

                var requestBoundingBox = ResolveRequestedBoundingBox(spatialContext, description, out var placementOrigin);

                options.SrsName = description.NativeSrs;
                options.AxisXLabel = description.AxisXLabel;
                options.AxisYLabel = description.AxisYLabel;
                options.BoundingBox = requestBoundingBox;

                var coverage = await _wcsClient.DownloadCoverageAsync(options);

                var elevationBase = spatialContext.UseAbsoluteCoordinates
                    ? 0.0
                    : SpatialElevationBaselineCache.ResolveOrStore(
                        spatialContext,
                        ResolveElevationBase(coverage.Raster));
                SpatialTerrainCache.Store(
                    spatialContext,
                    description.NativeSrs,
                    requestBoundingBox,
                    coverage.Raster,
                    elevationBase);
                var mesh = BuildTerrainMesh(
                    coverage.Raster,
                    requestBoundingBox,
                    placementOrigin,
                    spatialContext.UseAbsoluteCoordinates,
                    elevationBase);
                var status = BuildStatusMessage(coverageId, spatialContext.UseAbsoluteCoordinates, coverage.UsedCachedFile);

                return new SolveResults
                {
                    TerrainMeshes = mesh is null ? new List<Mesh>() : new List<Mesh> { mesh },
                    Status = $"{source.CreateStatusPrefix()}{status}"
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }
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

            var elevationBase = spatialContext.UseAbsoluteCoordinates
                ? 0.0
                : SpatialElevationBaselineCache.ResolveOrStore(
                    spatialContext,
                    ResolveElevationBase(raster));

            SpatialTerrainCache.Store(
                spatialContext,
                raster.SrsName,
                spatialContext.Wgs84BoundingBox,
                raster,
                elevationBase);

            var mesh = BuildProjectedTerrainMesh(
                raster,
                spatialContext.Wgs84BoundingBox,
                spatialContext,
                elevationBase);
            var status = BuildStatusMessage(raster.CoverageId, spatialContext.UseAbsoluteCoordinates, false)
                + " The built-in fallback is intended for quick context; use explicit terrain for project-grade elevation.";

            return new SolveResults
            {
                TerrainMeshes = mesh is null ? new List<Mesh>() : new List<Mesh> { mesh },
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

            var alignmentNote = useAbsoluteCoordinates
                ? "with absolute elevation."
                : "aligned to the shared local terrain/building elevation baseline.";

            return $"{actionPrefix}: {coverageId} {alignmentNote}";
        }

        private static BoundingBox2D ResolveRequestedBoundingBox(
            SpatialContext2D spatialContext,
            WcsCoverageDescription description,
            out Coordinate2D placementOrigin)
        {
            if (RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(spatialContext, description.NativeSrs, out var bbox, out placementOrigin))
            {
                return ClampBoundingBox(bbox, description.NativeBoundingBox);
            }

            placementOrigin = spatialContext.PlacementOrigin;
            return ClampBoundingBox(spatialContext.RequestBoundingBox, description.NativeBoundingBox);
        }

        private static BoundingBox2D ClampBoundingBox(BoundingBox2D requested, BoundingBox2D nativeBounds)
        {
            var minX = Math.Max(requested.MinX, nativeBounds.MinX);
            var minY = Math.Max(requested.MinY, nativeBounds.MinY);
            var maxX = Math.Min(requested.MaxX, nativeBounds.MaxX);
            var maxY = Math.Min(requested.MaxY, nativeBounds.MaxY);

            if (maxX <= minX || maxY <= minY)
            {
                return requested;
            }

            return new BoundingBox2D(minX, minY, maxX, maxY);
        }

        private static Mesh? BuildTerrainMesh(
            TerrainRasterData raster,
            BoundingBox2D requestBoundingBox,
            Coordinate2D placementOrigin,
            bool useAbsoluteCoordinates,
            double elevationBase)
        {
            var width = raster.Width;
            var height = raster.Height;
            if (width <= 1 || height <= 1)
            {
                return null;
            }

            var safeMaxGrid = Math.Clamp(InternalGridSize, 64, DefaultMaxGridSizeLimit);
            var strideX = Math.Max(1, (int)Math.Ceiling(width / (double)safeMaxGrid));
            var strideY = Math.Max(1, (int)Math.Ceiling(height / (double)safeMaxGrid));

            var sampleWidth = (int)Math.Ceiling(width / (double)strideX);
            var sampleHeight = (int)Math.Ceiling(height / (double)strideY);

            var spanX = requestBoundingBox.MaxX - requestBoundingBox.MinX;
            var spanY = requestBoundingBox.MaxY - requestBoundingBox.MinY;
            var cellSizeX = spanX / (width - 1);
            var cellSizeY = spanY / (height - 1);

            var offsetX = useAbsoluteCoordinates ? 0.0 : placementOrigin.X;
            var offsetY = useAbsoluteCoordinates ? 0.0 : placementOrigin.Y;
            var mesh = new Mesh();

            for (var y = 0; y < sampleHeight; y++)
            {
                var sourceY = Math.Min(height - 1, y * strideY);
                var worldY = requestBoundingBox.MaxY - sourceY * cellSizeY - offsetY;

                for (var x = 0; x < sampleWidth; x++)
                {
                    var sourceX = Math.Min(width - 1, x * strideX);
                    var worldX = requestBoundingBox.MinX + sourceX * cellSizeX - offsetX;
                    var elevation = raster.Elevations[sourceY * width + sourceX];

                    if (raster.NoDataValue.HasValue && Math.Abs(elevation - raster.NoDataValue.Value) < 1e-3)
                    {
                        elevation = (float)elevationBase;
                    }

                    mesh.Vertices.Add(worldX, worldY, elevation - elevationBase);
                }
            }

            for (var y = 0; y < sampleHeight - 1; y++)
            {
                for (var x = 0; x < sampleWidth - 1; x++)
                {
                    var index0 = y * sampleWidth + x;
                    var index1 = index0 + 1;
                    var index2 = index0 + sampleWidth + 1;
                    var index3 = index0 + sampleWidth;
                    mesh.Faces.AddFace(index0, index1, index2, index3);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        private static Mesh? BuildProjectedTerrainMesh(
            TerrainRasterData raster,
            BoundingBox2D rasterBoundingBox4326,
            SpatialContext2D spatialContext,
            double elevationBase)
        {
            var width = raster.Width;
            var height = raster.Height;
            if (width <= 1 || height <= 1)
            {
                return null;
            }

            var safeMaxGrid = Math.Clamp(InternalGridSize, 64, DefaultMaxGridSizeLimit);
            var strideX = Math.Max(1, (int)Math.Ceiling(width / (double)safeMaxGrid));
            var strideY = Math.Max(1, (int)Math.Ceiling(height / (double)safeMaxGrid));

            var sampleWidth = (int)Math.Ceiling(width / (double)strideX);
            var sampleHeight = (int)Math.Ceiling(height / (double)strideY);
            var spanX = rasterBoundingBox4326.MaxX - rasterBoundingBox4326.MinX;
            var spanY = rasterBoundingBox4326.MaxY - rasterBoundingBox4326.MinY;
            var cellSizeX = spanX / (width - 1);
            var cellSizeY = spanY / (height - 1);
            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;
            var mesh = new Mesh();

            for (var y = 0; y < sampleHeight; y++)
            {
                var sourceY = Math.Min(height - 1, y * strideY);
                var latitude = rasterBoundingBox4326.MaxY - sourceY * cellSizeY;

                for (var x = 0; x < sampleWidth; x++)
                {
                    var sourceX = Math.Min(width - 1, x * strideX);
                    var longitude = rasterBoundingBox4326.MinX + sourceX * cellSizeX;
                    if (!SpatialReferenceTransform.TryTransformXY(
                            "EPSG:4326",
                            spatialContext.ResolvedSrs,
                            longitude,
                            latitude,
                            out var worldX,
                            out var worldY))
                    {
                        return null;
                    }

                    var elevation = raster.Elevations[sourceY * width + sourceX];
                    if (raster.NoDataValue.HasValue && Math.Abs(elevation - raster.NoDataValue.Value) < 1e-3)
                    {
                        elevation = (float)elevationBase;
                    }

                    mesh.Vertices.Add(worldX - offsetX, worldY - offsetY, elevation - elevationBase);
                }
            }

            for (var y = 0; y < sampleHeight - 1; y++)
            {
                for (var x = 0; x < sampleWidth - 1; x++)
                {
                    var index0 = y * sampleWidth + x;
                    var index1 = index0 + 1;
                    var index2 = index0 + sampleWidth + 1;
                    var index3 = index0 + sampleWidth;
                    mesh.Faces.AddFace(index0, index1, index2, index3);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        private static double ResolveElevationBase(TerrainRasterData raster)
        {
            var minZ = double.PositiveInfinity;

            foreach (var elevation in raster.Elevations)
            {
                if (raster.NoDataValue.HasValue && Math.Abs(elevation - raster.NoDataValue.Value) < 1e-3)
                {
                    continue;
                }

                if (elevation < minZ)
                {
                    minZ = elevation;
                }
            }

            return double.IsInfinity(minZ) ? 0.0 : minZ;
        }

        private static int CountValidElevations(TerrainRasterData raster)
        {
            var validCount = 0;
            foreach (var elevation in raster.Elevations)
            {
                if (raster.NoDataValue.HasValue && Math.Abs(elevation - raster.NoDataValue.Value) < 1e-3)
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
