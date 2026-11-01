using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace RhinoSpatial.Core;

public record Lod2AtomSelection(IReadOnlyList<Lod2AtomLink> Tiles, int FeedCount, int DownloadCount);
public record Lod2AtomCachedTile(Lod2AtomLink Tile, string FilePath, bool UsedCache);

public class AtomDownloadClient
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly ConcurrentDictionary<string, (DateTime Expires, XDocument Document, Uri Url)> FeedCache = new(StringComparer.Ordinal);
    public static string DefaultCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RhinoSpatial", "Lod2AtomCache");
    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private const long MaximumDownloadBytes = 512L * 1024 * 1024;
    private const long MaximumExpandedBytes = 1024L * 1024 * 1024;

    private readonly AtomDownloadKind _kind;
    private readonly SourceDownloadCache _downloads;

    public AtomDownloadClient(HttpClient? httpClient = null, string? cacheDirectory = null, AtomDownloadKind kind = AtomDownloadKind.Buildings)
    {
        _http = httpClient ?? SharedHttpClient;
        _kind = kind;
        _cacheDirectory = cacheDirectory ?? (kind == AtomDownloadKind.Buildings ? DefaultCacheDirectory : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RhinoSpatial", "TerrainAtomCache"));
        _downloads = new SourceDownloadCache(kind, _http, _cacheDirectory);
    }

    public async Task<Lod2AtomSelection?> TrySelectAsync(string source, SpatialContext2D context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { return await TrySelectCoreAsync(source, context, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("ATOM tile discovery timed out after two minutes. Select a more specific dataset feed or try again later."); }
    }

    private async Task<Lod2AtomSelection?> TrySelectCoreAsync(string source, SpatialContext2D context, CancellationToken cancellationToken)
    {
        var initial = await LoadFeedAsync(new Uri(source), allowNonAtom: true, cancellationToken).ConfigureAwait(false);
        if (initial is null)
            return null;
        var queue = new Queue<(XDocument Document, Uri Url, AtomSpatialExtent? Extent, int Depth)>();
        queue.Enqueue((initial.Value.Document, initial.Value.Url, null, 0));
        var visited = new HashSet<string>(StringComparer.Ordinal) { initial.Value.Url.AbsoluteUri };
        var tiles = new Dictionary<string, Lod2AtomLink>(StringComparer.Ordinal);
        var downloadCount = 0;
        var feedCount = 0;
        var missing = new List<string>();
        var contextExtent = new AtomSpatialExtent(context.ResolvedSrs, context.RequestBoundingBox);
        var contextBounds = new Dictionary<string, BoundingBox2D>(StringComparer.OrdinalIgnoreCase);
        bool Intersects(AtomSpatialExtent extent)
        {
            if (!contextBounds.TryGetValue(extent.SrsName, out var bounds))
            {
                bounds = Lod2AtomFeedReader.TransformBounds(contextExtent, extent.SrsName);
                contextBounds[extent.SrsName] = bounds;
            }
            return Lod2AtomFeedReader.Intersects(extent.Bounds, bounds);
        }

        while (queue.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++feedCount > 64 || current.Depth > 8)
                throw new InvalidOperationException("ATOM feed traversal limit reached. Select a more specific dataset feed.");
            var feed = Lod2AtomFeedReader.Read(current.Document, current.Url, current.Extent, _kind);
            downloadCount += feed.Downloads.Count;
            if (downloadCount > 50000)
                throw new InvalidOperationException("The ATOM catalogue exceeds the supported download-entry limit.");
            foreach (var tile in feed.Downloads)
            {
                if (tile.Extent is null)
                {
                    if (missing.Count < 5) missing.Add(tile.Title);
                    continue;
                }
                if (!Intersects(tile.Extent)) continue;
                tiles.TryAdd(tile.Url.AbsoluteUri, tile);
            }
            foreach (var child in feed.Feeds)
            {
                if (child.Extent is not null && !Intersects(child.Extent)) continue;
                if (!visited.Add(child.Url.AbsoluteUri)) continue;
                if (visited.Count > 64 || current.Depth >= 8)
                    throw new InvalidOperationException("ATOM feed traversal limit reached. Select a more specific dataset feed.");
                var loaded = await LoadFeedAsync(child.Url, allowNonAtom: false, cancellationToken).ConfigureAwait(false);
                visited.Add(loaded!.Value.Url.AbsoluteUri);
                queue.Enqueue((loaded.Value.Document, loaded.Value.Url, child.Extent, current.Depth + 1));
            }
        }
        if (missing.Count > 0)
            throw new InvalidOperationException($"The ATOM feed does not provide sufficient per-tile spatial metadata to select downloads safely. Example entries: {string.Join(", ", missing)}. No data archives were downloaded. Use a spatially indexed feed or local tiles.");
        if (tiles.Count > 32)
            throw new InvalidOperationException($"The area selects {tiles.Count} ATOM tiles; the limit is 32 per solution. Select a smaller study area.");
        return new Lod2AtomSelection(tiles.Values.OrderBy(tile => tile.Url.AbsoluteUri, StringComparer.Ordinal).ToList(), feedCount, downloadCount);
    }

    private async Task<(XDocument Document, Uri Url)?> LoadFeedAsync(Uri url, bool allowNonAtom, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(_http, SharedHttpClient) && FeedCache.TryGetValue(url.AbsoluteUri, out var cached) && cached.Expires > DateTime.UtcNow)
            return (cached.Document, cached.Url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var actualUrl = response.RequestMessage?.RequestUri ?? url;
            var declaredAtom = response.Content.Headers.ContentType?.MediaType?.Equals("application/atom+xml", StringComparison.OrdinalIgnoreCase) == true;
            if (!response.IsSuccessStatusCode && allowNonAtom && !declaredAtom &&
                !url.AbsolutePath.TrimEnd('/').EndsWith("atom", StringComparison.OrdinalIgnoreCase))
                return null; // Some WFS roots require GetCapabilities query parameters.
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 16 * 1024 * 1024, IgnoreComments = true
            });
            try
            {
                await reader.MoveToContentAsync().ConfigureAwait(false);
                if (reader.LocalName != "feed" || reader.NamespaceURI != Lod2AtomFeedReader.Atom.NamespaceName)
                {
                    if (allowNonAtom && !declaredAtom) return null;
                    throw new InvalidOperationException($"Expected an ATOM feed at '{url}'.");
                }
            }
            catch (XmlException) when (allowNonAtom && !declaredAtom)
            {
                return null;
            }
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, timeout.Token).ConfigureAwait(false);
            if (ReferenceEquals(_http, SharedHttpClient))
            {
                if (FeedCache.Count > 128) FeedCache.Clear();
                FeedCache[url.AbsoluteUri] = (DateTime.UtcNow.AddMinutes(5), document, actualUrl);
            }
            return (document, actualUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out reading the ATOM feed '{url}'. Try again when the service is available.");
        }
    }

    public async Task<IReadOnlyList<Lod2AtomCachedTile>> DownloadAsync(Lod2AtomSelection selection,
        Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { return await DownloadCoreAsync(selection, progress, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("ATOM downloads exceeded five minutes. Completed tiles remain cached; reduce the study area or retry."); }
    }

    private async Task<IReadOnlyList<Lod2AtomCachedTile>> DownloadCoreAsync(Lod2AtomSelection selection,
        Action<string>? progress, CancellationToken cancellationToken)
    {
        var result = new List<Lod2AtomCachedTile>();
        foreach (var tile in selection.Tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke($"ATOM tile {result.Count + 1}/{selection.Tiles.Count}: {Path.GetFileName(tile.Url.LocalPath)}");
            try
            {
                var prefix = $"ATOM {result.Count + 1}/{selection.Tiles.Count}";
                var cachedTile = await _downloads.DownloadAsync(tile, note => progress?.Invoke($"{prefix}: {note}"), cancellationToken).ConfigureAwait(false);
                result.Add(cachedTile);
                progress?.Invoke($"{prefix}: {(cachedTile.UsedCache ? "reused cache" : "download complete")}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Could not load ATOM tile '{tile.Title}': {ex.Message} Completed {result.Count}/{selection.Tiles.Count} tile(s); successful downloads remain cached. No partial source result was returned.", ex);
            }
        }
        return result;
    }

    public Task<Lod2AtomCachedTile> DownloadFileAsync(Lod2AtomLink tile, CancellationToken cancellationToken = default) =>
        _downloads.DownloadAsync(tile, cancellationToken: cancellationToken);

    public async Task<ReferenceSourceMetadata?> TryReadReferenceAsync(string source, CancellationToken cancellationToken = default)
    {
        var feed = await LoadFeedAsync(new Uri(source), true, cancellationToken).ConfigureAwait(false);
        if (feed is null) return null;
        var root = feed.Value.Document.Root!;
        var extents = root.Elements(Lod2AtomFeedReader.Atom + "entry").Prepend(root)
            .Select(Lod2AtomFeedReader.ReadExtent).Where(e => e is not null).Cast<AtomSpatialExtent>().ToList();
        if (extents.Count == 0) throw new InvalidDataException("The ATOM feed has no readable coverage extent; use its service-level feed or select an area manually.");
        var boxes = extents.Select(e => Lod2AtomFeedReader.TransformBounds(e, "EPSG:4326")).ToList();
        var wgs = new BoundingBox2D(boxes.Min(b => b.MinX), boxes.Min(b => b.MinY), boxes.Max(b => b.MaxX), boxes.Max(b => b.MaxY));
        var codes = root.Elements(Lod2AtomFeedReader.Atom + "entry").Prepend(root)
            .Select(Lod2AtomFeedReader.ReadCategorySrs).Where(s => s is not null).Distinct().ToList();
        var srs = codes.Count == 1 ? codes[0]! : "EPSG:4326";
        var native = Lod2AtomFeedReader.TransformBounds(new AtomSpatialExtent("EPSG:4326", wgs), srs);
        return new ReferenceSourceMetadata("ATOM", srs, native, wgs, extents.Count);
    }
}

// Keep the original building client API available for existing callers.
public sealed class Lod2AtomClient : AtomDownloadClient
{
    public Lod2AtomClient(HttpClient? httpClient = null, string? cacheDirectory = null) : base(httpClient, cacheDirectory) { }
}
