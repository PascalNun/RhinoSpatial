using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace RhinoSpatial
{
    internal static class IconLoader
    {
        private static readonly Dictionary<string, Bitmap?> Cache = new();

        public static Bitmap? Load(string resourceName)
        {
            if (Cache.TryGetValue(resourceName, out var cachedBitmap))
            {
                return cachedBitmap;
            }

            using Stream? resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);

            if (resourceStream is null)
            {
                Cache[resourceName] = null;
                return null;
            }

            using var bitmap = new Bitmap(resourceStream);
            var clonedBitmap = new Bitmap(bitmap);
            Cache[resourceName] = clonedBitmap;

            return clonedBitmap;
        }
    }
}
