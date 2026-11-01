using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class Lod2AtomChecks
{
    private const string Header = "<feed xmlns='http://www.w3.org/2005/Atom' xmlns:georss='http://www.georss.org/georss' xmlns:gml='http://www.opengis.net/gml'>";
    private const string Building = """
        <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
          <gml:boundedBy><gml:Envelope srsName="EPSG:25833" srsDimension="3"><gml:lowerCorner>391000 5820000 0</gml:lowerCorner><gml:upperCorner>392000 5821000 100</gml:upperCorner></gml:Envelope></gml:boundedBy>
          <core:cityObjectMember><bldg:Building gml:id="fixture"><bldg:boundedBy><bldg:RoofSurface><bldg:lod2MultiSurface><gml:MultiSurface srsName="EPSG:25833" srsDimension="3"><gml:surfaceMember><gml:Polygon><gml:exterior><gml:LinearRing><gml:posList>391100 5820100 20 391200 5820100 20 391200 5820200 20 391100 5820200 20 391100 5820100 20</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon></gml:surfaceMember></gml:MultiSurface></bldg:lod2MultiSurface></bldg:RoofSurface></bldg:boundedBy></bldg:Building></core:cityObjectMember>
        </core:CityModel>
        """;

    internal static SpatialContext2D Context(string srs, BoundingBox2D bounds) =>
        new(srs, bounds, bounds, null, new Coordinate2D(bounds.MinX, bounds.MinY), false,
            new Dictionary<string, BoundingBox2D> { [srs] = bounds });

    private static string TileEntry(string name, string spatial, string updated = "2026-01-01T00:00:00Z") =>
        $"<entry><title>{name}</title><updated>{updated}</updated>{spatial}<link rel='enclosure' type='application/zip' href='{name}.zip'/></entry>";

    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rhinospatial-atom-check-" + Guid.NewGuid().ToString("N"));
        var checks = 0;
        async Task Check(string label, Func<Task> action)
        {
            await action();
            checks++;
            Console.WriteLine("PASS " + label);
        }
        try
        {
            var handler = new FixtureHandler();
            using var http = new HttpClient(handler);
            var client = new Lod2AtomClient(http, directory);
            var context = Context("EPSG:25833", new BoundingBox2D(391050, 5820050, 391250, 5820250));
            const string native = "<georss:where><gml:Envelope srsName='urn:ogc:def:crs:EPSG::25833'><gml:lowerCorner>391000 5820000</gml:lowerCorner><gml:upperCorner>392000 5821000</gml:upperCorner></gml:Envelope></georss:where>";
            const string distant = "<georss:box>40 1 41 2</georss:box>";
            handler.Xml("https://fixture.test/root", Header + "<entry><link type='application/atom+xml' href='dataset.atom'/></entry></feed>");
            handler.Xml("https://fixture.test/dataset.atom", Header + TileEntry("selected", native) + TileEntry("distant", distant) + "<link rel='next' type='application/atom+xml' href='page2.atom'/></feed>");
            handler.Xml("https://fixture.test/page2.atom", Header + TileEntry("edge", "<georss:where><gml:Envelope srsName='EPSG:25833'><gml:lowerCorner>391250 5820000</gml:lowerCorner><gml:upperCorner>392000 5821000</gml:upperCorner></gml:Envelope></georss:where>") + "<link rel='next' type='application/atom+xml' href='dataset.atom'/></feed>");
            Lod2AtomSelection selection = null!;
            await Check("subfeeds, pagination, cycles, native CRS and edge-only exclusion", async () =>
            {
                selection = (await client.TrySelectAsync("https://fixture.test/root", context))!;
                Require(selection.FeedCount == 3 && selection.DownloadCount == 3 && selection.Tiles.Count == 1, "Incorrect traversal/selection.");
                Require(handler.Count("https://fixture.test/selected.zip") == 0, "Discovery downloaded building data.");
            });
            await Check("GeoRSS polygon lat/lon, independent of dataset CRS category", () =>
            {
                var feed = Lod2AtomFeedReader.Read(XDocument.Parse(Header + "<entry><category term='http://www.opengis.net/def/crs/EPSG/0/25833'/><georss:polygon>52 13 52 14 53 14 53 13 52 13</georss:polygon><link rel='section' href='a.zip'/></entry></feed>"), new Uri("https://fixture.test/root"));
                Require(feed.Downloads[0].Extent == new AtomSpatialExtent("EPSG:4326", new BoundingBox2D(13, 52, 14, 53)), "GeoRSS axis order or CRS changed.");
                return Task.CompletedTask;
            });
            await Check("xml:base and relative URL resolution", () =>
            {
                var feed = Lod2AtomFeedReader.Read(XDocument.Parse(Header.Replace(">", " xml:base='../data/'>") + "<entry xml:base='tiles/'><georss:box>52 13 53 14</georss:box><link xml:base='../archives/' href='tile.zip?format=gml&amp;v=2#ignored' rel='enclosure'/></entry></feed>"), new Uri("https://fixture.test/service/root.atom"));
                Require(feed.Downloads[0].Url.AbsoluteUri == "https://fixture.test/data/archives/tile.zip?format=gml&v=2", "Relative URL resolution failed.");
                return Task.CompletedTask;
            });
            await Check("authority GML geographic axis order and CRS84", () =>
            {
                var node = XElement.Parse("<entry xmlns:georss='http://www.georss.org/georss' xmlns:gml='http://www.opengis.net/gml'><georss:where><gml:Envelope srsName='http://www.opengis.net/def/crs/EPSG/0/4326'><gml:lowerCorner>52 13</gml:lowerCorner><gml:upperCorner>53 14</gml:upperCorner></gml:Envelope></georss:where></entry>");
                Require(Lod2AtomFeedReader.ReadExtent(node)!.Bounds == new BoundingBox2D(13, 52, 14, 53), "GML EPSG:4326 requires authority axis order.");
                node.Descendants().Single(element => element.Name.LocalName == "Envelope").Attribute("srsName")!.Remove();
                Require(Lod2AtomFeedReader.ReadExtent(node)!.Bounds == new BoundingBox2D(13, 52, 14, 53), "GeoRSS GML defaults to latitude/longitude.");
                node.Descendants().Single(element => element.Name.LocalName == "Envelope").SetAttributeValue("srsName", "urn:ogc:def:crs:OGC::CRS84");
                node.Descendants().Single(element => element.Name.LocalName == "lowerCorner").Value = "13 52";
                node.Descendants().Single(element => element.Name.LocalName == "upperCorner").Value = "14 53";
                Require(Lod2AtomFeedReader.ReadExtent(node)!.Bounds == new BoundingBox2D(13, 52, 14, 53), "CRS84 must use longitude/latitude.");
                return Task.CompletedTask;
            });
            await Check("declared UTM kilometre grid across two tile boundaries", async () =>
            {
                handler.Xml("https://fixture.test/grid", Header + "<entry><georss:box>52.3 13.0 52.7 13.8</georss:box><category term='http://www.opengis.net/def/crs/EPSG/0/25833'/><link title='Tile index 1x1 Km' href='index.pdf'/><link rel='section' type='application/x-gmz' href='LoD2_391_5820.zip'/><link rel='section' type='application/x-gmz' href='LoD2_392_5820.zip'/></entry></feed>");
                var selected = await client.TrySelectAsync("https://fixture.test/grid", Context("EPSG:25833", new BoundingBox2D(391950, 5820100, 392050, 5820200)));
                Require(selected!.Tiles.Count == 2, "Both intersecting tiles must be selected.");
                Require(selected.Tiles[0].Extent!.Bounds == new BoundingBox2D(391000, 5820000, 392000, 5821000), "Grid origin is incorrect.");
            });
            await Check("projected study area selects geographic tile bounds", async () =>
            {
                handler.Xml("https://fixture.test/geographic", Header + TileEntry("geographic", "<georss:box>52.5 13.3 52.6 13.5</georss:box>") + "</feed>");
                Require((await client.TrySelectAsync("https://fixture.test/geographic", context))!.Tiles.Count == 1, "Projected to geographic selection failed.");
            });
            await Check("multiple unindexed links cannot inherit whole-dataset bounds", async () =>
            {
                handler.Xml("https://fixture.test/missing", Header + "<entry><georss:box>52 13 53 14</georss:box><link rel='section' href='one.zip'/><link rel='section' href='two.zip'/></entry></feed>");
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/missing", context), "per-tile");
            });
            await Check("terrain download formats are diagnosed before grid selection", async () =>
            {
                handler.Xml("https://fixture.test/terrain", Header + "<entry><georss:box>52.3 13.0 52.7 13.8</georss:box><category term='EPSG:25833'/><link title='Tile index 2x2 Km' href='index.gif'/><link rel='section' type='text/csv' href='DGM1_416_5810.zip'/></entry></feed>");
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/terrain", context), "not CityGML building data");
                Require(handler.Count("https://fixture.test/DGM1_416_5810.zip") == 0, "A terrain archive was downloaded by the building loader.");
            });
            await Check("out-of-area subfeeds are not requested", async () =>
            {
                handler.Xml("https://fixture.test/outside", Header + "<entry>" + distant + "<link rel='alternate' type='application/atom+xml' href='unused.atom'/></entry></feed>");
                var empty = await client.TrySelectAsync("https://fixture.test/outside", context);
                Require(empty!.Tiles.Count == 0 && handler.Count("https://fixture.test/unused.atom") == 0, "Out-of-area subfeed was fetched.");
            });
            await Check("HTTP feed errors and timeouts remain actionable", async () =>
            {
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/unavailable.atom", context), "404");
                handler.Responses["https://fixture.test/timeout.atom"] = () => throw new TaskCanceledException();
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/timeout.atom", context), "Timed out");
            });
            await Check("traversal and selected-tile budgets reject oversized work", async () =>
            {
                handler.Xml("https://fixture.test/oversized", Header + string.Concat(Enumerable.Range(0, 33).Select(i => TileEntry("budget" + i, native))) + "</feed>");
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/oversized", context), "limit is 32");
                for (var i = 0; i < 10; i++)
                    handler.Xml($"https://fixture.test/deep{i}.atom", Header + $"<entry><link type='application/atom+xml' href='deep{i + 1}.atom'/></entry></feed>");
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/deep0.atom", context), "traversal limit");
            });
            await Check("non-HTTP links are rejected", () =>
            {
                try { Lod2AtomFeedReader.Read(XDocument.Parse(Header + TileEntry("file:///private/tile", native) + "</feed>"), new Uri("https://fixture.test/")); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("HTTP")) { return Task.CompletedTask; }
                throw new Exception("Non-HTTP link accepted.");
            });
            await Check("tile names alone are insufficient", async () =>
            {
                handler.Xml("https://fixture.test/guessed", Header + "<entry><category term='EPSG:25833'/><link rel='section' href='LoD2_391_5820.zip'/><link rel='section' href='LoD2_392_5820.zip'/></entry></feed>");
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/guessed", context), "per-tile");
            });
            await Check("unsupported CRS, malformed and nonfinite bounds fail", async () =>
            {
                handler.Xml("https://fixture.test/unsupported", Header + TileEntry("u", native.Replace("25833", "99999")) + "</feed>");
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/unsupported", context), "transformation");
                handler.Xml("https://fixture.test/nonfinite", Header + TileEntry("u", "<georss:box>NaN 13 53 14</georss:box>") + "</feed>");
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/nonfinite", context), "bounds");
                handler.Xml("https://fixture.test/ambiguous", Header + TileEntry("u", native + distant) + "</feed>");
                await ThrowsAsync(() => client.TrySelectAsync("https://fixture.test/ambiguous", context), "ambiguous");
            });
            await Check("AdV compound CRS uses UTM XY without changing elevation", () =>
            {
                Require(SpatialReferenceTransform.TryTransformXY("urn:adv:crs:ETRS89_UTM33*DE_DHHN2016_NH", "EPSG:25833", 391100, 5820100, out var x, out var y) &&
                    x == 391100 && y == 5820100, "AdV UTM33 alias must preserve XY.");
                Require(SpatialReferenceTransform.NormalizeSrs("urn:adv:crs:ETRS89_UTM32*DE_DHHN92_NH") == "EPSG:25832", "AdV UTM32 alias failed.");
                Require(!SpatialReferenceTransform.TryTransformXY("urn:adv:crs:ETRS89_UTM33_WITH_ZONE", "EPSG:25833", 33391100, 5820100, out _, out _), "Unknown AdV variants must not be guessed.");
                return Task.CompletedTask;
            });
            await Check("non-ATOM WFS source preserves WFS routing", async () =>
            {
                handler.Xml("https://fixture.test/wfs", "<WFS_Capabilities/>");
                Require(await client.TrySelectAsync("https://fixture.test/wfs", context) is null, "Non-ATOM source was captured.");
                handler.Responses["https://fixture.test/wfs-error"] = () => new HttpResponseMessage(HttpStatusCode.BadRequest);
                Require(await client.TrySelectAsync("https://fixture.test/wfs-error", context) is null, "WFS root requiring a query must retain its route.");
            });

            var zip = Zip(Building);
            handler.Bytes("https://fixture.test/selected.zip", zip);
            IReadOnlyList<Lod2AtomCachedTile> first = null!;
            await Check("download, archive validation and existing CityGML pipeline", async () =>
            {
                first = await client.DownloadAsync(selection);
                Require(first.Count == 1 && !first[0].UsedCache, "First load should download.");
                using var archive = ZipFile.OpenRead(first[0].FilePath);
                using var stream = archive.Entries.Single().Open();
                using var reader = new StreamReader(stream);
                var gml = await reader.ReadToEndAsync();
                Require(Lod2GmlReader.ReadBuildings(gml, "tile", context.RequestBoundingBox).Count == 1, "Existing CityGML parser failed.");
                Require(Lod2GmlReader.ReadBuildings(gml, "tile", new BoundingBox2D(1, 1, 2, 2)).Count == 0, "Existing building filter failed.");
            });
            await Check("persistent cache works with a new client without network", async () =>
            {
                var cached = await new Lod2AtomClient(http, directory).DownloadAsync(selection);
                Require(cached[0].UsedCache && handler.Count("https://fixture.test/selected.zip") == 1, "Repeated load downloaded again.");
            });
            await Check("corrupt and incomplete cached files are redownloaded", async () =>
            {
                await File.WriteAllBytesAsync(first[0].FilePath, new byte[zip.Length]);
                Require(!(await client.DownloadAsync(selection))[0].UsedCache, "Same-size corrupt cache was accepted.");
                await File.WriteAllBytesAsync(first[0].FilePath, new byte[] { 1, 2 });
                Require(!(await client.DownloadAsync(selection))[0].UsedCache, "Truncated cache was accepted.");
            });
            await Check("feed revision invalidates the archive cache", async () =>
            {
                var revised = selection with { Tiles = new[] { selection.Tiles[0] with { Revision = "new-revision" } } };
                Require(!(await client.DownloadAsync(revised))[0].UsedCache, "Changed feed revision used stale cache.");
            });
            await Check("invalid ZIP and unavailable download leave no completed cache", async () =>
            {
                var invalid = selection with { Tiles = new[] { selection.Tiles[0] with { Url = new Uri("https://fixture.test/invalid.zip") } } };
                handler.Bytes("https://fixture.test/invalid.zip", Encoding.UTF8.GetBytes("not a ZIP"));
                await ThrowsAsync(() => client.DownloadAsync(invalid), "Could not load ATOM tile");
                handler.Bytes("https://fixture.test/invalid.zip", zip);
                Require(!(await client.DownloadAsync(invalid))[0].UsedCache, "Failure poisoned the cache.");
                var absent = selection with { Tiles = new[] { selection.Tiles[0] with { Url = new Uri("https://fixture.test/absent.zip") } } };
                await ThrowsAsync(() => client.DownloadAsync(absent), "404");
                Require(!Directory.EnumerateFiles(directory, "*.partial").Any(), "Partial download remains.");
            });
            await Check("unsupported archive content fails clearly", async () =>
            {
                var unsupported = selection with { Tiles = new[] { selection.Tiles[0] with { Url = new Uri("https://fixture.test/unsupported.zip") } } };
                handler.Bytes("https://fixture.test/unsupported.zip", Zip("<unrelated/>", "readme.xml"));
                await ThrowsAsync(() => client.DownloadAsync(unsupported), "expected a ZIP");
            });
            await Check("truncated CityGML is rejected before becoming a completed cache", async () =>
            {
                var malformed = selection with { Tiles = new[] { selection.Tiles[0] with { Url = new Uri("https://fixture.test/malformed.zip") } } };
                handler.Bytes("https://fixture.test/malformed.zip", Zip(Building[..^30]));
                await ThrowsAsync(() => client.DownloadAsync(malformed), "Could not load ATOM tile");
                handler.Bytes("https://fixture.test/malformed.zip", zip);
                Require(!(await client.DownloadAsync(malformed))[0].UsedCache, "Malformed CityGML poisoned cache.");
            });
            await Check("HTTP length mismatch rejected and partial removed", async () =>
            {
                var mismatch = selection with { Tiles = new[] { selection.Tiles[0] with { Url = new Uri("https://fixture.test/short.zip") } } };
                handler.Responses["https://fixture.test/short.zip"] = () =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
                    response.Content.Headers.ContentLength = zip.Length + 10;
                    return response;
                };
                await ThrowsAsync(() => client.DownloadAsync(mismatch), "incomplete");
                Require(!Directory.EnumerateFiles(directory, "*.partial").Any(), "Partial file remains.");
            });
            await Check("cancellation does not publish an archive", async () =>
            {
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                try { await client.DownloadAsync(selection, cancellationToken: cancelled.Token); throw new Exception("Cancellation ignored."); }
                catch (OperationCanceledException) { }
            });
            await Check("cancellation during transfer removes partial files and releases locks", async () =>
            {
                using var cancel = new CancellationTokenSource();
                var interrupted = selection with { Tiles = new[] { selection.Tiles[0] with { Url = new Uri("https://fixture.test/interrupted.zip") } } };
                handler.Responses["https://fixture.test/interrupted.zip"] = () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new CancelAfterReadStream(zip, cancel))
                };
                try { await client.DownloadAsync(interrupted, cancellationToken: cancel.Token); throw new Exception("Transfer was not cancelled."); }
                catch (OperationCanceledException) { }
                Require(!Directory.EnumerateFiles(directory, "*.partial").Any(), "Interrupted transfer left a partial file.");
                handler.Bytes("https://fixture.test/interrupted.zip", zip);
                Require(!(await client.DownloadAsync(interrupted))[0].UsedCache, "Interrupted transfer became a completed archive.");
            });
            await Check("concurrent clients share a completed archive", async () =>
            {
                var concurrent = selection with { Tiles = new[] { selection.Tiles[0] with { Revision = "concurrent" } } };
                var before = handler.Count("https://fixture.test/selected.zip");
                var results = await Task.WhenAll(client.DownloadAsync(concurrent), new Lod2AtomClient(http, directory).DownloadAsync(concurrent));
                Require(handler.Count("https://fixture.test/selected.zip") == before + 1, "Concurrent solve downloaded twice.");
                Require(results.Count(result => result[0].UsedCache) == 1, "Concurrent writer did not reuse cache.");
            });
            Console.WriteLine($"All {checks} ATOM checks passed.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    public static async Task ProbeAsync(string[] args)
    {
        if (args.Length < 4) throw new ArgumentException("Usage: lod2atom <feed-url> <srs> <minX,minY,maxX,maxY> [download]");
        var bounds = args[3].Split(',').Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var context = Context(args[2], new BoundingBox2D(bounds[0], bounds[1], bounds[2], bounds[3]));
        var client = new Lod2AtomClient();
        var selection = await client.TrySelectAsync(args[1], context) ?? throw new InvalidOperationException("Not an ATOM source.");
        Console.WriteLine($"Feeds {selection.FeedCount}; indexed downloads {selection.DownloadCount}; selected tiles {selection.Tiles.Count}.");
        foreach (var tile in selection.Tiles) Console.WriteLine($"Selected {tile.Url} ({tile.Extent})");
        if (args.Length < 5 || args[4] != "download") return;
        var files = await client.DownloadAsync(selection, Console.WriteLine);
        foreach (var file in files)
        {
            Console.WriteLine($"Cached={file.UsedCache}; path={file.FilePath}; size={new FileInfo(file.FilePath).Length}");
            using var archive = ZipFile.OpenRead(file.FilePath);
            foreach (var entry in archive.Entries.Where(entry => new[] { ".gml", ".xml", ".citygml" }.Contains(Path.GetExtension(entry.FullName).ToLowerInvariant())))
            {
                using var stream = entry.Open();
                var metadata = Lod2GmlReader.ReadSourceMetadata(stream);
                var filter = Lod2AtomFeedReader.TransformBounds(new AtomSpatialExtent(context.ResolvedSrs, context.RequestBoundingBox), metadata.SrsName);
                using var text = new StreamReader(entry.Open());
                var buildings = Lod2GmlReader.ReadBuildings(await text.ReadToEndAsync(), entry.FullName, filter);
                Console.WriteLine($"{entry.FullName}: {metadata}; kept {buildings.Count} buildings / {buildings.Sum(b => b.Surfaces.Count)} surfaces.");
                foreach (var point in buildings.SelectMany(b => b.Surfaces).SelectMany(surface => surface.OuterPoints).Take(1))
                {
                    Require(SpatialReferenceTransform.TryTransformXY(metadata.SrsName, context.ResolvedSrs, point.X, point.Y, out var x, out var y), "Building transform failed.");
                    Console.WriteLine($"First placed XY: {x - context.PlacementOrigin.X:0.###}, {y - context.PlacementOrigin.Y:0.###}; elevation {point.Z:0.###}.");
                }
            }
        }
    }

    private static byte[] Zip(string content, string name = "tile.gml")
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write(content);
        return bytes.ToArray();
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static async Task ThrowsAsync(Func<Task> action, string contains)
    {
        try { await action(); }
        catch (Exception ex) when (ex.Message.Contains(contains, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Expected failure containing: " + contains);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Responses { get; } = new(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new();
        public int Count(string url) => _counts.GetValueOrDefault(url);
        public void Xml(string url, string xml) => Responses[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };
        public void Bytes(string url, byte[] bytes) => Responses[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            _counts.AddOrUpdate(url, 1, (_, count) => count + 1);
            var response = Responses.TryGetValue(url, out var factory) ? factory() : new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class CancelAfterReadStream(byte[] bytes, CancellationTokenSource cancel) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            cancel.Cancel();
            return count;
        }
    }
}
