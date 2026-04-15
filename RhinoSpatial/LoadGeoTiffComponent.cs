using System;
using System.Collections.Generic;
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
        private Mesh? _previewMesh;
        private DisplayMaterial? _previewMaterial;
        private string? _previewImageFilePath;
        private BoundingBox _previewBox = BoundingBox.Empty;

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

                UpdatePreviewState(result);

                if (result.ImageMesh is not null)
                {
                    dataAccess.SetData(0, result.ImageMesh);
                }

                var material = RhinoSpatialRasterDisplayTools.CreateGrasshopperMaterial(result.ImageFilePath);
                if (material is not null)
                {
                    dataAccess.SetData(1, material);
                }

                if (!string.IsNullOrWhiteSpace(result.ImageFilePath))
                {
                    dataAccess.SetData(2, result.ImageFilePath);
                }

                dataAccess.SetData(3, result.Status);
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
                SpatialContext = spatialContext
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            var rasterInfo = GeoTiffReader.ReadImageInfo(requestData.FilePath);

            if (!TryCreateImageMesh(rasterInfo, requestData.SpatialContext, out var imageMesh, out var transformedBoundingBox))
            {
                return new SolveResults
                {
                    Status = $"The GeoTIFF could not be aligned from '{rasterInfo.SrsName}' into the current Spatial Context SRS '{requestData.SpatialContext.ResolvedSrs}'.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            if (!RhinoSpatialContextTools.DoBoundingBoxesIntersect(
                    transformedBoundingBox,
                    requestData.SpatialContext.PlacementBoundingBox))
            {
                return new SolveResults
                {
                    ImageFilePath = rasterInfo.LocalFilePath,
                    ImageMesh = imageMesh,
                    Status = $"Loaded GeoTIFF '{Path.GetFileName(rasterInfo.LocalFilePath)}'. The current file does not overlap the selected Spatial Context area, so it may appear outside the active study area.",
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }

            return new SolveResults
            {
                ImageFilePath = rasterInfo.LocalFilePath,
                ImageMesh = imageMesh,
                Status = $"Loaded GeoTIFF '{Path.GetFileName(rasterInfo.LocalFilePath)}' in {rasterInfo.SrsName}. The current RhinoSpatial GeoTIFF loader keeps the full georeferenced raster and applies shared placement/localization, but does not crop the file to the Spatial Context yet."
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

        private static bool TryCreateImageMesh(
            GeoReferencedRasterInfo rasterInfo,
            SpatialContext2D spatialContext,
            out Mesh imageMesh,
            out BoundingBox2D transformedBoundingBox)
        {
            imageMesh = new Mesh();
            transformedBoundingBox = new BoundingBox2D(0.0, 0.0, 0.0, 0.0);

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
            imageMesh = new Mesh();
            imageMesh.Vertices.Add(transformedCorners[0]);
            imageMesh.Vertices.Add(transformedCorners[1]);
            imageMesh.Vertices.Add(transformedCorners[2]);
            imageMesh.Vertices.Add(transformedCorners[3]);
            imageMesh.Faces.AddFace(0, 1, 2, 3);
            imageMesh.TextureCoordinates.Add(0.0f, 0.0f);
            imageMesh.TextureCoordinates.Add(1.0f, 0.0f);
            imageMesh.TextureCoordinates.Add(1.0f, 1.0f);
            imageMesh.TextureCoordinates.Add(0.0f, 1.0f);
            imageMesh.Normals.ComputeNormals();
            imageMesh.Compact();
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

        private void UpdatePreviewState(SolveResults result)
        {
            _previewMesh = result.ImageMesh;
            _previewMaterial = RhinoSpatialRasterDisplayTools.CreateDisplayMaterial(result.ImageFilePath);
            _previewImageFilePath = result.ImageFilePath;
            _previewBox = _previewMesh?.GetBoundingBox(false) ?? BoundingBox.Empty;
        }

        private void ClearPreviewState()
        {
            _previewMesh = null;
            _previewMaterial = null;
            _previewImageFilePath = null;
            _previewBox = BoundingBox.Empty;
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

            var meshToBake = _previewMesh.DuplicateMesh();
            var attributes = att.Duplicate();
            var material = RhinoSpatialRasterDisplayTools.CreateRhinoMaterial(_previewImageFilePath);
            RenderMaterial? renderMaterial = null;

            if (material is not null)
            {
                material.Name = $"RhinoSpatial {Path.GetFileNameWithoutExtension(_previewImageFilePath)}";
                material.CommitChanges();
                var materialIndex = doc.Materials.Add(material);
                if (materialIndex >= 0)
                {
                    attributes.MaterialIndex = materialIndex;
                    attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
                    renderMaterial = RenderMaterial.CreateBasicMaterial(doc.Materials[materialIndex], doc);

                    if (renderMaterial is not null)
                    {
                        renderMaterial.Name = material.Name;
                        doc.RenderMaterials.Add(renderMaterial);
                        attributes.RenderMaterial = renderMaterial;
                    }
                }
            }

            var objectId = doc.Objects.AddMesh(meshToBake, attributes);
            if (objectId != Guid.Empty)
            {
                if (renderMaterial is not null)
                {
                    doc.Objects.ModifyRenderMaterial(objectId, renderMaterial);
                }

                objIds.Add(objectId);
            }
        }

        public override Guid ComponentGuid => new Guid("5cf2c75a-1f04-458d-bb6f-f8ab7af59bfb");
    }
}
