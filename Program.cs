using System;
using System.Threading.Tasks;
using WfsCore;

namespace RhinoWFS.Sandbox
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var requestOptions = CreateRequestOptions(args);

            var client = new WfsClient();

            try
            {
                Console.WriteLine("Loading WFS features...");
                Console.WriteLine($"SRS: {requestOptions.SrsName}");
                Console.WriteLine($"Bounding box active: {requestOptions.BoundingBox is not null}");
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
