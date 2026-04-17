using System;
using System.Collections.Concurrent;
using System.IO;

namespace RhinoSpatial.Core
{
    public static class GeoTiffInfoCache
    {
        private readonly record struct CacheKey(long FileSizeBytes, long LastWriteUtcTicks);

        private readonly record struct CacheEntry(CacheKey Key, GeoReferencedRasterInfo RasterInfo);

        private static readonly ConcurrentDictionary<string, CacheEntry> EntriesByPath = new(StringComparer.OrdinalIgnoreCase);

        public static GeoReferencedRasterInfo GetOrRead(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            var key = new CacheKey(fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks);

            if (EntriesByPath.TryGetValue(filePath, out var cachedEntry) && cachedEntry.Key.Equals(key))
            {
                return cachedEntry.RasterInfo;
            }

            var rasterInfo = GeoTiffReader.ReadImageInfo(filePath);
            EntriesByPath[filePath] = new CacheEntry(key, rasterInfo);
            return rasterInfo;
        }

        public static void Invalidate(string filePath)
        {
            EntriesByPath.TryRemove(filePath, out _);
        }
    }
}
