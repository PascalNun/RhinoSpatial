using System.Globalization;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class MediumCompatibilityProbe
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("medium-probe wmts <capabilities-url> [layer srs minX,minY,maxX,maxY] | cityjson <file> | raster <file>");
        if (args[1] == "examples") { WriteExamples(args[2]); return; }
        if (args[1] == "wmts")
        {
            var client = new WmtsClient(); var caps = await client.TryLoadCapabilitiesAsync(args[2]) ?? throw new Exception("Not WMTS.");
            Console.WriteLine($"Layers: {caps.Layers.Count}; supported matrix sets: {caps.Sets.Count}");
            foreach (var layer in caps.Layers.Take(8)) Console.WriteLine($"{layer.Id} | {layer.Title} | {string.Join(",", layer.Links.Select(l => l.Set))}");
            if (args.Length < 6) return;
            var values = args[5].Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            var context = Lod2AtomChecks.Context(args[4], new(values[0], values[1], values[2], values[3]));
            var plan = WmtsClient.Plan(caps, args[3], context); var result = await client.DownloadAsync(plan);
            var mesh = RasterImageMeshBuilder.Build(result.Raster, context);
            Console.WriteLine(result.Status); Console.WriteLine($"Image: {result.LocalFilePath}; clipped mesh: {mesh.Vertices.Count} vertices, {mesh.Faces.Count} triangles");
        }
        else if (args[1] == "cityjson")
        {
            using var metadata = File.OpenRead(args[2]); var m = CityJsonReader.ReadSourceMetadata(metadata);
            using var input = File.OpenRead(args[2]); var buildings = CityJsonReader.ReadBuildings(input, "probe");
            Console.WriteLine($"{m.SrsName}; buildings: {buildings.Count}; surfaces: {buildings.Sum(b => b.Surfaces.Count)}");
            Console.WriteLine("Selected LoDs: " + string.Join(", ", buildings.Select(b => b.Attributes["source_lod"]).Distinct()));
        }
        else if (args[1] == "raster")
        {
            var info = GeoTiffReader.ReadImageInfo(args[2]); var context = Lod2AtomChecks.Context(info.SrsName, info.BoundingBox);
            var mesh = RasterImageMeshBuilder.Build(info, context);
            Console.WriteLine($"{info.SrsName}; {info.Width}x{info.Height}; affine: {info.GeoTransform}; mesh: {mesh.Faces.Count} triangles");
        }
        else throw new ArgumentException("Unknown medium probe mode.");
    }
    private static void WriteExamples(string directory)
    {
        Directory.CreateDirectory(directory);
        var faces = new[] { new[] { 0, 3, 2, 1 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 }, new[] { 4, 5, 6, 7 } };
        object Boundaries(int offset) => new[] { faces.Select(face => new[] { face.Select(i => i + offset).ToArray() }).ToArray() };
        object Geometry(string lod, int offset) => new { type = "Solid", lod, boundaries = Boundaries(offset) };
        var vertices = new[] { new[] { 0, 0, 0 }, new[] { 8, 0, 0 }, new[] { 8, 6, 0 }, new[] { 0, 6, 0 }, new[] { 0, 0, 10 }, new[] { 8, 0, 10 }, new[] { 8, 6, 10 }, new[] { 0, 6, 10 }, new[] { 0, 0, 0 }, new[] { 8, 0, 0 }, new[] { 8, 6, 0 }, new[] { 0, 6, 0 }, new[] { 0, 0, 12 }, new[] { 8, 0, 12 }, new[] { 8, 6, 14 }, new[] { 0, 6, 14 } };
        var objects = new Dictionary<string, object> { { "example-building", new { type = "Building", geometry = new[] { Geometry("1", 0), Geometry("2.2", 8) } } } };
        var metadata = new { referenceSystem = "https://www.opengis.net/def/crs/EPSG/0/25833", geographicalExtent = new[] { 391300d, 5820100d, 32d, 391308d, 5820106d, 46d } };
        var transform = new { scale = new[] { 1, 1, 1 }, translate = new[] { 391300, 5820100, 32 } };
        var model = new { type = "CityJSON", version = "2.0", metadata, transform, CityObjects = objects, vertices };
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        void Json(string name, object value) => File.WriteAllText(Path.Combine(directory, name), System.Text.Json.JsonSerializer.Serialize(value, options) + "\n");
        Json("building-lods.city.json", model);
        var header = new { type = "CityJSON", version = "2.0", metadata, transform, CityObjects = new Dictionary<string, object>(), vertices = Array.Empty<int[]>() };
        var feature = new { type = "CityJSONFeature", id = "example-building", CityObjects = objects, vertices };
        File.WriteAllText(Path.Combine(directory, "building-sequence.city.jsonl"), System.Text.Json.JsonSerializer.Serialize(header) + "\n" + System.Text.Json.JsonSerializer.Serialize(feature) + "\n");
        var instance = new { type = "GeometryInstance", template = 0, boundaries = new[] { 0 }, transformationMatrix = new[] { 8, 0, 0, 0, 0, 6, 0, 0, 0, 0, 12, 0, 0, 0, 0, 1 } };
        var template = new Dictionary<string, object>();
        template["type"] = "CityJSON"; template["version"] = "2.0"; template["metadata"] = metadata;
        template["CityObjects"] = new Dictionary<string, object> { { "template-building", new { type = "Building", geometry = new[] { instance } } } };
        template["vertices"] = new[] { new[] { 391300, 5820100, 32 } };
        template["geometry-templates"] = new Dictionary<string, object> { { "templates", new[] { Geometry("2.2", 0) } }, { "vertices-templates", new[] { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 0, 1, 0 }, new[] { 0, 0, 1 }, new[] { 1, 0, 1 }, new[] { 1, 1, 1 }, new[] { 0, 1, 1 } } } };
        Json("building-template.city.json", template);
        var pixels = new byte[4 * 4 * 4];
        for (var row = 0; row < 4; row++) for (var col = 0; col < 4; col++) { var i = (row * 4 + col) * 4; pixels[i] = (byte)(col < 2 ? 240 : 40); pixels[i + 1] = (byte)(row < 2 ? 180 : 40); pixels[i + 2] = (byte)(col < 2 ? 40 : 220); pixels[i + 3] = 255; }
        RasterImageCodec.WritePng(Path.Combine(directory, "rotated-raster.png"), 4, 4, pixels);
        File.WriteAllText(Path.Combine(directory, "rotated-raster.pgw"), "2\n0.5\n1\n-2\n391300\n5820106\n");
        File.WriteAllText(Path.Combine(directory, "rotated-raster.prj"), MediumCompatibilityChecks.Wkt + "\n");
        Console.WriteLine("Wrote synthetic EPSG:25833 examples to " + Path.GetFullPath(directory));
    }

}
