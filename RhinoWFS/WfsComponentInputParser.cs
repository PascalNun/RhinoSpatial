using System;
using System.Globalization;
using System.Linq;
using WfsCore;

namespace RhinoWFS
{
    internal static class WfsComponentInputParser
    {
        public static string ParseLayerName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var parts = text.Split('|', 2, StringSplitOptions.TrimEntries);
            return parts.FirstOrDefault()?.Trim() ?? string.Empty;
        }

        public static bool TryParseBoundingBox(string? text, out BoundingBox2D? boundingBox, out string errorMessage)
        {
            boundingBox = null;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4)
            {
                errorMessage = "BBox must use the format minX,minY,maxX,maxY.";
                return false;
            }

            if (!TryParseDouble(parts[0], out var minX) ||
                !TryParseDouble(parts[1], out var minY) ||
                !TryParseDouble(parts[2], out var maxX) ||
                !TryParseDouble(parts[3], out var maxY))
            {
                errorMessage = "BBox values must be valid numbers using '.' as decimal separator.";
                return false;
            }

            if (minX > maxX || minY > maxY)
            {
                errorMessage = "BBox must satisfy minX <= maxX and minY <= maxY.";
                return false;
            }

            boundingBox = new BoundingBox2D(minX, minY, maxX, maxY);
            return true;
        }

        public static string FormatBoundingBox(BoundingBox2D boundingBox)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{boundingBox.MinX},{boundingBox.MinY},{boundingBox.MaxX},{boundingBox.MaxY}");
        }

        private static bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
        }
    }
}
