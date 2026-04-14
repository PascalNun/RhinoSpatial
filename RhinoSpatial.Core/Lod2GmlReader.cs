using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace RhinoSpatial.Core
{
    public static class Lod2GmlReader
    {
        public static List<Lod2Building> ReadBuildings(string gmlText, string sourceLayerName)
        {
            var document = XDocument.Parse(gmlText);
            var root = document.Root;

            if (root is null)
            {
                throw new InvalidOperationException("The XML response is empty.");
            }

            if (IsExceptionReport(root))
            {
                throw new InvalidOperationException(ReadExceptionMessage(root));
            }

            if (!string.Equals(root.Name.LocalName, "FeatureCollection", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The XML response is not a supported WFS FeatureCollection.");
            }

            var buildings = new List<Lod2Building>();

            foreach (var memberElement in root.Elements().Where(IsFeatureMemberElement))
            {
                var featureElement = memberElement.Elements().FirstOrDefault();
                if (featureElement is null)
                {
                    continue;
                }

                var building = ReadBuilding(featureElement, sourceLayerName);
                if (building.Surfaces.Count == 0)
                {
                    continue;
                }

                buildings.Add(building);
            }

            return buildings;
        }

        private static Lod2Building ReadBuilding(XElement featureElement, string sourceLayerName)
        {
            var featureId = featureElement.Attributes()
                .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "id", StringComparison.OrdinalIgnoreCase))
                ?.Value ?? string.Empty;

            var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            TryAddAttribute(attributes, "Identifier", featureElement.Descendants().FirstOrDefault(element => string.Equals(element.Name.LocalName, "identifier", StringComparison.OrdinalIgnoreCase))?.Value);
            TryAddAttribute(attributes, "LocalId", featureElement.Descendants().FirstOrDefault(element => string.Equals(element.Name.LocalName, "localId", StringComparison.OrdinalIgnoreCase))?.Value);
            TryAddAttribute(attributes, "HeightAboveGround", ReadHeightAboveGround(featureElement));

            return new Lod2Building(
                Id: featureId,
                SourceLayerName: sourceLayerName,
                Surfaces: ReadSurfaces(featureElement),
                Attributes: attributes);
        }

        private static List<SurfaceRing3D> ReadSurfaces(XElement featureElement)
        {
            var surfaces = new List<SurfaceRing3D>();

            var lod2GeometryContainers = featureElement
                .Descendants()
                .Where(element =>
                    string.Equals(element.Name.LocalName, "geometry3DLoD2", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(element.Name.LocalName, "geometryMultiSurface", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (lod2GeometryContainers.Count == 0)
            {
                lod2GeometryContainers.Add(featureElement);
            }

            foreach (var container in lod2GeometryContainers)
            {
                foreach (var polygonElement in EnumerateSupportedPolygonElements(container))
                {
                    var surface = ReadSurface(polygonElement);
                    if (surface.Points.Count >= 4)
                    {
                        surfaces.Add(surface);
                    }
                }
            }

            return surfaces;
        }

        private static IEnumerable<XElement> EnumerateSupportedPolygonElements(XElement container)
        {
            foreach (var element in container.Descendants())
            {
                if (!IsSupportedPolygonElement(element))
                {
                    continue;
                }

                if (string.Equals(element.Name.LocalName, "Surface", StringComparison.OrdinalIgnoreCase) &&
                    ContainsNestedPolygonGeometry(element))
                {
                    continue;
                }

                yield return element;
            }
        }

        private static SurfaceRing3D ReadSurface(XElement polygonElement)
        {
            var linearRingElement = polygonElement
                .Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "LinearRing", StringComparison.OrdinalIgnoreCase));

            if (linearRingElement is null)
            {
                return new SurfaceRing3D(new List<Coordinate3D>());
            }

            var srsName = FindSrsName(linearRingElement);
            var posListElement = linearRingElement
                .Elements()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "posList", StringComparison.OrdinalIgnoreCase));

            if (posListElement is not null)
            {
                return ReadSurfaceFromPosList(posListElement, srsName);
            }

            var posElements = linearRingElement
                .Elements()
                .Where(element => string.Equals(element.Name.LocalName, "pos", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return ReadSurfaceFromPosElements(posElements, srsName);
        }

        private static SurfaceRing3D ReadSurfaceFromPosList(XElement posListElement, string srsName)
        {
            var rawValues = posListElement.Value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var dimension = GetCoordinateDimension(posListElement, srsName, rawValues.Length);
            var points = new List<Coordinate3D>();

            for (int valueIndex = 0; valueIndex + 1 < rawValues.Length; valueIndex += dimension)
            {
                if (!double.TryParse(rawValues[valueIndex], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var firstValue) ||
                    !double.TryParse(rawValues[valueIndex + 1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var secondValue))
                {
                    continue;
                }

                var thirdValue = 0.0;
                if (dimension >= 3 && valueIndex + 2 < rawValues.Length)
                {
                    double.TryParse(rawValues[valueIndex + 2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out thirdValue);
                }

                points.Add(CreateCoordinate(firstValue, secondValue, thirdValue, srsName));
            }

            return new SurfaceRing3D(points);
        }

        private static SurfaceRing3D ReadSurfaceFromPosElements(List<XElement> posElements, string srsName)
        {
            var points = new List<Coordinate3D>();

            foreach (var posElement in posElements)
            {
                var rawValues = posElement.Value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (rawValues.Length < 2)
                {
                    continue;
                }

                if (!double.TryParse(rawValues[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var firstValue) ||
                    !double.TryParse(rawValues[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var secondValue))
                {
                    continue;
                }

                var thirdValue = 0.0;
                if (rawValues.Length >= 3)
                {
                    double.TryParse(rawValues[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out thirdValue);
                }

                points.Add(CreateCoordinate(firstValue, secondValue, thirdValue, srsName));
            }

            return new SurfaceRing3D(points);
        }

        private static Coordinate3D CreateCoordinate(double firstValue, double secondValue, double thirdValue, string srsName)
        {
            if (ShouldSwapAxisOrder(firstValue, secondValue, srsName))
            {
                return new Coordinate3D(secondValue, firstValue, thirdValue);
            }

            return new Coordinate3D(firstValue, secondValue, thirdValue);
        }

        private static bool ShouldSwapAxisOrder(double firstValue, double secondValue, string srsName)
        {
            if (!string.IsNullOrWhiteSpace(srsName) &&
                (srsName.Contains("4326", StringComparison.OrdinalIgnoreCase) ||
                 srsName.Contains("7423", StringComparison.OrdinalIgnoreCase) ||
                 srsName.Contains("4283", StringComparison.OrdinalIgnoreCase) ||
                 srsName.Contains("7844", StringComparison.OrdinalIgnoreCase)))
            {
                var firstLooksLikeLatitude = Math.Abs(firstValue) <= 90;
                var secondLooksLikeLatitude = Math.Abs(secondValue) <= 90;
                var firstLooksLikeLongitude = Math.Abs(firstValue) <= 180;
                var secondLooksLikeLongitude = Math.Abs(secondValue) <= 180;

                if (firstLooksLikeLatitude && secondLooksLikeLongitude && !secondLooksLikeLatitude)
                {
                    return true;
                }

                if (firstLooksLikeLongitude && !firstLooksLikeLatitude && secondLooksLikeLatitude)
                {
                    return false;
                }

                if (firstLooksLikeLatitude && secondLooksLikeLatitude)
                {
                    return Math.Abs(firstValue) > Math.Abs(secondValue);
                }
            }

            if (!string.IsNullOrWhiteSpace(srsName) &&
                srsName.Contains("EPSG", StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(firstValue) >= 1_000_000 &&
                Math.Abs(secondValue) < 1_000_000)
            {
                return true;
            }

            return false;
        }

        private static int GetCoordinateDimension(XElement posListElement, string srsName, int valueCount)
        {
            var srsDimensionValue = posListElement
                .Attributes()
                .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "srsDimension", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (int.TryParse(srsDimensionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDimension) &&
                parsedDimension >= 2)
            {
                return parsedDimension;
            }

            if (!string.IsNullOrWhiteSpace(srsName) &&
                (srsName.Contains("7423", StringComparison.OrdinalIgnoreCase) ||
                 srsName.Contains("4979", StringComparison.OrdinalIgnoreCase)) &&
                valueCount % 3 == 0)
            {
                return 3;
            }

            return 2;
        }

        private static string ReadHeightAboveGround(XElement featureElement)
        {
            var heightElement = featureElement
                .Descendants()
                .FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, "value", StringComparison.OrdinalIgnoreCase) &&
                    element.Ancestors().Any(ancestor => string.Equals(ancestor.Name.LocalName, "HeightAboveGround", StringComparison.OrdinalIgnoreCase)));

            return heightElement?.Value?.Trim() ?? string.Empty;
        }

        private static void TryAddAttribute(Dictionary<string, string?> attributes, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes[key] = value.Trim();
            }
        }

        private static string FindSrsName(XElement element)
        {
            var currentElement = element;

            while (currentElement is not null)
            {
                var srsName = currentElement
                    .Attributes()
                    .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "srsName", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                if (!string.IsNullOrWhiteSpace(srsName))
                {
                    return srsName;
                }

                currentElement = currentElement.Parent;
            }

            return string.Empty;
        }

        private static bool IsFeatureMemberElement(XElement element)
        {
            return string.Equals(element.Name.LocalName, "member", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(element.Name.LocalName, "featureMember", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportedPolygonElement(XElement element)
        {
            var localName = element.Name.LocalName;
            return string.Equals(localName, "Polygon", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "Surface", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "PolygonPatch", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsNestedPolygonGeometry(XElement element)
        {
            return element
                .Descendants()
                .Any(child =>
                    string.Equals(child.Name.LocalName, "Polygon", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(child.Name.LocalName, "PolygonPatch", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsExceptionReport(XElement root)
        {
            return string.Equals(root.Name.LocalName, "ExceptionReport", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.Name.LocalName, "ServiceExceptionReport", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadExceptionMessage(XElement root)
        {
            var exceptionText = root
                .Descendants()
                .FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, "ExceptionText", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(element.Name.LocalName, "ServiceException", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();

            return string.IsNullOrWhiteSpace(exceptionText)
                ? "The LoD2 WFS service returned an XML exception instead of feature data."
                : exceptionText;
        }
    }
}
