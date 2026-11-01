using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RhinoSpatial.Core;

public record CityJsonSourceMetadata(string SrsName, BoundingBox2D? BoundingBox);

public static class CityJsonReader
{
    public static CityJsonSourceMetadata ReadSourceMetadata(string text)
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)); return ReadSourceMetadata(stream); }

    public static CityJsonSourceMetadata ReadSourceMetadata(Stream stream, CancellationToken cancellationToken = default)
    {
        string srs = ""; BoundingBox2D? bounds = null;
        foreach (var (root, header) in Records(stream, cancellationToken))
        {
            srs = ReadSrs(header);
            if (header.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("geographicalExtent", out var extent))
            {
                var a = extent.EnumerateArray().Select(Number).ToArray();
                if (a.Length != 6 || a[0] > a[3] || a[1] > a[4]) throw new InvalidDataException("Invalid CityJSON geographicalExtent.");
                return new(srs, new(a[0], a[1], a[3], a[4]));
            }
            var vertices = Vertices(root, header);
            bounds = Union(bounds, Bounds(vertices));
            if (root.TryGetProperty("CityObjects", out var objects))
                foreach (var obj in objects.EnumerateObject())
                    if (obj.Value.TryGetProperty("geometry", out var geometry))
                        foreach (var g in geometry.EnumerateArray())
                            if (Type(g) == "GeometryInstance") bounds = Union(bounds, Bounds(InstanceVertices(g, header, vertices)));
        }
        return new(srs, bounds);
    }

    public static List<Lod2Building> ReadBuildings(string text, string layer, BoundingBox2D? filterBounds = null)
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)); return ReadBuildings(stream, layer, filterBounds); }

    public static List<Lod2Building> ReadBuildings(Stream stream, string layer, BoundingBox2D? filterBounds = null, CancellationToken cancellationToken = default)
    {
        var result = new List<Lod2Building>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        long pointCount = 0;
        foreach (var (root, header) in Records(stream, cancellationToken))
        {
            var vertices = Vertices(root, header);
            if (!root.TryGetProperty("CityObjects", out var objects) || objects.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("CityJSON requires a CityObjects object.");
            foreach (var obj in objects.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Type(obj.Value) is not ("Building" or "BuildingPart" or "BuildingInstallation" or "BuildingConstructiveElement" or "BuildingRoom")) continue;
                if (!ids.Add(obj.Name)) throw new InvalidDataException("Duplicate CityJSON building id: " + obj.Name);
                if (!obj.Value.TryGetProperty("geometry", out var geometries)) continue;
                var candidates = geometries.EnumerateArray().Select(g => (Geometry: g, Lod: ReadLod(g, header))).ToList();
                var lods = candidates.Where(g => g.Lod.HasValue).Select(g => g.Lod!.Value).Distinct().OrderBy(x => x).ToList();
                if (lods.Count > 0 && candidates.Any(c => c.Lod is null)) throw new InvalidDataException("CityJSON mixes declared and unspecified geometry LoDs; a complete representation cannot be chosen safely.");
                decimal? selected = lods.Count == 0 ? null : lods.Where(l => l <= 2.2m).Select(l => (decimal?)l).LastOrDefault() ?? lods[0];
                var surfaces = new List<SurfacePolygon3D>();
                foreach (var candidate in candidates.Where(c => c.Lod == selected))
                {
                    var geometry = candidate.Geometry; IReadOnlyList<Coordinate3D> points = vertices;
                    if (Type(geometry) == "GeometryInstance")
                    {
                        points = InstanceVertices(geometry, header, vertices);
                        geometry = Template(geometry, header);
                    }
                    if (Type(geometry) is not ("MultiSurface" or "CompositeSurface" or "Solid" or "MultiSolid" or "CompositeSolid"))
                        throw new InvalidDataException("Unsupported CityJSON building primitive: " + Type(geometry));
                    Collect(geometry.GetProperty("boundaries"), points, surfaces);
                }
                pointCount += surfaces.Sum(s => (long)s.OuterPoints.Count + s.InnerRings.Sum(r => r.Count));
                if (pointCount > 4000000 || result.Count >= 100000) throw new InvalidDataException("CityJSON output exceeds bounded import limits; select a smaller dataset.");
                if (filterBounds is not null) surfaces = surfaces.Where(s => Intersects(Bounds(s.OuterPoints), filterBounds)).ToList();
                if (surfaces.Count == 0) continue;
                result.Add(new(obj.Name, layer, surfaces, new Dictionary<string, string?> { ["source_format"] = "CityJSON", ["source_lod"] = selected?.ToString(CultureInfo.InvariantCulture) ?? "unspecified" }));
            }
        }
        return result;
    }

    // Validation consumes every record, even when reference metadata can stop at a header extent.
    public static bool Validate(Stream stream, bool requireCrs = false, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var (root, header) in Records(stream, cancellationToken, true))
        {
            count++;
            if (requireCrs && string.IsNullOrWhiteSpace(ReadSrs(header))) throw new InvalidDataException("Downloaded CityJSON must declare metadata.referenceSystem.");
            if (!root.TryGetProperty("CityObjects", out var objects) || objects.ValueKind != JsonValueKind.Object || !root.TryGetProperty("vertices", out _))
                throw new InvalidDataException("CityJSON requires CityObjects and vertices.");
            _ = Vertices(root, header);
        }
        return count > 0;
    }

    public static bool IsCityJson(Stream stream)
    {
        try { using var e = Records(stream, default).GetEnumerator(); return e.MoveNext(); }
        catch (JsonException) { return false; }
        catch (InvalidDataException) { return false; }
    }

    private static IEnumerable<(JsonElement Root, JsonElement Header)> Records(Stream stream, CancellationToken token, bool allowOtherRoot = false)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 65536, leaveOpen: true);
        JsonElement header = default; long total = 0; int count = 0;
        while (true)
        {
            int ch; do { ch = reader.Read(); } while (ch >= 0 && char.IsWhiteSpace((char)ch));
            if (ch < 0) yield break;
            if (ch != '{') throw new InvalidDataException("Expected a CityJSON object or CityJSONSeq record.");
            var text = new StringBuilder("{"); int depth = 1; bool quoted = false, escaped = false;
            while (depth > 0)
            {
                ch = reader.Read(); if (ch < 0) throw new InvalidDataException("Truncated CityJSON record.");
                var c = (char)ch; text.Append(c);
                if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '\"') quoted = false; }
                else if (c == '\"') quoted = true;
                else if (c is '{' or '[') depth++;
                else if (c is '}' or ']') depth--;
                if ((text.Length & 8191) == 0) token.ThrowIfCancellationRequested();
                if (text.Length > 64 * 1024 * 1024) throw new InvalidDataException("CityJSON record exceeds 64 MiB of text.");
            }
            total += text.Length;
            if (total > 512L * 1024 * 1024 || ++count > 100001) throw new InvalidDataException("CityJSON stream exceeds import limits.");
            using var doc = JsonDocument.Parse(text.ToString()); var root = doc.RootElement;
            if (count == 1)
            {
                if (Type(root) != "CityJSON" && allowOtherRoot) yield break;
                if (Type(root) != "CityJSON") throw new InvalidDataException("CityJSONSeq requires a CityJSON header before CityJSONFeature records.");
                header = root.Clone();
            }
            else
            {
                if (Type(root) != "CityJSONFeature") throw new InvalidDataException("Expected a CityJSONFeature sequence record.");
                if (header.GetProperty("CityObjects").EnumerateObject().Any() || header.GetProperty("vertices").GetArrayLength() != 0)
                    throw new InvalidDataException("CityJSONSeq header CityObjects and vertices must be empty.");
                if (root.TryGetProperty("transform", out _) || root.TryGetProperty("geometry-templates", out _) || root.TryGetProperty("metadata", out _))
                    throw new InvalidDataException("CityJSONFeature inherits transform, templates and metadata from its header.");
            }
            token.ThrowIfCancellationRequested(); yield return (root, header);
        }
    }

    private static string Type(JsonElement e) => e.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
    private static double Number(JsonElement e)
    { var n = e.GetDouble(); if (!double.IsFinite(n)) throw new InvalidDataException("CityJSON coordinates must be finite."); return n; }
    private static string ReadSrs(JsonElement header)
    {
        if (!header.TryGetProperty("metadata", out var m) || !m.TryGetProperty("referenceSystem", out var r)) return "";
        var name = r.GetString() ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(name, @"(?:EPSG[/,:]+(?:0[/,:]+)?)(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? "EPSG:" + match.Groups[1].Value : "";
    }
    private static List<Coordinate3D> Vertices(JsonElement root, JsonElement header)
    {
        var scale = new[] { 1d, 1d, 1d }; var translate = new[] { 0d, 0d, 0d };
        if (header.TryGetProperty("transform", out var t))
        {
            scale = t.GetProperty("scale").EnumerateArray().Select(Number).ToArray();
            translate = t.GetProperty("translate").EnumerateArray().Select(Number).ToArray();
            if (scale.Length != 3 || translate.Length != 3) throw new InvalidDataException("Invalid CityJSON transform.");
        }
        var result = new List<Coordinate3D>();
        foreach (var v in root.GetProperty("vertices").EnumerateArray())
        {
            var a = v.EnumerateArray().Select(Number).ToArray(); if (a.Length != 3) throw new InvalidDataException("CityJSON vertex must contain XYZ.");
            var p = new Coordinate3D(a[0] * scale[0] + translate[0], a[1] * scale[1] + translate[1], a[2] * scale[2] + translate[2]);
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)) throw new InvalidDataException("CityJSON transform overflow.");
            result.Add(p);
        }
        return result;
    }
    private static JsonElement Template(JsonElement g, JsonElement header)
    {
        var templates = header.GetProperty("geometry-templates").GetProperty("templates");
        var i = g.GetProperty("template").GetInt32();
        if (i < 0 || i >= templates.GetArrayLength()) throw new InvalidDataException("CityJSON template index out of range.");
        return templates[i];
    }
    private static decimal? ReadLod(JsonElement g, JsonElement header)
    {
        if (Type(g) == "GeometryInstance") g = Template(g, header);
        if (!g.TryGetProperty("lod", out var lod)) return null;
        var text = lod.ValueKind == JsonValueKind.String ? lod.GetString() : lod.GetRawText();
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0 || value >= 5)
            throw new InvalidDataException("Invalid CityJSON LoD: " + text);
        return value;
    }
    private static List<Coordinate3D> InstanceVertices(JsonElement g, JsonElement header, IReadOnlyList<Coordinate3D> vertices)
    {
        var boundaries = g.GetProperty("boundaries"); if (boundaries.GetArrayLength() != 1) throw new InvalidDataException("GeometryInstance requires one anchor index.");
        var index = boundaries[0].GetInt32(); if (index < 0 || index >= vertices.Count) throw new InvalidDataException("Invalid template anchor index.");
        var anchor = vertices[index]; var m = g.GetProperty("transformationMatrix").EnumerateArray().Select(Number).ToArray();
        if (m.Length != 16 || m[12] != 0 || m[13] != 0 || m[14] != 0 || m[15] != 1) throw new InvalidDataException("GeometryInstance requires an affine row-major 4x4 matrix.");
        var result = new List<Coordinate3D>();
        foreach (var v in header.GetProperty("geometry-templates").GetProperty("vertices-templates").EnumerateArray())
        {
            var a = v.EnumerateArray().Select(Number).ToArray(); if (a.Length != 3) throw new InvalidDataException("Invalid template vertex.");
            var p = new Coordinate3D(m[0] * a[0] + m[1] * a[1] + m[2] * a[2] + m[3] + anchor.X, m[4] * a[0] + m[5] * a[1] + m[6] * a[2] + m[7] + anchor.Y, m[8] * a[0] + m[9] * a[1] + m[10] * a[2] + m[11] + anchor.Z);
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)) throw new InvalidDataException("Template transform overflow.");
            result.Add(p);
        }
        return result;
    }
    private static void Collect(JsonElement e, IReadOnlyList<Coordinate3D> vertices, List<SurfacePolygon3D> surfaces)
    {
        if (e.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid CityJSON boundaries.");
        if (e.GetArrayLength() == 0) return;
        if (e[0].ValueKind == JsonValueKind.Array && e[0].GetArrayLength() > 0 && e[0][0].ValueKind == JsonValueKind.Number)
        {
            var rings = new List<List<Coordinate3D>>();
            foreach (var ring in e.EnumerateArray())
            {
                if (ring.GetArrayLength() < 3) throw new InvalidDataException("CityJSON surface ring requires three vertices.");
                var points = new List<Coordinate3D>();
                foreach (var ix in ring.EnumerateArray()) { var i = ix.GetInt32(); if (i < 0 || i >= vertices.Count) throw new InvalidDataException("CityJSON vertex index out of range."); points.Add(vertices[i]); }
                rings.Add(points);
            }
            surfaces.Add(new(rings[0], rings.Skip(1).ToList())); return;
        }
        foreach (var child in e.EnumerateArray()) Collect(child, vertices, surfaces);
    }
    private static BoundingBox2D? Bounds(IReadOnlyList<Coordinate3D> p) => p.Count == 0 ? null : new(p.Min(v => v.X), p.Min(v => v.Y), p.Max(v => v.X), p.Max(v => v.Y));
    private static BoundingBox2D? Union(BoundingBox2D? a, BoundingBox2D? b) => a is null ? b : b is null ? a : new(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));
    private static bool Intersects(BoundingBox2D? a, BoundingBox2D b) => a is not null && a.MinX <= b.MaxX && a.MaxX >= b.MinX && a.MinY <= b.MaxY && a.MaxY >= b.MinY;
}
