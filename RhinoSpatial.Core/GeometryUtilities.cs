using System.Collections.Generic;

namespace RhinoSpatial.Core
{
    public static class GeometryUtilities
    {
        public static bool IsClosedRing(IReadOnlyList<Coordinate2D> points)
        {
            if (points.Count < 2)
            {
                return false;
            }

            var first = points[0];
            var last = points[^1];

            return first.X == last.X && first.Y == last.Y;
        }
    }
}
