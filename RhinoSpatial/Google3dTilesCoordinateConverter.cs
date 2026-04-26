using System;

namespace RhinoSpatial
{
    internal static class Google3dTilesCoordinateConverter
    {
        private const double Wgs84SemiMajorAxis = 6378137.0;
        private const double Wgs84FirstEccentricitySquared = 6.69437999014e-3;
        private const double Wgs84SemiMinorAxis = 6356752.3142451793;
        private const double Wgs84SecondEccentricitySquared = 6.73949674228e-3;

        internal static bool TryConvertEcefToGeodetic(
            double x,
            double y,
            double z,
            out double latitudeDegrees,
            out double longitudeDegrees,
            out double height)
        {
            var p = Math.Sqrt((x * x) + (y * y));

            if (p < 1e-9 && Math.Abs(z) < 1e-9)
            {
                latitudeDegrees = 0.0;
                longitudeDegrees = 0.0;
                height = 0.0;
                return false;
            }

            var theta = Math.Atan2(z * Wgs84SemiMajorAxis, p * Wgs84SemiMinorAxis);
            var sinTheta = Math.Sin(theta);
            var cosTheta = Math.Cos(theta);
            var latitude = Math.Atan2(
                z + (Wgs84SecondEccentricitySquared * Wgs84SemiMinorAxis * sinTheta * sinTheta * sinTheta),
                p - (Wgs84FirstEccentricitySquared * Wgs84SemiMajorAxis * cosTheta * cosTheta * cosTheta));
            var longitude = Math.Atan2(y, x);

            var sinLatitude = Math.Sin(latitude);
            var radiusOfCurvature = Wgs84SemiMajorAxis / Math.Sqrt(1.0 - (Wgs84FirstEccentricitySquared * sinLatitude * sinLatitude));

            if (Math.Abs(Math.Cos(latitude)) > 1e-9)
            {
                height = (p / Math.Cos(latitude)) - radiusOfCurvature;
            }
            else
            {
                height = (z / Math.Sign(sinLatitude)) - (radiusOfCurvature * (1.0 - Wgs84FirstEccentricitySquared));
            }

            latitudeDegrees = latitude * (180.0 / Math.PI);
            longitudeDegrees = longitude * (180.0 / Math.PI);
            return true;
        }
    }
}
