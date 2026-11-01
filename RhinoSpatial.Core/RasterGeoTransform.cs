using System.Globalization;
using System.Security.Cryptography;

namespace RhinoSpatial.Core;

/// <summary>Maps pixel corners to source coordinates; sample centers are at (col+.5,row+.5).</summary>
public record RasterGeoTransform(double A, double B, double C, double D, double E, double F)
{
    public Coordinate2D At(double column, double row) => new(A * column + B * row + C, D * column + E * row + F);
    public void Validate()
    {
        if (new[] { A, B, C, D, E, F }.Any(v => !double.IsFinite(v)) || !double.IsFinite(A * E - B * D) || A * E - B * D == 0 || Math.Abs(A * E - B * D) <= 1e-12 * (Math.Abs(A * E) + Math.Abs(B * D)))
            throw new InvalidDataException("Raster georeferencing must be finite and invertible.");
    }
    public Coordinate2D Pixel(double x, double y)
    { var det = A * E - B * D; return new((E * (x - C) - B * (y - F)) / det, (-D * (x - C) + A * (y - F)) / det); }
    public BoundingBox2D Bounds(int width, int height)
    {
        var p = new[] { At(0, 0), At(width, 0), At(width, height), At(0, height) };
        return new(p.Min(p => p.X), p.Min(p => p.Y), p.Max(p => p.X), p.Max(p => p.Y));
    }
    public static RasterGeoTransform FromWorldFile(string path)
    {
        var values = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => double.Parse(l, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length != 6) throw new InvalidDataException("World file must contain six numeric lines.");
        var g = new RasterGeoTransform(values[0], values[2], values[4] - (values[0] + values[2]) / 2, values[1], values[3], values[5] - (values[1] + values[3]) / 2);
        g.Validate(); return g;
    }
}

public static class RasterSidecars
{
    public static IEnumerable<string> Files(string raster)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(raster))!;
        var ext = Path.GetExtension(raster).TrimStart('.');
        var stem = Path.GetFileNameWithoutExtension(raster);
        var names = ext.Length == 0 ? new[] { stem + ".wld", stem + ".prj" } : new[] { stem + "." + ext[0] + ext[^1] + "w", stem + "." + ext + "w", stem + ".wld", stem + ".prj" };
        var files = Directory.EnumerateFiles(directory, stem + ".*", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).ToList();
        foreach (var name in names)
        {
            var file = files.FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (file is not null) yield return file;
        }
    }
    public static string Signature(string raster) => string.Join("|", Files(raster).Select(p =>
    {
        if (new FileInfo(p).Length > 65536) return p + ":" + new FileInfo(p).Length + ":" + File.GetLastWriteTimeUtc(p).Ticks;
        return p + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    }));
    public static RasterGeoTransform ReadTransform(string raster)
    {
        var world = Files(raster).FirstOrDefault(p => !p.EndsWith(".prj", StringComparison.OrdinalIgnoreCase));
        if (world is null) throw new InvalidDataException("Raster requires readable embedded georeferencing or a world file.");
        if (new FileInfo(world).Length > 65536) throw new InvalidDataException("World file exceeds 64 KiB.");
        return RasterGeoTransform.FromWorldFile(world);
    }
    public static string ReadSrs(string raster)
    {
        var prj = Files(raster).FirstOrDefault(p => p.EndsWith(".prj", StringComparison.OrdinalIgnoreCase));
        if (prj is null) throw new InvalidDataException("Raster requires an embedded CRS or a resolvable .prj; no context CRS was assumed.");
        if (new FileInfo(prj).Length > 65536) throw new InvalidDataException("Raster .prj exceeds 64 KiB.");
        var srs = ShapefileFeatureReader.NormalizeKnownProjection(File.ReadAllText(prj));
        return string.IsNullOrWhiteSpace(srs) ? throw new InvalidDataException("Raster .prj CRS is unsupported.") : srs;
    }
    public static (RasterGeoTransform Transform, string Srs) Read(string raster) => (ReadTransform(raster), ReadSrs(raster));
}
