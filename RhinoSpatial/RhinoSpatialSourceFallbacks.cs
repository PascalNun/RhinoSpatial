using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal enum RhinoSpatialSourceTier
    {
        UserProvided,
        BuiltInOfficialExample,
        BuiltInGlobalFallback
    }

    internal sealed record ResolvedImagerySource(
        string BaseUrl,
        string? PreferredLayerName,
        RhinoSpatialSourceTier Tier,
        string DisplayName,
        string? RequiredQuerySrs = null)
    {
        public bool UsesFallback => Tier != RhinoSpatialSourceTier.UserProvided;

        public string CreateStatusPrefix()
        {
            return Tier switch
            {
                RhinoSpatialSourceTier.BuiltInGlobalFallback => $"Using fallback global imagery source ({DisplayName}). ",
                RhinoSpatialSourceTier.BuiltInOfficialExample => $"Using built-in imagery source ({DisplayName}). ",
                _ => string.Empty
            };
        }
    }

    internal sealed record ResolvedTerrainSource(
        string BaseUrl,
        string CoverageId,
        RhinoSpatialSourceTier Tier,
        string DisplayName)
    {
        public bool UsesFallback => Tier != RhinoSpatialSourceTier.UserProvided;

        public string CreateStatusPrefix()
        {
            return Tier switch
            {
                RhinoSpatialSourceTier.BuiltInGlobalFallback => $"Using fallback global terrain source ({DisplayName}). ",
                RhinoSpatialSourceTier.BuiltInOfficialExample => $"Using built-in terrain source ({DisplayName}). ",
                _ => string.Empty
            };
        }
    }

    internal static class RhinoSpatialSourceFallbacks
    {
        private const string DefaultWmsUrl = "https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi";
        private const string DefaultWmsLayer = "BlueMarble_ShadedRelief_Bathymetry";
        private const string DefaultTerrainServiceUrl = "https://inspire-hessen.de/raster/dgm1/ows";
        private const string DefaultTerrainCoverageId = "he_dgm1";

        public static ResolvedImagerySource ResolveImagerySource(string? baseUrl, string? layerName)
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                return new ResolvedImagerySource(
                    baseUrl.Trim(),
                    string.IsNullOrWhiteSpace(layerName) ? null : layerName.Trim(),
                    RhinoSpatialSourceTier.UserProvided,
                    "user-provided imagery source");
            }

            return new ResolvedImagerySource(
                DefaultWmsUrl,
                string.IsNullOrWhiteSpace(layerName) ? DefaultWmsLayer : layerName.Trim(),
                RhinoSpatialSourceTier.BuiltInGlobalFallback,
                "NASA GIBS low-resolution global imagery",
                "EPSG:4326");
        }

        public static ResolvedTerrainSource ResolveTerrainSource(string? serviceUrl, string? coverageId)
        {
            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                return new ResolvedTerrainSource(
                    serviceUrl.Trim(),
                    string.IsNullOrWhiteSpace(coverageId) ? DefaultTerrainCoverageId : coverageId.Trim(),
                    RhinoSpatialSourceTier.UserProvided,
                    "user-provided terrain source");
            }

            return new ResolvedTerrainSource(
                DefaultTerrainServiceUrl,
                string.IsNullOrWhiteSpace(coverageId) ? DefaultTerrainCoverageId : coverageId.Trim(),
                RhinoSpatialSourceTier.BuiltInOfficialExample,
                "Hessen DGM1 official example terrain source");
        }

        public static bool TryCreateRequestSpatialContext(
            SpatialContext2D spatialContext,
            string? requiredSrs,
            out SpatialContext2D requestSpatialContext,
            out string errorMessage)
        {
            requestSpatialContext = spatialContext;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(requiredSrs))
            {
                return true;
            }

            if (!RhinoSpatialContextTools.TryResolveBoundingBoxForSrs(spatialContext, requiredSrs, out var requestedBoundingBox, out _))
            {
                errorMessage = $"The Spatial Context could not provide a usable {requiredSrs} bounding box for the selected fallback source.";
                return false;
            }

            requestSpatialContext = new SpatialContext2D(
                requiredSrs,
                requestedBoundingBox,
                spatialContext.PlacementBoundingBox,
                spatialContext.Wgs84BoundingBox,
                spatialContext.PlacementOrigin,
                spatialContext.UseAbsoluteCoordinates,
                spatialContext.BoundingBoxesBySrs);

            return true;
        }
    }
}
