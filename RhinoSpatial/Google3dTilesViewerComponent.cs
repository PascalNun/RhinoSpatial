using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;
using Rhino;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public class Google3dTilesViewerComponent : GH_Component
    {
        private bool _lastOpenViewerRequest;
        private readonly List<Google3dTilesReferenceSession.DisplayPrimitive> _previewPrimitives = new();
        private readonly List<Mesh> _previewFallbackMeshes = new();
        private Curve? _previewFrame;
        private BoundingBox _previewBox = BoundingBox.Empty;

        public Google3dTilesViewerComponent()
            : base(
                "3D Tiles Viewer (Google)",
                "3D Tiles View",
                "Enable a runtime-streamed Google Photorealistic 3D Tiles viewer for the selected Spatial Context. This feature is reference-only and does not create bakeable Rhino geometry.",
                "RhinoSpatial",
                "Viewers")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        public override bool IsPreviewCapable => true;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("API Key", "API Key", "User-managed Google Maps API key for the Map Tiles API. The key is used only at runtime in the local Google 3D Tiles viewer backend.", GH_ParamAccess.item);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context from the Spatial Context component.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Enable Viewer", "Enable", "Enable the transient Google 3D Tiles viewer in the Rhino viewport. This layer is reference-only and is not bakeable or exportable.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Open Viewer Window", "Open", "Open the diagnostic Google 3D Tiles runtime window. The viewer backend otherwise stays in the background while Rhino draws the transient viewport mesh.", GH_ParamAccess.item, false);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Viewer URL", "Viewer URL", "Local runtime URL for the streamed Google 3D Tiles viewer page.", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "Status", "Status text for the Google 3D Tiles viewer.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Viewer Active", "Active", "True when the transient Google 3D Tiles viewer is enabled in the Rhino viewport.", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Google3dTilesReferenceManager.RegisterComponent(this);
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            string? apiKey = null;
            string? spatialContextText = null;
            var enableReference = false;
            var openViewer = false;

            dataAccess.GetData(0, ref apiKey);
            dataAccess.GetData(1, ref spatialContextText);
            dataAccess.GetData(2, ref enableReference);
            dataAccess.GetData(3, ref openViewer);

            var viewerUrl = Google3dTilesViewerHost.GetCurrentUrl();
            dataAccess.SetData(0, viewerUrl);
            dataAccess.SetData(2, false);

            if (!RhinoSpatialInputParser.TryGetRequiredSpatialContext(spatialContextText, out var spatialContext, out var errorMessage))
            {
                dataAccess.SetData(1, errorMessage);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, errorMessage);
                Google3dTilesReferenceManager.RemoveSession(InstanceGuid);
                ClearLivePreviewState();
                ResetOpenViewerRequest(openViewer);
                return;
            }

            var normalizedApiKey = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : apiKey.Trim();

            if (string.IsNullOrWhiteSpace(normalizedApiKey))
            {
                const string missingKeyMessage = "API Key is required. This viewer is user-managed and runtime-streamed. RhinoSpatial does not ship or store a shared Google Maps API key.";
                dataAccess.SetData(1, missingKeyMessage);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, missingKeyMessage);
                Google3dTilesReferenceManager.RemoveSession(InstanceGuid);
                ClearLivePreviewState();
                ResetOpenViewerRequest(openViewer);
                return;
            }

            if (!TryResolveWgs84BoundingBox(spatialContext!, out var boundingBox4326))
            {
                const string missingBoundingBoxMessage = "The Spatial Context could not provide a usable EPSG:4326 bounding box for the 3D Tiles viewer.";
                dataAccess.SetData(1, missingBoundingBoxMessage);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, missingBoundingBoxMessage);
                Google3dTilesReferenceManager.RemoveSession(InstanceGuid);
                ClearLivePreviewState();
                ResetOpenViewerRequest(openViewer);
                return;
            }

            const string policyStatus = "Google Photorealistic 3D Tiles are reference-only. RhinoSpatial streams them at runtime as a viewer layer and keeps them outside the editable Rhino workflow: no bake, no export, no offline geometry cache.";

            Google3dTilesViewerHost.UpdateConfiguration(InstanceGuid, normalizedApiKey, boundingBox4326, policyStatus);
            dataAccess.SetData(0, Google3dTilesViewerHost.GetCurrentUrl());
            dataAccess.SetData(2, enableReference);

            if (enableReference)
            {
                var session = CreateViewportSession(spatialContext!, normalizedApiKey, boundingBox4326);
                Google3dTilesReferenceManager.SetSession(session);

                Google3dTilesViewerHost.EnsureBackgroundRuntime(InstanceGuid, normalizedApiKey, boundingBox4326, policyStatus);
                if (Google3dTilesReferenceManager.TryGetSession(InstanceGuid, out var currentSession) && currentSession is not null)
                {
                    ApplyLivePreviewState(currentSession);
                    dataAccess.SetData(1, currentSession.Status);
                }
                else
                {
                    dataAccess.SetData(1, policyStatus);
                }
            }
            else
            {
                Google3dTilesReferenceManager.RemoveSession(InstanceGuid);
                ClearLivePreviewState();
                dataAccess.SetData(1, policyStatus);
            }

            HandleOpenViewer(openViewer, normalizedApiKey, boundingBox4326, policyStatus);
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.View3DTiles.png");

        public override Guid ComponentGuid => new Guid("64c2d9f6-03ab-4e33-9eb7-445ff4b8f3a1");

        public override void RemovedFromDocument(GH_Document document)
        {
            Google3dTilesReferenceManager.UnregisterComponent(InstanceGuid);
            Google3dTilesReferenceManager.RemoveSession(InstanceGuid);
            ClearLivePreviewState();
            base.RemovedFromDocument(document);
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                return _previewBox;
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            if (_previewPrimitives.Count > 0)
            {
                foreach (var primitive in _previewPrimitives)
                {
                    args.Display.DrawMeshShaded(primitive.Mesh, primitive.Material);
                }

                return;
            }

            foreach (var mesh in _previewFallbackMeshes)
            {
                if (mesh.VertexColors.Count == mesh.Vertices.Count)
                {
                    args.Display.DrawMeshFalseColors(mesh);
                }
                else
                {
                    args.Display.DrawMeshShaded(mesh, args.ShadeMaterial);
                }
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (_previewPrimitives.Count > 0 && Attributes?.Selected == true)
            {
                foreach (var primitive in _previewPrimitives)
                {
                    args.Display.DrawMeshWires(primitive.Mesh, args.WireColour_Selected);
                }
            }

            foreach (var mesh in _previewFallbackMeshes)
            {
                args.Display.DrawMeshWires(mesh, Attributes?.Selected == true ? args.WireColour_Selected : args.WireColour);
            }

            if (_previewFrame is not null)
            {
                args.Display.DrawCurve(_previewFrame, Attributes?.Selected == true ? args.WireColour_Selected : System.Drawing.Color.Gold, 2);
            }
        }

        public override void BakeGeometry(RhinoDoc doc, System.Collections.Generic.List<Guid> objIds)
        {
        }

        public override void BakeGeometry(RhinoDoc doc, Rhino.DocObjects.ObjectAttributes att, System.Collections.Generic.List<Guid> objIds)
        {
        }

        private void HandleOpenViewer(bool openViewer, string apiKey, BoundingBox2D boundingBox4326, string status)
        {
            if (!openViewer)
            {
                _lastOpenViewerRequest = false;
                return;
            }

            if (_lastOpenViewerRequest)
            {
                return;
            }

            _lastOpenViewerRequest = true;
            Google3dTilesViewerHost.OpenInBrowser(InstanceGuid, apiKey, boundingBox4326, status);
        }

        private void ResetOpenViewerRequest(bool openViewer)
        {
            if (!openViewer)
            {
                _lastOpenViewerRequest = false;
            }
        }

        private static bool TryResolveWgs84BoundingBox(SpatialContext2D spatialContext, out BoundingBox2D boundingBox4326)
        {
            if (spatialContext.BoundingBoxesBySrs.TryGetValue("EPSG:4326", out var explicitBoundingBox))
            {
                boundingBox4326 = explicitBoundingBox;
                return true;
            }

            if (spatialContext.Wgs84BoundingBox is not null)
            {
                boundingBox4326 = spatialContext.Wgs84BoundingBox;
                return true;
            }

            boundingBox4326 = new BoundingBox2D(0.0, 0.0, 0.0, 0.0);
            return false;
        }

        private Google3dTilesReferenceSession CreateViewportSession(
            SpatialContext2D spatialContext,
            string apiKey,
            BoundingBox2D boundingBox4326)
        {
            var frame = RhinoSpatialContextTools.CreateBoundingBoxFrame(
                spatialContext.PlacementBoundingBox,
                spatialContext.PlacementOrigin,
                spatialContext.UseAbsoluteCoordinates);

            return new Google3dTilesReferenceSession
            {
                OwnerId = InstanceGuid,
                ApiKey = apiKey,
                SpatialContext = spatialContext,
                BoundingBox4326 = boundingBox4326,
                Status = "Google 3D Tiles runtime is starting in the background. Rhino will draw the transient viewer mesh directly in the viewport.",
                AreaFrame = frame
            };
        }

        internal void ApplyLivePreviewState(Google3dTilesReferenceSession session)
        {
            _previewPrimitives.Clear();
            _previewFallbackMeshes.Clear();
            _previewFrame = session.AreaFrame?.DuplicateCurve();
            _previewBox = BoundingBox.Empty;

            if (_previewFrame is not null)
            {
                _previewBox.Union(_previewFrame.GetBoundingBox(accurate: false));
            }

                foreach (var primitive in session.DecodedPrimitives)
                {
                    _previewPrimitives.Add(new Google3dTilesReferenceSession.DisplayPrimitive
                    {
                        Mesh = primitive.Mesh,
                        Material = primitive.Material ?? session.TileMaterial,
                        SourceUrl = primitive.SourceUrl
                    });
                    _previewBox.Union(primitive.Mesh.GetBoundingBox(accurate: false));
                }

            foreach (var mesh in session.RuntimeMeshes)
            {
                _previewFallbackMeshes.Add(mesh);
                _previewBox.Union(mesh.GetBoundingBox(accurate: false));
            }
        }

        internal void ClearLivePreviewState()
        {
            _previewPrimitives.Clear();
            _previewFallbackMeshes.Clear();
            _previewFrame = null;
            _previewBox = BoundingBox.Empty;
        }
    }
}
