namespace RhinoSpatial.Core;

public record RasterImageVertex(double X, double Y, double U, double V);
public record RasterImageMeshData(List<RasterImageVertex> Vertices, List<(int A, int B, int C)> Faces) { public double CoverageRatio { get; init; } }

public static class RasterImageMeshBuilder
{
    public static RasterImageMeshData Build(GeoReferencedRasterInfo info, SpatialContext2D context, int divisions = 16)
    {
        if (divisions < 1 || divisions > 128) throw new ArgumentOutOfRangeException(nameof(divisions));
        var g = info.GeoTransform ?? new RasterGeoTransform((info.BoundingBox.MaxX - info.BoundingBox.MinX) / info.Width, 0, info.BoundingBox.MinX, 0, -(info.BoundingBox.MaxY - info.BoundingBox.MinY) / info.Height, info.BoundingBox.MaxY);
        g.Validate();
        var selected=Lod2AtomFeedReader.TransformBounds(new(context.ResolvedSrs,context.PlacementBoundingBox),info.SrsName);
        var pixelCorners=new[]{g.Pixel(selected.MinX,selected.MinY),g.Pixel(selected.MaxX,selected.MinY),g.Pixel(selected.MaxX,selected.MaxY),g.Pixel(selected.MinX,selected.MaxY)};
        var u0=Math.Clamp(pixelCorners.Min(p=>p.X)/info.Width,0,1);var u1=Math.Clamp(pixelCorners.Max(p=>p.X)/info.Width,0,1);
        var r0=Math.Clamp(pixelCorners.Min(p=>p.Y)/info.Height,0,1);var r1=Math.Clamp(pixelCorners.Max(p=>p.Y)/info.Height,0,1);
        if(u1<=u0 || r1<=r0)return new(new(),new());
        var grid = new RasterImageVertex[(divisions + 1) * (divisions + 1)];
        for (var r = 0; r <= divisions; r++) for (var c = 0; c <= divisions; c++)
        {
            var uv = new Coordinate2D(u0+(u1-u0)*c/divisions,r0+(r1-r0)*r/divisions); var p = g.At(uv.X * info.Width, uv.Y * info.Height);
            if (!SpatialReferenceTransform.TryTransformXY(info.SrsName, context.ResolvedSrs, p.X, p.Y, out var x, out var y) || !double.IsFinite(x) || !double.IsFinite(y))
                throw new InvalidDataException($"Cannot transform raster from '{info.SrsName}' into '{context.ResolvedSrs}'.");
            grid[r * (divisions + 1) + c] = new(x, y, uv.X, 1 - uv.Y);
        }
        var vertices = new List<RasterImageVertex>(); var faces = new List<(int, int, int)>();
        var b = context.PlacementBoundingBox; double keptArea = 0;
        for (var r = 0; r < divisions; r++) for (var c = 0; c < divisions; c++)
        {
            var i = r * (divisions + 1) + c;
            // Triangles preserve an unambiguous UV interpolation when projection is nonlinear.
            foreach (var triangle in new[] { new[] { grid[i], grid[i + 1], grid[i + divisions + 2] }, new[] { grid[i], grid[i + divisions + 2], grid[i + divisions + 1] } })
            {
                var polygon = triangle.ToList();
                polygon = Clip(polygon, p => p.X - b.MinX);
                polygon = Clip(polygon, p => b.MaxX - p.X);
                polygon = Clip(polygon, p => p.Y - b.MinY);
                polygon = Clip(polygon, p => b.MaxY - p.Y);
                if (polygon.Count < 3) continue;
                var start = vertices.Count;
                vertices.AddRange(polygon.Select(p => p with { X = p.X - (context.UseAbsoluteCoordinates ? 0 : context.PlacementOrigin.X), Y = p.Y - (context.UseAbsoluteCoordinates ? 0 : context.PlacementOrigin.Y) }));
                for (var n = 1; n < polygon.Count - 1; n++)
                {
                    var a = polygon[0]; var d = polygon[n]; var e = polygon[n + 1];
                    var cross = Cross(a, d, e);
                    if (Math.Abs(cross) > 1e-18) { keptArea += Math.Abs((d.U-a.U)*(e.V-a.V)-(d.V-a.V)*(e.U-a.U))/2; faces.Add(cross > 0 ? (start, start + n, start + n + 1) : (start, start + n + 1, start + n)); }
                }
            }
        }
        return new(vertices, faces) { CoverageRatio = Math.Clamp(keptArea,0,1) };
    }
    private static double Cross(RasterImageVertex a, RasterImageVertex b, RasterImageVertex c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static List<RasterImageVertex> Clip(List<RasterImageVertex> input, Func<RasterImageVertex, double> distance)
    {
        var output = new List<RasterImageVertex>(); if (input.Count == 0) return output;
        var previous = input[^1]; var a = distance(previous);
        foreach (var point in input)
        {
            var b = distance(point);
            if ((a >= 0) != (b >= 0))
            {
                var t = a / (a - b);
                output.Add(new(previous.X + t * (point.X - previous.X), previous.Y + t * (point.Y - previous.Y), previous.U + t * (point.U - previous.U), previous.V + t * (point.V - previous.V)));
            }
            if (b >= 0) output.Add(point);
            previous = point; a = b;
        }
        return output;
    }
}
