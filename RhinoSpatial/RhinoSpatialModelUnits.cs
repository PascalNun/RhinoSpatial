using System;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal static class RhinoSpatialModelUnits
    {
        // Source requests, saved contexts and terrain caches stay in their declared CRS.
        // Convert copies only when publishing geometry to the active Rhino model.
        public static double OutputScale(SpatialContext2D context)
        {
            if (context.UseAbsoluteCoordinates ||
                !SpatialReferenceTransform.TryGetMetresPerUnit(context.ResolvedSrs, out var metresPerUnit)) return 1;
            var units = RhinoDoc.ActiveDoc?.ModelUnitSystem ?? UnitSystem.Meters;
            if (units is UnitSystem.None or UnitSystem.CustomUnits)
                throw new InvalidOperationException("Choose a standard Rhino model unit, such as metres, millimetres or feet, for localized project geometry.");
            var scale = metresPerUnit * RhinoMath.UnitScale(UnitSystem.Meters, units);
            if (!double.IsFinite(scale) || scale <= 0)
                throw new InvalidOperationException("The Rhino model units could not be converted from the project CRS.");
            return scale;
        }

        public static T? Geometry<T>(T? geometry, double scale) where T : GeometryBase
        {
            if (geometry is null || scale == 1) return geometry;
            var copy = (T)geometry.Duplicate();
            if (!copy.Transform(Transform.Scale(Point3d.Origin, scale)))
                throw new InvalidOperationException("Geometry could not be converted to the Rhino model units.");
            return copy;
        }

        public static GH_Structure<T> Tree<T>(GH_Structure<T> tree, double scale) where T : IGH_GeometricGoo
        {
            if (scale == 1) return tree;
            var output = new GH_Structure<T>();
            var transform = Transform.Scale(Point3d.Origin, scale);
            for (var i = 0; i < tree.PathCount; i++)
            {
                var path = tree.Paths[i];
                output.EnsurePath(path);
                foreach (var item in tree.Branches[i])
                {
                    var converted = item.Transform(transform);
                    if (converted is not T value)
                        throw new InvalidOperationException("Geometry could not be converted to the Rhino model units.");
                    output.Append(value, path);
                }
            }
            return output;
        }
    }
}
