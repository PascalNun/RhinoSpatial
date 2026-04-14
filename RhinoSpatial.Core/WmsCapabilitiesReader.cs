using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace RhinoSpatial.Core
{
    public static class WmsCapabilitiesReader
    {
        public static WmsCapabilitiesInfo ReadCapabilities(string capabilitiesXml)
        {
            var document = XDocument.Parse(capabilitiesXml);
            var serviceVersion = document.Root?
                .Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "version")
                ?.Value
                ?.Trim() ?? string.Empty;
            var rootLayer = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Capability")
                ?.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Layer");

            var layers = new List<WmsLayerInfo>();

            if (rootLayer is not null)
            {
                ReadLayerTree(
                    rootLayer,
                    inheritedSrs: new List<string>(),
                    inheritedBoundingBox: null,
                    target: layers);
            }

            layers = layers
                .GroupBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var maxWidth = TryReadPositiveInt(document, "MaxWidth");
            var maxHeight = TryReadPositiveInt(document, "MaxHeight");
            var getMapUrl = ReadOperationUrl(document, "GetMap");

            return new WmsCapabilitiesInfo(layers, getMapUrl, serviceVersion, maxWidth, maxHeight);
        }

        public static List<WmsLayerInfo> ReadLayers(string capabilitiesXml)
        {
            return ReadCapabilities(capabilitiesXml).Layers;
        }

        private static void ReadLayerTree(
            XElement layerElement,
            List<string> inheritedSrs,
            BoundingBox2D? inheritedBoundingBox,
            List<WmsLayerInfo> target)
        {
            var layerSrs = MergeSrsLists(inheritedSrs, ReadSrsValues(layerElement));
            var layerBoundingBox = ReadWgs84BoundingBox(layerElement) ?? inheritedBoundingBox;
            var name = GetChildValue(layerElement, "Name");
            var title = GetChildValue(layerElement, "Title");

            if (!string.IsNullOrWhiteSpace(name))
            {
                target.Add(new WmsLayerInfo(
                    Name: name,
                    Title: string.IsNullOrWhiteSpace(title) ? name : title,
                    SupportedSrs: layerSrs,
                    Wgs84BoundingBox: layerBoundingBox));
            }

            foreach (var childLayer in layerElement.Elements().Where(element => element.Name.LocalName == "Layer"))
            {
                ReadLayerTree(childLayer, layerSrs, layerBoundingBox, target);
            }
        }

        private static List<string> ReadSrsValues(XElement layerElement)
        {
            return layerElement
                .Elements()
                .Where(element => element.Name.LocalName is "CRS" or "SRS")
                .SelectMany(element => element.Value.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries))
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> MergeSrsLists(List<string> inheritedSrs, List<string> localSrs)
        {
            var merged = new List<string>(inheritedSrs);

            foreach (var srs in localSrs)
            {
                if (!merged.Contains(srs, StringComparer.OrdinalIgnoreCase))
                {
                    merged.Add(srs);
                }
            }

            return merged;
        }

        private static BoundingBox2D? ReadWgs84BoundingBox(XElement layerElement)
        {
            var exBoundingBox = layerElement.Elements().FirstOrDefault(element => element.Name.LocalName == "EX_GeographicBoundingBox");
            if (exBoundingBox is not null)
            {
                var west = TryParseDouble(GetChildValue(exBoundingBox, "westBoundLongitude"));
                var east = TryParseDouble(GetChildValue(exBoundingBox, "eastBoundLongitude"));
                var south = TryParseDouble(GetChildValue(exBoundingBox, "southBoundLatitude"));
                var north = TryParseDouble(GetChildValue(exBoundingBox, "northBoundLatitude"));

                if (west.HasValue && east.HasValue && south.HasValue && north.HasValue)
                {
                    return new BoundingBox2D(west.Value, south.Value, east.Value, north.Value);
                }
            }

            var latLonBoundingBox = layerElement.Elements().FirstOrDefault(element => element.Name.LocalName == "LatLonBoundingBox");
            if (latLonBoundingBox is not null)
            {
                var minX = TryParseDouble(GetAttributeValue(latLonBoundingBox, "minx"));
                var minY = TryParseDouble(GetAttributeValue(latLonBoundingBox, "miny"));
                var maxX = TryParseDouble(GetAttributeValue(latLonBoundingBox, "maxx"));
                var maxY = TryParseDouble(GetAttributeValue(latLonBoundingBox, "maxy"));

                if (minX.HasValue && minY.HasValue && maxX.HasValue && maxY.HasValue)
                {
                    return new BoundingBox2D(minX.Value, minY.Value, maxX.Value, maxY.Value);
                }
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

        private static string GetAttributeValue(XElement element, string localName)
        {
            return element
                .Attributes()
                .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim() ?? string.Empty;
        }

        private static string ReadOperationUrl(XDocument document, string operationName)
        {
            var operationElement = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == operationName);

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
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "OnlineResource")
                ?.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "href")
                ?.Value
                ?.Trim() ?? string.Empty;
        }

        private static int? TryReadPositiveInt(XDocument document, string localName)
        {
            var text = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == localName)
                ?.Value
                ?.Trim();

            if (!int.TryParse(text, out var value) || value <= 0)
            {
                return null;
            }

            return value;
        }

        private static double? TryParseDouble(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            return value;
        }
    }
}
