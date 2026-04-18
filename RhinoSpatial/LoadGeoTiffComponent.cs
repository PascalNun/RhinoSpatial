using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Render;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public class LoadGeoTiffComponent : GH_TaskCapableComponent<LoadGeoTiffComponent.SolveResults>
    {
        private const long LargeRasterFileSizeBytes = 200L * 1024L * 1024L;
        private const long LargeRasterPixelCount = 40_000_000L;

        private Mesh? _previewMesh;
        private DisplayMaterial? _previewMaterial;
        private string? _previewImageFilePath;
        private BoundingBox _previewBox = BoundingBox.Empty;
        private GH_Material? _outputMaterial;
        private string? _outputMaterialFilePath;
        private string? _cachedRequestSignature;
        private SolveResults? _cachedResult;

        public class SolveResults
        {
            public string ImageFilePath { get; init; } = string.Empty;
            public Mesh? ImageMesh { get; init; }
            public string Status { get; init; } = string.Empty;
            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private sealed class RequestData
        {
            public string FilePath { get; init; } = string.Empty;
            public SpatialContext2D SpatialContext { get; init; } = null!;
            public string RequestSignature { get; init; } = string.Empty;
        }

        public LoadGeoTiffComponent()
            : base("Load GeoTIFF", "Load GeoTIFF",
                "Load a georeferenced GeoTIFF raster and align it to the shared RhinoSpatial spatial context.",
                "RhinoSpatial", "Sources")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("GeoTIFF File", "GeoTIFF", "Path to a georeferenced GeoTIFF raster file.", GH_ParamAccess.item);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context from the Spatial Context component.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Image Mesh", "Image Mesh", "Aligned local mesh for the GeoTIFF image.", GH_ParamAccess.item);
            pManager.AddGenericParameter("Material", "Material", "Material with the GeoTIFF already attached.", GH_ParamAccess.item);
            pManager.AddTextParameter("Image File", "Image File", "Local GeoTIFF file used for the aligned raster source.", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "Status", "Status or warning information from the GeoTIFF loader.", GH_ParamAccess.item);
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.LoadGeoTIFF.png");

        public override bool Read(GH_IReader reader)
        {
            var result = base.Read(reader);
            ClearPreviewState();
            return result;
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            try
            {
                if (!TryGetRequestData(dataAccess, out var requestData))
                {
                    ClearPreviewState();
                    return;
                }

                if (TryGetCachedSolveResult(requestData, out var cachedResult))
                {
                    ApplySolveResults(dataAccess, cachedResult);
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

                CacheSolveResult(requestData, result);
                ApplySolveResults(dataAccess, result);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? filePath = null;
            string? spatialContextText = null;

            dataAccess.GetData(0, ref filePath);
            dataAccess.GetData(1, ref spatialContextText);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GeoTIFF File is required.");
                return false;
            }

            if (!File.Exists(filePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"GeoTIFF file not found: {filePath}");
                return false;
            }

            if (!RhinoSpatialInputParser.TryGetRequiredSpatialContext(spatialContextText, out var spatialContext, out var errorMessage))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, errorMessage);
                return false;
            }

            requestData = new RequestData
            {
                FilePath = filePath.Trim(),
                SpatialContext = spatialContext,
                RequestSignature = BuildRequestSignature(filePath.Trim(), spatialContext)
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            var rasterInfo = GeoTiffInfoCache.GetOrRead(requestData.FilePath);

            if (!TryCreateImageMesh(
                    rasterInfo,
                    requestData.SpatialContext,
                    out var imageMesh,
                    out var transformedBoundingBox,
                    out var clippedToSpatialContext,
                    out var overlapRatio))
            {
                return new SolveResults
                {
                    Status = $"The GeoTIFF could not be aligned from '{rasterInfo.SrsName}' into the current Spatial Context SRS '{requestData.SpatialContext.ResolvedSrs}'.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            if (!RhinoSpatialContextTools.DoBoundingBoxesIntersect(
                    transformedBoundingBox,
                    RhinoSpatialContextTools.CreatePlacedBoundingBox(
                        requestData.SpatialContext.PlacementBoundingBox,
                        requestData.SpatialContext.PlacementOrigin,
                        requestData.SpatialContext.UseAbsoluteCoordinates)))
            {
                return new SolveResults
                {
                    ImageFilePath = rasterInfo.LocalFilePath,
                    Status = $"Loaded GeoTIFF '{Path.GetFileName(rasterInfo.LocalFilePath)}' in {rasterInfo.SrsName}, but it does not overlap the selected Spatial Context area.",
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }

            var largeRasterNote = BuildLargeRasterNote(rasterInfo);
            if (clippedToSpatialContext)
            {
                return new SolveResults
                {
                    ImageFilePath = rasterInfo.LocalFilePath,
                    ImageMesh = imageMesh,
                    Status = $"Loaded GeoTIFF '{Path.GetFileName(rasterInfo.LocalFilePath)}' ({rasterInfo.Width}×{rasterInfo.Height}, {rasterInfo.SrsName}) and clipped the preview to the selected Spatial Context. Visible overlap: {overlapRatio:P0}. The source file remains unchanged.{largeRasterNote}",
                    MessageLevel = GH_RuntimeMessageLevel.Remark
                };
            }

            return new SolveResults
            {
                ImageFilePath = rasterInfo.LocalFilePath,
                ImageMesh = imageMesh,
                Status = $"Loaded GeoTIFF '{Path.GetFileName(rasterInfo.LocalFilePath)}' ({rasterInfo.Width}×{rasterInfo.Height}, {rasterInfo.SrsName}) and aligned it to the current Spatial Context.{largeRasterNote}",
                MessageLevel = string.IsNullOrWhiteSpace(largeRasterNote) ? null : GH_RuntimeMessageLevel.Remark
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

        private bool TryGetCachedSolveResult(RequestData requestData, out SolveResults result)
        {
            result = default!;

            if (string.IsNullOrWhiteSpace(requestData.RequestSignature) ||
                string.IsNullOrWhiteSpace(_cachedRequestSignature) ||
                _cachedResult is null ||
                !string.Equals(_cachedRequestSignature, requestData.RequestSignature, StringComparison.Ordinal))
            {
                return false;
            }

            result = _cachedResult;
            return true;
        }

        private void CacheSolveResult(RequestData requestData, SolveResults result)
        {
            _cachedRequestSignature = requestData.RequestSignature;
            _cachedResult = result;
        }

        private void ApplySolveResults(IGH_DataAccess dataAccess, SolveResults result)
        {
            if (!string.IsNullOrWhiteSpace(result.Status) && result.MessageLevel.HasValue)
            {
                AddRuntimeMessage(result.MessageLevel.Value, result.Status);
            }

            UpdatePreviewState(result);

            if (result.ImageMesh is not null)
            {
                dataAccess.SetData(0, result.ImageMesh);
            }

            var material = GetOrCreateOutputMaterial(result.ImageFilePath);
            if (result.ImageMesh is not null && material is not null)
            {
                dataAccess.SetData(1, material);
            }

            if (!string.IsNullOrWhiteSpace(result.ImageFilePath))
            {
                dataAccess.SetData(2, result.ImageFilePath);
            }

            dataAccess.SetData(3, result.Status);
        }

        private static bool TryCreateImageMesh(
            GeoReferencedRasterInfo rasterInfo,
            SpatialContext2D spatialContext,
            out Mesh imageMesh,
            out BoundingBox2D transformedBoundingBox,
            out bool clippedToSpatialContext,
            out double overlapRatio)
        {
            imageMesh = new Mesh();
            transformedBoundingBox = new BoundingBox2D(0.0, 0.0, 0.0, 0.0);
            clippedToSpatialContext = false;
            overlapRatio = 0.0;

            var corners = new[]
            {
                new Coordinate2D(rasterInfo.BoundingBox.MinX, rasterInfo.BoundingBox.MinY),
                new Coordinate2D(rasterInfo.BoundingBox.MaxX, rasterInfo.BoundingBox.MinY),
                new Coordinate2D(rasterInfo.BoundingBox.MaxX, rasterInfo.BoundingBox.MaxY),
                new Coordinate2D(rasterInfo.BoundingBox.MinX, rasterInfo.BoundingBox.MaxY)
            };

            var transformedCorners = new Point3d[4];
            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;

            for (var index = 0; index < corners.Length; index++)
            {
                var sourceCorner = corners[index];
                if (!SpatialReferenceTransform.TryTransformXY(
                        rasterInfo.SrsName,
                        spatialContext.ResolvedSrs,
                        sourceCorner.X,
                        sourceCorner.Y,
                        out var x,
                        out var y))
                {
                    return false;
                }

                transformedCorners[index] = new Point3d(x - offsetX, y - offsetY, 0.0);
            }

            transformedBoundingBox = BoundingBox2DFromPoints(transformedCorners);

            var placedSpatialContextBoundingBox = RhinoSpatialContextTools.CreatePlacedBoundingBox(
                spatialContext.PlacementBoundingBox,
                spatialContext.PlacementOrigin,
                spatialContext.UseAbsoluteCoordinates);

            var fullArea = RhinoSpatialContextTools.CalculateBoundingBoxArea(transformedBoundingBox);
            if (fullArea > 0.0 &&
                RhinoSpatialContextTools.TryIntersectBoundingBoxes(
                    transformedBoundingBox,
                    placedSpatialContextBoundingBox,
                    out var intersectionBoundingBox))
            {
                overlapRatio = RhinoSpatialContextTools.CalculateBoundingBoxArea(intersectionBoundingBox) / fullArea;
                if (overlapRatio > 0.0 && overlapRatio < 0.999)
                {
                    clippedToSpatialContext = true;

                    var minU = (float)((intersectionBoundingBox.MinX - transformedBoundingBox.MinX) / (transformedBoundingBox.MaxX - transformedBoundingBox.MinX));
                    var maxU = (float)((intersectionBoundingBox.MaxX - transformedBoundingBox.MinX) / (transformedBoundingBox.MaxX - transformedBoundingBox.MinX));
                    var minV = (float)((intersectionBoundingBox.MinY - transformedBoundingBox.MinY) / (transformedBoundingBox.MaxY - transformedBoundingBox.MinY));
                    var maxV = (float)((intersectionBoundingBox.MaxY - transformedBoundingBox.MinY) / (transformedBoundingBox.MaxY - transformedBoundingBox.MinY));

                    imageMesh = RhinoSpatialContextTools.CreateTexturedBoundingBoxMesh(
                        intersectionBoundingBox,
                        new Coordinate2D(0.0, 0.0),
                        true,
                        minU,
                        minV,
                        maxU,
                        maxV);

                    return true;
                }
            }

            imageMesh = RhinoSpatialContextTools.CreateTexturedBoundingBoxMesh(
                transformedBoundingBox,
                new Coordinate2D(0.0, 0.0),
                true,
                0.0f,
                0.0f,
                1.0f,
                1.0f);
            return true;
        }

        private static BoundingBox2D BoundingBox2DFromPoints(IEnumerable<Point3d> points)
        {
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;

            foreach (var point in points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            return new BoundingBox2D(minX, minY, maxX, maxY);
        }

        private static string BuildLargeRasterNote(GeoReferencedRasterInfo rasterInfo)
        {
            var pixelCount = (long)rasterInfo.Width * rasterInfo.Height;
            if (pixelCount <= LargeRasterPixelCount && rasterInfo.FileSizeBytes <= LargeRasterFileSizeBytes)
            {
                return string.Empty;
            }

            return $" Large rasters can take longer to preview and bake ({FormatFileSize(rasterInfo.FileSizeBytes)}).";
        }

        private static string FormatFileSize(long bytes)
        {
            const double kilobyte = 1024.0;
            const double megabyte = kilobyte * 1024.0;
            const double gigabyte = megabyte * 1024.0;

            if (bytes >= gigabyte)
            {
                return $"{bytes / gigabyte:0.0} GB";
            }

            if (bytes >= megabyte)
            {
                return $"{bytes / megabyte:0.0} MB";
            }

            if (bytes >= kilobyte)
            {
                return $"{bytes / kilobyte:0.0} KB";
            }

            return $"{bytes} B";
        }

        private void UpdatePreviewState(SolveResults result)
        {
            if (result.ImageMesh is null || string.IsNullOrWhiteSpace(result.ImageFilePath))
            {
                ClearPreviewState();
                return;
            }

            _previewMesh = result.ImageMesh;
            if (_previewMaterial is null || !string.Equals(_previewImageFilePath, result.ImageFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _previewMaterial = RhinoSpatialRasterDisplayTools.CreateDisplayMaterial(result.ImageFilePath);
            }

            _previewImageFilePath = result.ImageFilePath;
            _previewBox = _previewMesh?.GetBoundingBox(false) ?? BoundingBox.Empty;
        }

        private void ClearPreviewState()
        {
            _previewMesh = null;
            _previewMaterial = null;
            _previewImageFilePath = null;
            _previewBox = BoundingBox.Empty;
            _cachedRequestSignature = null;
            _cachedResult = null;
        }

        private GH_Material? GetOrCreateOutputMaterial(string imageFilePath)
        {
            if (string.IsNullOrWhiteSpace(imageFilePath))
            {
                _outputMaterial = null;
                _outputMaterialFilePath = null;
                return null;
            }

            if (_outputMaterial is not null &&
                string.Equals(_outputMaterialFilePath, imageFilePath, StringComparison.OrdinalIgnoreCase))
            {
                return _outputMaterial;
            }

            _outputMaterial = RhinoSpatialRasterDisplayTools.CreateGrasshopperMaterial(imageFilePath);
            _outputMaterialFilePath = imageFilePath;
            return _outputMaterial;
        }

        public override BoundingBox ClippingBox => _previewBox.IsValid ? _previewBox : BoundingBox.Empty;

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            if (_previewMesh is null || _previewMaterial is null)
            {
                return;
            }

            args.Display.DrawMeshShaded(_previewMesh, _previewMaterial);
        }

        public override void BakeGeometry(RhinoDoc doc, List<Guid> objIds)
        {
            BakeGeometry(doc, new ObjectAttributes(), objIds);
        }

        public override void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds)
        {
            if (_previewMesh is null || string.IsNullOrWhiteSpace(_previewImageFilePath) || !File.Exists(_previewImageFilePath))
            {
                return;
            }

            var objectId = RhinoSpatialRasterDisplayTools.BakeTexturedMesh(doc, _previewMesh, _previewImageFilePath, att);
            if (objectId != Guid.Empty)
            {
                objIds.Add(objectId);
            }
        }

        private static string BuildRequestSignature(string filePath, SpatialContext2D spatialContext)
        {
            var fileInfo = new FileInfo(filePath);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}|" +
                $"{spatialContext.ResolvedSrs}|{spatialContext.UseAbsoluteCoordinates}|" +
                $"{FormatBoundingBox(spatialContext.PlacementBoundingBox)}|" +
                $"{FormatCoordinate(spatialContext.PlacementOrigin)}");
        }

        private static string FormatBoundingBox(BoundingBox2D boundingBox)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{boundingBox.MinX:F6},{boundingBox.MinY:F6},{boundingBox.MaxX:F6},{boundingBox.MaxY:F6}");
        }

        private static string FormatCoordinate(Coordinate2D coordinate)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{coordinate.X:F6},{coordinate.Y:F6}");
        }

        public override Guid ComponentGuid => new Guid("5cf2c75a-1f04-458d-bb6f-f8ab7af59bfb");
    }
}
