using System;
using System.Collections.Generic;
using System.Linq;

namespace RhinoSpatial
{
    internal static class Google3dTilesElevationBaseline
    {
        private const double LowerElevationPercentile = 0.02;

        public static bool TryResolveCandidate(
            IEnumerable<double> elevations,
            out double elevationBaseline)
        {
            var orderedElevations = elevations
                .Where(static elevation => !double.IsNaN(elevation) && !double.IsInfinity(elevation))
                .OrderBy(static elevation => elevation)
                .ToList();
            if (orderedElevations.Count == 0)
            {
                elevationBaseline = 0.0;
                return false;
            }

            var percentileIndex = (int)Math.Floor((orderedElevations.Count - 1) * LowerElevationPercentile);
            elevationBaseline = orderedElevations[Math.Clamp(percentileIndex, 0, orderedElevations.Count - 1)];
            return true;
        }
    }
}
