using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RhinoSpatial.Core;

public record AtomSpatialExtent(string SrsName, BoundingBox2D Bounds);
public record Lod2AtomLink(Uri Url, string Title, string Revision, AtomSpatialExtent? Extent);
public record Lod2AtomFeed(IReadOnlyList<Lod2AtomLink> Feeds, IReadOnlyList<Lod2AtomLink> Downloads);

/// <summary>Reads download metadata only; never opens building archives to locate tiles.</summary>
public static class Lod2AtomFeedReader
{
    public static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace GeoRss = "http://www.georss.org/georss";

    public static Lod2AtomFeed Read(XDocument document, Uri source, AtomSpatialExtent? inheritedExtent = null)
    {
        var root = document.Root;
        if (root?.Name != Atom + "feed")
            throw new InvalidOperationException("The source is not an ATOM feed.");

        var feeds = new List<Lod2AtomLink>();
        var downloads = new List<Lod2AtomLink>();
        var feedExtent = ReadExtent(root) ?? inheritedExtent;
        var feedSrs = ReadCategorySrs(root);
        AddFeedLinks(root, feedExtent);

        foreach (var entry in root.Elements(Atom + "entry"))
        {
            var extent = ReadExtent(entry);
            var coverage = extent ?? feedExtent;
            AddFeedLinks(entry, coverage);
            var links = entry.Elements(Atom + "link").Where(IsDownloadLink).ToList();
            var srs = ReadCategorySrs(entry) ?? feedSrs;
            var gridSize = ReadGridSize(entry) ?? ReadGridSize(root);
            foreach (var link in links)
            {
                var contentType = ((string?)link.Attribute("type") ?? "").Split(';')[0].Trim();
                if (contentType.Equals("text/csv", StringComparison.OrdinalIgnoreCase) ||
                    contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"This ATOM download advertises '{contentType}', not CityGML building data. Load LoD2 Buildings requires a CityGML download feed. Terrain ATOM feeds are not supported; use local terrain files or a WCS source with Load Terrain.");
                var url = ResolveUrl(link, source);
                var tileExtent = ReadExtent(link);
                if (tileExtent is null && links.Count == 1)
                    tileExtent = extent;
                if (tileExtent is null && gridSize.HasValue && srs is not null)
                    tileExtent = ReadDeclaredGridTile(url, srs, gridSize.Value, coverage);
                downloads.Add(CreateLink(link, entry, url, tileExtent));
            }
        }

        return new Lod2AtomFeed(feeds, downloads);

        void AddFeedLinks(XElement owner, AtomSpatialExtent? coverage)
        {
            foreach (var link in owner.Elements(Atom + "link"))
            {
                var relation = (string?)link.Attribute("rel") ?? "alternate";
                var type = (string?)link.Attribute("type") ?? "";
                var href = (string?)link.Attribute("href") ?? "";
                if (relation is "self" or "up" or "describedby" or "previous" or "prev")
                    continue;
                if (relation != "next" && !type.StartsWith("application/atom+xml", StringComparison.OrdinalIgnoreCase) &&
                    !href.Split('?', '#')[0].EndsWith(".atom", StringComparison.OrdinalIgnoreCase))
                    continue;
                // A pagination link describes another page of this feed, not a tile.
                feeds.Add(CreateLink(link, owner, ResolveUrl(link, source), relation == "next" ? feedExtent : coverage));
            }
        }
    }

    private static Lod2AtomLink CreateLink(XElement link, XElement owner, Uri url, AtomSpatialExtent? extent) =>
        new(url, (string?)link.Attribute("title") ?? (string?)owner.Element(Atom + "title") ?? Path.GetFileName(url.LocalPath),
            (string?)owner.Element(Atom + "updated") ?? (string?)owner.Parent?.Element(Atom + "updated") ?? "", extent);

    private static bool IsDownloadLink(XElement link)
    {
        var relation = (string?)link.Attribute("rel") ?? "alternate";
        if (relation is not ("enclosure" or "section" or "alternate"))
            return false;
        var type = ((string?)link.Attribute("type") ?? "").ToLowerInvariant();
        var href = ((string?)link.Attribute("href") ?? "").Split('?', '#')[0];
        return (relation == "enclosure" && !type.Contains("atom+xml")) || type.Contains("zip") || type.Contains("gmz") || type.Contains("7z") ||
               new[] { ".zip", ".gmz", ".7z", ".gz", ".tar", ".rar" }.Any(extension => href.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static Uri ResolveUrl(XElement link, Uri source)
    {
        var baseUri = source;
        foreach (var element in link.AncestorsAndSelf().Reverse())
        {
            if (element.Attribute(XNamespace.Xml + "base") is { } xmlBase)
                baseUri = new Uri(baseUri, xmlBase.Value);
        }
        var uri = new Uri(baseUri, (string?)link.Attribute("href") ?? throw new InvalidOperationException("ATOM link has no URL."));
        if (uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("ATOM downloads and subfeeds must use HTTP or HTTPS URLs.");
        return new UriBuilder(uri) { Fragment = "" }.Uri;
    }

    private static string? ReadCategorySrs(XElement owner)
    {
        var codes = owner.Elements(Atom + "category")
            .Select(category => Regex.Match((string?)category.Attribute("term") ?? "", @"EPSG(?::|/)(?:[^:/]*[:/])?(\d+)$", RegexOptions.IgnoreCase))
            .Where(match => match.Success).Select(match => "EPSG:" + match.Groups[1].Value).Distinct().ToList();
        return codes.Count == 1 ? codes[0] : null;
    }

    private static double? ReadGridSize(XElement owner)
    {
        // Only an explicit square-grid declaration enables kilometre-coordinate filenames.
        var sizes = owner.Elements(Atom + "link").Select(link => Regex.Match(
                (string?)link.Attribute("title") ?? "", @"\b(\d+(?:[.,]\d+)?)\s*[x×]\s*(\d+(?:[.,]\d+)?)\s*(km|m)\b", RegexOptions.IgnoreCase))
            .Where(match => match.Success).Select(match => (
                X: double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture),
                Y: double.Parse(match.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture),
                Scale: match.Groups[3].Value.Equals("km", StringComparison.OrdinalIgnoreCase) ? 1000 : 1))
            .Where(size => size.X == size.Y && size.X > 0).Select(size => size.X * size.Scale).Distinct().ToList();
        return sizes.Count == 1 ? sizes[0] : null;
    }

    private static AtomSpatialExtent? ReadDeclaredGridTile(Uri url, string srs, double size, AtomSpatialExtent? coverage)
    {
        // Supported convention: UTM easting/northing in kilometres, southwest tile corner.
        // The service must also publish a grid size, CRS and enclosing geographic coverage.
        if (srs is not ("EPSG:25832" or "EPSG:25833") || coverage is null)
            return null;
        var match = Regex.Match(Path.GetFileNameWithoutExtension(url.LocalPath), @"_(\d{3})_(\d{4})$");
        if (!match.Success)
            return null;
        var x = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 1000.0;
        var y = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 1000.0;
        var bounds = new BoundingBox2D(x, y, x + size, y + size);
        var extent = new AtomSpatialExtent(srs, bounds);
        var coverageBounds = TransformBounds(coverage, srs);
        if (!Intersects(bounds, coverageBounds))
            throw new InvalidOperationException($"The declared ATOM grid and coverage disagree for '{url.LocalPath}'. Tile placement cannot be determined safely.");
        return extent;
    }

    public static AtomSpatialExtent? ReadExtent(XElement owner)
    {
        if (owner.Elements().Count(element => element.Name.Namespace == GeoRss && element.Name.LocalName is "box" or "polygon" or "where") > 1)
            throw new InvalidOperationException("ATOM entry has multiple spatial extents; tile coverage is ambiguous.");
        var box = owner.Element(GeoRss + "box");
        if (box is not null)
        {
            var values = Numbers(box.Value);
            if (values.Length != 4)
                throw new InvalidOperationException("Invalid ATOM GeoRSS box; expected south west north east.");
            return Geographic(new BoundingBox2D(values[1], values[0], values[3], values[2]));
        }
        var polygon = owner.Element(GeoRss + "polygon");
        if (polygon is not null)
        {
            var values = Numbers(polygon.Value);
            if (values.Length < 8 || values.Length % 2 != 0)
                throw new InvalidOperationException("Invalid ATOM GeoRSS polygon.");
            return Geographic(new BoundingBox2D(
                values.Where((_, i) => i % 2 == 1).Min(), values.Where((_, i) => i % 2 == 0).Min(),
                values.Where((_, i) => i % 2 == 1).Max(), values.Where((_, i) => i % 2 == 0).Max()));
        }
        var envelope = owner.Element(GeoRss + "where")?.Descendants().FirstOrDefault(element => element.Name.LocalName == "Envelope");
        if (envelope is null)
            return null;
        var lower = envelope.Elements().FirstOrDefault(element => element.Name.LocalName == "lowerCorner");
        var upper = envelope.Elements().FirstOrDefault(element => element.Name.LocalName == "upperCorner");
        var srs = (string?)envelope.Attribute("srsName") ?? "urn:ogc:def:crs:EPSG::4326";
        if (lower is null || upper is null)
            throw new InvalidOperationException("ATOM GML envelope is missing its corners.");
        var lo = Numbers(lower.Value);
        var hi = Numbers(upper.Value);
        if (lo.Length < 2 || hi.Length < 2)
            throw new InvalidOperationException("Invalid ATOM GML envelope corners.");
        var code = Regex.Match(srs, @"EPSG(?::|/)(?:[^:/]*[:/])?(\d+)$", RegexOptions.IgnoreCase);
        var normalized = code.Success ? "EPSG:" + code.Groups[1].Value : srs.Contains("CRS84", StringComparison.OrdinalIgnoreCase) ? "EPSG:4326" : srs;
        var swap = code.Success && (normalized is "EPSG:4326" or "EPSG:4258");
        var bounds = swap ? new BoundingBox2D(lo[1], lo[0], hi[1], hi[0]) : new BoundingBox2D(lo[0], lo[1], hi[0], hi[1]);
        ValidateBounds(bounds);
        return new AtomSpatialExtent(normalized, bounds);
    }

    private static AtomSpatialExtent Geographic(BoundingBox2D bounds)
    {
        ValidateBounds(bounds);
        if (bounds.MinX < -180 || bounds.MaxX > 180 || bounds.MinY < -90 || bounds.MaxY > 90)
            throw new InvalidOperationException("ATOM geographic extent is outside longitude/latitude limits.");
        return new AtomSpatialExtent("EPSG:4326", bounds);
    }

    private static double[] Numbers(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(number => double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();

    public static void ValidateBounds(BoundingBox2D bounds)
    {
        if (!double.IsFinite(bounds.MinX) || !double.IsFinite(bounds.MinY) || !double.IsFinite(bounds.MaxX) || !double.IsFinite(bounds.MaxY) ||
            bounds.MinX >= bounds.MaxX || bounds.MinY >= bounds.MaxY)
            throw new InvalidOperationException("Invalid or unsupported ATOM spatial bounds (including wrapped antimeridian extents).");
    }

    public static BoundingBox2D TransformBounds(AtomSpatialExtent extent, string targetSrs)
    {
        ValidateBounds(extent.Bounds);
        var points = new List<Coordinate2D>();
        // Sample edges as well as corners so projected/geographic tile selection stays conservative.
        for (var i = 0; i <= 16; i++)
        {
            var t = i / 16.0;
            var b = extent.Bounds;
            Add(b.MinX + (b.MaxX - b.MinX) * t, b.MinY);
            Add(b.MinX + (b.MaxX - b.MinX) * t, b.MaxY);
            Add(b.MinX, b.MinY + (b.MaxY - b.MinY) * t);
            Add(b.MaxX, b.MinY + (b.MaxY - b.MinY) * t);
        }
        return new BoundingBox2D(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));

        void Add(double x, double y)
        {
            if (!SpatialReferenceTransform.TryTransformXY(extent.SrsName, targetSrs, x, y, out var tx, out var ty) || !double.IsFinite(tx) || !double.IsFinite(ty))
                throw new InvalidOperationException($"Unsupported ATOM coordinate transformation from '{extent.SrsName}' to '{targetSrs}'.");
            points.Add(new Coordinate2D(tx, ty));
        }
    }

    public static bool Intersects(BoundingBox2D a, BoundingBox2D b) =>
        a.MinX < b.MaxX && a.MaxX > b.MinX && a.MinY < b.MaxY && a.MaxY > b.MinY;
}
