using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public class WfsListLayersComponent : GH_TaskCapableComponent<WfsListLayersComponent.SolveResults>
    {
        private readonly WfsClient _wfsClient = new();

        public class SolveResults
        {
            public List<WfsLayerInfo> Layers { get; init; } = new();

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private class RequestData
        {
            public string BaseUrl { get; init; } = string.Empty;
        }

        public WfsListLayersComponent()
            : base("List WFS Layers", "WFS Layers",
                "List WFS layers, OGC API Features collections, or local GeoPackage feature layers.",
                "RhinoSpatial", "Layers")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("WFS Service URL", "WFS URL", "WFS/OGC API service URL or local GeoPackage path.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Layer", "Layer", "Combined layer entries in the format name | title. Use List Item to choose one, or merge explicit selections if you want to load several layers.", GH_ParamAccess.list);
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
                    var token = CancelToken;
                    Task<SolveResults> task = Task.Run(() => ComputeSafe(requestData, token), token);
                    TaskList.Add(task);
                    return;
                }

                if (CancelToken.IsCancellationRequested) return;
                if (!GetSolveResults(dataAccess, out SolveResults result))
                {
                    result = ComputeSafe(requestData, CancelToken);
                }

                if (CancelToken.IsCancellationRequested) return;
                if (!string.IsNullOrWhiteSpace(result.Status) && result.MessageLevel.HasValue)
                {
                    AddRuntimeMessage(result.MessageLevel.Value, result.Status);
                }

                var layers = result.Layers;
                var labels = new List<string>(layers.Count);

                foreach (var layer in layers)
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

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.ListWfsLayers.png");

        public override Guid ComponentGuid => new Guid("863AE12B-8053-4696-9069-F5368CC22979");

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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "WFS Service URL is required.");
                return false;
            }

            requestData = new RequestData
            {
                BaseUrl = baseUrl
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData, CancellationToken token)
        {
            if (VectorSourceResolver.Resolve(requestData.BaseUrl).Kind == VectorSourceKind.LocalGeoPackage)
            {
                var localLayers = GeoPackageReader.ReadLayers(requestData.BaseUrl, token);
                return new SolveResults
                {
                    Layers = localLayers.Select(l => new WfsLayerInfo(l.Name, l.Title, l.SourceSrs, new List<string>(),
                    l.Bounds is not null && ReferenceSourceMetadataReader.TryTransformBoundsToWgs84(l.SourceSrs, l.Bounds, out var wgs) ? wgs : null)).ToList()
                };
            }
            var collections = new OgcApiFeaturesClient().TryDiscoverAsync(requestData.BaseUrl, token).GetAwaiter().GetResult();
            var layers = collections is null ? _wfsClient.LoadLayersAsync(requestData.BaseUrl, token).GetAwaiter().GetResult()
                : System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(collections, c => new WfsLayerInfo(c.Id, c.Title, "EPSG:4326", new List<string>(), c.Wgs84Bounds)));

            return new SolveResults
            {
                Layers = layers
            };
        }

        private SolveResults ComputeSafe(RequestData requestData, CancellationToken token)
        {
            try
            {
                return SourceSolveDeadline.Run(token, ct => Compute(requestData, ct));
            }
            catch (Exception ex)
            {
                return new SolveResults
                {
                    Layers = new List<WfsLayerInfo>(),
                    Status = ex.Message,
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }
        }
    }
}
