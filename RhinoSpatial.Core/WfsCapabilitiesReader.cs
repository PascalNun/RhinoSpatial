using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace RhinoSpatial.Core
{
    public static class WfsCapabilitiesReader
    {
        public static WfsCapabilitiesInfo ReadCapabilities(string capabilitiesXml)
        {
            var document = XDocument.Parse(capabilitiesXml);
            var serviceVersion = document.Root?
                .Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "version")
                ?.Value
                ?.Trim() ?? string.Empty;

            var layers = document
                .Descendants()
                .Where(element => element.Name.LocalName == "FeatureType")
                .Select(ReadLayerInfo)
                .Where(layer => !string.IsNullOrWhiteSpace(layer.Name))
                .GroupBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var getFeatureUrl = ReadOperationUrl(document, "GetFeature");

            return new WfsCapabilitiesInfo(layers, getFeatureUrl, serviceVersion);
        }

        public static List<WfsLayerInfo> ReadLayers(string capabilitiesXml)
        {
            return ReadCapabilities(capabilitiesXml).Layers;
        }

        private static WfsLayerInfo ReadLayerInfo(XElement featureTypeElement)
        {
            var name = GetChildValue(featureTypeElement, "Name");
            var title = GetChildValue(featureTypeElement, "Title");
            var defaultSrs = GetChildValue(featureTypeElement, "DefaultSRS");

            if (string.IsNullOrWhiteSpace(defaultSrs))
            {
                defaultSrs = GetChildValue(featureTypeElement, "DefaultCRS");
            }

            var otherSrs = GetChildValues(featureTypeElement, "OtherSRS");

            if (otherSrs.Count == 0)
            {
                otherSrs = GetChildValues(featureTypeElement, "OtherCRS");
            }

            return new WfsLayerInfo(
                Name: name,
                Title: string.IsNullOrWhiteSpace(title) ? name : title,
                DefaultSrs: defaultSrs,
                OtherSrs: otherSrs,
                Wgs84BoundingBox: ReadWgs84BoundingBox(featureTypeElement)
            );
        }

        private static string ReadOperationUrl(XDocument document, string operationName)
        {
            var operationElement = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "Operation" &&
                    string.Equals(
                        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "name")?.Value,
                        operationName,
                        StringComparison.OrdinalIgnoreCase));

            if (operationElement is null)
            {
                return string.Empty;
            }

            var getElement = operationElement
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Get");

            if (getElement is null)
            {
                return string.Empty;
            }

            return getElement
                .Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "href")
                ?.Value
                ?.Trim() ?? string.Empty;
        }

        private static BoundingBox2D? ReadWgs84BoundingBox(XElement featureTypeElement)
        {
            var wgs84BoundingBoxElement = featureTypeElement
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "WGS84BoundingBox");

            if (wgs84BoundingBoxElement is not null)
            {
                var lowerCorner = GetChildValue(wgs84BoundingBoxElement, "LowerCorner");
                var upperCorner = GetChildValue(wgs84BoundingBoxElement, "UpperCorner");

                if (TryParseCorner(lowerCorner, out var lowerX, out var lowerY) &&
                    TryParseCorner(upperCorner, out var upperX, out var upperY))
                {
                    return new BoundingBox2D(
                        MinX: Math.Min(lowerX, upperX),
                        MinY: Math.Min(lowerY, upperY),
                        MaxX: Math.Max(lowerX, upperX),
                        MaxY: Math.Max(lowerY, upperY));
                }
            }

            var latLongBoundingBoxElement = featureTypeElement
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "LatLongBoundingBox");

            if (latLongBoundingBoxElement is not null &&
                TryParseAttributeDouble(latLongBoundingBoxElement, "minx", out var minX) &&
                TryParseAttributeDouble(latLongBoundingBoxElement, "miny", out var minY) &&
                TryParseAttributeDouble(latLongBoundingBoxElement, "maxx", out var maxX) &&
                TryParseAttributeDouble(latLongBoundingBoxElement, "maxy", out var maxY))
            {
                return new BoundingBox2D(
                    MinX: Math.Min(minX, maxX),
                    MinY: Math.Min(minY, maxY),
                    MaxX: Math.Max(minX, maxX),
                    MaxY: Math.Max(minY, maxY));
            }

            return null;
        }

        private static string GetChildValue(XElement parentElement, string localName)
        {
            return parentElement
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == localName)
                ?.Value
                ?.Trim() ?? string.Empty;
        }

        private static List<string> GetChildValues(XElement parentElement, string localName)
        {
            return parentElement
                .Elements()
                .Where(element => element.Name.LocalName == localName)
                .Select(element => element.Value?.Trim() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool TryParseCorner(string text, out double x, out double y)
        {
            x = 0;
            y = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return false;
            }

            return double.TryParse(parts[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out x) &&
                   double.TryParse(parts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out y);
        }

        private static bool TryParseAttributeDouble(XElement element, string attributeName, out double value)
        {
            value = 0;

            var attributeValue = element
                .Attributes()
                .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, attributeName, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return double.TryParse(attributeValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
        }
    }
}
