using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace RhinoSpatial.Core
{
    public static class WcsCapabilitiesReader
    {
        public static WcsCapabilitiesInfo ReadCapabilities(string capabilitiesXml)
        {
            var document = XDocument.Parse(capabilitiesXml);
            var serviceVersion = document.Root?
                .Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "version")
                ?.Value
                ?.Trim() ?? string.Empty;

            var coverages = new List<WcsCoverageInfo>();

            foreach (var coverageSummary in document.Descendants().Where(element => element.Name.LocalName == "CoverageSummary"))
            {
                var coverageId = GetChildValue(coverageSummary, "CoverageId");
                if (string.IsNullOrWhiteSpace(coverageId))
                {
                    coverageId = GetChildValue(coverageSummary, "Identifier");
                }

                if (string.IsNullOrWhiteSpace(coverageId))
                {
                    continue;
                }

                var title = GetChildValue(coverageSummary, "Title");
                var wgs84BoundingBox = TryReadWgs84BoundingBox(coverageSummary);

                coverages.Add(new WcsCoverageInfo(
                    coverageId,
                    string.IsNullOrWhiteSpace(title) ? coverageId : title,
                    wgs84BoundingBox));
            }

            coverages = coverages
                .GroupBy(coverage => coverage.CoverageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(coverage => coverage.CoverageId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var getCoverageUrl = ReadOperationUrl(document, "GetCoverage");
            var describeCoverageUrl = ReadOperationUrl(document, "DescribeCoverage");

            return new WcsCapabilitiesInfo(coverages, getCoverageUrl, describeCoverageUrl, serviceVersion);
        }

        public static WcsCoverageDescription ReadCoverageDescription(string describeCoverageXml)
        {
            var document = XDocument.Parse(describeCoverageXml);

            var coverageId = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName is "CoverageId" or "Identifier")
                ?.Value
                ?.Trim() ?? string.Empty;

            var envelope = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Envelope");
            if (envelope is null)
            {
                throw new InvalidOperationException("WCS DescribeCoverage response did not contain an Envelope.");
            }

            var nativeSrs = envelope
                .Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName is "srsName" or "srsName")
                ?.Value
                ?.Trim() ?? string.Empty;

            var axisLabels = envelope
                .Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "axisLabels")
                ?.Value
                ?.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var axisXLabel = axisLabels.Length > 0 ? axisLabels[0] : "x";
            var axisYLabel = axisLabels.Length > 1 ? axisLabels[1] : "y";

            var lowerCorner = ParseCoordinatePair(GetChildValue(envelope, "lowerCorner"));
            var upperCorner = ParseCoordinatePair(GetChildValue(envelope, "upperCorner"));
            var nativeBoundingBox = new BoundingBox2D(lowerCorner.X, lowerCorner.Y, upperCorner.X, upperCorner.Y);

            var originElement = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "origin");
            var originPoint = originElement is null
                ? new Coordinate2D(lowerCorner.X, upperCorner.Y)
                : ParseCoordinatePair(GetChildValue(originElement, "pos"));

            var offsetVectors = document
                .Descendants()
                .Where(element => element.Name.LocalName == "offsetVector")
                .Select(element => ParseCoordinatePair(element.Value))
                .ToList();

            var offsetX = offsetVectors.Count > 0 ? offsetVectors[0] : new Coordinate2D(1, 0);
            var offsetY = offsetVectors.Count > 1 ? offsetVectors[1] : new Coordinate2D(0, -1);

            var noDataValue = TryReadNoDataValue(document);

            return new WcsCoverageDescription(
                coverageId,
                nativeSrs,
                nativeBoundingBox,
                originPoint,
                offsetX,
                offsetY,
                axisXLabel,
                axisYLabel,
                noDataValue);
        }

        private static string GetChildValue(XElement parentElement, string localName)
        {
            return parentElement
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == localName)
                ?.Value
                ?.Trim() ?? string.Empty;
        }

        private static Coordinate2D ParseCoordinatePair(string value)
        {
            var parts = value.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return new Coordinate2D(0, 0);
            }

            return new Coordinate2D(
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        private static BoundingBox2D? TryReadWgs84BoundingBox(XElement coverageSummary)
        {
            var wgs84BoundingBox = coverageSummary
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "WGS84BoundingBox");

            if (wgs84BoundingBox is null)
            {
                return null;
            }

            var lowerCorner = ParseCoordinatePair(GetChildValue(wgs84BoundingBox, "LowerCorner"));
            var upperCorner = ParseCoordinatePair(GetChildValue(wgs84BoundingBox, "UpperCorner"));

            return new BoundingBox2D(lowerCorner.X, lowerCorner.Y, upperCorner.X, upperCorner.Y);
        }

        private static string ReadOperationUrl(XDocument document, string operationName)
        {
            var operationElement = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Operation" &&
                                           string.Equals(element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "name")?.Value, operationName, StringComparison.OrdinalIgnoreCase));

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

            var getHref = getElement
                .Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "href")
                ?.Value
                ?.Trim();

            if (!string.IsNullOrWhiteSpace(getHref))
            {
                return getHref;
            }

            var onlineResource = getElement
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "OnlineResource");

            if (onlineResource is not null)
            {
                var hrefAttribute = onlineResource
                    .Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName == "href");

                if (!string.IsNullOrWhiteSpace(hrefAttribute?.Value))
                {
                    return hrefAttribute.Value.Trim();
                }
            }

            return string.Empty;
        }

        private static double? TryReadNoDataValue(XDocument document)
        {
            var noDataElement = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "nilValue" || element.Name.LocalName == "NoData" || element.Name.LocalName == "nodata");

            if (noDataElement is null)
            {
                return null;
            }

            if (double.TryParse(noDataElement.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return null;
        }
    }
}
