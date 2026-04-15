using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino;
using Rhino.Render;
using RhinoSpatial.Core;
using System.Linq;
using Rhino.DocObjects.Tables;

namespace RhinoSpatial
{
    public class WmsLoadComponent : GH_TaskCapableComponent<WmsLoadComponent.SolveResults>
    {
        private readonly WmsClient _wmsClient = new();
        private Mesh? _previewMesh;
        private DisplayMaterial? _previewMaterial;
        private string? _previewImageFilePath;
        private BoundingBox _previewBox = BoundingBox.Empty;

        public class SolveResults
        {
            public string ImageFilePath { get; init; } = string.Empty;

            public string GetMapUrl { get; init; } = string.Empty;

            public Mesh? ImageMesh { get; init; }

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        public WmsLoadComponent()
            : base("Load WMS", "Load WMS",
                "Load aligned WMS imagery for the shared RhinoSpatial spatial context and output a textured mesh that previews cleanly and bakes more reliably.",
                "RhinoSpatial", "Sources")
        {
            NormalizeInputConfiguration();
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("WMS Service URL", "WMS URL", "Base URL of the WMS service. Leave empty to use the global NASA GIBS fallback imagery source.", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "Layer", "Optional WMS layer name to request. Leave empty to let RhinoSpatial choose a usable requestable layer automatically.", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context from the Spatial Context component. No WFS inputs are required for the WMS workflow.", GH_ParamAccess.item);
            pManager.AddTextParameter("Format", "Format", "Requested image format.", GH_ParamAccess.item, "image/png");

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[3].Optional = true;
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            NormalizeInputConfiguration();
        }

        public override bool Read(GH_IReader reader)
        {
            var result = base.Read(reader);
            NormalizeInputConfiguration();
            return result;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Image Mesh", "Image Mesh", "Aligned local mesh for the downloaded WMS image.", GH_ParamAccess.item);
            pManager.AddGenericParameter("Material", "Material", "Material with the downloaded WMS image already attached. Connect with the mesh to Custom Preview if needed.", GH_ParamAccess.item);
            pManager.AddTextParameter("Image File", "Image File", "Local cached image file downloaded from the WMS service.", GH_ParamAccess.item);
            pManager.AddTextParameter("GetMap URL", "GetMap URL", "Final WMS GetMap request URL built from the shared spatial context.", GH_ParamAccess.item);
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.LoadWms.png");

        protected override void SolveInstance(IGH_DataAccess dataAccess)
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

            if (!string.IsNullOrWhiteSpace(result.GetMapUrl))
            {
                dataAccess.SetData(3, result.GetMapUrl);
            }
        }

        private sealed class RequestData
        {
            public ResolvedImagerySource Source { get; init; } = null!;

            public SpatialContext2D SpatialContext { get; init; } = null!;

            public string Format { get; init; } = "image/png";
        }

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? baseUrl = null;
            string? layerName = null;
            string? spatialContextText = null;
            var format = "image/png";

            dataAccess.GetData(0, ref baseUrl);
            dataAccess.GetData(1, ref layerName);

            dataAccess.GetData(2, ref spatialContextText);
            dataAccess.GetData(3, ref format);

            if (!string.IsNullOrWhiteSpace(layerName))
            {
                layerName = RhinoSpatialInputParser.ParseLayerName(layerName);
            }

            if (!string.IsNullOrWhiteSpace(layerName) &&
                (layerName.Contains("://", StringComparison.Ordinal) ||
                 layerName.Contains("REQUEST=GetCapabilities", StringComparison.OrdinalIgnoreCase) ||
                 layerName.Contains("SERVICE=WMS", StringComparison.OrdinalIgnoreCase)))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Layer must be a WMS layer name, not the WMS service URL. Connect the service URL to 'WMS URL' and provide an actual WMS layer name here.");
                return false;
            }

            if (!RhinoSpatialInputParser.TryGetRequiredSpatialContext(spatialContextText, out var spatialContext, out var errorMessage))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, errorMessage);
                return false;
            }

            requestData = new RequestData
            {
                Source = RhinoSpatialSourceFallbacks.ResolveImagerySource(baseUrl, layerName),
                SpatialContext = spatialContext,
                Format = string.IsNullOrWhiteSpace(format) ? "image/png" : format.Trim()
            };

            return true;
        }

        private sealed class ResolvedLayer
        {
            public string LayerName { get; init; } = string.Empty;
        }

        private ResolvedLayer ResolveLayerName(WmsCapabilitiesInfo capabilities, string? requestedLayerName)
        {
            if (!string.IsNullOrWhiteSpace(requestedLayerName))
            {
                return new ResolvedLayer
                {
                    LayerName = requestedLayerName.Trim()
                };
            }

            if (capabilities.Layers.Count == 0)
            {
                return new ResolvedLayer();
            }

            if (capabilities.Layers.Count == 1)
            {
                return new ResolvedLayer
                {
                    LayerName = capabilities.Layers[0].Name
                };
            }

            var preferredLayer = ChoosePreferredLayer(capabilities.Layers);

            return new ResolvedLayer
            {
                LayerName = preferredLayer.Name
            };
        }

        private static WmsLayerInfo ChoosePreferredLayer(List<WmsLayerInfo> layers)
        {
            var rankedLayers = layers
                .Select(layer => new
                {
                    Layer = layer,
                    Score = ScoreLayer(layer)
                })
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Layer.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return rankedLayers[0].Layer;
        }

        private static int ScoreLayer(WmsLayerInfo layer)
        {
            var combinedText = $"{layer.Name} {layer.Title}";
            var score = 0;

            if (Contains(combinedText, "dop"))
            {
                score += 100;
            }

            if (Contains(combinedText, "ortho"))
            {
                score += 80;
            }

            if (Contains(combinedText, "rgb"))
            {
                score += 40;
            }

            if (Contains(combinedText, "image") || Contains(combinedText, "imagery"))
            {
                score += 20;
            }

            if (Contains(combinedText, "cir"))
            {
                score -= 20;
            }

            if (Contains(combinedText, "info"))
            {
                score -= 60;
            }

            if (Contains(combinedText, "alk"))
            {
                score -= 80;
            }

            if (Contains(combinedText, "tk"))
            {
                score -= 20;
            }

            return score;
        }

        private static bool Contains(string text, string token)
        {
            return text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private SolveResults Compute(RequestData requestData)
        {
            SpatialContext2D requestSpatialContext = requestData.SpatialContext;
            if (!RhinoSpatialSourceFallbacks.TryCreateRequestSpatialContext(
                    requestData.SpatialContext,
                    requestData.Source.RequiredQuerySrs,
                    out requestSpatialContext,
                    out var fallbackError))
            {
                return new SolveResults
                {
                    Status = fallbackError,
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var capabilities = _wmsClient.LoadCapabilitiesAsync(requestData.Source.BaseUrl).GetAwaiter().GetResult();
            var resolvedLayer = ResolveLayerName(capabilities, requestData.Source.PreferredLayerName);
            if (string.IsNullOrWhiteSpace(resolvedLayer.LayerName))
            {
                return new SolveResults
                {
                    Status = "No usable WMS layer could be resolved automatically. Connect a specific requestable WMS layer if this service needs one.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var requestOptions = RhinoSpatialContextTools.CreateWmsRequestOptions(
                requestData.Source.BaseUrl,
                resolvedLayer.LayerName,
                requestSpatialContext,
                capabilities,
                requestData.Format);

            var imageResult = _wmsClient.DownloadImageAsync(requestOptions).GetAwaiter().GetResult();
            var imageMesh = RhinoSpatialContextTools.CreateBoundingBoxMesh(
                requestSpatialContext.PlacementBoundingBox,
                requestSpatialContext.PlacementOrigin,
                requestSpatialContext.UseAbsoluteCoordinates);

            var statusPrefix = requestData.Source.CreateStatusPrefix();

            return new SolveResults
            {
                ImageFilePath = imageResult.LocalFilePath,
                ImageMesh = imageMesh,
                GetMapUrl = imageResult.RequestUrl,
                Status = $"{statusPrefix}Downloaded WMS image to '{imageResult.LocalFilePath}'.",
                MessageLevel = requestData.Source.UsesFallback ? GH_RuntimeMessageLevel.Remark : null
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

        public override Guid ComponentGuid => new Guid("70c5074b-d37f-4459-b287-2a1ecaf17870");

        private void NormalizeInputConfiguration()
        {
            if (Params.Input.Count < 4)
            {
                return;
            }

            Params.Input[0].Optional = true;
            Params.Input[1].Optional = true;
            Params.Input[3].Optional = true;
        }
    }
}
