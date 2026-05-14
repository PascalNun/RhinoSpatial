using System.Collections.Generic;
using System.IO;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal enum TerrainSourceKind
    {
        Wcs,
        LocalGeoTiffDem,
        GlobalSkadiTiles
    }

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
        string DisplayName,
        TerrainSourceKind Kind = TerrainSourceKind.Wcs)
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
        private const string DefaultWmsUrl = "https://ows.terrestris.de/osm/service";
        private const string DefaultWmsLayer = "OSM-WMS";
        private const string BackupWmsUrl = "https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi";
        private const string BackupWmsLayer = "BlueMarble_ShadedRelief_Bathymetry";
        private const string GlobalTerrainTilesUrl = "https://s3.amazonaws.com/elevation-tiles-prod/skadi";
        private const string GlobalTerrainCoverageId = "Mapzen Skadi HGT";

        public static ResolvedImagerySource ResolveImagerySource(string? baseUrl, string? layerName)
        {
            return ResolveImagerySources(baseUrl, layerName)[0];
        }

        public static IReadOnlyList<ResolvedImagerySource> ResolveImagerySources(string? baseUrl, string? layerName)
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                return new[]
                {
                    new ResolvedImagerySource(
                        baseUrl.Trim(),
                        string.IsNullOrWhiteSpace(layerName) ? null : layerName.Trim(),
                        RhinoSpatialSourceTier.UserProvided,
                        "user-provided imagery source")
                };
            }

            return new[]
            {
                new ResolvedImagerySource(
                    DefaultWmsUrl,
                    string.IsNullOrWhiteSpace(layerName) ? DefaultWmsLayer : layerName.Trim(),
                    RhinoSpatialSourceTier.BuiltInGlobalFallback,
                    "terrestris OpenStreetMap WMS",
                    "EPSG:3857"),
                new ResolvedImagerySource(
                    BackupWmsUrl,
                    string.IsNullOrWhiteSpace(layerName) ? BackupWmsLayer : layerName.Trim(),
                    RhinoSpatialSourceTier.BuiltInGlobalFallback,
                    "NASA GIBS global imagery",
                    "EPSG:4326")
            };
        }

        public static ResolvedTerrainSource ResolveTerrainSource(string? serviceUrl, string? coverageId)
        {
            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                return new ResolvedTerrainSource(
                    serviceUrl.Trim(),
                    string.IsNullOrWhiteSpace(coverageId) ? string.Empty : coverageId.Trim(),
                    RhinoSpatialSourceTier.UserProvided,
                    IsLocalGeoTiffTerrainSource(serviceUrl)
                        ? "user-provided local GeoTIFF DEM"
                        : "user-provided terrain source",
                    IsLocalGeoTiffTerrainSource(serviceUrl)
                        ? TerrainSourceKind.LocalGeoTiffDem
                        : TerrainSourceKind.Wcs);
            }

            return new ResolvedTerrainSource(
                GlobalTerrainTilesUrl,
                string.IsNullOrWhiteSpace(coverageId) ? GlobalTerrainCoverageId : coverageId.Trim(),
                RhinoSpatialSourceTier.BuiltInGlobalFallback,
                "Mapzen/AWS Skadi global elevation tiles",
                TerrainSourceKind.GlobalSkadiTiles);
        }

        public static IReadOnlyList<ResolvedTerrainSource> ResolveTerrainSources(string? serviceUrl, string? coverageId)
        {
            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                return new[]
                {
                    ResolveTerrainSource(serviceUrl, coverageId)
                };
            }

            return new[]
            {
                new ResolvedTerrainSource(
                    GlobalTerrainTilesUrl,
                    GlobalTerrainCoverageId,
                    RhinoSpatialSourceTier.BuiltInGlobalFallback,
                    "Mapzen/AWS Skadi global elevation tiles",
                    TerrainSourceKind.GlobalSkadiTiles)
            };
        }

        private static bool IsLocalGeoTiffTerrainSource(string source)
        {
            var trimmedSource = source.Trim();
            if (!File.Exists(trimmedSource))
            {
                return false;
            }

            var extension = Path.GetExtension(trimmedSource);
            return extension.Equals(".tif", System.StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".tiff", System.StringComparison.OrdinalIgnoreCase);
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
