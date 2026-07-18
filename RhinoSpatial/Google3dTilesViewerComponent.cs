using System;
using System.Collections.Generic;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;
using Rhino;
using RhinoSpatial.Core;
using System.Threading;
using System.Threading.Tasks;

namespace RhinoSpatial
{
    public class Google3dTilesViewerComponent : GH_TaskCapableComponent<Google3dTilesViewerComponent.SolveResults>
    {
        private const string DisabledStatus = "Google 3D Tiles viewer is disabled. Connect a Boolean Toggle set to True to Enable when you want RhinoSpatial to request bounded reference content.";
        private const string PolicyStatus = "Google 3D Tiles viewer is enabled. RhinoSpatial requests bounded reference tile content directly for contextual viewing and outputs temporary preview meshes with aligned materials.";
        private static readonly TimeSpan DirectLoadTimeout = TimeSpan.FromSeconds(45);

        private readonly List<Google3dTilesDisplayPrimitive> _previewPrimitives = new();
        private Curve? _previewFrame;
        private BoundingBox _previewBox = BoundingBox.Empty;

        public class SolveResults
        {
            public List<Google3dTilesDisplayPrimitive> Primitives { get; init; } = new();

            public Curve? AreaFrame { get; init; }

            public string Status { get; init; } = string.Empty;

            public bool Active { get; init; }

            public string Attribution { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private sealed class RequestData
        {
            public string ApiKey { get; init; } = string.Empty;

            public SpatialContext2D SpatialContext { get; init; } = null!;

            public BoundingBox2D BoundingBox4326 { get; init; } = null!;

            public Curve AreaFrame { get; init; } = null!;

            public bool EnableViewer { get; init; }
        }

        public Google3dTilesViewerComponent()
            : base(
                "3D Tiles Viewer (Google)",
                "3D Tiles View",
                "View bounded Google Photorealistic 3D Tiles directly as contextual preview meshes aligned to the selected Spatial Context.",
                "RhinoSpatial",
                "Viewers")
        {
            NormalizeComponentLayout();
        }

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        public override bool IsPreviewCapable => true;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("API Key", "API Key", "User-managed Google Maps API key for the Map Tiles API. RhinoSpatial uses it only for direct tile requests from this component.", GH_ParamAccess.item);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context from the Spatial Context component.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Enable Viewer", "Enable", "Request and decode Google 3D Tiles preview meshes for the selected Spatial Context.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "Status text for the Google 3D Tiles viewer.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Viewer Active", "Active", "True when the Google 3D Tiles viewer is enabled.", GH_ParamAccess.item);
            pManager.AddMeshParameter("Meshes", "Meshes", "Decoded Google 3D Tiles preview meshes for contextual viewing.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Materials", "Materials", "Materials for the decoded Google 3D Tiles preview meshes, aligned by list index.", GH_ParamAccess.list);
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
            NormalizeComponentLayout();

            if (!TryGetRequestData(dataAccess, out var requestData))
            {
                ClearLivePreviewState();
                return;
            }

            if (!requestData.EnableViewer)
            {
                ClearLivePreviewState();
                dataAccess.SetData(0, DisabledStatus);
                dataAccess.SetData(1, false);
                return;
            }

            if (InPreSolve)
            {
                Task<SolveResults> task = Task.Run(() => ComputeSafe(requestData, CancelToken), CancelToken);
                TaskList.Add(task);
                return;
            }

            if (!GetSolveResults(dataAccess, out SolveResults result))
            {
                result = ComputeSafe(requestData, CancellationToken.None);
            }

            ApplySolveResults(result);
            dataAccess.SetData(0, result.Status);
            dataAccess.SetData(1, result.Active);
            dataAccess.SetDataList(2, result.Primitives.Select(static primitive => primitive.Mesh));
            dataAccess.SetDataList(3, BuildOutputMaterials(result.Primitives));

            if (!string.IsNullOrWhiteSpace(result.Status) && result.MessageLevel.HasValue)
            {
                AddRuntimeMessage(result.MessageLevel.Value, result.Status);
            }
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.View3DTiles.png");

        public override Guid ComponentGuid => new Guid("64c2d9f6-03ab-4e33-9eb7-445ff4b8f3a1");

        public override void RemovedFromDocument(GH_Document document)
        {
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

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? apiKey = null;
            string? spatialContextText = null;
            var enableViewer = false;

            dataAccess.GetData(0, ref apiKey);
            dataAccess.GetData(1, ref spatialContextText);
            dataAccess.GetData(2, ref enableViewer);

            dataAccess.SetData(1, false);

            if (!RhinoSpatialInputParser.TryGetRequiredSpatialContext(spatialContextText, out var spatialContext, out var errorMessage))
            {
                dataAccess.SetData(0, errorMessage);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, errorMessage);
                return false;
            }

            var normalizedApiKey = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : apiKey.Trim();

            if (string.IsNullOrWhiteSpace(normalizedApiKey))
            {
                const string missingKeyMessage = "API Key is required. Google 3D Tiles viewer access is user-managed. RhinoSpatial does not ship or store a shared Google Maps API key.";
                dataAccess.SetData(0, missingKeyMessage);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, missingKeyMessage);
                return false;
            }

            if (!TryResolveWgs84BoundingBox(spatialContext, out var boundingBox4326))
            {
                const string missingBoundingBoxMessage = "The Spatial Context could not provide a usable EPSG:4326 bounding box for the 3D Tiles viewer.";
                dataAccess.SetData(0, missingBoundingBoxMessage);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, missingBoundingBoxMessage);
                return false;
            }

            requestData = new RequestData
            {
                ApiKey = normalizedApiKey,
                SpatialContext = spatialContext,
                BoundingBox4326 = boundingBox4326,
                AreaFrame = CreateAreaFrame(spatialContext),
                EnableViewer = enableViewer
            };
            return true;
        }

        private static SolveResults Compute(RequestData requestData, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DirectLoadTimeout);

            var loadResult = Google3dTilesDirectLoader
                .LoadAsync(
                    requestData.ApiKey,
                    requestData.BoundingBox4326,
                    requestData.SpatialContext,
                    null,
                    timeout.Token)
                .GetAwaiter()
                .GetResult();

            return new SolveResults
            {
                Primitives = loadResult.Primitives,
                AreaFrame = requestData.AreaFrame.DuplicateCurve(),
                Status = BuildStatusWithBounds(
                    string.IsNullOrWhiteSpace(loadResult.Status) ? PolicyStatus : loadResult.Status,
                    loadResult.Primitives),
                Active = true,
                Attribution = loadResult.Attribution,
                MessageLevel = loadResult.Primitives.Count == 0 ? GH_RuntimeMessageLevel.Warning : null
            };
        }

        private static SolveResults ComputeSafe(RequestData requestData, CancellationToken cancellationToken)
        {
            try
            {
                return Compute(requestData, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new SolveResults
                {
                    AreaFrame = requestData.AreaFrame.DuplicateCurve(),
                    Status = "Google 3D Tiles viewer timed out after 45 seconds. Try a smaller Spatial Context or check the Google Maps API key and network access.",
                    Active = true,
                    MessageLevel = GH_RuntimeMessageLevel.Warning
                };
            }
            catch (Exception exception)
            {
                return new SolveResults
                {
                    AreaFrame = requestData.AreaFrame.DuplicateCurve(),
                    Status = $"Google 3D Tiles viewer failed: {exception.Message}",
                    Active = true,
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }
        }

        private void ApplySolveResults(SolveResults result)
        {
            _previewPrimitives.Clear();
            _previewFrame = result.AreaFrame?.DuplicateCurve();
            _previewBox = BoundingBox.Empty;
            Message = FormatAttributionMessage(result.Attribution);

            if (_previewFrame is not null)
            {
                _previewBox.Union(_previewFrame.GetBoundingBox(accurate: false));
            }

            foreach (var primitive in result.Primitives)
            {
                _previewPrimitives.Add(new Google3dTilesDisplayPrimitive
                {
                    Mesh = primitive.Mesh,
                    Material = primitive.Material,
                    SourceUrl = primitive.SourceUrl,
                    SourceKey = primitive.SourceKey
                });
                _previewBox.Union(primitive.Mesh.GetBoundingBox(accurate: false));
            }
        }

        private void NormalizeComponentLayout()
        {
            var changed = false;
            for (var index = Params.Input.Count - 1; index >= 0; index--)
            {
                var input = Params.Input[index];
                if (input.Name.Equals("Open Viewer Window", StringComparison.OrdinalIgnoreCase) ||
                    input.NickName.Equals("Open", StringComparison.OrdinalIgnoreCase))
                {
                    Params.UnregisterInputParameter(input, true);
                    changed = true;
                }
            }

            if (Params.Input.Count > 2)
            {
                changed |= SetParameterMetadata(
                    Params.Input[2],
                    "Enable Viewer",
                    "Enable",
                    "Request and decode Google 3D Tiles preview meshes for the selected Spatial Context.");
            }

            for (var index = Params.Output.Count - 1; index >= 0; index--)
            {
                var output = Params.Output[index];
                if (output.Name.Equals("Viewer URL", StringComparison.OrdinalIgnoreCase) ||
                    output.NickName.Equals("Viewer URL", StringComparison.OrdinalIgnoreCase))
                {
                    Params.UnregisterOutputParameter(output, true);
                    changed = true;
                }
            }

            if (Params.Output.Count > 0)
            {
                changed |= SetParameterMetadata(
                    Params.Output[0],
                    "Status",
                    "Status",
                    "Status text for the Google 3D Tiles viewer.");
            }

            if (Params.Output.Count > 1)
            {
                changed |= SetParameterMetadata(
                    Params.Output[1],
                    "Viewer Active",
                    "Active",
                    "True when the Google 3D Tiles viewer is enabled.");
            }

            if (Params.Output.Count < 3)
            {
                Params.RegisterOutputParam(new Grasshopper.Kernel.Parameters.Param_Mesh
                {
                    Name = "Meshes",
                    NickName = "Meshes",
                    Description = "Decoded Google 3D Tiles preview meshes for contextual viewing.",
                    Access = GH_ParamAccess.list
                });
                changed = true;
            }

            if (Params.Output.Count < 4)
            {
                Params.RegisterOutputParam(new Grasshopper.Kernel.Parameters.Param_GenericObject
                {
                    Name = "Materials",
                    NickName = "Materials",
                    Description = "Materials for the decoded Google 3D Tiles preview meshes, aligned by list index.",
                    Access = GH_ParamAccess.list
                });
                changed = true;
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

        private static List<object> BuildOutputMaterials(IEnumerable<Google3dTilesDisplayPrimitive> primitives)
        {
            var materials = new List<object>();
            foreach (var primitive in primitives)
            {
                if (!string.IsNullOrWhiteSpace(primitive.TextureFilePath))
                {
                    var grasshopperMaterial = RhinoSpatialRasterDisplayTools.CreateGrasshopperMaterial(primitive.TextureFilePath);
                    if (grasshopperMaterial is not null)
                    {
                        materials.Add(grasshopperMaterial);
                        continue;
                    }
                }

                if (primitive.Material is not null)
                {
                    materials.Add(new GH_Material(primitive.Material));
                }
            }

            return materials;
        }

        private static string BuildStatusWithBounds(
            string status,
            IReadOnlyCollection<Google3dTilesDisplayPrimitive> primitives)
        {
            if (primitives.Count == 0)
            {
                return status;
            }

            var bounds = BoundingBox.Empty;
            foreach (var primitive in primitives)
            {
                bounds.Union(primitive.Mesh.GetBoundingBox(accurate: false));
            }

            if (!bounds.IsValid)
            {
                return status;
            }

            var diagonal = bounds.Diagonal.Length;
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{status} Local mesh bounds: X {bounds.Min.X:0.##}..{bounds.Max.X:0.##}, Y {bounds.Min.Y:0.##}..{bounds.Max.Y:0.##}, Z {bounds.Min.Z:0.##}..{bounds.Max.Z:0.##}, diagonal {diagonal:0.##}.");
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

        private static Curve CreateAreaFrame(SpatialContext2D spatialContext)
        {
            return RhinoSpatialContextTools.CreateBoundingBoxFrame(
                spatialContext.PlacementBoundingBox,
                spatialContext.PlacementOrigin,
                spatialContext.UseAbsoluteCoordinates);
        }

        internal void ClearLivePreviewState()
        {
            _previewPrimitives.Clear();
            _previewFrame = null;
            _previewBox = BoundingBox.Empty;
            Message = string.Empty;
        }

        private static string FormatAttributionMessage(string attribution)
        {
            if (string.IsNullOrWhiteSpace(attribution))
            {
                return string.Empty;
            }

            const int maximumLength = 90;
            var normalized = attribution.Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized[..maximumLength] + "...";
        }
    }
}
