using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace RhinoSpatial.Core;

public enum AtomDownloadKind { Buildings, Terrain, Vector }

internal sealed class LimitedReadStream : Stream
{
    private readonly Stream inner;
    private readonly long maximum;
    public LimitedReadStream(Stream stream, long limit) { inner = stream; maximum = limit; }
    private long _read;
    private int Count(int count) { _read += count; if (_read > maximum) throw new InvalidDataException("ZIP expanded beyond its declared entry size."); return count; }
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => Count(await inner.ReadAsync(buffer, token).ConfigureAwait(false));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}

/// <summary>Validated, atomic payload cache shared by direct downloads and ATOM services.</summary>
public sealed class SourceDownloadCache
{
    private static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly string[] Extensions = { ".zip", ".geojson", ".cityjson", ".jsonl", ".gml", ".tif", ".asc", ".csv" };
    private readonly HttpClient _http;
    private readonly string _directory;
    private readonly AtomDownloadKind _kind;
    private sealed record Stamp(long Length, string Hash, DateTime DownloadedUtc);
    public SourceDownloadCache(AtomDownloadKind kind, HttpClient? http = null, string? directory = null)
    {
        _kind = kind; _http = http ?? SharedHttp;
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhinoSpatial", kind == AtomDownloadKind.Buildings ? "Lod2AtomCache" : kind == AtomDownloadKind.Terrain ? "TerrainAtomCache" : "VectorDownloadCache");
    }

    public async Task<Lod2AtomCachedTile> DownloadAsync(Lod2AtomLink tile, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tile.Url.AbsoluteUri + "\n" + tile.Revision)));
        var stem = Path.Combine(_directory, key);
        var temporary = stem + "." + Guid.NewGuid().ToString("N") + ".partial";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await using var cacheLock = await LockAsync(stem + ".lock", timeout.Token).ConfigureAwait(false);
            foreach (var suffix in Extensions)
                if (await ValidCacheAsync(stem + suffix, tile.Revision, timeout.Token).ConfigureAwait(false))
                    return new Lod2AtomCachedTile(tile, stem + suffix, true);
            using var response = await _http.GetAsync(tile.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var maximum = _kind == AtomDownloadKind.Vector ? 128L * 1024 * 1024 : 512L * 1024 * 1024;
            var expected = response.Content.Headers.ContentLength;
            if (expected > maximum) throw new InvalidDataException("Download exceeds the supported payload size limit.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long count = 0; long previous = 0; int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    count += read;
                    if (count > maximum) throw new InvalidDataException("Download exceeds the supported payload size limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    if (count - previous > 1024 * 1024) { progress?.Invoke($"downloaded {count / (1024 * 1024)} MiB"); previous = count; }
                }
                if (expected.HasValue && count != expected) throw new InvalidDataException("The source download was incomplete.");
            }
            var extension = await InspectPayloadAsync(temporary, _kind, timeout.Token).ConfigureAwait(false);
            var target = stem + extension;
            Stamp stamp;
            await using (var input = File.OpenRead(temporary))
                stamp = new Stamp(input.Length, Convert.ToHexString(await SHA256.HashDataAsync(input, timeout.Token).ConfigureAwait(false)), DateTime.UtcNow);
            File.Move(temporary, target, true);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(stamp), timeout.Token).ConfigureAwait(false);
            File.Move(temporary, target + ".json", true);
            return new Lod2AtomCachedTile(tile, target, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("The source download timed out after three minutes."); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<FileStream> LockAsync(string path, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(150, token).ConfigureAwait(false); }
        }
    }
    private static async Task<bool> ValidCacheAsync(string path, string revision, CancellationToken token)
    {
        try
        {
            if (!File.Exists(path) || !File.Exists(path + ".json")) return false;
            var stamp = JsonSerializer.Deserialize<Stamp>(await File.ReadAllTextAsync(path + ".json", token).ConfigureAwait(false));
            if (stamp is null || new FileInfo(path).Length != stamp.Length ||
                (string.IsNullOrWhiteSpace(revision) && DateTime.UtcNow - stamp.DownloadedUtc > TimeSpan.FromDays(1))) return false;
            await using var input = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)) == stamp.Hash;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return false; }
    }

    private static async Task<string> InspectPayloadAsync(string path, AtomDownloadKind kind, CancellationToken token)
    {
        var header = new byte[512]; int length;
        await using (var input = File.OpenRead(path)) length = await input.ReadAsync(header, token).ConfigureAwait(false);
        if (length >= 4 && header[0] == 'P' && header[1] == 'K')
        {
            using var archive = ZipFile.OpenRead(path);
            ValidateEntries(archive);
            var supported = 0;
            var names = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(entry.FullName).ToLowerInvariant();
                if (kind == AtomDownloadKind.Vector && extension == ".shp")
                {
                    foreach (var companion in new[] { ".shx", ".dbf", ".prj" })
                        if (!names.Contains(Path.ChangeExtension(entry.FullName, companion))) throw new InvalidDataException("Shapefile ZIP is missing a matching " + companion + " companion file.");
                    supported++; continue;
                }
                var candidate = kind switch
                {
                    AtomDownloadKind.Buildings => extension is ".gml" or ".citygml" or ".xml" or ".json" or ".cityjson" or ".jsonl" or ".geojson",
                    AtomDownloadKind.Terrain => extension is ".tif" or ".tiff" or ".asc" or ".xyz" or ".csv",
                    _ => extension is ".json" or ".geojson"
                };
                if (!candidate) continue;
                await using var stream = new LimitedReadStream(entry.Open(), entry.Length);
                if (kind == AtomDownloadKind.Buildings && extension is ".gml" or ".citygml" or ".xml")
                { if (await IsBuildingXmlAsync(stream, token).ConfigureAwait(false)) supported++; }
                else if (extension is ".json" or ".geojson" or ".cityjson" or ".jsonl")
                { if (await IsSupportedJsonAsync(stream, kind, token).ConfigureAwait(false)) supported++; }
                else
                {
                    // Read through EOF to verify decompression before promoting a completed cache entry.
                    var buffer = new byte[81920]; while (await stream.ReadAsync(buffer, token).ConfigureAwait(false) > 0) { }
                    supported++;
                }
            }
            if (supported == 0) throw new InvalidDataException($"Archive contains no supported {kind} data. Expected a ZIP with CityGML/CityJSON buildings, terrain grids, or vector files for the selected loader.");
            return ".zip";
        }
        if (new FileInfo(path).Length > 256L * 1024 * 1024) throw new InvalidDataException("Direct data file exceeds 256 MiB; use a smaller dataset.");
        var text = Encoding.UTF8.GetString(header, 0, length).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        if (text.StartsWith("{", StringComparison.Ordinal))
        {
            await using var input = File.OpenRead(path);
            if (await IsSupportedJsonAsync(input, kind, token).ConfigureAwait(false))
                return kind == AtomDownloadKind.Buildings ? ".cityjson" : ".geojson";
        }
        if (kind == AtomDownloadKind.Buildings && text.StartsWith("<", StringComparison.Ordinal))
        {
            await using var input = File.OpenRead(path);
            if (await IsBuildingXmlAsync(input, token).ConfigureAwait(false)) return ".gml";
        }
        if (kind == AtomDownloadKind.Terrain)
        {
            if (length >= 4 && ((header[0] == 'I' && header[1] == 'I') || (header[0] == 'M' && header[1] == 'M')))
            { GeoTiffReader.ReadImageInfo(path); return ".tif"; }
            if (text.StartsWith("ncols", StringComparison.OrdinalIgnoreCase)) return ".asc";
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts.Take(3).All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _))) return ".csv";
            }
        }
        throw new InvalidDataException($"Unsupported {kind} download; its content does not match a supported data file or ZIP archive.");
    }

    private static async Task<bool> IsSupportedJsonAsync(Stream input, AtomDownloadKind kind, CancellationToken token)
    {
        if (kind == AtomDownloadKind.Buildings) return CityJsonReader.Validate(input, requireCrs: true, cancellationToken: token);
        using var document = await JsonDocument.ParseAsync(input, cancellationToken: token).ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return false;
        return kind == AtomDownloadKind.Vector && type.GetString() is "FeatureCollection" or "Feature";
    }
    private static async Task<bool> IsBuildingXmlAsync(Stream input, CancellationToken token)
    {
        using var reader = XmlReader.Create(input, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 256L * 1024 * 1024 });
        await reader.MoveToContentAsync().ConfigureAwait(false);
        if (reader.LocalName != "CityModel" || !reader.NamespaceURI.StartsWith("http://www.opengis.net/citygml", StringComparison.Ordinal)) return false;
        while (await reader.ReadAsync().ConfigureAwait(false)) token.ThrowIfCancellationRequested();
        return true;
    }
    internal static void ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > 10000) throw new InvalidDataException("ZIP contains too many entries.");
        long size = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p is ".." or ".") ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || !names.Add(name))
                throw new InvalidDataException("ZIP contains an unsafe, symbolic-link or duplicate entry path.");
            if (entry.Length > 256L * 1024 * 1024 || (size += entry.Length) > 1024L * 1024 * 1024)
                throw new InvalidDataException("ZIP exceeds the expanded-size limit (256 MiB per file, 1 GiB total).");
        }
    }
}

public sealed class SourceFilesLease : IDisposable
{
    private readonly string? _temporary;
    public IReadOnlyList<string> Files { get; }
    private SourceFilesLease(IReadOnlyList<string> files, string? temporary) { Files = files; _temporary = temporary; }
    public static SourceFilesLease Open(string path, CancellationToken cancellationToken = default)
    {
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return new SourceFilesLease(new[] { path }, null);
        var directory = Path.Combine(Path.GetTempPath(), "rhinospatial-source-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var archive = ZipFile.OpenRead(path); SourceDownloadCache.ValidateEntries(archive);
            Directory.CreateDirectory(directory);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var target = Path.Combine(directory, entry.FullName.Replace('\\', '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                cancellationToken.ThrowIfCancellationRequested();
                using var input = entry.Open(); using var output = File.Create(target);
                var buffer = new byte[81920]; long count = 0; int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested(); count += read;
                    if (count > entry.Length || count > 256L * 1024 * 1024) throw new InvalidDataException("ZIP expanded beyond its declared entry size.");
                    output.Write(buffer, 0, read);
                }
                if (count != entry.Length) throw new InvalidDataException("ZIP entry is truncated.");
            }
            var extracted = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            foreach (var shape in extracted.Where(f => Path.GetExtension(f).Equals(".shp", StringComparison.OrdinalIgnoreCase)))
                foreach (var extension in new[] { ".dbf", ".shx", ".prj", ".cpg" })
                {
                    var expected = Path.ChangeExtension(shape, extension);
                    var actual = extracted.FirstOrDefault(f => f.Equals(expected, StringComparison.OrdinalIgnoreCase));
                    if (actual is not null && actual != expected)
                    {
                        var intermediate = actual + ".normalize-" + Guid.NewGuid().ToString("N");
                        File.Move(actual, intermediate); File.Move(intermediate, expected);
                    }
                }
            return new SourceFilesLease(Directory.GetFiles(directory, "*", SearchOption.AllDirectories), directory);
        }
        catch { if (Directory.Exists(directory)) Directory.Delete(directory, true); throw; }
    }
    public void Dispose() { if (_temporary is not null && Directory.Exists(_temporary)) Directory.Delete(_temporary, true); }
}
