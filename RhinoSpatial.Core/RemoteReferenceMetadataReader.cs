using System.Collections.Concurrent;

namespace RhinoSpatial.Core;

/// <summary>Uses discovery metadata endpoints and rejects known data-file paths.</summary>
public sealed class RemoteReferenceMetadataReader
{
    private static readonly ConcurrentDictionary<string, (DateTime Expires, ReferenceSourceMetadata? Value)> Cache = new();
    private readonly AtomDownloadClient _atom;
    private readonly OgcApiFeaturesClient _ogc;
    private readonly bool _cache;
    private readonly WmtsClient _wmts;
    private readonly CogReader _cog;
    public RemoteReferenceMetadataReader(HttpClient? http = null)
    { _atom = new AtomDownloadClient(http); _ogc = new OgcApiFeaturesClient(http); _cache = http is null; _wmts=new WmtsClient(http); _cog=new CogReader(http); }

    public async Task<ReferenceSourceMetadata?> TryReadAsync(string source, string? layer = null, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        if (System.Text.RegularExpressions.Regex.IsMatch(source, @"[?&]service=(wfs|wms|wcs)(?:&|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return null;
        if(CogReader.LooksLike(source))
        {
            var cogKey="COG\n"+source;
            if(_cache&&Cache.TryGetValue(cogKey,out var prior)&&prior.Expires>DateTime.UtcNow)return prior.Value;
            var info=await _cog.ReadMetadataAsync(source,cancellationToken).ConfigureAwait(false);
            var metadata=new ReferenceSourceMetadata("Remote COG",info.SrsName,info.BoundingBox,
                ReferenceSourceMetadataReader.TryTransformBoundsToWgs84(info.SrsName,info.BoundingBox,out var wgs)?wgs:null,1);
            if(_cache){if(Cache.Count>128)Cache.Clear();Cache[cogKey]=(DateTime.UtcNow.AddMinutes(5),metadata);}
            return metadata;
        }
        if (new[] { ".zip", ".geojson", ".cityjson", ".jsonl", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".asc", ".xyz", ".csv", ".gml", ".citygml" }
            .Contains(Path.GetExtension(uri.AbsolutePath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Use the provider's service/feed metadata URL as Reference Source, or a supported local file. Remote data files are not downloaded to orient the map.");
        if(WmtsClient.LooksLike(source))
        {
            var wmts=await _wmts.TryLoadCapabilitiesAsync(source,cancellationToken).ConfigureAwait(false);
            if(wmts is null) throw new InvalidDataException("WMTS reference metadata was not found.");
            var selected=string.IsNullOrWhiteSpace(layer)?wmts.Layers:wmts.Layers.Where(l=>l.Id==layer).ToList();
            if(selected.Count==0)throw new InvalidDataException("WMTS reference layer was not found.");
            var extents=selected.Select(l=>l.Wgs84Bounds).Where(b=>b is not null).Cast<BoundingBox2D>().ToList();
            var bounds=extents.Count==0?null:new BoundingBox2D(extents.Min(b=>b.MinX),extents.Min(b=>b.MinY),extents.Max(b=>b.MaxX),extents.Max(b=>b.MaxY));
            return new ReferenceSourceMetadata("WMTS","EPSG:4326",bounds,bounds,selected.Count);
        }
        var key = source + "\n" + layer;
        if (_cache && Cache.TryGetValue(key, out var cached) && cached.Expires > DateTime.UtcNow) return cached.Value;
        // The parent collection holds metadata; /items would download feature data.
        var metadataUrl = source;
        if (uri.AbsolutePath.TrimEnd('/').EndsWith("/items", StringComparison.OrdinalIgnoreCase))
        {
            var parent = new UriBuilder(uri); parent.Path = parent.Path.TrimEnd('/')[..^6]; parent.Query = "f=json";
            metadataUrl = parent.Uri.AbsoluteUri;
        }
        var collections = await _ogc.TryDiscoverAsync(metadataUrl, cancellationToken).ConfigureAwait(false);
        ReferenceSourceMetadata? result;
        if (collections is not null)
        {
            var selected = string.IsNullOrWhiteSpace(layer) ? collections : collections.Where(c => c.Id == layer).ToList();
            if (selected.Count == 0) throw new InvalidDataException("The selected OGC API reference collection was not found.");
            var bounds = selected.Select(c => c.Wgs84Bounds).Where(b => b is not null).Cast<BoundingBox2D>().ToList();
            var union = bounds.Count == 0 ? null : new BoundingBox2D(bounds.Min(b => b.MinX), bounds.Min(b => b.MinY), bounds.Max(b => b.MaxX), bounds.Max(b => b.MaxY));
            result = new ReferenceSourceMetadata("OGC API Features", "EPSG:4326", union, union, selected.Count);
        }
        else result = await _atom.TryReadReferenceAsync(source, cancellationToken).ConfigureAwait(false);
        if (_cache) { if (Cache.Count > 128) Cache.Clear(); Cache[key] = (DateTime.UtcNow.AddMinutes(5), result); }
        return result;
    }
}
