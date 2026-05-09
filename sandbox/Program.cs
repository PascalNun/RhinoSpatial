using System;
using System.Threading.Tasks;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "lod2file", StringComparison.OrdinalIgnoreCase))
            {
                RunLod2File(args);
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
            CityGmlSourceMetadata metadata;
            using (var stream = System.IO.File.OpenRead(filePath))
            {
                metadata = Lod2GmlReader.ReadSourceMetadata(stream);
            }
            var metadataElapsed = DateTime.UtcNow - metadataStartedAt;

            var startedAt = DateTime.UtcNow;
            var text = System.IO.File.ReadAllText(filePath);
            var buildings = Lod2GmlReader.ReadBuildings(text, System.IO.Path.GetFileNameWithoutExtension(filePath), filterBounds);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine($"File: {filePath}");
            Console.WriteLine($"Source SRS: {metadata.SrsName}");
            Console.WriteLine($"File bounds: {metadata.BoundingBox}");
            Console.WriteLine($"Filter active: {filterBounds is not null}");
            Console.WriteLine($"Parsed buildings: {buildings.Count}");
            Console.WriteLine($"Parsed surfaces: {buildings.Sum(building => building.Surfaces.Count)}");
            Console.WriteLine($"Metadata elapsed: {metadataElapsed.TotalSeconds:0.###} s");
            Console.WriteLine($"Parse elapsed: {elapsed.TotalSeconds:0.###} s");
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
