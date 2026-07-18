using System.Globalization;

namespace RhinoSpatial.Core
{
    public static class SpatialContextIdentity
    {
        public static string CreateKey(SpatialContext2D spatialContext)
        {
            var requestBoundingBox = spatialContext.RequestBoundingBox;
            var placementBoundingBox = spatialContext.PlacementBoundingBox;
            var placementOrigin = spatialContext.PlacementOrigin;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{NormalizeSrsKey(spatialContext.ResolvedSrs)}|{spatialContext.UseAbsoluteCoordinates}|{requestBoundingBox.MinX}|{requestBoundingBox.MinY}|{requestBoundingBox.MaxX}|{requestBoundingBox.MaxY}|{placementBoundingBox.MinX}|{placementBoundingBox.MinY}|{placementBoundingBox.MaxX}|{placementBoundingBox.MaxY}|{placementOrigin.X}|{placementOrigin.Y}");
        }

        private static string NormalizeSrsKey(string? srsName)
        {
            if (string.IsNullOrWhiteSpace(srsName))
            {
                return string.Empty;
            }

            if (srsName.Contains("25832", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25832";
            }

            if (srsName.Contains("25833", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25833";
            }

            if (srsName.Contains("27700", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:27700";
            }

            if (srsName.Contains("3857", StringComparison.OrdinalIgnoreCase) ||
                srsName.Contains("900913", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:3857";
            }

            if (srsName.Contains("4283", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4283";
            }

            if (srsName.Contains("7423", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7423";
            }

            if (srsName.Contains("7844", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7844";
            }

            if (srsName.Contains("4326", StringComparison.OrdinalIgnoreCase) ||
                srsName.Contains("CRS:84", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4326";
            }

            return srsName.Trim();
        }
    }
}
