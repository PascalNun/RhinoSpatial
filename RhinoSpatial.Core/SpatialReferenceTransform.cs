using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace RhinoSpatial.Core
{
    public static class SpatialReferenceTransform
    {
        private static readonly CoordinateTransformationFactory TransformationFactory = new();

        public static bool TryTransformXY(string sourceSrs, string targetSrs, double x, double y, out double transformedX, out double transformedY)
        {
            transformedX = x;
            transformedY = y;

            var normalizedSource = NormalizeSrs(sourceSrs);
            var normalizedTarget = NormalizeSrs(targetSrs);

            if (string.IsNullOrWhiteSpace(normalizedSource) || string.IsNullOrWhiteSpace(normalizedTarget))
            {
                return false;
            }

            if (normalizedSource == normalizedTarget)
            {
                return true;
            }

            var source = CreateCoordinateSystem(normalizedSource);
            var target = CreateCoordinateSystem(normalizedTarget);

            if (source is null || target is null)
            {
                return false;
            }

            var transformed = TransformationFactory.CreateFromCoordinateSystems(source, target).MathTransform.Transform(x, y);
            transformedX = transformed.x;
            transformedY = transformed.y;
            return true;
        }

        private static CoordinateSystem? CreateCoordinateSystem(string normalizedSrs)
        {
            return normalizedSrs switch
            {
                "EPSG:4326" => GeographicCoordinateSystem.WGS84,
                "EPSG:7423" => GeographicCoordinateSystem.WGS84,
                "EPSG:4283" => GeographicCoordinateSystem.WGS84,
                "EPSG:7844" => GeographicCoordinateSystem.WGS84,
                "EPSG:3857" => ProjectedCoordinateSystem.WebMercator,
                "EPSG:25832" => ProjectedCoordinateSystem.WGS84_UTM(32, true),
                "EPSG:25833" => ProjectedCoordinateSystem.WGS84_UTM(33, true),
                _ => null
            };
        }

        private static string NormalizeSrs(string? srs)
        {
            if (string.IsNullOrWhiteSpace(srs))
            {
                return string.Empty;
            }

            var trimmed = srs.Trim();

            if (trimmed.Contains("4326", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4326";
            }

            if (trimmed.Contains("7423", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7423";
            }

            if (trimmed.Contains("25832", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25832";
            }

            if (trimmed.Contains("25833", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25833";
            }

            if (trimmed.Contains("3857", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:3857";
            }

            if (trimmed.Contains("27700", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:27700";
            }

            if (trimmed.Contains("4283", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4283";
            }

            if (trimmed.Contains("7844", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7844";
            }

            return trimmed.ToUpperInvariant();
        }
    }
}
