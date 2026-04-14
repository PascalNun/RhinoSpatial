using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public class WmsListLayersComponent : GH_TaskCapableComponent<WmsListLayersComponent.SolveResults>
    {
        private readonly WmsClient _wmsClient = new();

        public class SolveResults
        {
            public List<WmsLayerInfo> Layers { get; init; } = new();

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private class RequestData
        {
            public string BaseUrl { get; init; } = string.Empty;
        }

        public WmsListLayersComponent()
            : base("List WMS Layers", "WMS Layers",
                "List available layers from a WMS GetCapabilities response.",
                "RhinoSpatial", "Layers")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("WMS Service URL", "WMS URL", "Base URL of the WMS service.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Layer", "Layer", "Combined layer entries in the format name | title. Use List Item to choose one.", GH_ParamAccess.list);
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

                var labels = new List<string>(result.Layers.Count);
                foreach (var layer in result.Layers)
                {
                    labels.Add($"{layer.Name} | {layer.Title}");
                }

                dataAccess.SetDataList(0, labels);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                dataAccess.SetDataList(0, Array.Empty<string>());
            }
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.ListWmsLayers.png");

        public override Guid ComponentGuid => new Guid("9c6037d7-18fc-49d9-95f5-4d32972c4c25");

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? baseUrl = null;

            if (!dataAccess.GetData(0, ref baseUrl))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "WMS Service URL is required.");
                return false;
            }

            requestData = new RequestData
            {
                BaseUrl = baseUrl
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            var layers = _wmsClient.LoadLayersAsync(requestData.BaseUrl).GetAwaiter().GetResult();

            return new SolveResults
            {
                Layers = layers
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
                    Layers = new List<WmsLayerInfo>(),
                    Status = ex.Message,
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }
        }
    }
}
