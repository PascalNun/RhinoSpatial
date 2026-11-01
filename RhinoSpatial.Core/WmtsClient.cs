using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RhinoSpatial.Core;

public record WmtsMatrix(string Id, double Resolution, Coordinate2D TopLeft, int TileWidth, int TileHeight, int Width, int Height);
public record WmtsMatrixSet(string Id, string Srs, List<WmtsMatrix> Matrices);
public record WmtsLimits(int MinRow, int MaxRow, int MinColumn, int MaxColumn);
public record WmtsLink(string Set, Dictionary<string, WmtsLimits> Limits);
public record WmtsLayer(string Id, string Title, string Style, List<string> Formats, List<WmtsLink> Links, Dictionary<string, string> Dimensions, Dictionary<string, string> Templates, BoundingBox2D? Wgs84Bounds);
public record WmtsCapabilities(List<WmtsLayer> Layers, Dictionary<string, WmtsMatrixSet> Sets, string TileUrl);
public record WmtsTile(int Row, int Column, string Url);
public record WmtsPlan(WmtsLayer Layer, WmtsMatrixSet Set, WmtsMatrix Matrix, List<WmtsTile> Tiles, int MinRow, int MinColumn, int Width, int Height, RasterGeoTransform Transform, bool CoverageClipped);
public record WmtsImageResult(string LocalFilePath, GeoReferencedRasterInfo Raster, WmtsPlan Plan, bool UsedCache, string Status);

public sealed class WmtsClient
{
    private static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly ConcurrentDictionary<string, (DateTime Expires, WmtsCapabilities? Value)> Cache = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private readonly HttpClient _http; private readonly string _directory;
    public WmtsClient(HttpClient? http = null, string? cacheDirectory = null)
    { _http = http ?? SharedHttp; _directory = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RhinoSpatial", "WmtsCache"); }
    public static bool LooksLike(string source) => source.Contains("wmts", StringComparison.OrdinalIgnoreCase);
    public async Task<WmtsCapabilities?> TryLoadCapabilitiesAsync(string source, CancellationToken token = default)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(source, @"[?&]service=(?:wms|wfs|wcs)(?:&|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return null;
        if (ReferenceEquals(_http, SharedHttp) && Cache.TryGetValue(source, out var cached) && cached.Expires > DateTime.UtcNow) return cached.Value;
        var uri = new Uri(source); var query = Query(uri.Query);
        var url = source;
        if (!uri.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            url = WithQuery(source, new() { { "SERVICE", "WMTS" }, { "REQUEST", "GetCapabilities" }, { "VERSION", "1.0.0" } });
        WmtsCapabilities? result;
        try
        {
            var response = await SourceHttp.ReadAsync(_http, url, token).ConfigureAwait(false);
            using var reader = XmlReader.Create(new StringReader(response.Text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var doc = XDocument.Load(reader);
            if (doc.Root?.Name.NamespaceName != "http://www.opengis.net/wmts/1.0")
            { if (LooksLike(source)) throw new InvalidDataException("The source did not return WMTS capabilities."); result = null; }
            else result = ReadCapabilities(doc, response.Url, query);
        }
        catch (Exception ex) when (!LooksLike(source) && ex is HttpRequestException or XmlException) { result = null; }
        if (ReferenceEquals(_http, SharedHttp)) { if (Cache.Count > 128) Cache.Clear(); Cache[source] = (DateTime.UtcNow.AddMinutes(5), result); }
        return result;
    }
    public static WmtsCapabilities ReadCapabilities(XDocument doc, Uri responseUrl, Dictionary<string, string>? overrides = null)
    {
        XNamespace w = "http://www.opengis.net/wmts/1.0", o = "http://www.opengis.net/ows/1.1", x = "http://www.w3.org/1999/xlink";
        var contents = doc.Root?.Element(w + "Contents") ?? throw new InvalidDataException("WMTS capabilities has no Contents.");
        var sets = new Dictionary<string, WmtsMatrixSet>(StringComparer.Ordinal);
        foreach (var set in contents.Elements(w + "TileMatrixSet"))
        {
            var id = Text(set, o + "Identifier"); var declared = Text(set, o + "SupportedCRS");
            var srs = declared.Contains("CRS84", StringComparison.OrdinalIgnoreCase) || declared.Contains("CRS:84", StringComparison.OrdinalIgnoreCase) ? "EPSG:4326" : SpatialReferenceTransform.NormalizeSrs(declared);
            if (!SpatialReferenceTransform.TryTransformXY(srs, "EPSG:4326", 0, 0, out _, out _)) continue;
            var swap = OgcUrlUtilities.UsesLatitudeLongitudeAxisOrder(declared);
            var degrees = srs is "EPSG:4326" or "EPSG:4258" or "EPSG:4283" or "EPSG:7844" or "EPSG:7423";
            var matrices = new List<WmtsMatrix>();
            foreach (var m in set.Elements(w + "TileMatrix"))
            {
                var top = Numbers(Text(m, w + "TopLeftCorner")); if (top.Length != 2) throw new InvalidDataException("Invalid WMTS top-left coordinate.");
                var resolution = Double(Text(m, w + "ScaleDenominator")) * 0.00028 / (degrees ? 111319.49079327358 : 1);
                var tw = Positive(Text(m, w + "TileWidth")); var th = Positive(Text(m, w + "TileHeight"));
                if (resolution <= 0 || !double.IsFinite(resolution) || tw > 2048 || th > 2048) throw new InvalidDataException("Unsupported WMTS matrix resolution/tile size.");
                matrices.Add(new(Text(m, o + "Identifier"), resolution, new(swap ? top[1] : top[0], swap ? top[0] : top[1]), tw, th, Positive(Text(m, w + "MatrixWidth")), Positive(Text(m, w + "MatrixHeight"))));
                if (matrices.Count > 128) throw new InvalidDataException("WMTS exceeds 128 matrices per set.");
            }
            sets.Add(id, new(id, srs, matrices)); if (sets.Count > 128) throw new InvalidDataException("WMTS exceeds 128 matrix sets.");
        }
        var layers = new List<WmtsLayer>();
        foreach (var l in contents.Elements(w + "Layer"))
        {
            var links = new List<WmtsLink>();
            foreach (var link in l.Elements(w + "TileMatrixSetLink"))
            {
                var limits = new Dictionary<string, WmtsLimits>(StringComparer.Ordinal);
                foreach (var limit in link.Element(w + "TileMatrixSetLimits")?.Elements(w + "TileMatrixLimits") ?? Enumerable.Empty<XElement>())
                {
                    var minR = Nonnegative(Text(limit, w + "MinTileRow")); var maxR = Nonnegative(Text(limit, w + "MaxTileRow")); var minC = Nonnegative(Text(limit, w + "MinTileCol")); var maxC = Nonnegative(Text(limit, w + "MaxTileCol"));
                    if (minR > maxR || minC > maxC) throw new InvalidDataException("Invalid WMTS tile limits.");
                    limits.Add(Text(limit, w + "TileMatrix"), new(minR, maxR, minC, maxC));
                }
                links.Add(new(Text(link, w + "TileMatrixSet"), limits));
            }
            var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in l.Elements(w + "Dimension"))
            {
                var name = Text(d, o + "Identifier"); var values = d.Elements(w + "Value").Select(e => e.Value).ToList();
                var value = overrides is not null && overrides.TryGetValue(name, out var supplied) ? supplied : (string?)d.Element(w + "Default") ?? (values.Count == 1 ? values[0] : "");
                dimensions.Add(name, value);
            }
            var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in l.Elements(w + "ResourceURL").Where(e => (string?)e.Attribute("resourceType") == "tile"))
                templates[(string?)resource.Attribute("format") ?? ""] = Resolve(responseUrl, (string?)resource.Attribute("template") ?? "").Replace("%7B", "{", StringComparison.OrdinalIgnoreCase).Replace("%7D", "}", StringComparison.OrdinalIgnoreCase);
            var bbox = l.Element(o + "WGS84BoundingBox"); BoundingBox2D? bounds = null;
            if (bbox is not null) { var lo = Numbers(Text(bbox, o + "LowerCorner")); var hi = Numbers(Text(bbox, o + "UpperCorner")); if (lo.Length == 2 && hi.Length == 2) bounds = new(lo[0], lo[1], hi[0], hi[1]); }
            var style = l.Elements(w + "Style").FirstOrDefault(e => (string?)e.Attribute("isDefault") is "true" or "1") ?? l.Element(w + "Style");
            layers.Add(new(Text(l, o + "Identifier"), (string?)l.Element(o + "Title") ?? Text(l, o + "Identifier"), style is null ? "" : Text(style, o + "Identifier"), l.Elements(w + "Format").Select(e => e.Value).ToList(), links, dimensions, templates, bounds));
            if (layers.Count > 2000) throw new InvalidDataException("WMTS exceeds 2000 layers.");
        }
        var operation = doc.Descendants(o + "Operation").FirstOrDefault(e => (string?)e.Attribute("name") == "GetTile");
        var get = operation?.Descendants(o + "Get").FirstOrDefault(e => !e.Descendants(o + "Value").Any() || e.Descendants(o + "Value").Any(v => v.Value == "KVP"));
        var endpoint = (string?)get?.Attribute(x + "href");
        return new(layers, sets, string.IsNullOrWhiteSpace(endpoint) ? "" : OgcUrlUtilities.PreferSecureSameHostOperationUrl(responseUrl.AbsoluteUri, Resolve(responseUrl, endpoint)));
    }
    public static WmtsPlan Plan(WmtsCapabilities capabilities, string? layerName, SpatialContext2D context, string format = "image/png")
    {
        var layer = string.IsNullOrWhiteSpace(layerName) ? capabilities.Layers.FirstOrDefault() : capabilities.Layers.FirstOrDefault(l => l.Id == layerName);
        if (layer is null) throw new InvalidDataException("The requested WMTS layer was not found.");
        if (layer.Dimensions.Values.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("WMTS dimension has no unambiguous default; set it in the source URL query.");
        if (layer.Wgs84Bounds is { } coverage && coverage.MinX <= coverage.MaxX && coverage.MinY <= coverage.MaxY)
        {
            var requested = Lod2AtomFeedReader.TransformBounds(new(context.ResolvedSrs, context.PlacementBoundingBox), "EPSG:4326");
            if (requested.MinX > coverage.MaxX || requested.MaxX < coverage.MinX || requested.MinY > coverage.MaxY || requested.MaxY < coverage.MinY)
                throw new InvalidDataException("WMTS layer declares no coverage for the selected Spatial Context.");
        }
        var selectedFormat = layer.Formats.FirstOrDefault(f => f == format && f is "image/png" or "image/jpeg") ?? layer.Formats.FirstOrDefault(f => f is "image/png" or "image/jpeg") ?? throw new InvalidDataException("WMTS layer has no supported PNG/JPEG format.");
        foreach (var link in layer.Links.OrderByDescending(l => capabilities.Sets.TryGetValue(l.Set, out var s) && s.Srs == context.ResolvedSrs))
        {
            if (!capabilities.Sets.TryGetValue(link.Set, out var set)) continue;
            var bounds = Lod2AtomFeedReader.TransformBounds(new(context.ResolvedSrs, context.PlacementBoundingBox), set.Srs);
            var desired = Math.Max(bounds.MaxX - bounds.MinX, bounds.MaxY - bounds.MinY) / 1024;
            if (desired <= 0) throw new InvalidDataException("WMTS requires a nonempty selected area.");
            foreach (var m in set.Matrices.OrderBy(m => Math.Abs(Math.Log(m.Resolution / desired))))
            {
                if (link.Limits.Count > 0 && !link.Limits.ContainsKey(m.Id)) continue;
                var sx = m.Resolution * m.TileWidth; var sy = m.Resolution * m.TileHeight;
                var c0 = Math.Floor((bounds.MinX - m.TopLeft.X) / sx); var c1 = Math.Ceiling((bounds.MaxX - m.TopLeft.X) / sx) - 1;
                var r0 = Math.Floor((m.TopLeft.Y - bounds.MaxY) / sy); var r1 = Math.Ceiling((m.TopLeft.Y - bounds.MinY) / sy) - 1;
                var limits = link.Limits.GetValueOrDefault(m.Id) ?? new(0, m.Height - 1, 0, m.Width - 1);
                var loC = Math.Max(Math.Max(c0, 0), limits.MinColumn); var hiC = Math.Min(Math.Min(c1, m.Width - 1), limits.MaxColumn);
                var loR = Math.Max(Math.Max(r0, 0), limits.MinRow); var hiR = Math.Min(Math.Min(r1, m.Height - 1), limits.MaxRow);
                if (hiC < loC || hiR < loR) continue;
                var minC = (int)loC; var maxC = (int)hiC; var minR = (int)loR; var maxR = (int)hiR;
                var count = (long)(maxC - minC + 1) * (maxR - minR + 1); var width = (long)(maxC - minC + 1) * m.TileWidth; var height = (long)(maxR - minR + 1) * m.TileHeight;
                if (count > 64 || width * height > 4194304 || width > 8192 || height > 8192) continue;
                var tiles = new List<WmtsTile>();
                for (var r = minR; r <= maxR; r++) for (var c = minC; c <= maxC; c++)
                    tiles.Add(new(r, c, TileUrl(capabilities, layer, set, m, r, c, selectedFormat)));
                return new(layer, set, m, tiles, minR, minC, (int)width, (int)height, new(m.Resolution, 0, m.TopLeft.X + minC * sx, 0, -m.Resolution, m.TopLeft.Y - minR * sy), c0 < minC || c1 > maxC || r0 < minR || r1 > maxR);
            }
        }
        throw new InvalidDataException("No supported WMTS matrix covers this area within 64 tiles/four million pixels; reduce the area or select another layer.");
    }
    public static string TileUrl(WmtsCapabilities caps, WmtsLayer layer, WmtsMatrixSet set, WmtsMatrix matrix, int row, int column, string format)
    {
        var values = new Dictionary<string, string>(layer.Dimensions, StringComparer.OrdinalIgnoreCase) { { "Layer", layer.Id }, { "Style", layer.Style }, { "TileMatrixSet", set.Id }, { "TileMatrix", matrix.Id }, { "TileRow", row.ToString(CultureInfo.InvariantCulture) }, { "TileCol", column.ToString(CultureInfo.InvariantCulture) }, { "Format", format } };
        if (layer.Templates.TryGetValue(format, out var template))
        {
            foreach (var v in values) template = template.Replace("{" + v.Key + "}", Uri.EscapeDataString(v.Value), StringComparison.OrdinalIgnoreCase);
            if (template.Contains('{')) throw new InvalidDataException("WMTS tile template has an unresolved dimension.");
            return template;
        }
        if (string.IsNullOrWhiteSpace(caps.TileUrl)) throw new InvalidDataException("WMTS advertises neither a usable REST tile template nor KVP GetTile endpoint.");
        values["SERVICE"] = "WMTS"; values["REQUEST"] = "GetTile"; values["VERSION"] = "1.0.0";
        return WithQuery(caps.TileUrl, values);
    }
    public async Task<WmtsImageResult> DownloadAsync(WmtsPlan plan, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { plan.Transform, plan.Width, plan.Height, plan.Set.Srs, Urls = plan.Tiles.Select(t => t.Url) }))));
        Directory.CreateDirectory(_directory); var target = Path.Combine(_directory, key + ".png"); var gate = Gates.GetOrAdd(target, _ => new(1, 1));
        await gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            bool cached = false;
            if (File.Exists(target) && DateTime.UtcNow - File.GetLastWriteTimeUtc(target) < TimeSpan.FromDays(1))
                try { using var cachedStream = File.OpenRead(target); var info = StbImageNext.ImageInfo.FromStream(cachedStream); cached = info.Width == plan.Width && info.Height == plan.Height && File.Exists(target + ".sha256") && File.ReadAllText(target + ".sha256") == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))); } catch { }
            if (!cached)
            {
                var rgba = new byte[checked(plan.Width * plan.Height * 4)]; long total = 0;
                foreach (var tile in plan.Tiles)
                {
                    using var response = await _http.GetAsync(tile.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false); response.EnsureSuccessStatusCode();
                    await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false); using var bytes = new MemoryStream(); var buffer = new byte[65536]; int n;
                    while ((n = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                    { total += n; if (bytes.Length + n > 4 * 1024 * 1024 || total > 64L * 1024 * 1024) throw new InvalidDataException("WMTS transfer exceeds tile/batch byte limits."); bytes.Write(buffer, 0, n); }
                    var pixels = RasterImageCodec.DecodeRgba(bytes.ToArray(), plan.Matrix.TileWidth, plan.Matrix.TileHeight);
                    for (var row = 0; row < plan.Matrix.TileHeight; row++) Buffer.BlockCopy(pixels, row * plan.Matrix.TileWidth * 4, rgba, ((tile.Row - plan.MinRow) * plan.Matrix.TileHeight * plan.Width + (tile.Column - plan.MinColumn) * plan.Matrix.TileWidth + row * plan.Width) * 4, plan.Matrix.TileWidth * 4);
                }
                timeout.Token.ThrowIfCancellationRequested(); var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { RasterImageCodec.WritePng(temporary, plan.Width, plan.Height, rgba); timeout.Token.ThrowIfCancellationRequested(); var checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(temporary))); File.Move(temporary, target, true); File.WriteAllText(target + ".sha256", checksum); } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            var raster = new GeoReferencedRasterInfo(target, plan.Set.Srs, plan.Width, plan.Height, plan.Transform.Bounds(plan.Width, plan.Height), new FileInfo(target).Length) { GeoTransform = plan.Transform };
            return new(target, raster, plan, cached, $"{(cached ? "Using cached" : "Loaded")} WMTS '{plan.Layer.Id}', matrix set '{plan.Set.Id}', matrix '{plan.Matrix.Id}', {plan.Set.Srs}, resolution {plan.Matrix.Resolution.ToString("G6", CultureInfo.InvariantCulture)}, {plan.Tiles.Count} tiles, {plan.Width}x{plan.Height}." + (plan.CoverageClipped ? " Declared matrix coverage clips the selected area." : ""));
        }
        finally { gate.Release(); }
    }
    private static string Text(XElement e, XName name) => (string?)e.Element(name) ?? throw new InvalidDataException("Missing WMTS " + name.LocalName);
    private static double Double(string s) { var d = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); if (!double.IsFinite(d)) throw new InvalidDataException("Nonfinite WMTS coordinate."); return d; }
    private static double[] Numbers(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Double).ToArray();
    private static int Positive(string s) { var n = Nonnegative(s); return n > 0 ? n : throw new InvalidDataException("WMTS size must be positive."); }
    private static int Nonnegative(string s) { var n = int.Parse(s, CultureInfo.InvariantCulture); return n >= 0 ? n : throw new InvalidDataException("WMTS limit must be nonnegative."); }
    private static string Resolve(Uri basis, string href) { var uri = new Uri(basis, href); if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) throw new InvalidDataException("WMTS links require HTTP(S) without embedded credentials."); return uri.AbsoluteUri; }
    private static Dictionary<string, string> Query(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).GroupBy(p => Uri.UnescapeDataString(p[0]), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().Length > 1 ? Uri.UnescapeDataString(g.Last()[1]) : "", StringComparer.OrdinalIgnoreCase);
    private static string WithQuery(string url, Dictionary<string, string> values) { var uri = new UriBuilder(url); var query = Query(uri.Query); foreach (var pair in values) query[pair.Key] = pair.Value; uri.Query = string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))); return uri.Uri.AbsoluteUri; }
}
