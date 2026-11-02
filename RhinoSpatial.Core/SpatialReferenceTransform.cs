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

        public delegate bool XYTransform(double x, double y, out double transformedX, out double transformedY);

        /// <summary>Prepare one transformation for a batch. Each operation owns its instance.</summary>
        public static bool TryCreateXYTransform(string sourceSrs, string targetSrs,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out XYTransform? transform)
        {
            transform = null;
            if (!TryCreateMathTransform(sourceSrs, targetSrs, out var math)) return false;
            transform = (double x, double y, out double tx, out double ty) => Transform(math, x, y, out tx, out ty);
            return true;
        }

        public static bool TryTransformXY(string sourceSrs, string targetSrs, double x, double y, out double transformedX, out double transformedY)
        {
            transformedX = x;
            transformedY = y;
            return double.IsFinite(x) && double.IsFinite(y) &&
                TryCreateMathTransform(sourceSrs, targetSrs, out var math) &&
                Transform(math, x, y, out transformedX, out transformedY);
        }

        public static bool IsSupportedSrs(string? srs) => CreateCoordinateSystem(NormalizeSrs(srs)) is not null;

        public static bool TryGetMetresPerUnit(string? srs, out double metresPerUnit)
        {
            metresPerUnit = 0;
            if (CreateCoordinateSystem(NormalizeSrs(srs)) is not ProjectedCoordinateSystem projected) return false;
            metresPerUnit = projected.LinearUnit.MetersPerUnit;
            return double.IsFinite(metresPerUnit) && metresPerUnit > 0;
        }

        private static bool TryCreateMathTransform(string sourceSrs, string targetSrs, out MathTransform? math)
        {
            math = null;
            var normalizedSource = NormalizeSrs(sourceSrs);
            var normalizedTarget = NormalizeSrs(targetSrs);
            var source = CreateCoordinateSystem(normalizedSource);
            var target = CreateCoordinateSystem(normalizedTarget);
            // Even an identity operation must have a supported, declared CRS.
            if (source is null || target is null) return false;
            if (normalizedSource != normalizedTarget)
                math = TransformationFactory.CreateFromCoordinateSystems(source, target).MathTransform;
            return true;
        }

        private static bool Transform(MathTransform? math, double x, double y, out double tx, out double ty)
        {
            tx = x;
            ty = y;
            if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
            if (math is not null)
            {
                try { (tx, ty) = math.Transform(x, y); }
                catch (Exception ex) when (ex is ArgumentException or ArithmeticException) { return false; }
            }
            return double.IsFinite(tx) && double.IsFinite(ty);
        }

        // Only the supported UTM zones enter this bounded cache. Mutable transforms stay per operation.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, CoordinateSystem> UtmSystems = new();
        private static CoordinateSystem Utm(int zone, bool north) => UtmSystems.GetOrAdd(
            zone + (north ? 0 : 60), key => ProjectedCoordinateSystem.WGS84_UTM(key > 60 ? key - 60 : key, key <= 60));

        private static CoordinateSystem? CreateCoordinateSystem(string normalizedSrs)
        {
            if (normalizedSrs.StartsWith("EPSG:", StringComparison.Ordinal) &&
                int.TryParse(normalizedSrs.AsSpan(5), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var epsg))
            {
                if (epsg is >= 32601 and <= 32660) return Utm(epsg - 32600, true);
                if (epsg is >= 32701 and <= 32760) return Utm(epsg - 32700, false);
            }
            return normalizedSrs switch
            {
                "EPSG:4326" => GeographicCoordinateSystem.WGS84,
                "EPSG:4258" => GeographicCoordinateSystem.WGS84,
                "EPSG:7415" => AmersfoortRdNew,
                "EPSG:7423" => GeographicCoordinateSystem.WGS84,
                "EPSG:4283" => GeographicCoordinateSystem.WGS84,
                "EPSG:7844" => GeographicCoordinateSystem.WGS84,
                "EPSG:3857" => ProjectedCoordinateSystem.WebMercator,
                "EPSG:25832" => Utm(32, true),
                "EPSG:25833" => Utm(33, true),
                "EPSG:28992" => AmersfoortRdNew,
                "EPSG:27700" => BritishNationalGrid,
                _ => null
            };
        }

        public static string NormalizeSrs(string? srs) => SpatialReferenceIdentifier.Normalize(srs);
    }
}
