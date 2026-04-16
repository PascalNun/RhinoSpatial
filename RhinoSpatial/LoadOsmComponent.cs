using System;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public class LoadOsmComponent : GH_TaskCapableComponent<LoadOsmComponent.SolveResults>
    {
        private const string DefaultOverpassUrl = "https://overpass-api.de/api/interpreter";
        private readonly OsmClient _osmClient = new();

        public class SolveResults
        {
            public GH_Structure<GH_Brep> Buildings { get; init; } = new();

            public GH_Structure<IGH_GeometricGoo> Roads { get; init; } = new();

            public GH_Structure<IGH_GeometricGoo> Water { get; init; } = new();

            public GH_Structure<IGH_GeometricGoo> Green { get; init; } = new();

            public GH_Structure<IGH_GeometricGoo> Rail { get; init; } = new();

            public string Status { get; init; } = string.Empty;

            public GH_RuntimeMessageLevel? MessageLevel { get; init; }
        }

        private sealed class RequestData
        {
            public string BaseUrl { get; init; } = string.Empty;

            public SpatialContext2D SpatialContext { get; init; } = null!;

            public bool IncludeBuildings { get; init; }

            public bool IncludeRoads { get; init; }

            public bool IncludeWater { get; init; }

            public bool IncludeGreen { get; init; }

            public bool IncludeRail { get; init; }
        }

        public LoadOsmComponent()
            : base("Load OSM", "Load OSM",
                "Load curated, study-oriented OSM context aligned to the shared RhinoSpatial spatial context.",
                "RhinoSpatial", "Sources")
        {
            NormalizeInputConfiguration();
        }

        public override GH_Exposure Exposure => GH_Exposure.last;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("OSM Service URL", "OSM URL", "Legacy/advanced override for the Overpass API endpoint. Leave empty to use the built-in RhinoSpatial OSM source.", GH_ParamAccess.item);
            pManager.AddTextParameter("Spatial Context", "Spatial Context", "Shared RhinoSpatial spatial context.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Buildings", "Buildings", "Include OSM building massing geometry.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Road", "Road", "Include major street and road study surfaces.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Water", "Water", "Include water area and waterway context.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Green", "Green", "Include merged green and landscape context.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Rail", "Rail", "Include rail corridor context.", GH_ParamAccess.item, false);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            NormalizeInputConfiguration();
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            var result = base.Read(reader);
            NormalizeInputConfiguration();
            return result;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Buildings", "Buildings", "Study-oriented OSM building geometry.", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("Road", "Road", "Curated OSM road context.", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("Water", "Water", "Curated OSM water context.", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("Green", "Green", "Merged OSM green/open landscape context.", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("Rail", "Rail", "Curated OSM rail context.", GH_ParamAccess.tree);
            pManager.AddTextParameter("Status", "Status", "Status information from the OSM loader.", GH_ParamAccess.item);
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

                dataAccess.SetDataTree(0, result.Buildings);
                dataAccess.SetDataTree(1, result.Roads);
                dataAccess.SetDataTree(2, result.Water);
                dataAccess.SetDataTree(3, result.Green);
                dataAccess.SetDataTree(4, result.Rail);
                dataAccess.SetData(5, result.Status);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        protected override System.Drawing.Bitmap? Icon => IconLoader.Load("RhinoSpatial.Resources.LoadOSM.png");

        public override Guid ComponentGuid => new Guid("e46bdb4f-3dd1-4cc8-bb32-83076f2a30b3");

        private bool TryGetRequestData(IGH_DataAccess dataAccess, out RequestData requestData)
        {
            requestData = new RequestData();

            string? baseUrl = null;
            string? spatialContextText = null;
            var buildingsInput = true;
            var roadsInput = true;
            var waterInput = false;
            var greenInput = false;
            var railInput = false;

            dataAccess.GetData(0, ref baseUrl);
            dataAccess.GetData(1, ref spatialContextText);
            dataAccess.GetData(2, ref buildingsInput);
            dataAccess.GetData(3, ref roadsInput);
            dataAccess.GetData(4, ref waterInput);
            dataAccess.GetData(5, ref greenInput);
            dataAccess.GetData(6, ref railInput);

            var includeBuildings = buildingsInput;
            var includeRoads = roadsInput;
            var includeWater = waterInput;
            var includeGreen = greenInput;
            var includeRail = railInput;

            var recoveredLegacyContextOrder = false;
            SpatialContext2D spatialContext;
            string spatialContextError;

            if (RhinoSpatialInputParser.TryGetRequiredSpatialContext(spatialContextText, out spatialContext, out spatialContextError))
            {
                // Current layout, nothing to do.
            }
            else if (TryGetLegacySpatialContext(baseUrl, out var legacySpatialContext))
            {
                spatialContext = legacySpatialContext;
                recoveredLegacyContextOrder = true;

                if (TryParseBooleanText(spatialContextText, out var legacyBuildings))
                {
                    includeBuildings = legacyBuildings;
                    includeRoads = buildingsInput;
                    includeWater = roadsInput;
                    includeGreen = waterInput;
                    includeRail = greenInput;
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, spatialContextError);
                return false;
            }

            if (!includeBuildings && !includeRoads && !includeWater && !includeGreen && !includeRail)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Enable at least one OSM context group to load study geometry.");
                return false;
            }

            if (recoveredLegacyContextOrder)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Remark,
                    "Recovered Load OSM from an older input layout. Replacing the component once will remove this compatibility fallback.");

                baseUrl = null;
            }

            requestData = new RequestData
            {
                BaseUrl = ResolveBaseUrl(baseUrl),
                SpatialContext = spatialContext,
                IncludeBuildings = includeBuildings,
                IncludeRoads = includeRoads,
                IncludeWater = includeWater,
                IncludeGreen = includeGreen,
                IncludeRail = includeRail
            };

            return true;
        }

        private SolveResults Compute(RequestData requestData)
        {
            if (!RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(
                    requestData.SpatialContext,
                    "EPSG:4326",
                    out var queryBoundingBox,
                    out _))
            {
                return new SolveResults
                {
                    Status = "The Spatial Context could not provide a usable EPSG:4326 bounding box for the OSM query.",
                    MessageLevel = GH_RuntimeMessageLevel.Error
                };
            }

            var dataSet = _osmClient.LoadDataAsync(new OsmRequestOptions
            {
                BaseUrl = requestData.BaseUrl,
                BoundingBox4326 = queryBoundingBox,
                IncludeBuildings = requestData.IncludeBuildings,
                IncludeRoads = requestData.IncludeRoads,
                IncludeWater = requestData.IncludeWater,
                IncludeGreen = requestData.IncludeGreen,
                IncludeRail = requestData.IncludeRail
            }).GetAwaiter().GetResult();

            var buildingTree = requestData.IncludeBuildings
                ? RhinoSpatialOsmOutputBuilder.BuildBuildingTree(dataSet.Buildings, requestData.SpatialContext)
                : new GH_Structure<GH_Brep>();
            var roadTree = requestData.IncludeRoads
                ? RhinoSpatialOsmOutputBuilder.BuildRoadTree(dataSet.Roads, requestData.SpatialContext)
                : new GH_Structure<IGH_GeometricGoo>();
            var waterTree = requestData.IncludeWater
                ? RhinoSpatialOsmOutputBuilder.BuildWaterTree(dataSet.WaterAreas, requestData.SpatialContext)
                : new GH_Structure<IGH_GeometricGoo>();
            var greenTree = requestData.IncludeGreen
                ? RhinoSpatialOsmOutputBuilder.BuildGreenTree(dataSet.GreenAreas, requestData.SpatialContext)
                : new GH_Structure<IGH_GeometricGoo>();
            var railTree = requestData.IncludeRail
                ? RhinoSpatialOsmOutputBuilder.BuildRailTree(dataSet.Rails, requestData.SpatialContext)
                : new GH_Structure<IGH_GeometricGoo>();

            var totalFeatureCount =
                dataSet.Buildings.Count +
                dataSet.Roads.Count +
                dataSet.WaterAreas.Count +
                dataSet.GreenAreas.Count +
                dataSet.Rails.Count;

            return new SolveResults
            {
                Buildings = buildingTree,
                Roads = roadTree,
                Water = waterTree,
                Green = greenTree,
                Rail = railTree,
                Status = totalFeatureCount == 0
                    ? "No OSM context was returned inside the current Spatial Context."
                    : BuildStatusMessage(requestData, dataSet),
                MessageLevel = ResolveMessageLevel(totalFeatureCount, dataSet)
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

        private static string BuildStatusMessage(RequestData requestData, OsmDataSet dataSet)
        {
            var summaryParts = new System.Collections.Generic.List<string>();

            if (requestData.IncludeBuildings)
            {
                summaryParts.Add($"{dataSet.Buildings.Count} building feature(s)");
            }

            if (requestData.IncludeRoads)
            {
                summaryParts.Add($"{dataSet.Roads.Count} road feature(s)");
            }

            if (requestData.IncludeWater)
            {
                summaryParts.Add($"{dataSet.WaterAreas.Count} water feature(s)");
            }

            if (requestData.IncludeGreen)
            {
                summaryParts.Add($"{dataSet.GreenAreas.Count} green feature(s)");
            }

            if (requestData.IncludeRail)
            {
                summaryParts.Add($"{dataSet.Rails.Count} rail feature(s)");
            }

            var summary = summaryParts.Count == 0
                ? "No OSM context groups were enabled."
                : $"Loaded {string.Join(", ", summaryParts)}.";

            return string.IsNullOrWhiteSpace(dataSet.StatusNote)
                ? summary
                : $"{summary} {dataSet.StatusNote}";
        }

        private static GH_RuntimeMessageLevel? ResolveMessageLevel(int totalFeatureCount, OsmDataSet dataSet)
        {
            if (totalFeatureCount == 0)
            {
                return GH_RuntimeMessageLevel.Warning;
            }

            if (!string.IsNullOrWhiteSpace(dataSet.StatusNote) ||
                dataSet.UnavailableCategories.Count > 0 ||
                dataSet.CachedCategories.Count > 0)
            {
                return GH_RuntimeMessageLevel.Remark;
            }

            return null;
        }

        private void NormalizeInputConfiguration()
        {
            if (Params.Input.Count < 7)
            {
                return;
            }

            Params.Input[0].Optional = true;
        }

        private static bool TryGetLegacySpatialContext(string? text, out SpatialContext2D spatialContext)
        {
            spatialContext = null!;

            if (!RhinoSpatialInputParser.TryParseSpatialContext(text, out var parsedSpatialContext, out _) || parsedSpatialContext is null)
            {
                return false;
            }

            spatialContext = parsedSpatialContext;
            return true;
        }

        private static bool TryParseBooleanText(string? text, out bool value)
        {
            value = false;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return bool.TryParse(text.Trim(), out value);
        }

        private static string ResolveBaseUrl(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return DefaultOverpassUrl;
            }

            var trimmedBaseUrl = baseUrl.Trim();
            if (!Uri.TryCreate(trimmedBaseUrl, UriKind.Absolute, out var uri))
            {
                return DefaultOverpassUrl;
            }

            var scheme = uri.Scheme;
            if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return DefaultOverpassUrl;
            }

            return trimmedBaseUrl;
        }
    }
}
