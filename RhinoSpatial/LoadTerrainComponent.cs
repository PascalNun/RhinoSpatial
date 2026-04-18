using System;
using System.Collections.Generic;
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

        private readonly WcsClient _wcsClient = new();

        public class SolveResults
        {
            public List<Mesh> TerrainMeshes { get; init; } = new();

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        public LoadTerrainComponent()
            : base("Load Terrain", "Load Terrain",
                "Load an aligned terrain surface for the shared RhinoSpatial spatial context.",
                "RhinoSpatial", "Sources")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.LoadTerrain.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Terrain Service URL", "Terrain URL", "Base URL of the terrain (WCS) service. Leave empty to use the built-in default terrain source. RhinoSpatial is structured so lower-resolution global DEM fallbacks can be added later.", GH_ParamAccess.item);
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
                    var task = Task.Run(() => ComputeAsync(requestData), CancelToken);
                    TaskList.Add(task);
                    return;
                }

                if (!GetSolveResults(dataAccess, out var results))
                {
                    results = ComputeAsync(requestData).GetAwaiter().GetResult();
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
                RhinoSpatialSourceFallbacks.ResolveTerrainSource(serviceUrl, coverageId),
                spatialContext);
        }

        private async Task<SolveResults> ComputeAsync(RequestData requestData)
        {
            try
            {
                var spatialContext = requestData.SpatialContext;
                var capabilities = await _wcsClient.LoadCapabilitiesAsync(requestData.Source.BaseUrl);

                var coverageId = requestData.Source.CoverageId;
                if (!capabilities.Coverages.Exists(coverage => coverage.CoverageId.Equals(coverageId, StringComparison.OrdinalIgnoreCase)))
                {
                    if (capabilities.Coverages.Count > 0)
                    {
                        coverageId = capabilities.Coverages[0].CoverageId;
                    }
                }

                var options = new WcsRequestOptions
                {
                    BaseUrl = requestData.Source.BaseUrl,
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
                    Status = $"{requestData.Source.CreateStatusPrefix()}{status}"
                };
            }
            catch (Exception ex)
            {
                return new SolveResults
                {
                    TerrainMeshes = new List<Mesh>(),
                    Status = ex.Message,
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }
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

        private record RequestData(ResolvedTerrainSource Source, SpatialContext2D SpatialContext);

        public override Guid ComponentGuid => new Guid("e6941d59-50c0-46f4-96fa-9546a0f54f9d");
    }
}
