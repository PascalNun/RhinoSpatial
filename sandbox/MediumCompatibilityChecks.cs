using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BitMiracle.LibTiff.Classic;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class MediumCompatibilityChecks
{
    private static int _count;
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double actual, double expected) => Require(Math.Abs(actual - expected) < 1e-7, $"Expected {expected}, got {actual}.");
    private static void Fails(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException) { return; } throw new Exception("Invalid source accepted."); }
    private static async Task Check(string label, Func<Task> action) { await action(); _count++; Console.WriteLine("PASS " + label); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    internal const string Wkt = "PROJCS[\"ETRS89 / UTM zone 33N\",GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",15],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1],AUTHORITY[\"EPSG\",\"25833\"]]";
    internal static string Geometry(string lod, string indices = "[[[0,1,2]]]") => "{\"type\":\"MultiSurface\",\"lod\":" + JsonSerializer.Serialize(lod) + ",\"boundaries\":" + indices + "}";
    internal static string Model(string geometry) => "{\"type\":\"CityJSON\",\"version\":\"2.0\",\"metadata\":{\"referenceSystem\":\"https://www.opengis.net/def/crs/EPSG/0/25833\"},\"CityObjects\":{\"building\":{\"type\":\"Building\",\"geometry\":[" + geometry + "]}},\"vertices\":[[0,0,0],[2,0,0],[0,2,1]]}";
    internal static string Sequence => "{\"type\":\"CityJSON\",\"version\":\"2.0\",\"metadata\":{\"referenceSystem\":\"https://www.opengis.net/def/crs/EPSG/0/25833\"},\"transform\":{\"scale\":[2,3,4],\"translate\":[10,20,30]},\"CityObjects\":{},\"vertices\":[]}\n" +
        Model(Geometry("2.2")).Replace("\"CityJSON\"", "\"CityJSONFeature\"").Replace("\"metadata\":{\"referenceSystem\":\"https://www.opengis.net/def/crs/EPSG/0/25833\"},", "") + "\n";
    internal static string Capabilities(string srs = "EPSG:25833", string top = "0 4", string scale = "3571.4285714285716", string extra = "") => $"""
        <Capabilities xmlns="http://www.opengis.net/wmts/1.0" xmlns:ows="http://www.opengis.net/ows/1.1" xmlns:xlink="http://www.w3.org/1999/xlink">
        <ows:OperationsMetadata><ows:Operation name="GetTile"><ows:DCP><ows:HTTP><ows:Get xlink:href="https://fixture.test/tiles"/></ows:HTTP></ows:DCP></ows:Operation></ows:OperationsMetadata>
        <Contents><Layer><ows:Identifier>ortho</ows:Identifier><ows:Title>Ortho</ows:Title><ows:WGS84BoundingBox><ows:LowerCorner>-180 -90</ows:LowerCorner><ows:UpperCorner>180 90</ows:UpperCorner></ows:WGS84BoundingBox><Style isDefault="true"><ows:Identifier>default</ows:Identifier></Style><Format>image/png</Format>{extra}<TileMatrixSetLink><TileMatrixSet>native</TileMatrixSet></TileMatrixSetLink></Layer>
        <TileMatrixSet><ows:Identifier>native</ows:Identifier><ows:SupportedCRS>{srs}</ows:SupportedCRS><TileMatrix><ows:Identifier>level</ows:Identifier><ScaleDenominator>{scale}</ScaleDenominator><TopLeftCorner>{top}</TopLeftCorner><TileWidth>2</TileWidth><TileHeight>2</TileHeight><MatrixWidth>2</MatrixWidth><MatrixHeight>2</MatrixHeight></TileMatrix></TileMatrixSet></Contents></Capabilities>
        """;
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> fetch) : HttpMessageHandler
    { public List<Uri> Requests { get; } = new(); protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!); return Task.FromResult(fetch(request)); } }
    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RhinoSpatial-Medium-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            await Check("one suitable LoD is selected without overlaying alternate representations", () => Sync(() =>
            { var b = CityJsonReader.ReadBuildings(Model(Geometry("1") + "," + Geometry("2.2") + "," + Geometry("3")), "test").Single(); Require(b.Surfaces.Count == 1 && b.Attributes["source_lod"] == "2.2", "Mixed LoDs imported."); }));
            await Check("same-LoD geometry parts are retained and higher-only LoDs have a deterministic fallback", () => Sync(() =>
            { var b = CityJsonReader.ReadBuildings(Model(Geometry("3") + "," + Geometry("3") + "," + Geometry("4")), "test").Single(); Require(b.Surfaces.Count == 2 && b.Attributes["source_lod"] == "3", "Geometry part or fallback lost."); }));
            await Check("invalid indices fail instead of creating partial rings", () => Sync(() => Fails(() => CityJsonReader.ReadBuildings(Model(Geometry("2", "[[[0,1,99]]]")), "test"))));
            await Check("sequence records inherit header transform and source CRS", () => Sync(() =>
            { using var s = Stream(Sequence); var b = CityJsonReader.ReadBuildings(s, "test").Single(); Near(b.Surfaces[0].OuterPoints[1].X, 14); Near(b.Surfaces[0].OuterPoints[2].Y, 26); Near(b.Surfaces[0].OuterPoints[2].Z, 34); var m = CityJsonReader.ReadSourceMetadata(Sequence); Require(m.SrsName == "EPSG:25833", "Header CRS lost."); Near(m.BoundingBox!.MaxY, 26); }));
            await Check("sequence input rejects truncated later records and repeated building ids", () => Sync(() =>
            { Fails(() => CityJsonReader.ReadBuildings(Sequence + "{", "test")); Fails(() => CityJsonReader.ReadBuildings(Sequence + Sequence.Split('\n')[1] + "\n", "test")); }));
            await Check("template vertices use row-major matrix then transformed instance anchor", () => Sync(() =>
            {
                var model = Model("{\"type\":\"GeometryInstance\",\"template\":0,\"boundaries\":[0],\"transformationMatrix\":[0,-2,0,5,3,0,0,7,0,0,4,9,0,0,0,1]}");
                model = model.Replace("\"vertices\":", "\"transform\":{\"scale\":[10,10,10],\"translate\":[100,200,300]},\"geometry-templates\":{\"templates\":[" + Geometry("2.2") + "],\"vertices-templates\":[[0,0,0],[2,0,0],[0,2,1]]},\"vertices\":");
                var b = CityJsonReader.ReadBuildings(model, "test").Single(); Near(b.Surfaces[0].OuterPoints[1].X, 105); Near(b.Surfaces[0].OuterPoints[1].Y, 213); Near(b.Surfaces[0].OuterPoints[2].Z, 313);
                var m = CityJsonReader.ReadSourceMetadata(model); Require(m.BoundingBox!.MaxY >= 213, "Template extent omitted.");
            }));
            await Check("sequence cancellation propagates", () => Sync(() =>
            { using var token = new CancellationTokenSource(); token.Cancel(); using var s = Stream(Sequence); try { CityJsonReader.ReadBuildings(s, "test", cancellationToken: token.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { } }));
            await Check("direct sequence downloads validate and cache through shared building path", async () =>
            { using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Sequence) })); var cache = new SourceDownloadCache(AtomDownloadKind.Buildings, http, Path.Combine(directory, "downloads")); var r = await cache.DownloadAsync(new(new Uri("https://fixture.test/model.city.jsonl"), "seq", "", null)); using var s = File.OpenRead(r.FilePath); Require(CityJsonReader.ReadBuildings(s, "test").Count == 1, "Sequence did not survive caching."); });
            var png = Path.Combine(directory, "rotated.png"); var rgba = Enumerable.Range(0, 64).Select(i => (byte)(i * 3)).ToArray(); RasterImageCodec.WritePng(png, 4, 4, rgba); File.WriteAllText(Path.ChangeExtension(png, ".pgw"), "2\n1\n0.5\n-3\n100\n200\n"); File.WriteAllText(Path.ChangeExtension(png, ".prj"), Wkt);
            await Check("world-file centers, rotation and shear preserve the true footprint", () => Sync(() =>
            { var info = GeoTiffReader.ReadImageInfo(png); var g = info.GeoTransform!; Near(g.At(.5, .5).X, 100); Near(g.At(.5, .5).Y, 200); Near(g.At(4, 4).X, 108.75); Near(g.At(4, 4).Y, 193); Near(g.Pixel(100, 200).X, .5); Require(info.SrsName == "EPSG:25833", "Sidecar CRS lost."); }));
            await Check("affine image clipping preserves UVs and localization", () => Sync(() =>
            { var info = GeoTiffReader.ReadImageInfo(png); var context = Lod2AtomChecks.Context("EPSG:25833", new(100, 191, 106, 202)); var mesh = RasterImageMeshBuilder.Build(info, context); Require(mesh.Faces.Count > 0, "Clipped image disappeared."); foreach (var v in mesh.Vertices) { var native = info.GeoTransform!.At(v.U * 4, (1 - v.V) * 4); Near(native.X - context.PlacementOrigin.X, v.X); Near(native.Y - context.PlacementOrigin.Y, v.Y); } Require(mesh.CoverageRatio < 1, "Clip ratio not retained."); }));
            await Check("affine imagery reprojects through the shared context without bbox scaling", () => Sync(() =>
            {
                var source = new RasterGeoTransform(2, 0.5, 391300, 0.5, -2, 5820100); var native = source.Bounds(4, 4);
                var bounds = Lod2AtomFeedReader.TransformBounds(new("EPSG:25833", native), "EPSG:3857"); var context = Lod2AtomChecks.Context("EPSG:3857", bounds);
                var info = new GeoReferencedRasterInfo("test", "EPSG:25833", 4, 4, native, 0) { GeoTransform = source }; var mesh = RasterImageMeshBuilder.Build(info, context);
                Require(mesh.Faces.Count > 0, "Reprojected image disappeared."); foreach (var v in mesh.Vertices) { var p = source.At(v.U * 4, (1 - v.V) * 4); Require(SpatialReferenceTransform.TryTransformXY(info.SrsName, context.ResolvedSrs, p.X, p.Y, out var x, out var y), "Projection failed."); Near(v.X, x - context.PlacementOrigin.X); Near(v.Y, y - context.PlacementOrigin.Y); }
            }));
            await Check("global geographic imagery projects only the selected window with correct UVs",()=>Sync(()=>
            {
                var g=new RasterGeoTransform(1,0,-180,0,-1,90);var bounds=Lod2AtomFeedReader.TransformBounds(new("EPSG:4326",new(13.3,52.4,13.4,52.5)),"EPSG:3857");var context=Lod2AtomChecks.Context("EPSG:3857",bounds);
                var info=new GeoReferencedRasterInfo("global","EPSG:4326",360,180,new(-180,-90,180,90),0){GeoTransform=g};var mesh=RasterImageMeshBuilder.Build(info,context);Require(mesh.Faces.Count>0&&mesh.CoverageRatio<0.001,"Global image was not windowed.");
                foreach(var v in mesh.Vertices){var p=g.At(v.U*360,(1-v.V)*180);SpatialReferenceTransform.TryTransformXY("EPSG:4326","EPSG:3857",p.X,p.Y,out var x,out var y);Near(v.X,x-context.PlacementOrigin.X);Near(v.Y,y-context.PlacementOrigin.Y);}
            }));
            await Check("sidecar edits invalidate cached metadata even with unchanged raster", () => Sync(() =>
            { var old = GeoTiffInfoCache.GetOrRead(png); File.WriteAllText(Path.ChangeExtension(png, ".pgw"), "2\n1\n0.5\n-3\n110\n200\n"); var updated = GeoTiffInfoCache.GetOrRead(png); Near(updated.GeoTransform!.C - old.GeoTransform!.C, 10); }));
            await Check("world file without a CRS and singular affine transforms fail", () => Sync(() =>
            { var prj = Path.ChangeExtension(png, ".prj"); File.Move(prj, prj + ".off"); Fails(() => GeoTiffReader.ReadImageInfo(png)); File.Move(prj + ".off", prj); Fails(() => new RasterGeoTransform(1, 2, 0, 2, 4, 0).Validate()); }));
            await Check("terrain uses affine pixel centers including rotated rows", () => Sync(() =>
            { var g = new RasterGeoTransform(2, 1, 100, 1, -2, 200); var raster = new TerrainRasterData("test", "EPSG:25833", 2, 2, new(0, 0), new(1, 0), new(0, -1), null, new float[] { 1, 2, 3, 4 }) { GeoTransform = g }; var mesh = TerrainMeshBuilder.Build(raster, g.Bounds(2, 2), Lod2AtomChecks.Context("EPSG:25833", g.Bounds(2, 2)), 2, 0); Near(mesh.Vertices[0].X, g.At(.5, .5).X - 100); Near(mesh.Vertices[2].Y, g.At(.5, 1.5).Y - 196); }));
            await Check("embedded affine GeoTIFF and PixelIsPoint retain centers and projected CRS", () => Sync(() =>
            {
                foreach (var point in new[] { false, true })
                {
                    var path = Path.Combine(directory, "affine-" + point + ".tif"); WriteAffineTiff(path, point);
                    var info = GeoTiffReader.ReadImageInfo(path); Require(info.SrsName == "EPSG:25833", "Geographic key overrode projected CRS.");
                    Near(info.GeoTransform!.At(.5, .5).X, point ? 100 : 101.5); Near(info.GeoTransform.At(.5, .5).Y, point ? 200 : 199.5);
                    var raster = TerrainRasterReader.ReadRaster(path, "test", info.SrsName); Near(raster.Origin.X, point ? 100 : 101.5); Require(raster.Elevations[3] == 4, "TIFF samples changed.");
                }
            }));
            await Check("small geographic affine resolutions remain invertible", () => Sync(() => new RasterGeoTransform(1e-8, 0, 13, 0, -1e-8, 52).Validate()));
            var caps = WmtsClient.ReadCapabilities(XDocument.Parse(Capabilities()), new("https://fixture.test/wmts.xml")); var plan = WmtsClient.Plan(caps, "ortho", Lod2AtomChecks.Context("EPSG:25833", new(0, 0, 4, 4)));
            await Check("projected WMTS selects exact tile ranges and native mosaic transform", () => Sync(() =>
            { Require(plan.Tiles.Count == 4 && plan.Width == 4 && plan.Height == 4, "Incorrect tile range."); Near(plan.Transform.At(0, 0).Y, 4); Near(plan.Transform.At(4, 4).X, 4); Require(plan.Tiles[3].Url.Contains("TileRow=1") && plan.Tiles[3].Url.Contains("TileCol=1"), "Wrong KVP tile indices."); }));
            await Check("geographic WMTS top-left respects authority axis order", () => Sync(() =>
            { var c = WmtsClient.ReadCapabilities(XDocument.Parse(Capabilities("urn:ogc:def:crs:EPSG::4326", "90 -180", "279541132.0143589")), new("https://fixture.test/wmts.xml")); Near(c.Sets["native"].Matrices[0].TopLeft.X, -180); Near(c.Sets["native"].Matrices[0].TopLeft.Y, 90); }));
            await Check("REST tile templates use default dimensions and URL-escaped identifiers", () => Sync(() =>
            { var c = WmtsClient.ReadCapabilities(XDocument.Parse(Capabilities(extra: "<Dimension><ows:Identifier>Time</ows:Identifier><Default>2024</Default></Dimension><ResourceURL resourceType=\"tile\" format=\"image/png\" template=\"tiles/{Time}/{TileMatrix}/{TileRow}/{TileCol}.png\"/>")), new("https://fixture.test/wmts.xml")); var p = WmtsClient.Plan(c, "ortho", Lod2AtomChecks.Context("EPSG:25833", new(0, 0, 4, 4))); Require(p.Tiles[0].Url == "https://fixture.test/tiles/2024/level/0/0.png", "REST template not resolved."); }));
            await Check("matrix limits exclude unadvertised tiles and report clipped coverage", () => Sync(() =>
            { var text = Capabilities().Replace("</TileMatrixSetLink>", "<TileMatrixSetLimits><TileMatrixLimits><TileMatrix>level</TileMatrix><MinTileRow>0</MinTileRow><MaxTileRow>0</MaxTileRow><MinTileCol>0</MinTileCol><MaxTileCol>1</MaxTileCol></TileMatrixLimits></TileMatrixSetLimits></TileMatrixSetLink>"); var p = WmtsClient.Plan(WmtsClient.ReadCapabilities(XDocument.Parse(text), new("https://fixture.test/wmts.xml")), "ortho", Lod2AtomChecks.Context("EPSG:25833", new(0, 0, 4, 4))); Require(p.Tiles.Count == 2 && p.CoverageClipped, "Limits ignored."); }));
            await Check("unknown CRS, oversized selections and ambiguous dimensions fail explicitly", () => Sync(() =>
            {
                var unsupported = WmtsClient.ReadCapabilities(XDocument.Parse(Capabilities("EPSG:999999")), new("https://fixture.test/wmts.xml")); Fails(() => WmtsClient.Plan(unsupported, "ortho", Lod2AtomChecks.Context("EPSG:25833", new(0, 0, 4, 4))));
                var wide = WmtsClient.ReadCapabilities(XDocument.Parse(Capabilities().Replace("<MatrixWidth>2", "<MatrixWidth>100").Replace("<MatrixHeight>2", "<MatrixHeight>100")), new("https://fixture.test/wmts.xml")); Fails(() => WmtsClient.Plan(wide, "ortho", Lod2AtomChecks.Context("EPSG:25833", new(0, -196, 200, 4))));
                var dim = WmtsClient.ReadCapabilities(XDocument.Parse(Capabilities(extra: "<Dimension><ows:Identifier>Time</ows:Identifier><Value>2023</Value><Value>2024</Value></Dimension>")), new("https://fixture.test/wmts.xml")); Fails(() => WmtsClient.Plan(dim, "ortho", Lod2AtomChecks.Context("EPSG:25833", new(0, 0, 4, 4))));
            }));
            var tile = Path.Combine(directory, "tile.png"); RasterImageCodec.WritePng(tile, 2, 2, Enumerable.Repeat(new byte[] { 12, 34, 56, 128 }, 4).SelectMany(x => x).ToArray()); var tileBytes = File.ReadAllBytes(tile);
            await Check("WMTS PNG mosaic preserves decoded pixels/alpha and reuses completed cache", async () =>
            { var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(tileBytes) }); using var http = new HttpClient(handler); var client = new WmtsClient(http, Path.Combine(directory, "wmts")); var first = await client.DownloadAsync(plan); var pixels = RasterImageCodec.DecodeRgba(File.ReadAllBytes(first.LocalFilePath), 4, 4); Require(pixels[0] == 12 && pixels[^1] == 128, "Mosaic pixel or alpha corrupted."); var second = await client.DownloadAsync(plan); Require(second.UsedCache && handler.Requests.Count == 4, "Completed mosaic was not reused."); });
            await Check("corrupt WMTS cache is reloaded and cancellation publishes no mosaic", async () =>
            {
                var folder = Path.Combine(directory, "corruption"); var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(tileBytes) }); using var http = new HttpClient(handler); var client = new WmtsClient(http, folder); var first = await client.DownloadAsync(plan);
                File.AppendAllText(first.LocalFilePath, "corrupt"); var second = await client.DownloadAsync(plan); Require(!second.UsedCache && handler.Requests.Count == 8, "Corrupt mosaic cache accepted.");
                using var token = new CancellationTokenSource(); token.Cancel(); try { await new WmtsClient(http, Path.Combine(directory, "cancelled")).DownloadAsync(plan, token.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
            });
            await Check("tile row/column order is preserved in the mosaic", async () =>
            {
                var images = new Dictionary<string, byte[]>();
                for (var row = 0; row < 2; row++) for (var col = 0; col < 2; col++)
                { var file = Path.Combine(directory, $"tile-{row}-{col}.png"); RasterImageCodec.WritePng(file, 2, 2, Enumerable.Repeat(new byte[] { (byte)(row * 100 + col * 20), 0, 0, 255 }, 4).SelectMany(x => x).ToArray()); images[$"{row},{col}"] = File.ReadAllBytes(file); }
                using var http = new HttpClient(new Handler(r =>
                { var q = r.RequestUri!.Query; var row = q.Contains("TileRow=1") ? 1 : 0; var col = q.Contains("TileCol=1") ? 1 : 0; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(images[$"{row},{col}"]) }; }));
                var image = await new WmtsClient(http, Path.Combine(directory, "ordering")).DownloadAsync(plan); var pixels = RasterImageCodec.DecodeRgba(File.ReadAllBytes(image.LocalFilePath), 4, 4);
                Require(pixels[0] == 0 && pixels[3 * 4] == 20 && pixels[(3 * 4) * 4] == 100 && pixels[(3 * 4 + 3) * 4] == 120, "Mosaic tiles were flipped or transposed.");
            });
            await Check("failed required WMTS tile does not publish a partial mosaic", async () =>
            { var folder = Path.Combine(directory, "failure"); using var http = new HttpClient(new Handler(r => r.RequestUri!.Query.Contains("TileRow=1") ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(tileBytes) })); try { await new WmtsClient(http, folder).DownloadAsync(plan); throw new Exception("Partial mosaic accepted."); } catch (HttpRequestException) { } Require(!Directory.EnumerateFiles(folder).Any(), "Partial output published."); });
            await Check("WMTS reference reads capabilities without requesting tile geometry", async () =>
            { var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Capabilities()) }); using var http = new HttpClient(handler); var metadata = await new RemoteReferenceMetadataReader(http).TryReadAsync("https://fixture.test/wmts.xml", "ortho"); Require(metadata?.SourceKind == "WMTS" && handler.Requests.Count == 1, "Reference fetched tiles."); });
            Console.WriteLine($"Medium compatibility checks passed: {_count}");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void WriteAffineTiff(string path, bool point)
    {
        using var writer = new BinaryWriter(File.Create(path));
        const uint image = 146, matrix = 150, keys = 278;
        writer.Write((ushort)0x4949); writer.Write((ushort)42); writer.Write((uint)8); writer.Write((ushort)11);
        void Entry(ushort tag, ushort type, uint count, uint value) { writer.Write(tag); writer.Write(type); writer.Write(count); writer.Write(value); }
        Entry(256, 3, 1, 2); Entry(257, 3, 1, 2); Entry(258, 3, 1, 8); Entry(259, 3, 1, 1); Entry(262, 3, 1, 1); Entry(273, 4, 1, image); Entry(277, 3, 1, 1); Entry(278, 4, 1, 2); Entry(279, 4, 1, 4); Entry(34264, 12, 16, matrix); Entry(34735, 3, 16, keys); writer.Write((uint)0);
        writer.Write(new byte[] { 1, 2, 3, 4 }); foreach (var v in new double[] { 2, 1, 0, 100, 1, -2, 0, 200, 0, 0, 1, 0, 0, 0, 0, 1 }) writer.Write(v);
        foreach (var v in new ushort[] { 1, 1, 0, 3, 1025, 0, 1, (ushort)(point ? 2 : 1), 2048, 0, 1, 4326, 3072, 0, 1, 25833 }) writer.Write(v);
    }

}
