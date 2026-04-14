using System.Collections.Concurrent;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal static class SpatialElevationBaselineCache
    {
        private static readonly ConcurrentDictionary<string, double> BaselinesByContext = new();

        public static double ResolveOrStore(SpatialContext2D spatialContext, double candidateBaseline)
        {
            if (spatialContext.UseAbsoluteCoordinates)
            {
                return 0.0;
            }

            var contextKey = RhinoSpatialContextTools.CreateSpatialContextKey(spatialContext);
            return BaselinesByContext.GetOrAdd(contextKey, candidateBaseline);
        }

        public static bool TryGet(SpatialContext2D spatialContext, out double elevationBaseline)
        {
            elevationBaseline = 0.0;

            if (spatialContext.UseAbsoluteCoordinates)
            {
                return false;
            }

            var contextKey = RhinoSpatialContextTools.CreateSpatialContextKey(spatialContext);
            return BaselinesByContext.TryGetValue(contextKey, out elevationBaseline);
        }
    }
}
