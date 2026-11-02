using System.Buffers.Binary;
using System.Text;
using BitMiracle.LibTiff.Classic;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.Esri;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class CorrectnessChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rhinospatial-correctness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var failures = new List<string>();
        var count = 0;
        void Check(string name, Action test)
        {
            count++;
            try { test(); Console.WriteLine("PASS: " + name); }
            catch (Exception ex) { failures.Add(name); Console.WriteLine($"FAIL: {name}: {ex.Message}"); }
        }

        try
        {
            var fixtures = Path.Combine("examples", "fixtures", "research");
            var json = File.ReadAllText(Path.Combine(fixtures, "courtyard.geojson"));
            var gml = File.ReadAllText(Path.Combine(fixtures, "courtyard.gml"));
            Check("GeoJSON courtyard and feature association", () => CheckCourtyard(GeoJsonReader.ReadFeatures(json, "site").Single()));
            Check("GML courtyard and feature association", () => CheckCourtyard(GmlReader.ReadFeatures(gml, "site").Single()));
            const string shell = "[[0,0],[4,0],[4,4],[0,4],[0,0]]";
            const string hole = "[[1,1],[1,3],[3,3],[3,1],[1,1]]";
            const string island = "[[1.5,1.5],[2,1.5],[2,2],[1.5,2],[1.5,1.5]]";
            var multi = $$$"""{"type":"Feature","id":"island","properties":{"name":"Site"},"geometry":{"type":"MultiPolygon","coordinates":[[{{{shell}}},{{{hole}}}],[{{{island}}}]]}}""";
            Check("GeoJSON multipolygon retains hole and island", () =>
            {
                var feature = GeoJsonReader.ReadFeatures(multi, "site").Single();
                Require(feature.Geometry.OuterRings.Count == 3 && feature.Attributes["name"] == "Site", "A ring or its feature attributes were lost.");
            });
            Check("GML surface patches retain rings once", () =>
            {
                var patched = gml.Replace("<gml:Polygon srsName=\"EPSG:25833\">", "<gml:MultiSurface srsName=\"EPSG:25833\"><gml:surfaceMember><gml:Surface><gml:patches><gml:PolygonPatch>")
                    .Replace("</gml:Polygon>", "</gml:PolygonPatch><gml:PolygonPatch><gml:exterior><gml:LinearRing><gml:posList>1.2 1.2 1.5 1.2 1.5 1.5 1.2 1.5 1.2 1.2</gml:posList></gml:LinearRing></gml:exterior></gml:PolygonPatch></gml:patches></gml:Surface></gml:surfaceMember></gml:MultiSurface>");
                Require(GmlReader.ReadFeatures(patched, "site").Single().Geometry.OuterRings.Count == 3, "Nested Surface/PolygonPatch rings were lost or counted twice.");
            });
            Check("GML interior pos coordinates retain XY from 3D", () =>
            {
                var positions = gml.Replace("<gml:posList>1 1 1 2 2 2 2 1 1 1</gml:posList>", "<gml:pos>1 1 7</gml:pos><gml:pos>1 2 8</gml:pos><gml:pos>2 2 9</gml:pos><gml:pos>2 1 10</gml:pos><gml:pos>1 1 7</gml:pos>");
                CheckCourtyard(GmlReader.ReadFeatures(positions, "site").Single());
            });
            Check("Shapefile multipolygon courtyard and island", () =>
            {
                var path = Path.Combine(directory, "courtyard.shp");
                var geometry = new WKTReader().Read("MULTIPOLYGON (((0 0,4 0,4 4,0 4,0 0),(1 1,1 3,3 3,3 1,1 1)),((1.5 1.5,2 1.5,2 2,1.5 2,1.5 1.5)))");
                Shapefile.WriteAllFeatures(new[] { new Feature(geometry, new AttributesTable { { "name", "Site" } }) }, path, MediumCompatibilityChecks.Wkt, Encoding.UTF8);
                var context = Lod2AtomChecks.Context("EPSG:25833", new BoundingBox2D(-1, -1, 5, 5));
                var result = ShapefileFeatureReader.ReadFeatures(path, "site", context, 10);
                var feature = result.Features.Single();
                Require(result.FailedFeatureCount == 0 && feature.Geometry.OuterRings.Count == 3, "Shapefile lost a courtyard or island boundary.");
                Require(feature.SourceLayerName == "site" && feature.Attributes["name"] == "Site", "Feature association changed.");
            });

            foreach (var alias in new[] { "EPSG:32632", "epsg:032632", "urn:ogc:def:crs:EPSG::32632", "urn:ogc:def:crs:EPSG:9.9:32632", "http://www.opengis.net/def/crs/EPSG/0/32632", "https://www.opengis.net/def/crs/EPSG/0/32632", "http://www.opengis.net/gml/srs/epsg.xml#32632" })
                Check("UTM identifier " + alias, () =>
                {
                    Require(SpatialReferenceTransform.NormalizeSrs(alias) == "EPSG:32632", "Identifier was not canonicalized.");
                    Require(SpatialReferenceTransform.TryTransformXY(alias, "EPSG:4326", 500000, 0, out var x, out var y), "Supported UTM transformation failed.");
                    Require(Math.Abs(x - 9) < 1e-8 && Math.Abs(y) < 1e-8, "UTM control point moved.");
                });
            foreach (var alias in new[] { "CRS84", "CRS:84", "OGC:CRS84", "urn:ogc:def:crs:OGC:1.3:CRS84", "http://www.opengis.net/def/crs/OGC/1.3/CRS84" })
                Check("CRS84 longitude-first alias " + alias, () =>
                {
                    Require(SpatialReferenceTransform.NormalizeSrs(alias) == "EPSG:4326", "CRS84 XY alias was not recognized.");
                    Require(!UsesLatitudeLongitudeAxes(alias), "CRS84 must remain longitude-first.");
                });
            foreach (var invalid in new[] { "EPSG:9994326", "EPSG:4326junk", "junkEPSG:4326", "urn:ogc:def:crs:EPSG::4326:3857", "https://example.com/def/crs/EPSG/0/4326", "https://www.opengis.net/def/crs/EPSG/0/4326?other=3857", "EPSG:+4326", "EPSG: 4326", "EPSG:+32632", "EPSG: 32632", "not-CRS84", "" })
                Check("Unsupported identifier fails explicitly: " + invalid, () =>
                {
                    Require(!SpatialReferenceTransform.TryTransformXY(invalid, "EPSG:3857", 1, 2, out _, out _), "Invalid CRS was mistaken for a supported identifier.");
                    Require(!SpatialReferenceTransform.TryTransformXY(invalid, invalid, 1, 2, out _, out _), "Unsupported identity transformation was accepted.");
                });
            Check("WMS authority axes distinguish EPSG from CRS84", () =>
            {
                Require(UsesLatitudeLongitudeAxes("urn:ogc:def:crs:EPSG::4258"), "ETRS89 axis convention changed.");
                Require(UsesLatitudeLongitudeAxes("EPSG:4326"), "WMS EPSG:4326 axis convention changed.");
                Require(!UsesLatitudeLongitudeAxes("EPSG:9994326"), "Malformed CRS must not select latitude-first axes.");
            });
            Check("AdV compound identifiers retain horizontal CRS", () =>
            {
                Require(SpatialReferenceTransform.NormalizeSrs("urn:adv:crs:ETRS89_UTM33*DE_DHHN2016_NH") == "EPSG:25833", "AdV UTM33 alias changed.");
                Require(SpatialReferenceTransform.NormalizeSrs("urn:adv:crs:ETRS89_UTM32") == "EPSG:25832", "AdV UTM32 alias changed.");
            });
            Check("CityGML CRS84 keeps longitude-first coordinates", () =>
            {
                var point = Lod2GmlReader.ReadBuildings(Building("urn:ogc:def:crs:OGC:1.3:CRS84", "-74 40 12 -73.99 40 12 -73.99 40.01 13 -74 40.01 13 -74 40 12"), "site")
                    .Single().Surfaces.Single().OuterPoints[0];
                Require(point == new Coordinate3D(-74, 40, 12), "CRS84 XY or source height changed.");
            });
            Check("CityGML AdV compound CRS preserves source heights", () =>
            {
                var point = Lod2GmlReader.ReadBuildings(Building("urn:adv:crs:ETRS89_UTM33*DE_DHHN2016_NH", "391100 5820100 20 391200 5820100 20 391200 5820200 21 391100 5820200 21 391100 5820100 20"), "site")
                    .Single().Surfaces.Single().OuterPoints[0];
                Require(point == new Coordinate3D(391100, 5820100, 20), "AdV coordinates or source height changed.");
            });
            Check("GeoJSON declaration uses the strict shared parser", () =>
            {
                var text = "{\"crs\":{\"properties\":{\"name\":\"https://example.com/EPSG/4326\"}}}";
                Require(GeoJsonReader.TryReadSourceSrs(text) != "EPSG:4326", "An unrelated URL was interpreted as an EPSG declaration.");
            });
            Check("Context identities share aliases without substring collisions", () =>
            {
                var context = Lod2AtomChecks.Context("EPSG:32632", new BoundingBox2D(0, 0, 1, 1));
                Require(SpatialContextIdentity.CreateKey(context) == SpatialContextIdentity.CreateKey(context with { ResolvedSrs = "urn:ogc:def:crs:EPSG::32632" }), "Equivalent CRS identifiers produced different context keys.");
                Require(SpatialContextIdentity.CreateKey(context with { ResolvedSrs = "EPSG:4326" }) != SpatialContextIdentity.CreateKey(context with { ResolvedSrs = "EPSG:9994326" }), "Unknown CRS collided with EPSG:4326.");
            });
            Check("Vector batch keeps every ring and source feature", () =>
            {
                var input = GeoJsonReader.ReadFeatures(multi, "site");
                var result = WfsFeatureGeometryTransformer.TransformFeatures(input, "EPSG:32632", "EPSG:3857").Single();
                Require(result.Geometry.OuterRings.Count == 3 && result.Geometry.OuterRings.Sum(r => r.Points.Count) == 15, "Batch transformation lost ring coordinates.");
                Require(result.Id == "island" && result.Attributes["name"] == "Site", "Batch lost feature identity.");
                Require(input.Single().Geometry.OuterRings[0].Points[0] == new Coordinate2D(0, 0), "Transformation mutated the source.");
            });
            Check("Unsupported vector batch cannot silently drop points", () =>
                Reject(() => WfsFeatureGeometryTransformer.TransformFeatures(GeoJsonReader.ReadFeatures(json, "site"), "EPSG:9994326", "EPSG:3857")));
            Check("Non-finite XY rejected, including identity transforms", () =>
            {
                Require(!SpatialReferenceTransform.TryTransformXY("EPSG:4326", "EPSG:4326", double.NaN, 1, out _, out _), "NaN identity was accepted.");
                Require(!SpatialReferenceTransform.TryTransformXY("EPSG:4326", "EPSG:3857", 1, double.PositiveInfinity, out _, out _), "Infinite coordinate was accepted.");
            });

            Check("Truncated terrain scanline cannot reuse previous row", () =>
                Reject(() => TerrainRasterReader.ReadRaster(Path.Combine(fixtures, "truncated-dem.tif"), "test", "EPSG:25833")));
            Check("Valid local BigTIFF remains readable", () =>
                Require(TerrainRasterReader.ReadRaster(Path.Combine(fixtures, "tiny-bigtiff.tif"), "test", "EPSG:25833").Elevations.SequenceEqual(new float[] { 10, 20, 30, 40 }), "Valid BigTIFF values changed."));
            foreach (var tiled in new[] { false, true })
            foreach (var compression in new[] { Compression.NONE, Compression.LZW, Compression.ADOBE_DEFLATE })
                Check($"Valid terrain tiled={tiled} compression={compression}", () =>
                {
                    var path = WriteTerrain(Path.Combine(directory, $"valid-{tiled}-{compression}.tif"), tiled, compression);
                    var raster = TerrainRasterReader.ReadRaster(path, "test", "EPSG:25833");
                    Require(raster.Width == 19 && raster.Height == 17 && raster.Elevations.Select((v, i) => v == i + 1).All(v => v), "Valid samples or edge tiles changed.");
                });
            Check("Short terrain tile rejected", () =>
            {
                var path = WriteTerrain(Path.Combine(directory, "short-tile.tif"), true, Compression.NONE);
                var end = checked((uint)new FileInfo(path).Length);
                PatchTag(path, 324, 3, end);
                PatchTag(path, 325, 3, 2);
                using (var output = new FileStream(path, FileMode.Append)) output.Write(new byte[] { 1, 0 });
                Reject(() => TerrainRasterReader.ReadRaster(path, "test", "EPSG:25833"));
            });
            Check("Oversized dimensions fail before elevation allocation", () =>
            {
                var path = Path.Combine(directory, "oversized.tif");
                File.Copy(Path.Combine(fixtures, "truncated-dem.tif"), path);
                PatchTag(path, 256, 0, 40000);
                PatchTag(path, 257, 0, 40000);
                Reject(() => TerrainRasterReader.ReadRaster(path, "test", "EPSG:25833"), "budget");
            });
            Check("Unsupported sample format fails before decode", () =>
            {
                var path = WriteTerrain(Path.Combine(directory, "unsupported.tif"), false, Compression.NONE);
                PatchTag(path, 339, 0, (uint)SampleFormat.IEEEFP);
                Reject(() => TerrainRasterReader.ReadRaster(path, "test", "EPSG:25833"), "sample format");
            });
            Check("Oversized tile fails before decode buffer allocation", () =>
            {
                var path = WriteTerrain(Path.Combine(directory, "oversized-tile.tif"), true, Compression.NONE);
                PatchTag(path, 322, 0, 8192);
                PatchTag(path, 323, 0, 8192);
                Reject(() => TerrainRasterReader.ReadRaster(path, "test", "EPSG:25833"), "buffer budget");
            });
            Check("Terrain NoData and embedded affine metadata survive", () =>
            {
                var path = Path.Combine(directory, "nodata.tif");
                File.WriteAllBytes(path, LowCompatibilityFixtures.Cog(size: 128, terrain: true, masks: false));
                var raster = TerrainRasterReader.ReadRaster(path, "test", "EPSG:25833");
                Require(raster.NoDataValue == -9999 && raster.Elevations.Any(v => v == -9999), "NoData metadata or samples changed.");
                Require(raster.GeoTransform is not null && raster.Origin == new Coordinate2D(392000.5, 5819999.5), "Embedded cell-centre placement changed.");
            });
        }
        finally { Directory.Delete(directory, true); }

        Console.WriteLine($"Correctness checks: {count - failures.Count}/{count} passed.");
        if (failures.Count > 0) throw new InvalidOperationException("Correctness regressions: " + string.Join(", ", failures));
    }

    private static bool UsesLatitudeLongitudeAxes(string srs)
    {
        var url = WmsClient.BuildGetMapRequestUrl(new WmsRequestOptions
        {
            BaseUrl = "https://example.invalid/wms", LayerName = "site", Version = "1.3.0",
            SrsName = srs, BoundingBox = new BoundingBox2D(1, 2, 3, 4)
        });
        return url.Contains("BBOX=2,1,4,3", StringComparison.Ordinal);
    }

    private static string Building(string srs, string coordinates) => $"""
        <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
          <core:cityObjectMember><bldg:Building gml:id="site"><bldg:boundedBy><bldg:RoofSurface><bldg:lod2MultiSurface>
            <gml:MultiSurface srsName="{srs}" srsDimension="3"><gml:surfaceMember><gml:Polygon><gml:exterior><gml:LinearRing><gml:posList>{coordinates}</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon></gml:surfaceMember></gml:MultiSurface>
          </bldg:lod2MultiSurface></bldg:RoofSurface></bldg:boundedBy></bldg:Building></core:cityObjectMember>
        </core:CityModel>
        """;

    private static void CheckCourtyard(WfsFeature feature)
    {
        Require(feature.Id == "courtyard" && feature.SourceLayerName == "site", "Feature grouping changed.");
        Require(feature.Geometry.OuterRings.Count == 2, "The interior boundary was lost.");
        Require(feature.Geometry.OuterRings.All(r => r.Points.Count == 5 && r.Points[0] == r.Points[^1]), "A boundary was altered.");
        Require(feature.Geometry.OuterRings[1].Points[0] == new Coordinate2D(1, 1), "Interior XY changed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action, string message = "")
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or NotSupportedException)
        {
            Require(ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase), "Unexpected rejection: " + ex.Message);
            return;
        }
        throw new Exception("Expected an explicit rejection.");
    }

    private static string WriteTerrain(string path, bool tiled, Compression compression)
    {
        using var tiff = Tiff.Open(path, "wl");
        tiff.SetField(TiffTag.IMAGEWIDTH, 19);
        tiff.SetField(TiffTag.IMAGELENGTH, 17);
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, 1);
        tiff.SetField(TiffTag.BITSPERSAMPLE, 16);
        tiff.SetField(TiffTag.SAMPLEFORMAT, SampleFormat.UINT);
        tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISBLACK);
        tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        tiff.SetField(TiffTag.COMPRESSION, compression);
        if (tiled)
        {
            tiff.SetField(TiffTag.TILEWIDTH, 16);
            tiff.SetField(TiffTag.TILELENGTH, 16);
            for (var ty = 0; ty < 17; ty += 16)
            for (var tx = 0; tx < 19; tx += 16)
            {
                var buffer = new byte[512];
                for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                    BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan((y * 16 + x) * 2), (ushort)((ty + y) * 19 + tx + x + 1));
                Require(tiff.WriteEncodedTile(tiff.ComputeTile(tx, ty, 0, 0), buffer, buffer.Length) >= 0, "Fixture tile write failed.");
            }
        }
        else
        {
            tiff.SetField(TiffTag.ROWSPERSTRIP, 2);
            var buffer = new byte[38];
            for (var y = 0; y < 17; y++)
            {
                for (var x = 0; x < 19; x++) BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(x * 2), (ushort)(y * 19 + x + 1));
                Require(tiff.WriteScanline(buffer, y), "Fixture scanline write failed.");
            }
        }
        return path;
    }

    private static void PatchTag(string path, ushort tag, int index, uint value)
    {
        var bytes = File.ReadAllBytes(path);
        var ifd = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ifd));
        for (var entry = ifd + 2; entry < ifd + 2 + count * 12; entry += 12)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry)) != tag) continue;
            var type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry + 2));
            var values = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 4));
            var size = type == 3 ? 2 : 4;
            var offset = values * size <= 4 ? entry + 8 : (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8));
            if (size == 2) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + index * size), checked((ushort)value));
            else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + index * size), value);
            File.WriteAllBytes(path, bytes);
            return;
        }
        throw new Exception("Fixture tag not found: " + tag);
    }
}
