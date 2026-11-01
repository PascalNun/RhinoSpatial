using System.Net;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class SourceCompatibilityChecks
{
    private static int _checks;
    internal static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Check(string label, Func<Task> action)
    { await action(); _checks++; Console.WriteLine("PASS " + label); }
    internal sealed class Handler(Func<Uri, string> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();
        public Dictionary<string, byte[]> Payloads { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = Payloads.TryGetValue(request.RequestUri!.AbsolutePath, out var bytes) ? new ByteArrayContent(bytes) : new StringContent(response(request.RequestUri!), Encoding.UTF8, "application/json"), RequestMessage = request });
        }
    }
    private static string Feature(int id) => "{\"type\":\"Feature\",\"id\":" + id + ",\"properties\":{},\"geometry\":{\"type\":\"Point\",\"coordinates\":[13,52]}}";
    private static string Page(int[] ids, string? next = null, int? total = null) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", ids.Select(Feature)) + "]" +
        (total.HasValue ? ",\"numberMatched\":" + total : "") +
        (next is null ? "" : ",\"links\":[{\"rel\":\"next\",\"href\":" + JsonSerializer.Serialize(next) + "}]") + "}";

    public static async Task RunAsync()
    {
        await Check("JSON pages resolve relative next links and deduplicate IDs", async () =>
        {
            var pages = new Dictionary<string,string> { ["https://fixture.test/items"] = Page(new[]{1,2},"?page=2"), ["https://fixture.test/items?page=2"] = Page(new[]{2,3}) };
            var result = await FeatureCollectionPager.LoadAsync("https://fixture.test/items", (url,_) => Task.FromResult(pages[url]),0);
            Require(result.PageCount == 2 && GeoJsonReader.ReadFeatures(result.Text,"test").Count == 3,"Wrong merged page result.");
        });
        await Check("user feature cap stops before the next request", async () =>
        {
            var calls=0;
            var result = await FeatureCollectionPager.LoadAsync("https://fixture.test/items",(_,_)=>{calls++;return Task.FromResult(Page(new[]{1,2},"?page=2"));},1);
            Require(calls==1 && GeoJsonReader.ReadFeatures(result.Text,"test").Count==1 && result.StatusNote.Contains("Max Features"),"Cap was not applied.");
        });
        await Check("repeated pages fail instead of returning a partial selection", async () =>
        {
            try { await FeatureCollectionPager.LoadAsync("https://fixture.test/items",(_,_)=>Task.FromResult(Page(new[]{1},"?page=2")),0); }
            catch(InvalidDataException){return;} throw new Exception("Repeated page accepted.");
        });
        await Check("missing next link for a known incomplete collection fails", async () =>
        {
            try { await FeatureCollectionPager.LoadAsync("https://fixture.test/items",(_,_)=>Task.FromResult(Page(new[]{1},total:2)),0); }
            catch(InvalidDataException){return;} throw new Exception("Incomplete result accepted.");
        });
        await Check("cancellation propagates", async () =>
        {
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            try {await FeatureCollectionPager.LoadAsync("https://fixture.test/items",(_,_)=>Task.FromResult(Page(new[]{1})),0,cancellationToken:cancel.Token);}
            catch(OperationCanceledException){return;}throw new Exception("Cancellation ignored.");
        });
        await Check("WFS 2 pages advance by the server's actual returned count", async () =>
        {
            var handler=new Handler(url=>{
                var query=url.Query;
                return query.Contains("STARTINDEX=2")?Page(new[]{3},total:3):query.Contains("STARTINDEX=1")?Page(new[]{2},total:3):Page(new[]{1},total:3);
            });
            using var http=new HttpClient(handler);
            var client=new WfsClient(http);
            var result=await client.LoadFeaturesWithStatusAsync(new WfsRequestOptions{BaseUrl="https://fixture.test/wfs",GetFeatureBaseUrl="https://fixture.test/wfs",TypeName="buildings",Version="2.0.0"});
            Require(result.Features.Count==3 && handler.Requests.Count==3,"WFS paging failed.");
        });
        await Check("GML pages preserve feature IDs, namespaces and geometry", async () =>
        {
            string Gml(int id,string next="")=>"<wfs:FeatureCollection xmlns:wfs='http://www.opengis.net/wfs/2.0' xmlns:gml='http://www.opengis.net/gml/3.2' xmlns:f='urn:fixture' "+next+"><wfs:member><f:feature gml:id='f"+id+"'><f:geometry><gml:Point><gml:pos>13 52</gml:pos></gml:Point></f:geometry></f:feature></wfs:member></wfs:FeatureCollection>";
            var result=await FeatureCollectionPager.LoadAsync("https://fixture.test/wfs",(url,_)=>Task.FromResult(url.Contains("page=2")?Gml(2):Gml(1,"next='?page=2'")),0);
            var features=GmlReader.ReadFeatures(result.Text,"test");
            Require(features.Count==2 && features[1].Id=="f2" && features[1].Geometry.Points.Count==1,"GML merge lost geometry.");
        });
        await Check("landing page follows data and items links without fetching features", async () =>
        {
            var handler=new Handler(url=>url.AbsolutePath=="/api/"?"{\"links\":[{\"rel\":\"data\",\"href\":\"datasets\"}]}":"{\"collections\":[{\"id\":\"buildings\",\"title\":\"Buildings\",\"links\":[{\"rel\":\"items\",\"href\":\"features/buildings\",\"type\":\"application/geo+json\"}],\"extent\":{\"spatial\":{\"bbox\":[[13,52,14,53]]}}}]}");
            using var http=new HttpClient(handler);var client=new OgcApiFeaturesClient(http);
            var result=await client.TryDiscoverAsync("https://fixture.test/api/");
            Require(result?.Count==1 && result[0].ItemsUrl=="https://fixture.test/api/features/buildings" && result[0].Wgs84Bounds?.MinX==13 && handler.Requests.Count==2,"Discovery followed wrong links.");
        });
        await Check("OGC items URL is not requested until a bounding box is attached", async () =>
        {
            var handler=new Handler(url=> { Require(url.Query.Contains("bbox="),"Unbounded items probe.");return Page(new[]{1}); });
            using var http=new HttpClient(handler);var client=new OgcApiFeaturesClient(http);
            var result=await client.LoadFeaturesWithStatusAsync("https://fixture.test/collections/a/items","a",new BoundingBox2D(12,51,14,53),5);
            Require(result.Features.Count==1 && handler.Requests.Count==1,"Unexpected items discovery request.");
        });
        await Check("feature total is retained when later pages omit it", async () =>
        {
            var calls = 0;
            try { await FeatureCollectionPager.LoadAsync("https://fixture.test/items", (_,_) => Task.FromResult(++calls == 1 ? Page(new[]{1}, "?page=2", 3) : Page(new[]{2})), 0); }
            catch (InvalidDataException) { return; }
            throw new Exception("An incomplete later page hid the original matching total.");
        });
        await Check("collection fallback paths retain query parameters correctly", async () =>
        {
            using var http = new HttpClient(new Handler(_ => "{\"collections\":[{\"id\":\"a\"}]}"));
            var collections = await new OgcApiFeaturesClient(http).TryDiscoverAsync("https://fixture.test/collections?f=json");
            Require(collections![0].ItemsUrl == "https://fixture.test/collections/a/items?f=json", "Collection path was appended to a query string.");
        });
        await Check("single GeoJSON Feature downloads use the ordinary reader", () =>
        {
            Require(GeoJsonReader.ReadFeatures(Feature(1), "download").Count == 1, "Single Feature unsupported.");
            return Task.CompletedTask;
        });
        await Check("relative paging links resolve against the final redirected response URL", async () =>
        {
            var result = await FeatureCollectionPager.LoadHttpAsync("https://fixture.test/old",
                (url,_) => Task.FromResult(url.Contains("page2")
                    ? new FeaturePageContent(Page(new[]{2}), new Uri(url))
                    : new FeaturePageContent(Page(new[]{1},"page2"),new Uri("https://fixture.test/new/page1"))),0);
            Require(result.PageCount == 2, "Redirected paging did not resolve.");
        });
        await Check("point and wrapped collection extents do not break API discovery", async () =>
        {
            using var http = new HttpClient(new Handler(_=>"{\"collections\":[{\"id\":\"point\",\"extent\":{\"spatial\":{\"bbox\":[[13,52,13,52]]}}},{\"id\":\"wrapped\",\"extent\":{\"spatial\":{\"bbox\":[[177,-20,-178,-15]]}}}]}"));
            var collections = await new OgcApiFeaturesClient(http).TryDiscoverAsync("https://fixture.test/collections");
            Require(collections?.Count == 2 && collections[1].Wgs84Bounds?.MinX == -180,"Valid collection extent rejected.");
        });
        await Check("identical anonymous records remain separate features", async () =>
        {
            var body = Page(new[]{1,1},total:2).Replace("\"id\":1,", "");
            var result = await FeatureCollectionPager.LoadAsync("https://fixture.test/items", (_,_)=>Task.FromResult(body),0);
            Require(GeoJsonReader.ReadFeatures(result.Text,"test").Count == 2 && result.StatusNote.Contains("without stable IDs"),"Anonymous records were silently collapsed.");
        });
        await Check("GeoJSON pages cannot silently mix coordinate systems", async () =>
        {
            var first = Page(new[]{1},"?page=2");
            var second = Page(new[]{2}).Replace("\"features\":", "\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"EPSG:3857\"}},\"features\":");
            try { await FeatureCollectionPager.LoadAsync("https://fixture.test/items", (url,_)=>Task.FromResult(url.Contains("page=2")?second:first),0); throw new Exception("Mixed CRS accepted."); }
            catch (InvalidDataException) { }
        });
        await RunDownloadAndTerrainChecksAsync();
        Console.WriteLine($"Source compatibility checks passed: {_checks}");
    }
    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var file in files) { using var writer = new StreamWriter(archive.CreateEntry(file.Name).Open()); writer.Write(file.Text); }
        return output.ToArray();
    }

    private static async Task RunDownloadAndTerrainChecksAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rhinospatial-compatibility-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new Handler(_ => throw new Exception("Unexpected network request."));
            using var http = new HttpClient(handler);
            var vector = new SourceDownloadCache(AtomDownloadKind.Vector, http, directory);
            var link = new Lod2AtomLink(new Uri("https://fixture.test/download"), "vector", "", null);
            handler.Payloads["/download"] = Encoding.UTF8.GetBytes(Page(new[]{1,2}));
            await Check("extensionless downloads detect GeoJSON content and reuse validated cache", async () =>
            {
                var first = await vector.DownloadAsync(link);
                var second = await new SourceDownloadCache(AtomDownloadKind.Vector, http, directory).DownloadAsync(link);
                Require(first.FilePath.EndsWith(".geojson") && second.UsedCache && handler.Requests.Count == 1, "Direct-file cache failed.");
            });
            await Check("projected Shapefile CRS uses outer WKT authority, not ellipsoid EPSG", () =>
            {
                var shape = Path.Combine(directory,"projection.shp"); File.WriteAllBytes(shape,Array.Empty<byte>());
                const string wkt = "PROJCS[\"ETRS89 / UTM zone 33N\",GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"6258\"]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4258\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",15],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1],AUTHORITY[\"EPSG\",\"25833\"]]";
                File.WriteAllText(Path.ChangeExtension(shape,".prj"),wkt);
                Require(ShapefileFeatureReader.ReadSourceMetadata(shape).SourceSrs == "EPSG:25833","Nested EPSG authority used as the source CRS.");
                return Task.CompletedTask;
            });
            await Check("uppercase Shapefile companions normalize within temporary extraction", () =>
            {
                var archive = Path.Combine(directory,"uppercase.zip");
                File.WriteAllBytes(archive,Zip(("A.SHP","shape"),("A.DBF","dbf"),("A.SHX","shx"),("A.PRJ","prj")));
                using var lease = SourceFilesLease.Open(archive);
                var shape = lease.Files.Single(f=>f.EndsWith(".SHP"));
                Require(lease.Files.Contains(Path.ChangeExtension(shape,".prj")),"PRJ case was not normalized.");
                return Task.CompletedTask;
            });
            await Check("archive traversal is rejected before extraction", async () =>
            {
                handler.Payloads["/unsafe.zip"] = Zip(("../outside.geojson",Page(new[]{1})));
                try { await vector.DownloadAsync(link with { Url = new Uri("https://fixture.test/unsafe.zip") }); }
                catch (InvalidDataException) { return; } throw new Exception("Unsafe ZIP accepted.");
            });
            await Check("Shapefile download requires companion files", async () =>
            {
                handler.Payloads["/shape.zip"] = Zip(("a.shp","not a shapefile"),("a.dbf","dbf"));
                try { await vector.DownloadAsync(link with { Url = new Uri("https://fixture.test/shape.zip") }); }
                catch (InvalidDataException) { return; } throw new Exception("Incomplete Shapefile accepted.");
            });
            await Check("downloaded CityJSON declares CRS and preserves its source coordinates", async () =>
            {
                const string city = "{\"type\":\"CityJSON\",\"version\":\"2.0\",\"metadata\":{\"referenceSystem\":\"https://www.opengis.net/def/crs/EPSG/0/25833\"},\"CityObjects\":{},\"vertices\":[[391000,5820000,42]]}";
                handler.Payloads["/city.json"] = Encoding.UTF8.GetBytes(city);
                var building = new SourceDownloadCache(AtomDownloadKind.Buildings,http,Path.Combine(directory,"buildings"));
                var result = await building.DownloadAsync(link with { Url = new Uri("https://fixture.test/city.json") });
                var metadata = CityJsonReader.ReadSourceMetadata(File.ReadAllText(result.FilePath));
                Require(metadata.SrsName == "EPSG:25833" && metadata.BoundingBox?.MinX == 391000, "CityJSON source metadata changed.");
                handler.Payloads["/missing.json"] = Encoding.UTF8.GetBytes(city.Replace("\"metadata\":", "\"ignored\":"));
                try { await building.DownloadAsync(link with { Url = new Uri("https://fixture.test/missing.json") }); }
                catch (InvalidDataException) { return; } throw new Exception("Missing downloaded CRS accepted.");
            });
            const string start = "<feed xmlns='http://www.w3.org/2005/Atom' xmlns:georss='http://www.georss.org/georss' xmlns:gml='http://www.opengis.net/gml'>";
            string Entry(string name,int x) => "<entry><title>"+name+"</title><category term='EPSG:25833'/><georss:where><gml:Envelope srsName='EPSG:25833'><gml:lowerCorner>"+x+" 5820000</gml:lowerCorner><gml:upperCorner>"+(x+3)+" 5820003</gml:upperCorner></gml:Envelope></georss:where><link rel='enclosure' type='text/csv' href='"+name+".zip'/></entry>";
            string Grid(int x)
            {
                var rows = new List<string>();
                for(var y=0;y<3;y++) for(var column=0;column<3;column++)
                    rows.Add($"{x+column};{5820000+y};{(column == 1 && y == 1 ? "NaN" : "10")}");
                return string.Join("\n",rows);
            }
            var terrainHandler = new Handler(url => url.AbsolutePath == "/terrain" ? start + Entry("left",391000) + Entry("right",391003) + "</feed>" : throw new Exception("Unexpected terrain request."));
            terrainHandler.Payloads["/left.zip"] = Zip(("left.csv",Grid(391000)));
            terrainHandler.Payloads["/right.zip"] = Zip(("right.csv",Grid(391003)));
            using var terrainHttp = new HttpClient(terrainHandler);
            var context = Lod2AtomChecks.Context("EPSG:25833",new BoundingBox2D(391000,5820000,391005,5820002));
            await Check("terrain ATOM joins adjacent CSV grids and preserves NoData holes", async () =>
            {
                var terrain = await new TerrainAtomLoader(terrainHttp,Path.Combine(directory,"terrain")).TryLoadAsync("https://fixture.test/terrain", context);
                Require(terrain?.Raster.Width == 6 && terrain.Raster.Height == 3 && float.IsNaN(terrain.Raster.Elevations[7]),"Terrain mosaic or NoData changed.");
                var mesh = TerrainMeshBuilder.Build(terrain!.Raster,terrain.Bounds,context,512,0);
                Require(mesh.Faces.Count > 0, "Terrain produced no connected surface.");
            });
            await Check("terrain mosaics reject conflicting elevations and mismatched grids", () =>
            {
                var raster = new TerrainRasterData("test","EPSG:25833",2,2,new Coordinate2D(0,1),new Coordinate2D(1,0),new Coordinate2D(0,-1),double.NaN,new[]{1f,1f,1f,1f});
                try { TerrainRasterMosaic.Merge(new[]{raster,raster with { Elevations = new[]{2f,2f,2f,2f} }}); }
                catch (InvalidDataException) {
                    try { TerrainRasterMosaic.Merge(new[]{raster,raster with { OffsetVectorX = new Coordinate2D(2,0) }}); }
                    catch (InvalidDataException) {return Task.CompletedTask;}
                }
                throw new Exception("Incompatible terrain grids accepted.");
            });
            await Check("ATOM reference metadata reads no archives and infers native CRS", async () =>
            {
                var reference = await new RemoteReferenceMetadataReader(terrainHttp).TryReadAsync("https://fixture.test/terrain");
                Require(reference?.SourceKind == "ATOM" && reference.SrsName == "EPSG:25833" && reference.Wgs84BoundingBox is not null,"ATOM reference metadata missing.");
                Require(terrainHandler.Requests.Count(r=>r.AbsolutePath.EndsWith(".zip")) == 2,"Reference metadata downloaded geometry.");
            });
            await Check("OGC API items reference reads parent collection metadata only", async () =>
            {
                var metadataHandler = new Handler(url => {
                    Require(url.AbsolutePath == "/collections/a", "Reference requested feature data.");
                    return "{\"id\":\"a\",\"extent\":{\"spatial\":{\"bbox\":[[13,52,14,53]]}}}";
                });
                using var metadataHttp = new HttpClient(metadataHandler);
                var reference = await new RemoteReferenceMetadataReader(metadataHttp).TryReadAsync("https://fixture.test/collections/a/items");
                Require(reference?.Wgs84BoundingBox?.MinX == 13 && metadataHandler.Requests.Count == 1,"Reference did not use the collection extent.");
            });
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }

}
