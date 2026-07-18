using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using ProjNet.IO.CoordinateSystems;

namespace RhinoSpatial.Core
{
    public static class SpatialReferenceTransform
    {
        private static readonly CoordinateTransformationFactory TransformationFactory = new();
        private static readonly CoordinateSystem BritishNationalGrid = (CoordinateSystem)CoordinateSystemWktReader.Parse(
            """
            PROJCS["OSGB 1936 / British National Grid",
              GEOGCS["OSGB 1936",
                DATUM["OSGB_1936",
                  SPHEROID["Airy 1830",6377563.396,299.3249646],
                  TOWGS84[446.448,-125.157,542.06,0.1502,0.2470,0.8421,-20.4894]],
                PRIMEM["Greenwich",0],
                UNIT["degree",0.0174532925199433]],
              PROJECTION["Transverse_Mercator"],
              PARAMETER["latitude_of_origin",49],
              PARAMETER["central_meridian",-2],
              PARAMETER["scale_factor",0.9996012717],
              PARAMETER["false_easting",400000],
              PARAMETER["false_northing",-100000],
              UNIT["metre",1],
              AXIS["Easting",EAST],
              AXIS["Northing",NORTH],
              AUTHORITY["EPSG","27700"]]
            """);
        private static readonly CoordinateSystem AmersfoortRdNew = (CoordinateSystem)CoordinateSystemWktReader.Parse(
            """
            PROJCS["Amersfoort / RD New",
              GEOGCS["Amersfoort",
                DATUM["Amersfoort",
                  SPHEROID["Bessel 1841",6377397.155,299.1528128],
                  TOWGS84[565.4171,50.3319,465.5524,-0.398957,0.343988,-1.8774,4.0725]],
                PRIMEM["Greenwich",0],
                UNIT["degree",0.0174532925199433]],
              PROJECTION["Oblique_Stereographic"],
              PARAMETER["latitude_of_origin",52.15616055555555],
              PARAMETER["central_meridian",5.38763888888889],
              PARAMETER["scale_factor",0.9999079],
              PARAMETER["false_easting",155000],
              PARAMETER["false_northing",463000],
              UNIT["metre",1],
              AXIS["Easting",EAST],
              AXIS["Northing",NORTH],
              AUTHORITY["EPSG","28992"]]
            """);

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
                "EPSG:4258" => GeographicCoordinateSystem.WGS84,
                "EPSG:7415" => AmersfoortRdNew,
                "EPSG:7423" => GeographicCoordinateSystem.WGS84,
                "EPSG:4283" => GeographicCoordinateSystem.WGS84,
                "EPSG:7844" => GeographicCoordinateSystem.WGS84,
                "EPSG:3857" => ProjectedCoordinateSystem.WebMercator,
                "EPSG:25832" => ProjectedCoordinateSystem.WGS84_UTM(32, true),
                "EPSG:25833" => ProjectedCoordinateSystem.WGS84_UTM(33, true),
                "EPSG:28992" => AmersfoortRdNew,
                "EPSG:27700" => BritishNationalGrid,
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

            if (trimmed.Contains("4258", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4258";
            }

            if (trimmed.Contains("7423", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7423";
            }

            if (trimmed.Contains("7415", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7415";
            }

            if (trimmed.Contains("25832", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25832";
            }

            if (trimmed.Contains("25833", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25833";
            }

            if (trimmed.Contains("28992", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:28992";
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
