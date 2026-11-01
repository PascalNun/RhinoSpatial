using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace RhinoSpatial.Core;

public record PagedFeatureResponse(string Text, string StatusNote, int PageCount);
public record FeaturePageContent(string Text, Uri Url);

/// <summary>Keeps source geometries intact while assembling bounded JSON or GML pages.</summary>
public static class FeatureCollectionPager
{
    public const int PageSize = 1000;
    public const int MaximumFeatures = 100000;
    public const int MaximumPages = 100;

    public static Task<PagedFeatureResponse> LoadAsync(string firstUrl,
        Func<string, CancellationToken, Task<string>> fetch, int maxFeatures,
        Func<int, int, string>? offsetUrl = null, CancellationToken cancellationToken = default) =>
        LoadHttpAsync(firstUrl, async (url, token) => new FeaturePageContent(await fetch(url, token).ConfigureAwait(false), new Uri(url)),
            maxFeatures, offsetUrl, cancellationToken);

    public static async Task<PagedFeatureResponse> LoadHttpAsync(string firstUrl,
        Func<string, CancellationToken, Task<FeaturePageContent>> fetch, int maxFeatures,
        Func<int, int, string>? offsetUrl = null, CancellationToken cancellationToken = default)
    {
        if (maxFeatures < 0) throw new ArgumentOutOfRangeException(nameof(maxFeatures));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var cap = maxFeatures > 0 ? Math.Min(maxFeatures, MaximumFeatures) : MaximumFeatures;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pageHashes = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var records = new List<string>();
        Page? first = null;
        var url = firstUrl;
        var pages = 0;
        var rawCount = 0;
        long bytes = 0;
        long? matchedTotal = null;
        var note = "";
        string? firstJsonSrs = null;
        var anonymous = false;
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (++pages > MaximumPages || !visited.Add(url))
                    throw new InvalidDataException("Feature pagination repeated a URL or exceeded 100 pages. No partial result was returned; reduce the area or set Max Features.");
                var response = await fetch(url, timeout.Token).ConfigureAwait(false);
                var body = response.Text;
                url = response.Url.AbsoluteUri;
                bytes += Encoding.UTF8.GetByteCount(body);
                if (bytes > 128L * 1024 * 1024)
                    throw new InvalidDataException("Feature responses exceeded 128 MiB. Reduce the area or set Max Features.");
                var page = Page.Read(body);
                first ??= page;
                if (matchedTotal.HasValue && page.Total.HasValue && matchedTotal != page.Total)
                    throw new InvalidDataException("The provider changed its matching feature total during pagination; retry the selection.");
                matchedTotal ??= page.Total;
                if (page.IsJson != first.IsJson)
                    throw new InvalidDataException("The provider changed feature encoding between pages.");
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", page.Records))));
                if (page.Records.Count > 0 && !pageHashes.Add(fingerprint))
                    throw new InvalidDataException("The provider repeated a feature page instead of advancing. No partial result was returned.");
                if (page.IsJson)
                {
                    var srs = SpatialReferenceTransform.NormalizeSrs(GeoJsonReader.TryReadSourceSrs(body) ?? "EPSG:4326");
                    if (firstJsonSrs is not null && firstJsonSrs != srs)
                        throw new InvalidDataException("The provider changed GeoJSON coordinate systems between pages.");
                    firstJsonSrs ??= srs;
                }
                var before = records.Count;
                for (var i = 0; i < page.Records.Count && records.Count < cap; i++)
                {
                    if (page.Keys[i].Length == 0) anonymous = true;
                    if (page.Keys[i].Length == 0 || ids.Add(page.Keys[i])) records.Add(page.Records[i]);
                }
                rawCount += page.Records.Count;
                if (matchedTotal.HasValue && records.Count > matchedTotal.Value)
                    throw new InvalidDataException("The provider returned more feature records than its advertised matching total; retry the selection.");
                var more = page.Next is not null || matchedTotal > rawCount ||
                    (offsetUrl is not null && matchedTotal is null && page.Records.Count >= PageSize);
                if (records.Count >= cap)
                {
                    if (maxFeatures <= 0 || maxFeatures > MaximumFeatures)
                    {
                        if (more || page.Records.Count > records.Count - before)
                            throw new InvalidDataException("Feature selection reached the 100,000-feature safety limit. Reduce the area or set Max Features.");
                    }
                    note = maxFeatures > 0 && maxFeatures <= MaximumFeatures
                        ? $"Stopped at Max Features ({maxFeatures}); further matching features may exist." : "";
                    break;
                }
                if (page.Records.Count == 0)
                {
                    if (page.Next is not null || matchedTotal > rawCount)
                        throw new InvalidDataException("The provider returned an empty page before completing its advertised feature result.");
                    break;
                }
                if (records.Count == before)
                    throw new InvalidDataException("Feature pagination made no progress; all returned IDs were duplicates.");
                if (!more)
                {
                    if (matchedTotal > records.Count)
                        throw new InvalidDataException("The provider ended paging before returning its advertised unique feature total.");
                    if (matchedTotal is null) note = "Provider supplied no matching total; paging ended at its last advertised page.";
                    break;
                }
                if (page.Next is not null)
                {
                    var next = new Uri(new Uri(url), page.Next);
                    if (next.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(next.UserInfo))
                        throw new InvalidDataException("Feature pagination requires an HTTP(S) next link without embedded credentials.");
                    url = next.AbsoluteUri;
                }
                else if (offsetUrl is not null)
                    url = offsetUrl(rawCount, Math.Min(PageSize, cap - records.Count));
                else
                    throw new InvalidDataException("The provider advertised more features but supplied no supported paging mechanism. No partial result was returned.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Feature pagination timed out after two minutes. Reduce the selected area or set Max Features."); }
        var identityNote = anonymous ? " Features without stable IDs are retained; identity-based deduplication is unavailable for those records." : "";
        return new PagedFeatureResponse(first!.Merge(records), $"Read {records.Count} {(anonymous ? "" : "unique ")}feature(s) in {pages} page(s). {note}{identityNote}".Trim(), pages);
    }

    private sealed record Page(bool IsJson, string Original, List<string> Records, List<string> Keys, string? Next, long? Total)
    {
        public static Page Read(string body)
        {
            var records = new List<string>();
            var keys = new List<string>();
            if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Expected a GeoJSON FeatureCollection, not service metadata or an error document.");
                foreach (var feature in features.EnumerateArray())
                {
                    var text = JsonSerializer.Serialize(feature);
                    records.Add(text);
                    keys.Add(feature.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String or JsonValueKind.Number
                        ? "id:" + id.ValueKind + ":" + id.GetRawText() : "");
                }
                string? next = null;
                if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
                    foreach (var link in links.EnumerateArray())
                        if (link.TryGetProperty("rel", out var rel) && rel.GetString() == "next" && link.TryGetProperty("href", out var href))
                            next = href.GetString();
                long? total = null;
                if ((root.TryGetProperty("numberMatched", out var count) || root.TryGetProperty("totalFeatures", out count)) && count.TryGetInt64Safe(out var n) && n >= 0) total = n;
                return new Page(true, body, records, keys, next, total);
            }
            var xml = XDocument.Parse(body);
            var element = xml.Root ?? throw new InvalidDataException("Empty feature response.");
            if (element.Name.LocalName != "FeatureCollection")
                throw new InvalidDataException("Expected a WFS FeatureCollection: " + element.Value[..Math.Min(250, element.Value.Length)]);
            foreach (var member in element.Elements().Where(e => e.Name.LocalName is "member" or "featureMember" or "featureMembers"))
                foreach (var feature in member.Elements())
                {
                    var wrapper = new XElement(XName.Get("member", "http://www.opengis.net/wfs/2.0"), new XElement(feature));
                    // Preserve inherited coordinate-system declarations and namespace bindings.
                    foreach (var attribute in element.Attributes().Where(a => a.IsNamespaceDeclaration || a.Name.LocalName is "srsName" or "srsDimension"))
                        if (wrapper.Attribute(attribute.Name) is null) wrapper.Add(new XAttribute(attribute));
                    var text = wrapper.ToString(SaveOptions.DisableFormatting);
                    records.Add(text);
                    var id = feature.Attributes().FirstOrDefault(a => a.Name.LocalName is "id" or "fid")?.Value;
                    keys.Add(string.IsNullOrEmpty(id) ? "" : "id:" + id);
                }
            var totalText = (string?)element.Attribute("numberMatched");
            return new Page(false, body, records, keys, (string?)element.Attribute("next"),
                long.TryParse(totalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalValue) && totalValue >= 0 ? totalValue : null);
        }

        public string Merge(List<string> records)
        {
            if (IsJson)
            {
                using var document = JsonDocument.Parse(Original);
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    foreach (var property in document.RootElement.EnumerateObject())
                        if (property.Name is not ("features" or "links" or "numberReturned" or "numberMatched" or "totalFeatures")) property.WriteTo(writer);
                    writer.WritePropertyName("features"); writer.WriteStartArray();
                    foreach (var record in records) { using var value = JsonDocument.Parse(record); value.RootElement.WriteTo(writer); }
                    writer.WriteEndArray(); writer.WriteNumber("numberReturned", records.Count); writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
            var xml = XDocument.Parse(Original);
            var root = xml.Root!;
            root.Elements().Where(e => e.Name.LocalName is "member" or "featureMember" or "featureMembers").Remove();
            root.Attribute("next")?.Remove(); root.Attribute("previous")?.Remove();
            root.SetAttributeValue("numberReturned", records.Count);
            foreach (var record in records) root.Add(XElement.Parse(record));
            return xml.ToString(SaveOptions.DisableFormatting);
        }
    }

    private static bool TryGetInt64Safe(this JsonElement element, out long value)
    { value = 0; return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value); }
}
