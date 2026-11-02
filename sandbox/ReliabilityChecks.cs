using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class ReliabilityChecks
{
    private const string WmsXml = "<WMS_Capabilities version='1.3.0'><Capability><Layer><Title>Root</Title><CRS>EPSG:25833</CRS><Layer><Name>site</Name><Title>Site</Title></Layer></Layer></Capability></WMS_Capabilities>";
    private const string WcsXml = "<Capabilities version='2.0.1'><Contents><CoverageSummary><CoverageId>dem</CoverageId></CoverageSummary></Contents></Capabilities>";
    private const string WfsXml = "<WFS_Capabilities version='2.0.0'><FeatureTypeList><FeatureType><Name>site</Name><Title>Site</Title><DefaultCRS>EPSG:25833</DefaultCRS></FeatureType></FeatureTypeList></WFS_Capabilities>";
    private const string Description = "<CoverageDescriptions><CoverageDescription><CoverageId>dem</CoverageId><Envelope srsName='EPSG:25833' axisLabels='x y'><lowerCorner>0 0</lowerCorner><upperCorner>4 4</upperCorner></Envelope></CoverageDescription></CoverageDescriptions>";
    private const string Road = "{\"elements\":[{\"type\":\"way\",\"id\":1,\"tags\":{\"highway\":\"residential\"},\"geometry\":[{\"lon\":13,\"lat\":52},{\"lon\":13.001,\"lat\":52.001}]}]}";
    private const string Water = "{\"elements\":[{\"type\":\"way\",\"id\":2,\"tags\":{\"natural\":\"water\"},\"geometry\":[{\"lon\":13,\"lat\":52},{\"lon\":13.001,\"lat\":52},{\"lon\":13.001,\"lat\":52.001},{\"lon\":13,\"lat\":52}]}]}";
    private static WmsRequestOptions ImageOptions(string name = "site") => new()
    { BaseUrl = "https://fixture.test/wms", GetMapBaseUrl = "https://fixture.test/wms", LayerName = name, SrsName = "EPSG:25833", Width = 1024, Height = 1024 };
    private static WcsRequestOptions TerrainOptions(string name = "dem") => new()
    { BaseUrl = "https://fixture.test/wcs", CoverageId = name, SrsName = "EPSG:25833", BoundingBox = new(0, 0, 4, 4), Format = "image/tiff" };
    private static OsmRequestOptions OsmOptions() => new()
    { BaseUrl = "https://fixture.test/osm", IncludeBuildings = false, IncludeRoads = true, BoundingBox4326 = new(13, 52, 13.001, 52.001) };

    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhinospatial-reliability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var count = 0;
        var failures = new List<string>();
        async Task Check(string name, Func<Task> check)
        {
            count++;
            try { await check(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name); Console.WriteLine($"FAIL {name}: {ex}"); }
        }
        string Cache() { var dir = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); return dir; }
        var pngPath = Path.Combine(root, "source.png");
        RasterImageCodec.WritePng(pngPath, 2, 2, Enumerable.Repeat((byte)255, 16).ToArray());
        var png = await File.ReadAllBytesAsync(pngPath);
        var tiff = LowCompatibilityFixtures.Cog(128, terrain: true, masks: false);
        try
        {
            await Check("WMS valid download, receipt and cache reuse", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(png, "image/png"))); using var http = new HttpClient(h);
                var client = new WmsClient(http, Cache()); var first = await client.DownloadImageAsync(ImageOptions()); var second = await client.DownloadImageAsync(ImageOptions());
                Require(!first.UsedCachedFile && second.UsedCachedFile && h.Calls == 1 && File.Exists(first.LocalFilePath + ".json"), "A completed image was not reused.");
            });
            await Check("WMS simultaneous writers reuse one completed image", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(png, "image/png"))); using var http = new HttpClient(h); var dir = Cache();
                var clients = new[] { new WmsClient(http, dir), new WmsClient(http, dir) };
                var results = await Task.WhenAll(clients.Select(c => c.DownloadImageAsync(ImageOptions())));
                Require(h.Calls == 1 && results.Count(r => r.UsedCachedFile) == 1, "Concurrent writers exposed duplicate or incomplete assets.");
            });
            await Check("WMS receipt detects same-length corruption", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(png, "image/png"))); using var http = new HttpClient(h); var client = new WmsClient(http, Cache());
                var first = await client.DownloadImageAsync(ImageOptions()); var corrupted = png.ToArray(); corrupted[20] ^= 1; await File.WriteAllBytesAsync(first.LocalFilePath, corrupted);
                var next = await client.DownloadImageAsync(ImageOptions()); Require(!next.UsedCachedFile && h.Calls == 2, "A corrupted cached image was served.");
            });
            foreach (var image in new[]
            {
                (Type: "image/jpeg", Data: Convert.FromBase64String("/9j/4AAQSkZJRgABAQAASABIAAD/4QBMRXhpZgAATU0AKgAAAAgAAYdpAAQAAAABAAAAGgAAAAAAA6ABAAMAAAABAAEAAKACAAQAAAABAAAAAqADAAQAAAABAAAAAgAAAAD/7QA4UGhvdG9zaG9wIDMuMAA4QklNBAQAAAAAAAA4QklNBCUAAAAAABDUHYzZjwCyBOmACZjs+EJ+/8AAEQgAAgACAwEiAAIRAQMRAf/EAB8AAAEFAQEBAQEBAAAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNicoIJChYXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk5ebn6Onq8fLz9PX29/j5+v/EAB8BAAMBAQEBAQEBAQEAAAAAAAABAgMEBQYHCAkKC//EALURAAIBAgQEAwQHBQQEAAECdwABAgMRBAUhMQYSQVEHYXETIjKBCBRCkaGxwQkjM1LwFWJy0QoWJDThJfEXGBkaJicoKSo1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoKDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uLj5OXm5+jp6vLz9PX29/j5+v/bAEMAAgICAgICAwICAwUDAwMFBgUFBQUGCAYGBgYGCAoICAgICAgKCgoKCgoKCgwMDAwMDA4ODg4ODw8PDw8PDw8PD//bAEMBAgICBAQEBwQEBxALCQsQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEP/dAAQAAf/aAAwDAQACEQMRAD8A7P8A4Qvwf/0ArD/wFi/+Jo/4Qvwf/wBAKw/8BYv/AImulor+Az6g/9k=")),
                (Type: "image/gif", Data: Convert.FromBase64String("R0lGODlhAQABAIAAAAUEBAAAACwAAAAAAQABAAACAkQBADs="))
            })
            {
                await Check("WMS validates " + image.Type, async () =>
                {
                    var h = new Handler((_, _) => Done(Bytes(image.Data, image.Type))); using var http = new HttpClient(h);
                    _ = await new WmsClient(http, Cache()).DownloadImageAsync(ImageOptions());
                });
                await Check("WMS rejects truncated " + image.Type, async () =>
                {
                    var dir = Cache(); var h = new Handler((_, _) => Done(Bytes(image.Data[..^2], image.Type))); using var http = new HttpClient(h);
                    await Fails<Exception>(() => new WmsClient(http, dir).DownloadImageAsync(ImageOptions()));
                    Require(!Directory.EnumerateFiles(dir).Any(), "Truncated image was cached.");
                });
            }
            foreach (var kind in new[] { "length", "io", "format", "terminator" })
                await Check("WMS rejects " + kind + " failure and retries cleanly", async () =>
                {
                    var dir = Cache();
                    var h = new Handler((_, _) => Done(kind switch
                    {
                        "length" => Bytes(png, "image/png", declaredLength: png.Length + 5),
                        "io" => StreamResponse(new ProbeStream(png, failAt: 8), "image/png"),
                        "terminator" => Bytes(png[..^12], "image/png"),
                        _ => Bytes(Encoding.UTF8.GetBytes("not an image"), "image/png")
                    }));
                    using var http = new HttpClient(h); var client = new WmsClient(http, dir);
                    await Fails<Exception>(() => client.DownloadImageAsync(ImageOptions()));
                    Require(!Directory.EnumerateFiles(dir).Any(), "Failed image left a reusable or partial cache artifact.");
                    h.Reply = (_, _) => Done(Bytes(png, "image/png"));
                    Require(!(await client.DownloadImageAsync(ImageOptions())).UsedCachedFile, "A retry served a partial asset.");
                });
            foreach (var headers in new[] { true, false })
                await Check("WMS byte limit " + (headers ? "from headers" : "during chunked body"), async () =>
                {
                    var stream = new ProbeStream(png); var response = StreamResponse(stream, "image/png"); if (headers) response.Content.Headers.ContentLength = png.Length;
                    var h = new Handler((_, _) => Done(response)); using var http = new HttpClient(h);
                    await Fails<ServiceLimitException>(() => new WmsClient(http, Cache(), new() { MaximumImageBytes = 16 }).DownloadImageAsync(ImageOptions()));
                    Require(stream.ReadBytes <= (headers ? 0 : 17), "The size guard read an unbounded body.");
                });
            await Check("WMS decoded pixel limit precedes image decode", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(png, "image/png"))); using var http = new HttpClient(h);
                await Fails<ServiceLimitException>(() => new WmsClient(http, Cache(), new() { MaximumImagePixels = 3 }).DownloadImageAsync(ImageOptions()));
            });
            await Check("WMS size retry stays bounded and preserves final URL", async () =>
            {
                var h = new Handler((r, _) => Done(r.RequestUri!.Query.Contains("WIDTH=1024")
                    ? Bytes(Encoding.UTF8.GetBytes("<ServiceExceptionReport><ServiceException>image size too large</ServiceException></ServiceExceptionReport>"), "text/xml") : Bytes(png, "image/png")));
                using var http = new HttpClient(h); var result = await new WmsClient(http, Cache()).DownloadImageAsync(ImageOptions());
                Require(h.Calls == 2 && result.RequestUrl.Contains("WIDTH=870"), "The established image-size retry changed.");
            });
            foreach (var valid in new[] { true, false })
                await Check("WMS TIFF " + (valid ? "valid decode" : "read failure is fatal"), async () =>
                {
                    var data = await File.ReadAllBytesAsync(Path.Combine("examples", "fixtures", "research", valid ? "tiny-bigtiff.tif" : "truncated-dem.tif"));
                    var dir = Cache(); var h = new Handler((_, _) => Done(Bytes(data, "image/tiff"))); using var http = new HttpClient(h); var client = new WmsClient(http, dir);
                    if (valid) _ = await client.DownloadImageAsync(ImageOptions());
                    else
                    {
                        await Fails<Exception>(() => client.DownloadImageAsync(ImageOptions()));
                        Require(!Directory.EnumerateFiles(dir).Any(), "TIFF decoder failure was ignored.");
                    }
                });
            foreach (var service in new[] { "WMS", "WCS", "WFS", "OSM" })
                await Check(service + " cancellation reaches headers", async () =>
                {
                    var entered = Signal(); using var cancel = new CancellationTokenSource();
                    var h = new Handler(async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return Bytes(png, "image/png"); }); using var http = new HttpClient(h);
                    Task run = service switch
                    {
                        "WMS" => new WmsClient(http, Cache()).DownloadImageAsync(ImageOptions(), cancel.Token),
                        "WCS" => new WcsClient(http, Cache()).DownloadCoverageAsync(TerrainOptions(), cancel.Token),
                        "WFS" => new WfsClient(http).LoadCapabilitiesAsync("https://fixture.test/wfs", cancel.Token),
                        _ => new OsmClient(http).LoadDataAsync(OsmOptions(), cancel.Token)
                    };
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel(); await Fails<OperationCanceledException>(() => run); Require(h.Calls == 1, "Cancellation retried a request.");
                });
            foreach (var service in new[] { "WMS", "WCS", "OSM" })
                await Check(service + " cancellation reaches body and removes partial files", async () =>
                {
                    var dir = Cache(); var payload = service == "WCS" ? tiff : service == "OSM" ? Encoding.UTF8.GetBytes(Road) : png;
                    var stream = new ProbeStream(payload, blockAt: 4); var h = new Handler((_, _) => Done(StreamResponse(stream, service == "WCS" ? "image/tiff" : service == "OSM" ? "application/json" : "image/png")));
                    using var http = new HttpClient(h); using var cancel = new CancellationTokenSource();
                    Task run = service switch
                    {
                        "WMS" => new WmsClient(http, dir).DownloadImageAsync(ImageOptions(), cancel.Token),
                        "WCS" => new WcsClient(http, dir).DownloadCoverageAsync(TerrainOptions(), cancel.Token),
                        _ => new OsmClient(http).LoadDataAsync(OsmOptions(), cancel.Token)
                    };
                    await stream.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel(); await Fails<OperationCanceledException>(() => run);
                    Require(stream.Disposed && !Directory.EnumerateFiles(dir).Any() && h.Calls == 1, "Cancellation leaked a stream/file or fell back.");
                });
            await Check("A late body that ignores cancellation cannot publish", async () =>
            {
                var dir = Cache(); var stream = new ProbeStream(png, blockAt: 4, ignoreCancellation: true); using var cancel = new CancellationTokenSource();
                var h = new Handler((_, _) => Done(StreamResponse(stream, "image/png"))); using var http = new HttpClient(h);
                var work = new WmsClient(http, dir).DownloadImageAsync(ImageOptions(), cancel.Token);
                await stream.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel(); stream.Release.TrySetResult();
                await Fails<OperationCanceledException>(() => work); Require(!Directory.EnumerateFiles(dir).Any(), "Obsolete work published an image.");
            });
            foreach (var service in new[] { "WMS", "WCS", "WFS" })
                await Check(service + " completed metadata cache, case-sensitive paths and canceled owner recovery", async () =>
                {
                    var entered = Signal(); using var cancel = new CancellationTokenSource(); var calls = 0;
                    var xml = service == "WMS" ? WmsXml : service == "WCS" ? WcsXml : WfsXml;
                    var h = new Handler(async (_, token) => { if (Interlocked.Increment(ref calls) == 1) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } return Bytes(Encoding.UTF8.GetBytes(xml), "text/xml"); }); using var http = new HttpClient(h);
                    var wms = new WmsClient(http); var wcs = new WcsClient(http); var wfs = new WfsClient(http);
                    async Task Read(string url, CancellationToken ct)
                    { if (service == "WMS") _ = await wms.LoadCapabilitiesAsync(url, ct); else if (service == "WCS") _ = await wcs.LoadCapabilitiesAsync(url, ct); else _ = await wfs.LoadCapabilitiesAsync(url, ct); }
                    var owner = Read("https://fixture.test/Case", cancel.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    var waiter = Read("https://fixture.test/Case", CancellationToken.None); cancel.Cancel(); await Fails<OperationCanceledException>(() => owner); await waiter;
                    await Read("https://fixture.test/Case", CancellationToken.None); Require(h.Calls == 2, "A completed value was not cached or a canceled task poisoned a waiter.");
                    await Read("https://fixture.test/case", CancellationToken.None); Require(h.Calls == 3, "Case-sensitive source paths collided.");
                });
            await Check("Metadata limit works without Content-Length", async () =>
            {
                var stream = new ProbeStream(Encoding.UTF8.GetBytes(WmsXml)); var h = new Handler((_, _) => Done(StreamResponse(stream, "text/xml"))); using var http = new HttpClient(h);
                await Fails<ServiceLimitException>(() => new WmsClient(http, policy: new() { MaximumMetadataBytes = 16 }).LoadCapabilitiesAsync("https://fixture.test/wms"));
                Require(stream.ReadBytes == 17, "Metadata was buffered beyond its limit.");
            });
            await Check("Completed metadata cache evicts older entries", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(Encoding.UTF8.GetBytes(WmsXml), "text/xml"))); using var http = new HttpClient(h); var client = new WmsClient(http);
                for (var index = 0; index < 65; index++) _ = await client.LoadCapabilitiesAsync("https://fixture.test/service/" + index);
                _ = await client.LoadCapabilitiesAsync("https://fixture.test/service/0");
                Require(h.Calls == 66, "Metadata entries grew without eviction.");
            });
            await Check("WCS DescribeCoverage metadata is parsed and cached", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(Encoding.UTF8.GetBytes(Description), "text/xml"))); using var http = new HttpClient(h); var client = new WcsClient(http);
                Require((await client.LoadCoverageDescriptionAsync(TerrainOptions())).NativeSrs == "EPSG:25833", "Coverage metadata changed.");
                _ = await client.LoadCoverageDescriptionAsync(TerrainOptions()); Require(h.Calls == 1, "Completed description was not cached.");
            });
            await Check("WCS single-part decode and cache reuse", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(tiff, "image/tiff"))); using var http = new HttpClient(h); var client = new WcsClient(http, Cache());
                var first = await client.DownloadCoverageAsync(TerrainOptions()); var second = await client.DownloadCoverageAsync(TerrainOptions());
                Require(first.Raster.Width == 128 && first.Raster.NoDataValue == -9999 && second.UsedCachedFile && h.Calls == 1, "Single-part samples or cache reuse changed.");
            });
            foreach (var newline in new[] { "\r\n", "\n" })
                await Check("WCS multipart bounded extraction and binary tail " + (newline.Length == 2 ? "CRLF" : "LF"), async () =>
                {
                    var data = tiff.Concat(new byte[] { 13, 10 }).ToArray(); var multipart = Multipart(data, newline);
                    var h = new Handler((_, _) => Done(Bytes(multipart, "multipart/related; boundary=\"coverage-boundary\""))); using var http = new HttpClient(h);
                    var result = await new WcsClient(http, Cache()).DownloadCoverageAsync(TerrainOptions());
                    Require((await File.ReadAllBytesAsync(result.LocalFilePath)).SequenceEqual(data), "MIME framing trimmed TIFF bytes.");
                });
            foreach (var kind in new[] { "closing", "truncated", "xml", "limit" })
                await Check("WCS rejects " + kind + " without cache publication", async () =>
                {
                    var dir = Cache(); var data = kind == "closing" ? Multipart(tiff, "\r\n", closing: false) : kind == "truncated" ? File.ReadAllBytes(Path.Combine("examples", "fixtures", "research", "truncated-dem.tif")) : kind == "xml" ? Encoding.UTF8.GetBytes("<ExceptionReport><ExceptionText>No data</ExceptionText></ExceptionReport>") : tiff;
                    var h = new Handler((_, _) => Done(Bytes(data, kind == "closing" ? "multipart/related; boundary=coverage-boundary" : "image/tiff"))); using var http = new HttpClient(h);
                    var policy = kind == "limit" ? new ServiceRequestPolicy { MaximumCoverageBytes = 16 } : new();
                    await Fails<Exception>(() => new WcsClient(http, dir, policy).DownloadCoverageAsync(TerrainOptions()));
                    Require(!Directory.EnumerateFiles(dir).Any(), "Bad coverage was promoted or a spool leaked.");
                });
            await Check("WCS concurrent callers share completed coverage, not canceled tasks", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(tiff, "image/tiff"))); using var http = new HttpClient(h); var dir = Cache();
                var results = await Task.WhenAll(new WcsClient(http, dir).DownloadCoverageAsync(TerrainOptions()), new WcsClient(http, dir).DownloadCoverageAsync(TerrainOptions()));
                Require(h.Calls == 1 && results.Count(r => r.UsedCachedFile) == 1, "Concurrent coverages did not coordinate.");
            });
            await Check("OSM valid data and pre-canceled cache access", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(Encoding.UTF8.GetBytes(Road), "application/json"))); using var http = new HttpClient(h); var client = new OsmClient(http);
                Require((await client.LoadDataAsync(OsmOptions())).Roads.Count == 1, "Valid road was lost.");
                using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Fails<OperationCanceledException>(() => client.LoadDataAsync(OsmOptions(), cancel.Token)); Require(h.Calls == 1, "Canceled cache access contacted the server.");
            });
            foreach (var kind in new[] { "bytes", "elements", "coordinates" })
                await Check("OSM " + kind + " limit cannot fall back", async () =>
                {
                    var body = kind == "elements" ? Road.Replace("[{", "[{\"type\":\"node\",\"id\":9},{", StringComparison.Ordinal) : Road;
                    var policy = kind == "bytes" ? new ServiceRequestPolicy { MaximumOsmBytes = 16 } : kind == "elements" ? new ServiceRequestPolicy { MaximumOsmElements = 1 } : new ServiceRequestPolicy { MaximumOsmCoordinates = 1 };
                    var h = new Handler((_, _) => Done(Bytes(Encoding.UTF8.GetBytes(body), "application/json"))); using var http = new HttpClient(h);
                    await Fails<ServiceLimitException>(() => new OsmClient(http, policy).LoadDataAsync(OsmOptions())); Require(h.Calls == 1, "A resource limit retried another endpoint.");
                });
            await Check("OSM incomplete-query remark is not cached as success", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(Encoding.UTF8.GetBytes(Road.Replace("{\"elements\"", "{\"remark\":\"runtime error: Query timed out\",\"elements\"")), "application/json"))); using var http = new HttpClient(h); var client = new OsmClient(http);
                await Fails<InvalidOperationException>(() => client.LoadDataAsync(OsmOptions()));
                h.Reply = (_, _) => Done(Bytes(Encoding.UTF8.GetBytes(Road), "application/json")); Require((await client.LoadDataAsync(OsmOptions())).Roads.Count == 1 && h.Calls == 2, "Incomplete query became a cache hit.");
            });
            await Check("OSM reports provider category failure and retries the missing category", async () =>
            {
                var failWater = true;
                var h = new Handler(async (r, _) =>
                {
                    var query = await r.Content!.ReadAsStringAsync(); var water = query.Contains("water", StringComparison.OrdinalIgnoreCase);
                    return water && failWater ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Unavailable") } : Bytes(Encoding.UTF8.GetBytes(water ? Water : Road), "application/json");
                });
                using var http = new HttpClient(h); var client = new OsmClient(http); var options = OsmOptions() with { IncludeWater = true };
                var reduced = await client.LoadDataAsync(options); Require(reduced.Roads.Count == 1 && reduced.UnavailableCategories.Contains("Water"), "Category failure was hidden.");
                failWater = false; var full = await client.LoadDataAsync(options); Require(full.WaterAreas.Count == 1 && full.UnavailableCategories.Count == 0, "Reduced aggregate was cached as complete.");
            });
            await Check("OSM cancellation during retry never reaches another attempt", async () =>
            {
                var sent = Signal(); using var cancel = new CancellationTokenSource();
                var h = new Handler((_, _) => { sent.TrySetResult(); return Done(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Busy") }); }); using var http = new HttpClient(h);
                var run = new OsmClient(http).LoadDataAsync(OsmOptions() with { BaseUrl = "" }, cancel.Token); await sent.Task; cancel.Cancel(); await Fails<OperationCanceledException>(() => run); Require(h.Calls == 1, "Cancellation fell back to another endpoint.");
            });
            await Check("OSM cancellation after a completed category cannot return a reduced success", async () =>
            {
                var entered = Signal(); using var cancel = new CancellationTokenSource();
                var h = new Handler(async (r, token) =>
                {
                    if ((await r.Content!.ReadAsStringAsync()).Contains("water", StringComparison.OrdinalIgnoreCase))
                    { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
                    return Bytes(Encoding.UTF8.GetBytes(Road), "application/json");
                });
                using var http = new HttpClient(h);
                var run = new OsmClient(http).LoadDataAsync(OsmOptions() with { IncludeWater = true }, cancel.Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel(); await Fails<OperationCanceledException>(() => run);
                Require(h.Calls == 2, "Canceled categories triggered more requests.");
            });
            await Check("Overall deadline includes OSM retry delay", async () =>
            {
                var h = new Handler((_, _) => Done(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Busy") })); using var http = new HttpClient(h);
                await Fails<TimeoutException>(() => new OsmClient(http, new() { OperationTimeout = TimeSpan.FromMilliseconds(150) }).LoadDataAsync(OsmOptions() with { BaseUrl = "" }));
                Require(h.Calls == 1, "Retry reset the operation deadline.");
            });
            await Check("Request body timeout is distinct from user cancellation", async () =>
            {
                var stream = new ProbeStream(png, blockAt: 4); var h = new Handler((_, _) => Done(StreamResponse(stream, "image/png"))); using var http = new HttpClient(h);
                await Fails<TimeoutException>(() => new WmsClient(http, Cache(), new() { RequestTimeout = TimeSpan.FromMilliseconds(200) }).DownloadImageAsync(ImageOptions()));
                Require(stream.Disposed, "Timed-out response was not disposed.");
            });
            await Check("HTTP-client cancellation is classified as a timeout", async () =>
            {
                var h = new Handler((_, _) => throw new TaskCanceledException("Injected HTTP timeout")); using var http = new HttpClient(h);
                await Fails<TimeoutException>(() => new WmsClient(http, Cache()).DownloadImageAsync(ImageOptions()));
            });
            await Check("A failed replacement preserves an existing texture asset", async () =>
            {
                var h = new Handler((_, _) => Done(Bytes(png, "image/png"))); using var http = new HttpClient(h); var client = new WmsClient(http, Cache());
                var result = await client.DownloadImageAsync(ImageOptions()); File.Delete(result.LocalFilePath + ".json");
                h.Reply = (_, _) => Done(Bytes(png[..^12], "image/png")); await Fails<Exception>(() => client.DownloadImageAsync(ImageOptions()));
                Require((await File.ReadAllBytesAsync(result.LocalFilePath)).SequenceEqual(png), "A failed transfer damaged an existing texture.");
            });
            await Check("Multipart transport allocations do not scale with the body", async () =>
            {
                // Padding isolates transport memory from the tiny raster's decoded elevation array.
                var small = await File.ReadAllBytesAsync(Path.Combine("examples", "fixtures", "research", "tiny-bigtiff.tif"));
                var payload = new byte[16 * 1024 * 1024]; small.CopyTo(payload, 0);
                var response = Multipart(payload, "\r\n");
                var h = new Handler((_, _) => Done(Bytes(response, "multipart/related; boundary=coverage-boundary")));
                using var http = new HttpClient(h); var client = new WcsClient(http, Cache());
                var before = GC.GetTotalAllocatedBytes(true); var timer = System.Diagnostics.Stopwatch.StartNew();
                var result = await client.DownloadCoverageAsync(TerrainOptions());
                var allocated = GC.GetTotalAllocatedBytes(true) - before;
                Console.WriteLine($"MEASURE multipart: {response.Length} transfer bytes, {allocated} managed bytes allocated, {timer.ElapsedMilliseconds} ms; fixture input excluded.");
                Require(result.Raster.Elevations.SequenceEqual(new float[] { 10, 20, 30, 40 }) && allocated < payload.Length,
                    "Multipart transport created a whole-body allocation or changed samples.");
            });
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"Reliability checks: {count - failures.Count}/{count} passed.");
        if (failures.Count > 0) throw new InvalidOperationException("Reliability failures: " + string.Join(", ", failures));
    }

    private static byte[] Multipart(byte[] data, string newline, bool closing = true) =>
        Encoding.ASCII.GetBytes($"--coverage-boundary{newline}Content-Type: application/gml+xml{newline}{newline}<metadata/>{newline}--coverage-boundary{newline}Content-Type: image/tiff{newline}{newline}")
            .Concat(data).Concat(Encoding.ASCII.GetBytes(closing ? $"{newline}--coverage-boundary--{newline}" : "")).ToArray();
    private static HttpResponseMessage Bytes(byte[] bytes, string type, long? declaredLength = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
        if (declaredLength.HasValue) response.Content.Headers.ContentLength = declaredLength;
        return response;
    }
    private static HttpResponseMessage StreamResponse(Stream stream, string type)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(type); return response;
    }
    private static Task<HttpResponseMessage> Done(HttpResponseMessage response) => Task.FromResult(response);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Fails<T>(Func<Task> run) where T : Exception
    {
        try { await run(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply;
        internal int Calls;
        internal Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) => Reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return Reply(request, token); }
    }
    private sealed class ProbeStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _failAt, _blockAt;
        private readonly bool _ignoreCancellation;
        private int _position;
        internal long ReadBytes;
        internal bool Disposed;
        internal TaskCompletionSource Blocked = Signal(), Release = Signal();
        internal ProbeStream(byte[] data, int failAt = -1, int blockAt = -1, bool ignoreCancellation = false)
        { _data = data; _failAt = failAt; _blockAt = blockAt; _ignoreCancellation = ignoreCancellation; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_failAt >= 0 && _position >= _failAt) throw new IOException("Interrupted fixture transfer.");
            if (_blockAt >= 0 && _position >= _blockAt)
            { Blocked.TrySetResult(); await Release.Task.WaitAsync(_ignoreCancellation ? CancellationToken.None : token); }
            if (!_ignoreCancellation) token.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, _data.Length - _position);
            if (_failAt >= 0 && _position < _failAt) count = Math.Min(count, _failAt - _position);
            if (_blockAt >= 0 && _position < _blockAt) count = Math.Min(count, _blockAt - _position);
            _data.AsMemory(_position, count).CopyTo(buffer); _position += count; ReadBytes += count; return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
