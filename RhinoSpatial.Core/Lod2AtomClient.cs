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

public sealed class Lod2AtomClient
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly ConcurrentDictionary<string, (DateTime Expires, XDocument Document, Uri Url)> FeedCache = new(StringComparer.Ordinal);
    public static string DefaultCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RhinoSpatial", "Lod2AtomCache");
    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private const long MaximumDownloadBytes = 512L * 1024 * 1024;
    private const long MaximumExpandedBytes = 1024L * 1024 * 1024;

    public Lod2AtomClient(HttpClient? httpClient = null, string? cacheDirectory = null)
    {
        _http = httpClient ?? SharedHttpClient;
        _cacheDirectory = cacheDirectory ?? DefaultCacheDirectory;
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
            var feed = Lod2AtomFeedReader.Read(current.Document, current.Url, current.Extent);
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
            throw new InvalidOperationException($"The ATOM feed does not provide sufficient per-tile spatial metadata to select downloads safely. Example entries: {string.Join(", ", missing)}. No building archives were downloaded. Use a spatially indexed feed or local tiles.");
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
                var cachedTile = await DownloadTileAsync(tile, note => progress?.Invoke($"{prefix}: {note}"), cancellationToken).ConfigureAwait(false);
                result.Add(cachedTile);
                progress?.Invoke($"{prefix}: {(cachedTile.UsedCache ? "reused cache" : "download complete")}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Could not load ATOM tile '{tile.Title}': {ex.Message} Completed {result.Count}/{selection.Tiles.Count} tile(s); successful downloads remain cached. No partial building result was returned.", ex);
            }
        }
        return result;
    }

    private sealed record CacheStamp(long Length, string Hash, DateTime DownloadedUtc);

    private async Task<Lod2AtomCachedTile> DownloadTileAsync(Lod2AtomLink tile, Action<string> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tile.Url.AbsoluteUri + "\n" + tile.Revision)));
        var target = Path.Combine(_cacheDirectory, key + ".zip");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            // A filesystem lock also serializes writers from separate Rhino processes.
            await using var cacheLock = await AcquireLockAsync(target + ".lock", timeout.Token).ConfigureAwait(false);
            if (await IsValidCacheAsync(target, tile.Revision, timeout.Token).ConfigureAwait(false))
                return new Lod2AtomCachedTile(tile, target, true);
            using var response = await _http.GetAsync(tile.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var expectedLength = response.Content.Headers.ContentLength;
            if (expectedLength > MaximumDownloadBytes)
                throw new InvalidOperationException("Archive exceeds the 512 MiB download limit.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long count = 0;
                long lastProgressBytes = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    count += read;
                    if (count > MaximumDownloadBytes) throw new InvalidOperationException("Archive exceeds the 512 MiB download limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    if (count - lastProgressBytes >= 1024 * 1024)
                    {
                        progress($"downloaded {count / (1024 * 1024)} MiB" + (expectedLength.HasValue ? $" / {expectedLength.Value / (1024 * 1024)} MiB" : ""));
                        lastProgressBytes = count;
                    }
                }
                if (expectedLength.HasValue && count != expectedLength.Value)
                    throw new InvalidDataException("The archive download was incomplete.");
            }
            await ValidateArchiveAsync(temporary, timeout.Token).ConfigureAwait(false);
            await using var validated = File.OpenRead(temporary);
            var stamp = new CacheStamp(validated.Length, Convert.ToHexString(await SHA256.HashDataAsync(validated, timeout.Token).ConfigureAwait(false)), DateTime.UtcNow);
            validated.Close();
            File.Move(temporary, target, overwrite: true);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(stamp), timeout.Token).ConfigureAwait(false);
            File.Move(temporary, target + ".json", overwrite: true);
            return new Lod2AtomCachedTile(tile, target, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The ATOM archive download timed out after three minutes.");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(150, cancellationToken).ConfigureAwait(false); }
        }
    }

    private static async Task<bool> IsValidCacheAsync(string path, string revision, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path) || !File.Exists(path + ".json")) return false;
            var stamp = JsonSerializer.Deserialize<CacheStamp>(await File.ReadAllTextAsync(path + ".json", cancellationToken).ConfigureAwait(false));
            if (stamp is null || new FileInfo(path).Length != stamp.Length ||
                (string.IsNullOrWhiteSpace(revision) && DateTime.UtcNow - stamp.DownloadedUtc > TimeSpan.FromDays(1))) return false;
            await using var stream = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)) == stamp.Hash;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { return false; }
    }

    private static async Task ValidateArchiveAsync(string path, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 10000) throw new InvalidDataException("ZIP contains too many entries.");
        long expandedBytes = 0;
        var buildingFiles = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > 256L * 1024 * 1024 || (expandedBytes += entry.Length) > MaximumExpandedBytes)
                throw new InvalidDataException("ZIP exceeds the supported expanded-size limit (256 MiB per file, 1 GiB total).");
            var extension = Path.GetExtension(entry.FullName).ToLowerInvariant();
            if (extension is not (".gml" or ".citygml" or ".xml")) continue;
            await using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 256L * 1024 * 1024
            });
            await reader.MoveToContentAsync().ConfigureAwait(false);
            if (reader.LocalName == "CityModel" && reader.NamespaceURI.StartsWith("http://www.opengis.net/citygml", StringComparison.Ordinal))
            {
                buildingFiles++;
                // Verify XML and decompression through EOF before publishing a completed cache file.
                while (await reader.ReadAsync().ConfigureAwait(false)) cancellationToken.ThrowIfCancellationRequested();
            }
        }
        if (buildingFiles == 0)
            throw new InvalidDataException("Unsupported or invalid archive: expected a ZIP containing CityGML building data. Other archive formats and remote CityJSON downloads are not supported.");
    }
}
