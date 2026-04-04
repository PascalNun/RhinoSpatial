using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WfsCore
{
    public static class GeoJsonReader
    {
        public static List<WfsFeature> ReadFeatures(string geoJson, string sourceLayerName)
        {
            using var document = JsonDocument.Parse(geoJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("features", out var featuresElement) || featuresElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The GeoJSON response does not contain a valid features array.");
            }

            var features = new List<WfsFeature>();

            foreach (var featureElement in featuresElement.EnumerateArray())
            {
                features.Add(ReadFeature(featureElement, sourceLayerName));
            }

            return features;
        }

        private static WfsFeature ReadFeature(JsonElement featureElement, string sourceLayerName)
        {
            var featureId = featureElement.TryGetProperty("id", out var idElement)
                ? idElement.ToString()
                : string.Empty;

            var geometryElement = featureElement.GetProperty("geometry");
            var properties = featureElement.TryGetProperty("properties", out var propertiesElement)
                ? ReadAttributes(propertiesElement)
                : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var geometry = ReadGeometry(geometryElement);

            return new WfsFeature(
                Id: featureId,
                SourceLayerName: sourceLayerName,
                Geometry: geometry,
                Attributes: properties
            );
        }

        private static Dictionary<string, string?> ReadAttributes(JsonElement propertiesElement)
        {
            var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in propertiesElement.EnumerateObject())
            {
                attributes[property.Name] = ReadJsonValueAsString(property.Value);
            }

            return attributes;
        }

        private static WfsGeometry ReadGeometry(JsonElement geometryElement)
        {
            if (geometryElement.ValueKind == JsonValueKind.Null)
            {
                return new WfsGeometry(
                    Type: "Null",
                    OuterRings: new List<LinearRing>()
                );
            }

            if (!geometryElement.TryGetProperty("coordinates", out var coordinatesElement))
            {
                return new WfsGeometry(
                    Type: geometryElement.GetProperty("type").GetString() ?? "Unknown",
                    OuterRings: new List<LinearRing>()
                );
            }

            var geometryType = geometryElement.GetProperty("type").GetString();

            var outerRings = geometryType switch
            {
                "Polygon" => new List<LinearRing> { ReadRingPoints(coordinatesElement[0]) },
                "MultiPolygon" => ReadMultiPolygonOuterRings(coordinatesElement),
                _ => throw new NotSupportedException($"Geometry type '{geometryType}' is not supported yet.")
            };

            return new WfsGeometry(
                Type: geometryType ?? "Unknown",
                OuterRings: outerRings
            );
        }

        private static string? ReadJsonValueAsString(JsonElement valueElement)
        {
            return valueElement.ValueKind switch
            {
                JsonValueKind.String => valueElement.GetString(),
                JsonValueKind.Number => valueElement.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => valueElement.GetRawText()
            };
        }

        private static List<LinearRing> ReadMultiPolygonOuterRings(JsonElement coordinatesElement)
        {
            var outerRings = new List<LinearRing>();

            foreach (var polygonElement in coordinatesElement.EnumerateArray())
            {
                if (polygonElement.GetArrayLength() == 0)
                {
                    continue;
                }

                outerRings.Add(ReadRingPoints(polygonElement[0]));
            }

            return outerRings;
        }

        private static LinearRing ReadRingPoints(JsonElement ringElement)
        {
            var points = new List<Coordinate2D>();

            foreach (var pointElement in ringElement.EnumerateArray())
            {
                if (pointElement.GetArrayLength() < 2)
                {
                    continue;
                }

                var x = pointElement[0].GetDouble();
                var y = pointElement[1].GetDouble();

                points.Add(new Coordinate2D(x, y));
            }

            return new LinearRing(points);
        }
    }
}
