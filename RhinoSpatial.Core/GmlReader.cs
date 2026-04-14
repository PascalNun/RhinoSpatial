using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace RhinoSpatial.Core
{
    public static class GmlReader
    {
        public static List<WfsFeature> ReadFeatures(string gmlText, string sourceLayerName)
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

            var features = new List<WfsFeature>();

            foreach (var memberElement in root.Elements().Where(IsFeatureMemberElement))
            {
                var featureElement = memberElement.Elements().FirstOrDefault();

                if (featureElement is null)
                {
                    continue;
                }

                features.Add(ReadFeature(featureElement, sourceLayerName));
            }

            return features;
        }

        private static WfsFeature ReadFeature(XElement featureElement, string sourceLayerName)
        {
            var geometryPropertyElement = FindGeometryPropertyElement(featureElement);
            var geometryRootElement = geometryPropertyElement is null
                ? null
                : FindGeometryRootElement(geometryPropertyElement);

            var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var childElement in featureElement.Elements())
            {
                if (geometryPropertyElement is not null && ReferenceEquals(childElement, geometryPropertyElement))
                {
                    continue;
                }

                var value = childElement.Value?.Trim();
                attributes[childElement.Name.LocalName] = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            var featureId = featureElement.Attributes()
                .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "id", StringComparison.OrdinalIgnoreCase))
                ?.Value ?? string.Empty;

            var geometry = geometryRootElement is null
                ? new WfsGeometry
                {
                    Type = "Null"
                }
                : ReadGeometry(geometryRootElement);

            return new WfsFeature(
                Id: featureId,
                SourceLayerName: sourceLayerName,
                Geometry: geometry,
                Attributes: attributes
            );
        }

        private static WfsGeometry ReadGeometry(XElement geometryRootElement)
        {
            var geometryType = geometryRootElement.Name.LocalName;

            return geometryType switch
            {
                "Polygon" => new WfsGeometry
                {
                    Type = "Polygon",
                    OuterRings = new List<LinearRing> { ReadPolygonOuterRing(geometryRootElement) }
                },
                "Surface" => new WfsGeometry
                {
                    Type = "Polygon",
                    OuterRings = new List<LinearRing> { ReadPolygonOuterRing(geometryRootElement) }
                },
                "PolygonPatch" => new WfsGeometry
                {
                    Type = "Polygon",
                    OuterRings = new List<LinearRing> { ReadPolygonOuterRing(geometryRootElement) }
                },
                "MultiPolygon" => new WfsGeometry
                {
                    Type = "MultiPolygon",
                    OuterRings = ReadPolygonCollectionOuterRings(geometryRootElement)
                },
                "MultiSurface" => new WfsGeometry
                {
                    Type = "MultiPolygon",
                    OuterRings = ReadPolygonCollectionOuterRings(geometryRootElement)
                },
                "LineString" => new WfsGeometry
                {
                    Type = "LineString",
                    LineStrings = new List<LineString> { ReadLineString(geometryRootElement) }
                },
                "MultiLineString" => new WfsGeometry
                {
                    Type = "MultiLineString",
                    LineStrings = ReadLineStringCollection(geometryRootElement)
                },
                "Curve" => new WfsGeometry
                {
                    Type = "LineString",
                    LineStrings = new List<LineString> { ReadLineString(geometryRootElement) }
                },
                "MultiCurve" => new WfsGeometry
                {
                    Type = "MultiLineString",
                    LineStrings = ReadLineStringCollection(geometryRootElement)
                },
                "Point" => new WfsGeometry
                {
                    Type = "Point",
                    Points = new List<Coordinate2D> { ReadPoint(geometryRootElement) }
                },
                "MultiPoint" => new WfsGeometry
                {
                    Type = "MultiPoint",
                    Points = ReadPointCollection(geometryRootElement)
                },
                _ => throw new NotSupportedException($"Geometry type '{geometryType}' is not supported yet.")
            };
        }

        private static List<LinearRing> ReadPolygonCollectionOuterRings(XElement geometryRootElement)
        {
            var outerRings = new List<LinearRing>();

            foreach (var polygonElement in geometryRootElement.Descendants().Where(element => string.Equals(element.Name.LocalName, "Polygon", StringComparison.OrdinalIgnoreCase)))
            {
                outerRings.Add(ReadPolygonOuterRing(polygonElement));
            }

            foreach (var surfaceElement in geometryRootElement.Descendants().Where(element =>
                string.Equals(element.Name.LocalName, "Surface", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(element.Name.LocalName, "PolygonPatch", StringComparison.OrdinalIgnoreCase)))
            {
                outerRings.Add(ReadPolygonOuterRing(surfaceElement));
            }

            return outerRings;
        }

        private static LinearRing ReadPolygonOuterRing(XElement polygonElement)
        {
            var linearRingElement = polygonElement
                .Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "LinearRing", StringComparison.OrdinalIgnoreCase));

            if (linearRingElement is null)
            {
                return new LinearRing(new List<Coordinate2D>());
            }

            var srsName = FindSrsName(linearRingElement);
            var posListElement = linearRingElement
                .Elements()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "posList", StringComparison.OrdinalIgnoreCase));

            if (posListElement is not null)
            {
                return ReadLinearRingFromPosList(posListElement, srsName);
            }

            var posElements = linearRingElement
                .Elements()
                .Where(element => string.Equals(element.Name.LocalName, "pos", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return ReadLinearRingFromPosElements(posElements, srsName);
        }

        private static LineString ReadLineString(XElement lineElement)
        {
            var srsName = FindSrsName(lineElement);
            var posListElement = lineElement
                .Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "posList", StringComparison.OrdinalIgnoreCase));

            if (posListElement is not null)
            {
                return ReadLineStringFromPosList(posListElement, srsName);
            }

            var posElements = lineElement
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "pos", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return ReadLineStringFromPosElements(posElements, srsName);
        }

        private static List<LineString> ReadLineStringCollection(XElement geometryRootElement)
        {
            var lineStrings = new List<LineString>();

            foreach (var lineElement in geometryRootElement
                .Descendants()
                .Where(element =>
                    string.Equals(element.Name.LocalName, "LineString", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(element.Name.LocalName, "Curve", StringComparison.OrdinalIgnoreCase)))
            {
                lineStrings.Add(ReadLineString(lineElement));
            }

            return lineStrings;
        }

        private static Coordinate2D ReadPoint(XElement pointElement)
        {
            var srsName = FindSrsName(pointElement);
            var posElement = pointElement
                .Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "pos", StringComparison.OrdinalIgnoreCase));

            if (posElement is not null && TryParseCoordinatePair(posElement.Value, out var firstValue, out var secondValue))
            {
                return CreateCoordinate(firstValue, secondValue, srsName);
            }

            var coordinatesElement = pointElement
                .Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "coordinates", StringComparison.OrdinalIgnoreCase));

            if (coordinatesElement is not null && TryParseCoordinatePair(coordinatesElement.Value.Replace(",", " "), out firstValue, out secondValue))
            {
                return CreateCoordinate(firstValue, secondValue, srsName);
            }

            return new Coordinate2D(0, 0);
        }

        private static List<Coordinate2D> ReadPointCollection(XElement geometryRootElement)
        {
            var points = new List<Coordinate2D>();

            foreach (var pointElement in geometryRootElement
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Point", StringComparison.OrdinalIgnoreCase)))
            {
                points.Add(ReadPoint(pointElement));
            }

            return points;
        }

        private static LinearRing ReadLinearRingFromPosList(XElement posListElement, string srsName)
        {
            var text = posListElement.Value;
            var rawValues = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var dimension = GetCoordinateDimension(posListElement);
            var points = new List<Coordinate2D>();

            for (int i = 0; i + 1 < rawValues.Length; i += dimension)
            {
                if (!double.TryParse(rawValues[i], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var firstValue) ||
                    !double.TryParse(rawValues[i + 1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var secondValue))
                {
                    continue;
                }

                points.Add(CreateCoordinate(firstValue, secondValue, srsName));
            }

            return new LinearRing(points);
        }

        private static LinearRing ReadLinearRingFromPosElements(List<XElement> posElements, string srsName)
        {
            var points = new List<Coordinate2D>();

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

                points.Add(CreateCoordinate(firstValue, secondValue, srsName));
            }

            return new LinearRing(points);
        }

        private static LineString ReadLineStringFromPosList(XElement posListElement, string srsName)
        {
            var text = posListElement.Value;
            var rawValues = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var dimension = GetCoordinateDimension(posListElement);
            var points = new List<Coordinate2D>();

            for (int i = 0; i + 1 < rawValues.Length; i += dimension)
            {
                if (!double.TryParse(rawValues[i], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var firstValue) ||
                    !double.TryParse(rawValues[i + 1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var secondValue))
                {
                    continue;
                }

                points.Add(CreateCoordinate(firstValue, secondValue, srsName));
            }

            return new LineString(points);
        }

        private static LineString ReadLineStringFromPosElements(List<XElement> posElements, string srsName)
        {
            var points = new List<Coordinate2D>();

            foreach (var posElement in posElements)
            {
                if (!TryParseCoordinatePair(posElement.Value, out var firstValue, out var secondValue))
                {
                    continue;
                }

                points.Add(CreateCoordinate(firstValue, secondValue, srsName));
            }

            return new LineString(points);
        }

        private static Coordinate2D CreateCoordinate(double firstValue, double secondValue, string srsName)
        {
            if (ShouldSwapAxisOrder(firstValue, secondValue, srsName))
            {
                return new Coordinate2D(secondValue, firstValue);
            }

            return new Coordinate2D(firstValue, secondValue);
        }

        private static bool ShouldSwapAxisOrder(double firstValue, double secondValue, string srsName)
        {
            if (!string.IsNullOrWhiteSpace(srsName) &&
                srsName.Contains("EPSG", StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(firstValue) >= 1_000_000 &&
                Math.Abs(secondValue) < 1_000_000)
            {
                return true;
            }

            return false;
        }

        private static int GetCoordinateDimension(XElement posListElement)
        {
            var srsDimensionValue = posListElement
                .Attributes()
                .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "srsDimension", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return int.TryParse(srsDimensionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dimension) && dimension >= 2
                ? dimension
                : 2;
        }

        private static bool TryParseCoordinatePair(string text, out double firstValue, out double secondValue)
        {
            firstValue = 0;
            secondValue = 0;

            var rawValues = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (rawValues.Length < 2)
            {
                return false;
            }

            return double.TryParse(rawValues[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out firstValue) &&
                   double.TryParse(rawValues[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out secondValue);
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

        private static XElement? FindGeometryPropertyElement(XElement featureElement)
        {
            return featureElement.Elements().FirstOrDefault(element => FindGeometryRootElement(element) is not null);
        }

        private static XElement? FindGeometryRootElement(XElement containerElement)
        {
            return containerElement
                .DescendantsAndSelf()
                .FirstOrDefault(element => IsSupportedGeometryElement(element.Name.LocalName));
        }

        private static bool IsFeatureMemberElement(XElement element)
        {
            return string.Equals(element.Name.LocalName, "member", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(element.Name.LocalName, "featureMember", StringComparison.OrdinalIgnoreCase);
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
                ? "The WFS service returned an XML exception instead of feature data."
                : exceptionText;
        }

        private static bool IsSupportedGeometryElement(string localName)
        {
            return string.Equals(localName, "Polygon", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "Surface", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "PolygonPatch", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "MultiPolygon", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "MultiSurface", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "LineString", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "MultiLineString", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "Curve", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "MultiCurve", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "Point", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(localName, "MultiPoint", StringComparison.OrdinalIgnoreCase);
        }
    }
}
