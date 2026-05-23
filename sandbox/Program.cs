using System;
using System.Threading.Tasks;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "probe", StringComparison.OrdinalIgnoreCase))
            {
                await RunProbeAsync(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "lod2file", StringComparison.OrdinalIgnoreCase))
            {
                RunLod2File(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "geotiff", StringComparison.OrdinalIgnoreCase))
            {
                RunGeoTiff(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "terrainfile", StringComparison.OrdinalIgnoreCase))
            {
                RunTerrainFile(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "shapefile", StringComparison.OrdinalIgnoreCase))
            {
                RunShapefile(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "geojson", StringComparison.OrdinalIgnoreCase))
            {
                RunGeoJson(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "ogcapi", StringComparison.OrdinalIgnoreCase))
            {
                await RunOgcApiFeaturesAsync(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "lod2", StringComparison.OrdinalIgnoreCase))
            {
                await RunLod2Async(args);
                return;
            }

            var requestOptions = CreateRequestOptions(args);

            var client = new WfsClient();

            try
            {
                Console.WriteLine("Loading WFS features...");
                Console.WriteLine($"SRS: {requestOptions.SrsName}");
                Console.WriteLine($"Selected area active: {requestOptions.BoundingBox is not null}");
                Console.WriteLine(WfsClient.BuildGetFeatureRequestUrl(requestOptions));
                Console.WriteLine();

                var features = await client.LoadFeaturesAsync(requestOptions);

                Console.WriteLine("Parsed features:");
                Console.WriteLine();

                for (int i = 0; i < features.Count; i++)
                {
                    var feature = features[i];
                    var firstOuterRing = GetFirstOuterRing(feature);

                    Console.WriteLine($"Feature {i + 1}");
                    Console.WriteLine($"Feature id: {feature.Id}");
                    Console.WriteLine($"Title: {GetAttributeValue(feature, "titel")}");
                    Console.WriteLine($"Project number: {GetAttributeValue(feature, "projekt_nr")}");
                    Console.WriteLine($"Status: {GetAttributeValue(feature, "status")}");
                    Console.WriteLine($"Geometry type: {feature.Geometry.Type}");
                    Console.WriteLine($"Outer ring count: {feature.Geometry.OuterRings.Count}");
                    Console.WriteLine($"First outer ring point count: {firstOuterRing?.Points.Count ?? 0}");
                    Console.WriteLine($"First ring is closed: {firstOuterRing is not null && GeometryUtilities.IsClosedRing(firstOuterRing.Points)}");
                    Console.WriteLine($"Attribute count: {feature.Attributes.Count}");
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while loading or parsing the response:");
                Console.WriteLine(ex.Message);
            }
        }

        private static async Task RunProbeAsync(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: probe <wfs|wms|wcs> <GetCapabilities URL>");
                return;
            }

            var kind = args[1].Trim().ToLowerInvariant();
            var url = args[2];

            switch (kind)
            {
                case "wfs":
                    await ProbeSafeAsync("WFS", url, () => ProbeWfsAsync(url));
                    break;
                case "wms":
                    await ProbeSafeAsync("WMS", url, () => ProbeWmsAsync(url));
                    break;
                case "wcs":
                    await ProbeSafeAsync("WCS", url, () => ProbeWcsAsync(url));
                    break;
                default:
                    Console.WriteLine("Unknown probe type. Use one of: wfs, wms, wcs.");
                    break;
            }
        }

        private static async Task ProbeSafeAsync(string kind, string url, Func<Task> probe)
        {
            try
            {
                await probe();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{kind} capabilities probe failed");
                Console.WriteLine($"URL: {url}");
                Console.WriteLine(ex.Message);
            }
        }

        private static async Task ProbeWfsAsync(string url)
        {
            var startedAt = DateTime.UtcNow;
            var capabilities = await new WfsClient().LoadCapabilitiesAsync(url);
            Console.WriteLine("WFS capabilities probe");
            Console.WriteLine($"URL: {url}");
            Console.WriteLine($"Version: {capabilities.ServiceVersion}");
            Console.WriteLine($"Layer count: {capabilities.Layers.Count}");
            Console.WriteLine($"GetFeature URL: {capabilities.GetFeatureUrl}");
            PrintLayers(capabilities.Layers.Take(12));
            Console.WriteLine($"Elapsed: {(DateTime.UtcNow - startedAt).TotalSeconds:0.###} s");
        }

        private static async Task ProbeWmsAsync(string url)
        {
            var startedAt = DateTime.UtcNow;
            var capabilities = await new WmsClient().LoadCapabilitiesAsync(url);
            Console.WriteLine("WMS capabilities probe");
            Console.WriteLine($"URL: {url}");
            Console.WriteLine($"Version: {capabilities.ServiceVersion}");
            Console.WriteLine($"Layer count: {capabilities.Layers.Count}");
            Console.WriteLine($"GetMap URL: {capabilities.GetMapUrl}");
            Console.WriteLine($"Max size: {capabilities.MaxWidth?.ToString() ?? "?"} x {capabilities.MaxHeight?.ToString() ?? "?"}");
            PrintLayers(capabilities.Layers.Take(12));
            Console.WriteLine($"Elapsed: {(DateTime.UtcNow - startedAt).TotalSeconds:0.###} s");
        }

        private static async Task ProbeWcsAsync(string url)
        {
            var startedAt = DateTime.UtcNow;
            var capabilities = await new WcsClient().LoadCapabilitiesAsync(url);
            Console.WriteLine("WCS capabilities probe");
            Console.WriteLine($"URL: {url}");
            Console.WriteLine($"Version: {capabilities.ServiceVersion}");
            Console.WriteLine($"Coverage count: {capabilities.Coverages.Count}");
            Console.WriteLine($"GetCoverage URL: {capabilities.GetCoverageUrl}");
            Console.WriteLine($"DescribeCoverage URL: {capabilities.DescribeCoverageUrl}");
            foreach (var coverage in capabilities.Coverages.Take(12))
            {
                Console.WriteLine($"- {coverage.CoverageId} | {coverage.Title} | bbox {FormatBounds(coverage.Wgs84BoundingBox)}");
            }
            Console.WriteLine($"Elapsed: {(DateTime.UtcNow - startedAt).TotalSeconds:0.###} s");
        }

        private static void PrintLayers(IEnumerable<WfsLayerInfo> layers)
        {
            foreach (var layer in layers)
            {
                Console.WriteLine($"- {layer.Name} | {layer.Title} | default {layer.DefaultSrs} | bbox {FormatBounds(layer.Wgs84BoundingBox)}");
            }
        }

        private static void PrintLayers(IEnumerable<WmsLayerInfo> layers)
        {
            foreach (var layer in layers)
            {
                var srsPreview = layer.SupportedSrs.Count == 0
                    ? "?"
                    : string.Join(",", layer.SupportedSrs.Take(4));
                Console.WriteLine($"- {layer.Name} | {layer.Title} | srs {srsPreview} | bbox {FormatBounds(layer.Wgs84BoundingBox)}");
            }
        }

        private static string FormatBounds(BoundingBox2D? bounds)
        {
            return bounds is null
                ? "?"
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.####},{1:0.####},{2:0.####},{3:0.####}",
                    bounds.MinX,
                    bounds.MinY,
                    bounds.MaxX,
                    bounds.MaxY);
        }

        private static void RunLod2File(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: lod2file <path> [minX,minY,maxX,maxY]");
                return;
            }

            var filePath = args[1];
            var filterBounds = args.Length > 2 && TryParseBoundingBox(args[2], out var parsedBounds)
                ? parsedBounds
                : null;
            var metadataStartedAt = DateTime.UtcNow;
            var text = System.IO.File.ReadAllText(filePath);
            var isCityJson = filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                             filePath.EndsWith(".cityjson", StringComparison.OrdinalIgnoreCase);
            var metadata = isCityJson
                ? ConvertCityJsonMetadata(CityJsonReader.ReadSourceMetadata(text))
                : ReadCityGmlSourceMetadata(filePath);
            var metadataElapsed = DateTime.UtcNow - metadataStartedAt;

            var startedAt = DateTime.UtcNow;
            var buildings = isCityJson
                ? CityJsonReader.ReadBuildings(text, System.IO.Path.GetFileNameWithoutExtension(filePath), filterBounds)
                : Lod2GmlReader.ReadBuildings(text, System.IO.Path.GetFileNameWithoutExtension(filePath), filterBounds);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine($"File: {filePath}");
            Console.WriteLine($"Source format: {(isCityJson ? "CityJSON" : "CityGML/GML")}");
            Console.WriteLine($"Source SRS: {metadata.SrsName}");
            Console.WriteLine($"File bounds: {metadata.BoundingBox}");
            Console.WriteLine($"Filter active: {filterBounds is not null}");
            Console.WriteLine($"Parsed buildings: {buildings.Count}");
            Console.WriteLine($"Parsed surfaces: {buildings.Sum(building => building.Surfaces.Count)}");
            Console.WriteLine($"Metadata elapsed: {metadataElapsed.TotalSeconds:0.###} s");
            Console.WriteLine($"Parse elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static CityGmlSourceMetadata ReadCityGmlSourceMetadata(string filePath)
        {
            using var stream = System.IO.File.OpenRead(filePath);
            return Lod2GmlReader.ReadSourceMetadata(stream);
        }

        private static CityGmlSourceMetadata ConvertCityJsonMetadata(CityJsonSourceMetadata metadata)
        {
            return new CityGmlSourceMetadata(metadata.SrsName, metadata.BoundingBox);
        }

        private static void RunGeoTiff(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: geotiff <path>");
                return;
            }

            var startedAt = DateTime.UtcNow;
            var info = GeoTiffReader.ReadImageInfo(args[1]);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine($"File: {args[1]}");
            Console.WriteLine($"SRS: {info.SrsName}");
            Console.WriteLine($"Size: {info.Width} x {info.Height}");
            Console.WriteLine($"Bounds: {FormatBounds(info.BoundingBox)}");
            Console.WriteLine($"File size: {info.FileSizeBytes} bytes");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static void RunTerrainFile(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: terrainfile <path> [sourceSrs]");
                return;
            }

            var startedAt = DateTime.UtcNow;
            var extension = System.IO.Path.GetExtension(args[1]);
            TerrainRasterData raster;
            BoundingBox2D bounds;
            string sourceSrs;
            string formatLabel;
            if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            {
                var info = GeoTiffReader.ReadImageInfo(args[1]);
                raster = TerrainRasterReader.ReadRaster(args[1], System.IO.Path.GetFileNameWithoutExtension(args[1]), info.SrsName);
                bounds = info.BoundingBox;
                sourceSrs = info.SrsName;
                formatLabel = "GeoTIFF DEM";
            }
            else
            {
                sourceSrs = args.Length > 2 ? args[2] : "EPSG:4326";
                var readResult = TerrainTextGridReader.ReadRaster(
                    args[1],
                    System.IO.Path.GetFileNameWithoutExtension(args[1]),
                    sourceSrs);
                raster = readResult.Raster;
                bounds = readResult.BoundingBox;
                formatLabel = readResult.FormatLabel;
            }

            var elapsed = DateTime.UtcNow - startedAt;
            var validCount = 0;
            var minElevation = double.PositiveInfinity;
            var maxElevation = double.NegativeInfinity;

            foreach (var elevation in raster.Elevations)
            {
                if (float.IsNaN(elevation) ||
                    float.IsInfinity(elevation) ||
                    (raster.NoDataValue.HasValue &&
                     !double.IsNaN(raster.NoDataValue.Value) &&
                     Math.Abs(elevation - raster.NoDataValue.Value) < 1e-3))
                {
                    continue;
                }

                validCount++;
                minElevation = Math.Min(minElevation, elevation);
                maxElevation = Math.Max(maxElevation, elevation);
            }

            Console.WriteLine($"File: {args[1]}");
            Console.WriteLine($"Format: {formatLabel}");
            Console.WriteLine($"SRS: {sourceSrs}");
            Console.WriteLine($"Size: {raster.Width} x {raster.Height}");
            Console.WriteLine($"Bounds: {FormatBounds(bounds)}");
            Console.WriteLine($"Valid elevations: {validCount}");
            Console.WriteLine($"Elevation range: {(double.IsInfinity(minElevation) ? "?" : minElevation.ToString("0.###"))}..{(double.IsInfinity(maxElevation) ? "?" : maxElevation.ToString("0.###"))}");
            Console.WriteLine($"NoData: {raster.NoDataValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static void RunShapefile(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: shapefile <path>");
                return;
            }

            var startedAt = DateTime.UtcNow;
            var shapefilePath = args[1];
            var sourceSrs = ResolveSandboxShapefileSrs(shapefilePath);
            var bounds = ResolveSandboxShapefileBounds(shapefilePath);
            var spatialContext = new SpatialContext2D(
                sourceSrs,
                bounds,
                bounds,
                sourceSrs == "EPSG:4326" ? bounds : null,
                new Coordinate2D(bounds.MinX, bounds.MinY),
                false,
                new Dictionary<string, BoundingBox2D>
                {
                    [sourceSrs] = bounds
                });
            if (sourceSrs == "EPSG:4326")
            {
                spatialContext.BoundingBoxesBySrs["EPSG:7423"] = bounds;
            }

            var result = ShapefileFeatureReader.ReadFeatures(
                shapefilePath,
                System.IO.Path.GetFileNameWithoutExtension(shapefilePath),
                spatialContext,
                0);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine($"File: {shapefilePath}");
            Console.WriteLine($"Source SRS: {result.SourceSrs}");
            Console.WriteLine($"Source bounds: {FormatBounds(result.SourceBoundingBox)}");
            Console.WriteLine($"Scanned features: {result.SourceFeatureCount}");
            Console.WriteLine($"Parsed features: {result.Features.Count}");
            Console.WriteLine($"Skipped outside context: {result.SkippedOutsideContextCount}");
            Console.WriteLine($"Failed features: {result.FailedFeatureCount}");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static void RunGeoJson(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: geojson <path> [sourceSrs]");
                return;
            }

            var startedAt = DateTime.UtcNow;
            var geoJsonPath = args[1];
            var geoJson = System.IO.File.ReadAllText(geoJsonPath);
            var sourceSrs = args.Length > 2
                ? args[2]
                : GeoJsonReader.TryReadSourceSrs(geoJson) ?? "EPSG:4326";
            var features = GeoJsonReader.ReadFeatures(
                geoJson,
                System.IO.Path.GetFileNameWithoutExtension(geoJsonPath));
            var geometryItemCount = features.Sum(feature =>
                feature.Geometry.OuterRings.Count +
                feature.Geometry.LineStrings.Count +
                feature.Geometry.Points.Count);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine($"File: {geoJsonPath}");
            Console.WriteLine($"Source SRS: {sourceSrs}");
            Console.WriteLine($"Feature count: {features.Count}");
            Console.WriteLine($"Geometry items: {geometryItemCount}");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static async Task RunOgcApiFeaturesAsync(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ogcapi <collection-items-url> [minX,minY,maxX,maxY] [maxFeatures]");
                return;
            }

            var bbox = args.Length > 2 && TryParseBoundingBox(args[2], out var parsedBounds)
                ? parsedBounds
                : null;
            var maxFeatures = args.Length > 3 && int.TryParse(args[3], out var parsedMaxFeatures)
                ? parsedMaxFeatures
                : 0;
            var startedAt = DateTime.UtcNow;
            var client = new OgcApiFeaturesClient();
            var features = await client.LoadFeaturesAsync(args[1], "ogcapi", bbox, maxFeatures);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine("OGC API Features probe");
            Console.WriteLine($"URL: {args[1]}");
            Console.WriteLine($"Feature count: {features.Count}");
            Console.WriteLine($"Geometry items: {features.Sum(feature => feature.Geometry.OuterRings.Count + feature.Geometry.LineStrings.Count + feature.Geometry.Points.Count)}");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static string ResolveSandboxShapefileSrs(string shapefilePath)
        {
            var prjPath = System.IO.Path.ChangeExtension(shapefilePath, ".prj");
            if (!System.IO.File.Exists(prjPath))
            {
                return "EPSG:4326";
            }

            var text = System.IO.File.ReadAllText(prjPath);
            if (text.Contains("25832", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25832";
            }

            if (text.Contains("25833", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25833";
            }

            if (text.Contains("3857", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:3857";
            }

            return "EPSG:4326";
        }

        private static BoundingBox2D ResolveSandboxShapefileBounds(string shapefilePath)
        {
            var sourceSrs = ResolveSandboxShapefileSrs(shapefilePath);
            return sourceSrs == "EPSG:4326"
                ? new BoundingBox2D(-180.0, -90.0, 180.0, 90.0)
                : new BoundingBox2D(double.MinValue / 4.0, double.MinValue / 4.0, double.MaxValue / 4.0, double.MaxValue / 4.0);
        }

        private static bool TryParseBoundingBox(string text, out BoundingBox2D? boundingBox)
        {
            boundingBox = null;
            var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4 ||
                !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minX) ||
                !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minY) ||
                !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var maxX) ||
                !double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var maxY))
            {
                return false;
            }

            boundingBox = new BoundingBox2D(minX, minY, maxX, maxY);
            return true;
        }

        private static async Task RunLod2Async(string[] args)
        {
            var requestOptions = CreateLod2RequestOptions(args);
            var client = new WfsClient();

            try
            {
                Console.WriteLine("Loading LoD2 buildings...");
                Console.WriteLine($"SRS: {requestOptions.SrsName}");
                Console.WriteLine(WfsClient.BuildGetFeatureRequestUrl(requestOptions));
                Console.WriteLine();

                var featureResponse = await client.LoadFeatureResponseAsync(requestOptions);
                var buildings = Lod2GmlReader.ReadBuildings(featureResponse.ResponseText, requestOptions.TypeName);

                Console.WriteLine("Parsed LoD2 buildings:");
                Console.WriteLine();

                for (int i = 0; i < buildings.Count; i++)
                {
                    var building = buildings[i];
                    Console.WriteLine($"Building {i + 1}");
                    Console.WriteLine($"Id: {building.Id}");
                    Console.WriteLine($"Surface count: {building.Surfaces.Count}");
                    Console.WriteLine($"First surface outer point count: {(building.Surfaces.Count > 0 ? building.Surfaces[0].OuterPoints.Count : 0)}");
                    Console.WriteLine($"First surface inner ring count: {(building.Surfaces.Count > 0 ? building.Surfaces[0].InnerRings.Count : 0)}");

                    if (building.Attributes.TryGetValue("HeightAboveGround", out var height))
                    {
                        Console.WriteLine($"HeightAboveGround: {height}");
                    }

                    Console.WriteLine();
                }

                if (buildings.Count == 0)
                {
                    Console.WriteLine("No LoD2 buildings were parsed.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while loading or parsing the LoD2 response:");
                Console.WriteLine(ex.Message);
            }
        }

        private static WfsRequestOptions CreateRequestOptions(string[] args)
        {
            var options = new WfsRequestOptions
            {
                BaseUrl = "https://planas.frankfurt.de/wfs/bebauungsplaene_rv_flaechennutzung",
                TypeName = "n_bplan_rv",
                SrsName = string.Empty,
                MaxFeatures = 5
            };

            if (args.Length > 0)
            {
                options.BaseUrl = args[0];
            }

            if (args.Length > 1)
            {
                options.TypeName = args[1];
            }

            if (args.Length > 2 && int.TryParse(args[2], out var maxFeatures))
            {
                options.MaxFeatures = maxFeatures;
            }

            if (args.Length > 3)
            {
                options.SrsName = args[3];
            }

            return options;
        }

        private static WfsRequestOptions CreateLod2RequestOptions(string[] args)
        {
            var options = new WfsRequestOptions
            {
                BaseUrl = "https://www.geoportal.hessen.de/mapbender/php/wfs.php?FEATURETYPE_ID=5589&INSPIRE=1&REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0",
                TypeName = "bu-core3d:Building",
                SrsName = "EPSG:7423",
                MaxFeatures = 1,
                Version = "2.0.0",
                OutputFormat = "application/gml+xml; version=3.2"
            };

            if (args.Length > 1)
            {
                options.BaseUrl = args[1];
            }

            if (args.Length > 2)
            {
                options.TypeName = args[2];
            }

            if (args.Length > 3 && int.TryParse(args[3], out var maxFeatures))
            {
                options.MaxFeatures = maxFeatures;
            }

            if (args.Length > 4)
            {
                options.SrsName = args[4];
            }

            return options;
        }

        private static LinearRing? GetFirstOuterRing(WfsFeature feature)
        {
            return feature.Geometry.OuterRings.Count > 0
                ? feature.Geometry.OuterRings[0]
                : null;
        }

        private static string GetAttributeValue(WfsFeature feature, string key)
        {
            return feature.Attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : "(not available)";
        }
    }
}
