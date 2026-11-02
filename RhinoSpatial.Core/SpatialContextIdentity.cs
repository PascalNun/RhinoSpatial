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

        private static string NormalizeSrsKey(string? srsName) => SpatialReferenceTransform.NormalizeSrs(srsName);
    }
}
