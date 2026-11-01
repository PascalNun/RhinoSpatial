using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class TerrainMeshChecks
{
    public static void Run()
    {
        var bounds = new BoundingBox2D(500000, 0, 500010, 10);
        var raster = Raster("EPSG:25832", 2, 2, new float[] { 100, 110, 120, 130 });
        var context = Context("EPSG:4326", new Coordinate2D(8.9, 0));
        var mesh = TerrainMeshBuilder.Build(raster, bounds, context, 512, 25);
        var lowerLeft = mesh.Vertices[2];
        Require(Math.Abs(lowerLeft.X - 0.1) < 1e-7 && Math.Abs(lowerLeft.Y) < 1e-7,
            "Projected terrain must transform into the context CRS before localization (UTM zone 32 central meridian is 9 degrees east).");
        Require(lowerLeft.Z == 95 && mesh.Faces.Count == 1, "Terrain must retain its elevations relative to the shared baseline.");

        var absolute = TerrainMeshBuilder.Build(raster, bounds, context with { UseAbsoluteCoordinates = true }, 512, 25);
        Require(Math.Abs(absolute.Vertices[2].X - 9) < 1e-7 && absolute.Vertices[2].Z == 120,
            "Absolute mode must preserve full context coordinates and source elevation.");

        var sameCrs = TerrainMeshBuilder.Build(raster, bounds, Context("EPSG:25832", new Coordinate2D(500000, 0)), 512, 25);
        Require(sameCrs.Vertices[0] == new Coordinate3D(0, 10, 75), "Existing same-CRS placement must remain stable.");

        var geographic = TerrainMeshBuilder.Build(Raster("EPSG:4326", 2, 2, new float[] { 1, 2, 3, 4 }),
            new BoundingBox2D(0, 0, 1, 1), Context("EPSG:3857", new Coordinate2D(0, 0)), 512, 0);
        Require(Math.Abs(geographic.Vertices[1].X - 111319.490793) < 0.001 &&
                Math.Abs(geographic.Vertices[1].Y - 111325.142866) < 0.001,
            "The global geographic terrain path must still project into the selected context.");

        foreach (var missing in new[] { float.NaN, float.PositiveInfinity, -9999f })
        {
            var values = Enumerable.Repeat(100f, 9).ToArray();
            values[0] = missing;
            var sparse = TerrainMeshBuilder.Build(Raster("EPSG:25832", 3, 3, values) with { NoDataValue = -9999 },
                bounds, Context("EPSG:25832", new Coordinate2D(500000, 0)), 512, 25);
            Require(sparse.Faces.Count == 3 && sparse.Vertices.Count == 8,
                "Missing elevations must leave a hole, rather than creating a flat terrain patch.");
            Require(sparse.Vertices.All(vertex => double.IsFinite(vertex.Z) && vertex.Z == 75),
                "Non-finite and NoData elevations must never become vertices.");
        }

        var edgeMesh = TerrainMeshBuilder.Build(Raster("EPSG:25832", 6, 4, Enumerable.Repeat(100f, 24).ToArray()),
            new BoundingBox2D(0, 0, 5, 3), Context("EPSG:25832", new Coordinate2D(0, 0)), 3, 0);
        Require(edgeMesh.Vertices.Count <= 9 && edgeMesh.Vertices[^1] == new Coordinate3D(5, 0, 100),
            "Downsampling must retain the last raster row and column within the grid budget.");

        RequireInvalid(() => TerrainMeshBuilder.Build(raster with { SrsName = "EPSG:999999" }, bounds, context, 512, 25), "transform");
        RequireInvalid(() => TerrainMeshBuilder.Build(raster with { Elevations = new[] { float.NaN, float.NaN, float.NaN, float.NaN } },
            bounds, context, 512, 25), "usable");
        RequireInvalid(() => TerrainMeshBuilder.Build(raster with { Elevations = new[] { 100f } }, bounds, context, 512, 25), "matching");

        var hole = Enumerable.Repeat(100f, 25).ToArray();
        hole[12] = float.NaN;
        RequireInvalid(() => TerrainMeshBuilder.Build(Raster("EPSG:25832", 5, 5, hole), bounds, context, 2, 25), "usable");

        Console.WriteLine("Terrain mesh CRS, localization, absolute elevation, NoData, and boundary checks passed.");
    }

    private static TerrainRasterData Raster(string srs, int width, int height, float[] elevations) =>
        new("test", srs, width, height, new Coordinate2D(0, 0), new Coordinate2D(1, 0), new Coordinate2D(0, -1), null, elevations);

    private static SpatialContext2D Context(string srs, Coordinate2D origin) =>
        new(srs, new BoundingBox2D(0, 0, 1, 1), new BoundingBox2D(0, 0, 1, 1), null, origin, false, new());

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireInvalid(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"Expected a clear '{expectedMessage}' error.");
    }
}
