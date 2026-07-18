using System;
using System.Collections.Generic;
using System.Linq;

namespace RhinoSpatial
{
    internal static class Google3dTilesGeographicBounds
    {
        private const double FullCircleDegrees = 360.0;

        public static bool LongitudeRangesIntersect(
            double firstWest,
            double firstEast,
            double secondWest,
            double secondEast)
        {
            if (!TryCreateCircularInterval(firstWest, firstEast, out var firstStart, out var firstEnd) ||
                !TryCreateCircularInterval(secondWest, secondEast, out var secondStart, out var secondEnd))
            {
                return true;
            }

            if (firstEnd - firstStart >= FullCircleDegrees ||
                secondEnd - secondStart >= FullCircleDegrees)
            {
                return true;
            }

            for (var wrap = -1; wrap <= 1; wrap++)
            {
                var shiftedSecondStart = secondStart + (wrap * FullCircleDegrees);
                var shiftedSecondEnd = secondEnd + (wrap * FullCircleDegrees);
                if (firstStart <= shiftedSecondEnd && firstEnd >= shiftedSecondStart)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryCreateMinimalLongitudeArc(
            IReadOnlyCollection<double> longitudes,
            out double west,
            out double east)
        {
            west = 0.0;
            east = 0.0;
            var normalized = longitudes
                .Where(static longitude => !double.IsNaN(longitude) && !double.IsInfinity(longitude))
                .Select(NormalizeLongitude)
                .OrderBy(static longitude => longitude)
                .ToArray();
            if (normalized.Length == 0)
            {
                return false;
            }

            if (normalized.Length == 1)
            {
                west = normalized[0];
                east = normalized[0];
                return true;
            }

            var largestGap = double.NegativeInfinity;
            var largestGapStartIndex = 0;
            for (var index = 0; index < normalized.Length; index++)
            {
                var next = index == normalized.Length - 1
                    ? normalized[0] + FullCircleDegrees
                    : normalized[index + 1];
                var gap = next - normalized[index];
                if (gap > largestGap)
                {
                    largestGap = gap;
                    largestGapStartIndex = index;
                }
            }

            west = normalized[(largestGapStartIndex + 1) % normalized.Length];
            east = normalized[largestGapStartIndex];
            return true;
        }

        public static double NormalizeLongitude(double longitude)
        {
            var normalized = longitude % FullCircleDegrees;
            if (normalized < -180.0)
            {
                normalized += FullCircleDegrees;
            }
            else if (normalized >= 180.0)
            {
                normalized -= FullCircleDegrees;
            }

            return normalized;
        }

        private static bool TryCreateCircularInterval(
            double west,
            double east,
            out double start,
            out double end)
        {
            start = 0.0;
            end = 0.0;
            if (double.IsNaN(west) || double.IsInfinity(west) ||
                double.IsNaN(east) || double.IsInfinity(east))
            {
                return false;
            }

            var rawSpan = east - west;
            if (Math.Abs(rawSpan) >= FullCircleDegrees)
            {
                start = NormalizeLongitude(west);
                end = start + FullCircleDegrees;
                return true;
            }

            var span = rawSpan;
            while (span < 0.0)
            {
                span += FullCircleDegrees;
            }

            start = NormalizeLongitude(west);
            end = start + span;
            return true;
        }
    }
}
