using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RhinoSpatial.Core;

public record OgcFeatureCollection(string Id, string Title, string ItemsUrl, BoundingBox2D? Wgs84Bounds);
public record OgcFeatureResult(List<WfsFeature> Features, string StatusNote);

public sealed class OgcApiFeaturesClient
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly ConcurrentDictionary<string, (DateTime Expires, IReadOnlyList<OgcFeatureCollection>? Collections)> DiscoveryCache = new();
    private readonly HttpClient _http;

    static OgcApiFeaturesClient()
    {
        SharedHttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RhinoSpatial", "1.0"));
        SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/geo+json"));
    }

    public OgcApiFeaturesClient() : this(null) { }
    public OgcApiFeaturesClient(HttpClient? httpClient) => _http = httpClient ?? SharedHttpClient;

    public async Task<IReadOnlyList<OgcFeatureCollection>?> TryDiscoverAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source) || source.Scheme is not ("http" or "https")) return null;
        if (System.Text.RegularExpressions.Regex.IsMatch(sourceUrl, @"[?&]service=(?:wfs|wms|wcs)(?:&|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return null;
        if (ReferenceEquals(_http, SharedHttpClient) && DiscoveryCache.TryGetValue(sourceUrl, out var cached) && cached.Expires > DateTime.UtcNow)
            return cached.Collections;
        if (source.AbsolutePath.TrimEnd('/').EndsWith("/items", StringComparison.OrdinalIgnoreCase))
            return new[] { new OgcFeatureCollection(CollectionName(source), CollectionName(source), sourceUrl, null) };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var queue = new Queue<string>(); queue.Enqueue(sourceUrl);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var collections = new Dictionary<string, OgcFeatureCollection>(StringComparer.Ordinal);
        var recognized = false;
        while (queue.TryDequeue(out var url))
        {
            if (!visited.Add(url)) continue;
            if (visited.Count > 32) throw new InvalidDataException("OGC API discovery exceeded 32 metadata pages.");
            (string Text, Uri Url, string MediaType) response;
            try { response = await SourceHttp.ReadAsync(_http, url, timeout.Token).ConfigureAwait(false); }
            catch (HttpRequestException) when (!recognized && !LooksLikeOgcApiFeaturesUrl(sourceUrl)) { return Store(null); }
            if (!response.Text.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                if (recognized) throw new InvalidDataException("An OGC API metadata link did not return JSON.");
                return Store(null);
            }
            using var document = JsonDocument.Parse(response.Text);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "FeatureCollection")
            {
                if (!LooksLikeOgcApiFeaturesUrl(sourceUrl)) return Store(null);
                return Store(new[] { new OgcFeatureCollection(CollectionName(source), CollectionName(source), sourceUrl, null) });
            }
            if (root.TryGetProperty("collections", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                recognized = true;
                foreach (var collection in entries.EnumerateArray()) AddCollection(collection, response.Url, false);
                var next = Link(root, "next", response.Url);
                if (next is not null) queue.Enqueue(next);
            }
            else if (root.TryGetProperty("id", out _) &&
                     (Link(root, "items", response.Url) is not null || LooksLikeOgcApiFeaturesUrl(sourceUrl)))
            {
                recognized = true; AddCollection(root, response.Url, true);
            }
            else if (Link(root, "data", response.Url) is { } data)
            { recognized = true; queue.Enqueue(data); }
            else if (!recognized) return Store(null);
            if (collections.Count > 1000) throw new InvalidDataException("OGC API discovery exceeded 1,000 collections; use a specific collection URL.");
        }
        return Store(recognized ? collections.Values.ToList() : null);

        IReadOnlyList<OgcFeatureCollection>? Store(IReadOnlyList<OgcFeatureCollection>? value)
        {
            if (ReferenceEquals(_http, SharedHttpClient))
            {
                if (DiscoveryCache.Count > 128) DiscoveryCache.Clear();
                DiscoveryCache[sourceUrl] = (DateTime.UtcNow.AddMinutes(5), value);
            }
            return value;
        }

        void AddCollection(JsonElement value, Uri baseUrl, bool isSingle)
        {
            if (!value.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String) return;
            if (value.TryGetProperty("itemType", out var itemType) && itemType.GetString() != "feature") return;
            var id = idElement.GetString()!;
            var self = Link(value, "self", baseUrl);
            var items = Link(value, "items", baseUrl);
            if (items is null)
            {
                var builder = new UriBuilder(self ?? baseUrl.AbsoluteUri);
                if (self is null && !isSingle) builder.Path = builder.Path.TrimEnd('/') + "/" + Uri.EscapeDataString(id);
                builder.Path = builder.Path.TrimEnd('/') + "/items";
                items = builder.Uri.AbsoluteUri;
            }
            BoundingBox2D? bounds = null;
            if (value.TryGetProperty("extent", out var extent) && extent.TryGetProperty("spatial", out var spatial) &&
                (!spatial.TryGetProperty("crs", out var crs) || SpatialReferenceIdentifier.IsCrs84(crs.GetString())) &&
                spatial.TryGetProperty("bbox", out var boxes) && boxes.ValueKind == JsonValueKind.Array)
            {
                foreach (var box in boxes.EnumerateArray())
                {
                    if (box.ValueKind != JsonValueKind.Array || box.GetArrayLength() is not (4 or 6)) continue;
                    if (box.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number)) continue;
                    var n = box.GetArrayLength() / 2;
                    var b = new BoundingBox2D(box[0].GetDouble(), box[1].GetDouble(), box[n].GetDouble(), box[n + 1].GetDouble());
                    if (!double.IsFinite(b.MinX) || !double.IsFinite(b.MaxX) || !double.IsFinite(b.MinY) || !double.IsFinite(b.MaxY) ||
                        b.MinX < -180 || b.MinX > 180 || b.MaxX < -180 || b.MaxX > 180 || b.MinY < -90 || b.MaxY > 90 || b.MinY > b.MaxY) continue;
                    // A wrapped collection extent needs a conservative world-width map view.
                    if (b.MinX > b.MaxX) b = new BoundingBox2D(-180, b.MinY, 180, b.MaxY);
                    bounds = bounds is null ? b : new BoundingBox2D(Math.Min(bounds.MinX, b.MinX), Math.Min(bounds.MinY, b.MinY), Math.Max(bounds.MaxX, b.MaxX), Math.Max(bounds.MaxY, b.MaxY));
                }
            }
            collections[id] = new OgcFeatureCollection(id, value.TryGetProperty("title", out var title) ? title.GetString() ?? id : id, items, bounds);
        }
    }

    public async Task<List<WfsFeature>> LoadFeaturesAsync(string sourceUrl, string sourceLayerName, BoundingBox2D? wgs84BoundingBox, int maxFeatures) =>
        (await LoadFeaturesWithStatusAsync(sourceUrl, sourceLayerName, wgs84BoundingBox, maxFeatures).ConfigureAwait(false)).Features;

    public async Task<OgcFeatureResult> LoadFeaturesWithStatusAsync(string sourceUrl, string sourceLayerName,
        BoundingBox2D? wgs84BoundingBox, int maxFeatures, CancellationToken cancellationToken = default)
    {
        var collections = await TryDiscoverAsync(sourceUrl, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The URL is not an OGC API Features source.");
        var collection = collections.FirstOrDefault(c => c.Id == sourceLayerName) ??
            (collections.Count == 1 ? collections[0] : throw new InvalidOperationException("Select an OGC API collection using List WFS Layers and the Layer input."));
        var url = BuildItemsRequestUrl(collection.ItemsUrl, wgs84BoundingBox, maxFeatures);
        var result = await FeatureCollectionPager.LoadHttpAsync(url,
            async (pageUrl, token) => { var page = await SourceHttp.ReadAsync(_http, pageUrl, token).ConfigureAwait(false); return new FeaturePageContent(page.Text, page.Url); },
            maxFeatures, cancellationToken: cancellationToken).ConfigureAwait(false);
        var returnedSrs = GeoJsonReader.TryReadSourceSrs(result.Text);
        if (returnedSrs is not null && SpatialReferenceTransform.NormalizeSrs(returnedSrs) != "EPSG:4326")
            throw new InvalidDataException("The OGC API returned a non-default GeoJSON CRS despite a longitude/latitude request.");
        return new OgcFeatureResult(GeoJsonReader.ReadFeatures(result.Text, collection.Id), result.StatusNote);
    }

    public static bool LooksLikeOgcApiFeaturesUrl(string? sourceUrl) =>
        Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) &&
        (uri.AbsolutePath.Contains("/collections/", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.TrimEnd('/').EndsWith("/collections", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.Contains("/items", StringComparison.OrdinalIgnoreCase));

    private static string CollectionName(Uri uri)
    {
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(parts, p => p == "collections");
        return index >= 0 && index + 1 < parts.Length ? Uri.UnescapeDataString(parts[index + 1]) : "features";
    }

    private static string? Link(JsonElement owner, string relation, Uri source)
    {
        if (!owner.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array) return null;
        var matches = links.EnumerateArray().Where(l => l.TryGetProperty("rel", out var rel) && rel.GetString() == relation && l.TryGetProperty("href", out _))
            .OrderByDescending(l => l.TryGetProperty("type", out var type) && type.GetString()?.Contains("json", StringComparison.OrdinalIgnoreCase) == true);
        foreach (var link in matches)
        {
            var result = new Uri(source, link.GetProperty("href").GetString()!);
            if (result.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(result.UserInfo)) throw new InvalidDataException("Unsupported OGC API link URL.");
            return result.AbsoluteUri;
        }
        return null;
    }

    private static string BuildItemsRequestUrl(string sourceUrl, BoundingBox2D? bounds, int maxFeatures)
    {
        var builder = new UriBuilder(sourceUrl);
        var query = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length == 2 ? Uri.UnescapeDataString(p[1]) : "", StringComparer.OrdinalIgnoreCase);
        var pageSize = query.TryGetValue("limit", out var limit) && int.TryParse(limit, out var n) && n > 0 ? Math.Min(n, FeatureCollectionPager.PageSize) : FeatureCollectionPager.PageSize;
        query["f"] = "json";
        query["limit"] = (maxFeatures > 0 ? Math.Min(maxFeatures, pageSize) : pageSize).ToString(CultureInfo.InvariantCulture);
        // This client requests GeoJSON in its default longitude/latitude CRS.
        query.Remove("crs"); query.Remove("bbox-crs");
        if (bounds is not null) query["bbox"] = string.Join(",", new[] { bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY }.Select(x => x.ToString("G17", CultureInfo.InvariantCulture)));
        builder.Query = string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return builder.Uri.AbsoluteUri;
    }
}
