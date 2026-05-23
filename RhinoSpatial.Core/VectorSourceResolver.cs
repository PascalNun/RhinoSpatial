using System;
using System.IO;
using System.Linq;

namespace RhinoSpatial.Core
{
    public enum VectorSourceKind
    {
        Unsupported,
        Wfs,
        OgcApiFeatures,
        LocalShapefile,
        LocalGeoJson
    }

    public record VectorSourceInfo(
        string Source,
        VectorSourceKind Kind,
        string DefaultLayerName)
    {
        public bool IsSupported => Kind != VectorSourceKind.Unsupported;

        public bool CanUseDefaultLayerName =>
            Kind is VectorSourceKind.OgcApiFeatures or VectorSourceKind.LocalShapefile or VectorSourceKind.LocalGeoJson;
    }

    public static class VectorSourceResolver
    {
        public static VectorSourceInfo Resolve(string? source)
        {
            var trimmed = source?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return new VectorSourceInfo(string.Empty, VectorSourceKind.Unsupported, string.Empty);
            }

            if (IsLocalFile(trimmed, ".shp"))
            {
                return new VectorSourceInfo(
                    trimmed,
                    VectorSourceKind.LocalShapefile,
                    Path.GetFileNameWithoutExtension(trimmed));
            }

            if (IsLocalFile(trimmed, ".geojson", ".json"))
            {
                return new VectorSourceInfo(
                    trimmed,
                    VectorSourceKind.LocalGeoJson,
                    Path.GetFileNameWithoutExtension(trimmed));
            }

            if (OgcApiFeaturesClient.LooksLikeOgcApiFeaturesUrl(trimmed))
            {
                return new VectorSourceInfo(
                    trimmed,
                    VectorSourceKind.OgcApiFeatures,
                    ResolveOgcApiFeaturesLayerName(trimmed));
            }

            if (IsHttpUrl(trimmed))
            {
                return new VectorSourceInfo(trimmed, VectorSourceKind.Wfs, string.Empty);
            }

            return new VectorSourceInfo(trimmed, VectorSourceKind.Unsupported, string.Empty);
        }

        private static bool IsLocalFile(string source, params string[] extensions)
        {
            return File.Exists(source) &&
                   extensions.Any(extension => Path.GetExtension(source).Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsHttpUrl(string source)
        {
            return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                   (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveOgcApiFeaturesLayerName(string sourceUrl)
        {
            if (!Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return "OGC API Features";
            }

            var pathParts = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            var collectionIndex = pathParts.FindIndex(part => string.Equals(part, "collections", StringComparison.OrdinalIgnoreCase));
            if (collectionIndex >= 0 && collectionIndex + 1 < pathParts.Count)
            {
                return Uri.UnescapeDataString(pathParts[collectionIndex + 1]);
            }

            return string.IsNullOrWhiteSpace(pathParts.LastOrDefault())
                ? "OGC API Features"
                : Uri.UnescapeDataString(pathParts.Last());
        }
    }
}
