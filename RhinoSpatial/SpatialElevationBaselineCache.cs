using System.Collections.Concurrent;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal static class SpatialElevationBaselineCache
    {
        private static readonly ConcurrentDictionary<string, double> BaselinesByContext = new();

        public static double ResolveOrStore(SpatialContext2D spatialContext, double candidateBaseline)
        {
            return ResolveOrStore(spatialContext, candidateBaseline, out _);
        }

        public static double ResolveOrStore(
            SpatialContext2D spatialContext,
            double candidateBaseline,
            out bool storedCandidate)
        {
            storedCandidate = false;
            if (spatialContext.UseAbsoluteCoordinates)
            {
                return 0.0;
            }

            var contextKey = SpatialContextIdentity.CreateKey(spatialContext);
            if (BaselinesByContext.TryGetValue(contextKey, out var existingBaseline))
            {
                return existingBaseline;
            }

            if (BaselinesByContext.TryAdd(contextKey, candidateBaseline))
            {
                storedCandidate = true;
                return candidateBaseline;
            }

            return BaselinesByContext[contextKey];
        }

        public static bool TryGet(SpatialContext2D spatialContext, out double elevationBaseline)
        {
            elevationBaseline = 0.0;

            if (spatialContext.UseAbsoluteCoordinates)
            {
                return false;
            }

            var contextKey = SpatialContextIdentity.CreateKey(spatialContext);
            return BaselinesByContext.TryGetValue(contextKey, out elevationBaseline);
        }

        internal static void Remove(SpatialContext2D spatialContext)
        {
            BaselinesByContext.TryRemove(SpatialContextIdentity.CreateKey(spatialContext), out _);
        }
    }
}
