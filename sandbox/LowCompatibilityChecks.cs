using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class LowCompatibilityChecks
{
    private static int _count;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static void Near(double actual, double expected) =>
        Require(Math.Abs(actual - expected) < 1e-6, $"Expected {expected}, got {actual}.");

    private static async Task Check(string name, Func<Task> run)
    {
        await run();
        _count++;
        Console.WriteLine("PASS " + name);
    }

    private static Task Sync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static async Task Fails(Func<Task> action, string text = "")
    {
        try
        {
            await action();
        }
        catch (Exception ex)
            when (ex
                    is InvalidDataException
                        or InvalidOperationException
                        or OperationCanceledException
                        or ArgumentException
                        or Microsoft.Data.Sqlite.SqliteException
            )
        {
            if (text.Length > 0)
                Require(
                    ex.Message.Contains(text, StringComparison.OrdinalIgnoreCase),
                    "Unexpected error: " + ex.Message
                );
            return;
        }
        throw new Exception("Invalid source was accepted.");
    }

    internal sealed class RangeHandler(byte[] data, string mode = "") : HttpMessageHandler
    {
        public readonly List<(long Start, long End)> Ranges = new();
        public int Requests => Ranges.Count;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            var range =
                request.Headers.Range?.Ranges.Single()
                ?? throw new Exception("Missing Range header.");
            var start = range.From!.Value;
            var end = Math.Min(range.To!.Value, data.LongLength - 1);
            Ranges.Add((start, end));
            Require(
                request.Headers.AcceptEncoding.Any(e => e.Value == "identity"),
                "Encoded ranges requested."
            );
            if (mode == "ignore")
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Array.Empty<byte>()),
                    }
                );
            if (start >= data.Length)
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
                );
            var payload = data.AsSpan((int)start, (int)(end - start + 1)).ToArray();
            if (mode == "truncate")
                payload = payload[..^1];
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload),
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                mode == "wrong" ? start + 1 : start,
                end,
                data.LongLength
            );
            if (mode != "no-validator")
                response.Headers.ETag = new EntityTagHeaderValue(
                    mode == "changed" && Requests > 1 ? "\"changed\"" : "\"fixture-v1\""
                );
            if (Requests > 1 && mode != "no-validator")
                Require(request.Headers.IfRange is not null, "Missing representation pin.");
            return Task.FromResult(response);
        }
    }

    public static async Task RunAsync()
    {
        _count = 0;
        var directory = Path.Combine(
            Path.GetTempPath(),
            "rhinospatial-low-check-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var context = Lod2AtomChecks.Context("EPSG:25833", new(392000, 5819968, 392032, 5820000));
        try
        {
            var raw = Enumerable.Range(0, 400000).Select(i => (byte)(i % 251)).ToArray();
            await Check(
                "byte ranges seek, pin representation and reuse cached blocks",
                async () =>
                {
                    var handler = new RangeHandler(raw);
                    using var http = new HttpClient(handler);
                    using var stream = await HttpRangeStream.OpenAsync(
                        http,
                        "https://fixture.test/data.tif"
                    );
                    stream.Position = 140000;
                    var b = new byte[30];
                    Require(
                        stream.Read(b, 0, b.Length) == 30 && b.SequenceEqual(raw[140000..140030]),
                        "Incorrect range bytes."
                    );
                    stream.Position = 140002;
                    Require(stream.Read(b, 0, b.Length) == b.Length, "Short cached range read.");
                    Require(
                        handler.Requests == 2 && stream.TransferredBytes == 131072,
                        "Range block not reused."
                    );
                    stream.Position = raw.Length - 4;
                    Require(
                        stream.Read(b, 0, 30) == 4 && stream.Read(b, 0, 30) == 0,
                        "EOF not bounded."
                    );
                }
            );
            foreach (var mode in new[] { "ignore", "wrong", "truncate" })
                await Check(
                    "range server " + mode + " rejected without fallback",
                    async () =>
                    {
                        using var http = new HttpClient(new RangeHandler(raw, mode));
                        await Fails(async () =>
                        {
                            using var s = await HttpRangeStream.OpenAsync(
                                http,
                                "https://fixture.test/a.tif"
                            );
                        });
                    }
                );
            foreach (var mode in new[] { "changed", "no-validator" })
                await Check(
                    "range snapshot " + mode + " rejected",
                    async () =>
                    {
                        using var http = new HttpClient(new RangeHandler(raw, mode));
                        using var s = await HttpRangeStream.OpenAsync(
                            http,
                            "https://fixture.test/a.tif"
                        );
                        await Fails(() =>
                            Sync(() =>
                            {
                                s.Position = 140000;
                                s.ReadByte();
                            })
                        );
                    }
                );
            await Check(
                "range byte budget enforced",
                async () =>
                {
                    using var http = new HttpClient(new RangeHandler(raw));
                    using var s = await HttpRangeStream.OpenAsync(
                        http,
                        "https://fixture.test/a.tif",
                        maximumBytes: 65536
                    );
                    await Fails(
                        () =>
                            Sync(() =>
                            {
                                s.Position = 140000;
                                s.ReadByte();
                            }),
                        "budget"
                    );
                }
            );
            await Check(
                "range request budget enforced",
                async () =>
                {
                    using var http = new HttpClient(new RangeHandler(raw));
                    using var s = await HttpRangeStream.OpenAsync(
                        http,
                        "https://fixture.test/a.tif",
                        maximumRequests: 1
                    );
                    await Fails(
                        () =>
                            Sync(() =>
                            {
                                s.Position = 140000;
                                s.ReadByte();
                            }),
                        "budget"
                    );
                }
            );
            await Check(
                "range cancellation honored before IO",
                async () =>
                {
                    using var http = new HttpClient(new RangeHandler(raw));
                    await Fails(async () =>
                    {
                        using var s = await HttpRangeStream.OpenAsync(
                            http,
                            "https://fixture.test/a.tif",
                            new CancellationToken(true)
                        );
                    });
                }
            );
            var imagery = LowCompatibilityFixtures.Cog();
            await Check(
                "COG reference reads headers without tile ranges",
                async () =>
                {
                    var handler = new RangeHandler(imagery);
                    using var http = new HttpClient(handler);
                    var metadata = await new RemoteReferenceMetadataReader(http).TryReadAsync(
                        "https://fixture.test/image.tif"
                    );
                    Require(
                        metadata?.SrsName == "EPSG:25833"
                            && metadata.NativeBoundingBox
                                == new BoundingBox2D(392000, 5819872, 392128, 5820000),
                        "Wrong COG reference extent."
                    );
                    Require(
                        handler.Ranges.All(r => r.End < 65536),
                        "Reference fetched tile ranges."
                    );
                }
            );
            await Check(
                "COG selected window pixels, affine, mask and local mesh",
                async () =>
                {
                    using var http = new HttpClient(new RangeHandler(imagery));
                    var result = await new CogReader(http).ReadImageAsync(
                        "https://fixture.test/image.tif",
                        context
                    );
                    var image = result.Image;
                    Require(image.Width == 32 && image.Height == 32, "Window not selected.");
                    Near(image.GeoTransform!.C, 392000);
                    Near(image.GeoTransform.F, 5820000);
                    var decoded = RasterImageCodec.DecodeRgba(
                        File.ReadAllBytes(image.LocalFilePath),
                        image.Width,
                        image.Height
                    );
                    Require(
                        decoded[0] == 1 && decoded[1] == 1 && decoded[2] == 170,
                        "TIFF tile row/color orientation changed."
                    );
                    Require(
                        decoded[(10 * 32 + 10) * 4 + 3] == 0
                            && decoded[(20 * 32 + 20) * 4 + 3] == 255,
                        "Internal mask lost."
                    );
                    var mesh = RasterImageMeshBuilder.Build(image, context);
                    Require(
                        mesh.Faces.Count > 0
                            && mesh.Vertices.All(v =>
                                v.X >= -1e-6 && v.X <= 32.000001 && v.Y >= -1e-6 && v.Y <= 32.000001
                            ),
                        "Shared local placement lost."
                    );
                }
            );
            await Check(
                "COG non-aligned crop uses correct tile sample offsets",
                async () =>
                {
                    using var http = new HttpClient(new RangeHandler(imagery));
                    var ctx = Lod2AtomChecks.Context(
                        "EPSG:25833",
                        new(392005, 5819960, 392025, 5819980)
                    );
                    var result = await new CogReader(http).ReadImageAsync(
                        "https://fixture.test/image.tif",
                        ctx
                    );
                    Require(
                        result.Image.Width == 20 && result.Image.Height == 20,
                        "Incorrect cropped dimensions."
                    );
                    var decoded = RasterImageCodec.DecodeRgba(
                        File.ReadAllBytes(result.Image.LocalFilePath),
                        result.Image.Width,
                        result.Image.Height
                    );
                    Require(decoded[0] == 6 && decoded[1] == 21, "Incorrect crop pixels.");
                    Near(result.Image.GeoTransform!.C, 392005);
                    Near(result.Image.GeoTransform.F, 5819980);
                }
            );
            await Check(
                "COG overview chosen for bounded large-area texture",
                async () =>
                {
                    var data = LowCompatibilityFixtures.Cog(2048, false, false);
                    using var http = new HttpClient(new RangeHandler(data));
                    var ctx = Lod2AtomChecks.Context(
                        "EPSG:25833",
                        new(392000, 5817952, 394048, 5820000)
                    );
                    var result = await new CogReader(http).ReadImageAsync(
                        "https://fixture.test/large.tif",
                        ctx
                    );
                    Require(
                        result.OverviewWidth == 1024
                            && result.Image.Width == 1024
                            && result.TransferredBytes < data.Length,
                        "Overview was not used for range subset access."
                    );
                    Near(result.Image.GeoTransform!.A, 2);
                }
            );
            await Check(
                "remote DEM preserves elevation, NoData, mask and cell centers",
                async () =>
                {
                    using var http = new HttpClient(
                        new RangeHandler(LowCompatibilityFixtures.Cog(128, true))
                    );
                    var result = await new CogReader(http).ReadTerrainAsync(
                        "https://fixture.test/dem.tif",
                        context
                    );
                    var raster = result.Raster;
                    Require(
                        raster.Width == 32 && raster.Height == 32 && raster.NoDataValue == -9999,
                        $"Wrong DEM metadata: {raster.Width}x{raster.Height}, NoData {raster.NoDataValue}."
                    );
                    Near(raster.Origin.X, 392000.5);
                    Near(raster.Origin.Y, 5819999.5);
                    Near(raster.Elevations[1], 100.25);
                    Require(
                        raster.Elevations[0] == -9999
                            && float.IsNaN(raster.Elevations[10 * 32 + 10]),
                        "Missing DEM values fabricated."
                    );
                }
            );
            await Check(
                "RGB imagery rejected as a DEM",
                async () =>
                {
                    using var http = new HttpClient(new RangeHandler(imagery));
                    await Fails(
                        () =>
                            new CogReader(http).ReadTerrainAsync(
                                "https://fixture.test/image.tif",
                                context
                            ),
                        "one contiguous"
                    );
                }
            );
            await Check(
                "COG non-overlap rejected",
                async () =>
                {
                    using var http = new HttpClient(new RangeHandler(imagery));
                    await Fails(
                        () =>
                            new CogReader(http).ReadImageAsync(
                                "https://fixture.test/image.tif",
                                Lod2AtomChecks.Context("EPSG:25833", new(1, 1, 2, 2))
                            ),
                        "overlap"
                    );
                }
            );
            await Check(
                "BigTIFF has explicit decoder limitation",
                async () =>
                {
                    var big = (byte[])imagery.Clone();
                    big[2] = 43;
                    using var http = new HttpClient(new RangeHandler(big));
                    await Fails(
                        () => new CogReader(http).ReadMetadataAsync("https://fixture.test/big.tif"),
                        "BigTIFF"
                    );
                }
            );
            await Check(
                "TIFF field allocation bomb rejected before decoder allocation",
                async () =>
                {
                    var bomb = (byte[])imagery.Clone();
                    var count = BinaryPrimitives.ReadUInt16LittleEndian(bomb.AsSpan(8));
                    for (var i = 0; i < count; i++)
                    {
                        var offset = 10 + i * 12;
                        if (BinaryPrimitives.ReadUInt16LittleEndian(bomb.AsSpan(offset)) == 324)
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                bomb.AsSpan(offset + 4),
                                uint.MaxValue
                            );
                    }
                    using var http = new HttpClient(new RangeHandler(bomb));
                    await Fails(
                        () =>
                            new CogReader(http).ReadMetadataAsync("https://fixture.test/bomb.tif"),
                        "allocation budget"
                    );
                }
            );
            await Check(
                "WGS84 UTM north/south codes transform without CRS substitution",
                () =>
                    Sync(() =>
                    {
                        foreach (
                            var test in new[]
                            {
                                (Srs: "EPSG:32632", Lon: 13.4, Lat: 52.5),
                                (Srs: "EPSG:32734", Lon: 18.4, Lat: -33.9),
                            }
                        )
                        {
                            Require(
                                SpatialReferenceTransform.TryTransformXY(
                                    "EPSG:4326",
                                    test.Srs,
                                    test.Lon,
                                    test.Lat,
                                    out var x,
                                    out var y
                                ),
                                "UTM forward transform unavailable."
                            );
                            Require(
                                SpatialReferenceTransform.TryTransformXY(
                                    test.Srs,
                                    "EPSG:4326",
                                    x,
                                    y,
                                    out var lon,
                                    out var lat
                                ),
                                "UTM inverse transform unavailable."
                            );
                            Near(lon, test.Lon);
                            Near(lat, test.Lat);
                        }
                    })
            );
            await Check(
                "COG alpha is converted from associated TIFF to straight PNG",
                async () =>
                {
                    using var http = new HttpClient(
                        new RangeHandler(LowCompatibilityFixtures.Cog(128, false, false, true))
                    );
                    var result = await new CogReader(http).ReadImageAsync(
                        "https://fixture.test/alpha.tif",
                        context
                    );
                    var pixels = RasterImageCodec.DecodeRgba(
                        File.ReadAllBytes(result.Image.LocalFilePath),
                        32,
                        32
                    );
                    Require(
                        pixels[3] == 128 && Math.Abs(pixels[2] - 170) <= 1,
                        "Semi-transparent pixels were darkened."
                    );
                }
            );
            await Check(
                "local GeoTIFF uses the GDAL ASCII NoData value rather than its count",
                () =>
                    Sync(() =>
                    {
                        var path = Path.Combine(directory, "local-dem.tif");
                        File.WriteAllBytes(path, LowCompatibilityFixtures.Cog(128, true));
                        var raster = TerrainRasterReader.ReadRaster(path, "test", "EPSG:25833");
                        Require(raster.NoDataValue == -9999, "Local NoData tag was misread.");
                    })
            );
            var gpkg = LowCompatibilityFixtures.GeoPackage(Path.Combine(directory, "indexed.gpkg"));
            var before = SHA256.HashData(File.ReadAllBytes(gpkg));
            await Check(
                "GeoPackage discovers feature layers, CRS and quoted identifiers",
                () =>
                    Sync(() =>
                    {
                        var layers = GeoPackageReader.ReadLayers(gpkg);
                        Require(
                            layers.Count == 2
                                && layers.Any(l => l.Name == "survey \"points\"")
                                && layers.All(l => l.SourceSrs == "EPSG:25833"),
                            "Catalog discovery lost layers or CRS."
                        );
                        Require(
                            VectorSourceResolver.Resolve(gpkg).Kind
                                == VectorSourceKind.LocalGeoPackage,
                            "GeoPackage routed to service loader."
                        );
                    })
            );
            await Check(
                "GeoPackage RTree query preserves attributes and polygon holes",
                () =>
                    Sync(() =>
                    {
                        var read = GeoPackageReader.ReadFeatures(gpkg, "buildings", context);
                        Require(
                            read.UsedSpatialIndex
                                && read.ScannedCount == 1
                                && read.Features.Count == 1,
                            "Spatial index was not used."
                        );
                        var feature = read.Features[0];
                        Require(
                            feature.Geometry.OuterRings.Count == 2
                                && feature.Attributes["name"] == "courtyard"
                                && feature.Attributes["nullable"] is null
                                && feature.Attributes["payload"] == "[BLOB 3 bytes]",
                            "Holes/attributes were lost."
                        );
                        Near(feature.Geometry.OuterRings[1].Points[0].X, 392008);
                    })
            );
            await Check(
                "GeoPackage quoted layer uses bounded fallback scan",
                () =>
                    Sync(() =>
                    {
                        var read = GeoPackageReader.ReadFeatures(
                            gpkg,
                            "survey \"points\"",
                            context
                        );
                        Require(
                            !read.UsedSpatialIndex
                                && read.Features.Single().Geometry.Points.Single()
                                    == new Coordinate2D(392005, 5819990),
                            "Quoted layer was not read."
                        );
                    })
            );
            await Check(
                "GeoPackage without RTree filters context and null geometry",
                () =>
                    Sync(() =>
                    {
                        var path = LowCompatibilityFixtures.GeoPackage(
                            Path.Combine(directory, "scan.gpkg"),
                            false
                        );
                        var read = GeoPackageReader.ReadFeatures(path, "buildings", context);
                        Require(
                            !read.UsedSpatialIndex
                                && read.ScannedCount == 3
                                && read.Features.Count == 1,
                            "Fallback scan did not filter."
                        );
                    })
            );
            await Check(
                "GeoPackage multiple layers require explicit selection",
                () =>
                    Fails(
                        () => Sync(() => GeoPackageReader.ReadFeatures(gpkg, "", context)),
                        "explicit selection"
                    )
            );
            await Check(
                "GeoPackage sole feature layer can be selected automatically",
                () =>
                    Sync(() =>
                    {
                        var path = LowCompatibilityFixtures.GeoPackage(
                            Path.Combine(directory, "sole.gpkg")
                        );
                        LowCompatibilityFixtures.Sql(
                            path,
                            "DELETE FROM gpkg_contents WHERE table_name<>'buildings'; DELETE FROM gpkg_geometry_columns WHERE table_name<>'buildings'"
                        );
                        Require(
                            GeoPackageReader.ReadFeatures(path, "", context).Features.Count == 1,
                            "Sole layer not selected."
                        );
                    })
            );
            await Check(
                "GeoPackage reference combines declared catalog metadata only",
                () =>
                    Sync(() =>
                    {
                        var path = LowCompatibilityFixtures.GeoPackage(
                            Path.Combine(directory, "metadata.gpkg")
                        );
                        LowCompatibilityFixtures.Sql(path, "UPDATE buildings SET geom=x'00'");
                        var metadata = ReferenceSourceMetadataReader.Read(path);
                        Require(
                            metadata.SourceKind == "GeoPackage"
                                && metadata.SourceItemCount == 2
                                && metadata.Wgs84BoundingBox is not null,
                            "Catalog reference failed or scanned geometry."
                        );
                    })
            );
            await Check(
                "GeoPackage missing extents do not trigger geometry scan",
                () =>
                    Fails(
                        () =>
                            Sync(() =>
                            {
                                var path = LowCompatibilityFixtures.GeoPackage(
                                    Path.Combine(directory, "noextent.gpkg")
                                );
                                LowCompatibilityFixtures.Sql(
                                    path,
                                    "UPDATE gpkg_contents SET min_x=NULL"
                                );
                                ReferenceSourceMetadataReader.Read(path);
                            }),
                        "catalog extents"
                    )
            );
            await Check(
                "GeoPackage custom table SRS ID honors EPSG organization code",
                () =>
                    Sync(() =>
                    {
                        var path = LowCompatibilityFixtures.GeoPackage(
                            Path.Combine(directory, "custom.gpkg"),
                            customSrs: true
                        );
                        Require(
                            GeoPackageReader.ReadFeatures(path, "buildings", context).Features.Count
                                == 1,
                            "Local SRS id was mistaken for EPSG code."
                        );
                    })
            );
            await Check(
                "GeoPackage unknown CRS is rejected without guessing",
                () =>
                    Fails(
                        () =>
                            Sync(() =>
                            {
                                var path = LowCompatibilityFixtures.GeoPackage(
                                    Path.Combine(directory, "unknown.gpkg")
                                );
                                LowCompatibilityFixtures.Sql(
                                    path,
                                    "UPDATE gpkg_spatial_ref_sys SET organization='LOCAL' WHERE srs_id=25833"
                                );
                                GeoPackageReader.ReadFeatures(path, "buildings", context);
                            }),
                        "no CRS was guessed"
                    )
            );
            await Check(
                "GeoPackage malformed binary geometry is rejected",
                () =>
                    Fails(
                        () =>
                            Sync(() =>
                            {
                                var path = LowCompatibilityFixtures.GeoPackage(
                                    Path.Combine(directory, "badgeometry.gpkg")
                                );
                                LowCompatibilityFixtures.Sql(
                                    path,
                                    "UPDATE buildings SET geom=x'4750000100000000' WHERE fid=1"
                                );
                                GeoPackageReader.ReadFeatures(path, "buildings", context);
                            }),
                        "CRS/envelope"
                    )
            );
            await Check(
                "GeoPackage malicious WKB count rejected before NTS allocation",
                () =>
                    Fails(
                        () =>
                            Sync(() =>
                            {
                                var path = LowCompatibilityFixtures.GeoPackage(
                                    Path.Combine(directory, "badwkb.gpkg")
                                );
                                var blob = Convert.FromHexString(
                                    "47500001E96400000102000000FFFFFF7F"
                                );
                                LowCompatibilityFixtures.Sql(
                                    path,
                                    "UPDATE buildings SET geom=x'"
                                        + Convert.ToHexString(blob)
                                        + "' WHERE fid=1"
                                );
                                GeoPackageReader.ReadFeatures(path, "buildings", context);
                            }),
                        "Truncated WKB"
                    )
            );
            await Check(
                "GeoPackage application ID validation",
                () =>
                    Fails(
                        () =>
                            Sync(() =>
                            {
                                var path = LowCompatibilityFixtures.GeoPackage(
                                    Path.Combine(directory, "notgpkg.gpkg")
                                );
                                LowCompatibilityFixtures.Sql(path, "PRAGMA application_id=0");
                                GeoPackageReader.ReadLayers(path);
                            }),
                        "application ID"
                    )
            );
            await Check(
                "GeoPackage explicit feature limit reported",
                () =>
                    Sync(() =>
                    {
                        var result = GeoPackageReader.ReadFeatures(gpkg, "buildings", context, 1);
                        Require(
                            result.Features.Count == 1 && result.ReachedFeatureLimit,
                            "Feature cap not reported."
                        );
                    })
            );
            await Check(
                "GeoPackage cancellation honored",
                () =>
                    Fails(() =>
                        Sync(() =>
                            GeoPackageReader.ReadFeatures(
                                gpkg,
                                "buildings",
                                context,
                                token: new CancellationToken(true)
                            )
                        )
                    )
            );
            await Check(
                "GeoPackage standard multi-geometries retain every curve and point",
                () =>
                    Sync(() =>
                    {
                        var path = LowCompatibilityFixtures.GeoPackage(
                            Path.Combine(directory, "collection.gpkg")
                        );
                        var factory = new NetTopologySuite.Geometries.GeometryFactory();
                        var a = new NetTopologySuite.Geometries.Coordinate(392005, 5819990);
                        var b = new NetTopologySuite.Geometries.Coordinate(392006, 5819991);
                        var line = factory.CreateLineString(new[] { a, b });
                        var points = factory.CreateMultiPoint(
                            new[] { factory.CreatePoint(a), factory.CreatePoint(b) }
                        );
                        var collection = factory.CreateGeometryCollection(
                            new NetTopologySuite.Geometries.Geometry[]
                            {
                                factory.CreateMultiLineString(new[] { line, line }),
                                points,
                            }
                        );
                        var blob = LowCompatibilityFixtures.BinaryGeometry(collection, 25833);
                        LowCompatibilityFixtures.Sql(
                            path,
                            "UPDATE gpkg_geometry_columns SET geometry_type_name='GEOMETRY' WHERE table_name='buildings'; UPDATE buildings SET geom=x'"
                                + Convert.ToHexString(blob)
                                + "' WHERE fid=1"
                        );
                        var geometry = GeoPackageReader
                            .ReadFeatures(path, "buildings", context)
                            .Features.Single()
                            .Geometry;
                        Require(
                            geometry.LineStrings.Count == 2 && geometry.Points.Count == 2,
                            "Multi-geometries lost parts."
                        );
                    })
            );
            await Check(
                "GeoPackage big-endian header, XY envelope and WKB",
                () =>
                    Sync(() =>
                    {
                        var path = LowCompatibilityFixtures.GeoPackage(
                            Path.Combine(directory, "big-endian.gpkg")
                        );
                        var blob = new byte[61];
                        blob[0] = 71;
                        blob[1] = 80;
                        blob[3] = 2;
                        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(4, 4), 25833);
                        var values = new[] { 392005d, 392005d, 5819990d, 5819990d };
                        for (var i = 0; i < 4; i++)
                            BinaryPrimitives.WriteInt64BigEndian(
                                blob.AsSpan(8 + i * 8, 8),
                                BitConverter.DoubleToInt64Bits(values[i])
                            );
                        blob[40] = 0;
                        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(41, 4), 1);
                        BinaryPrimitives.WriteInt64BigEndian(
                            blob.AsSpan(45, 8),
                            BitConverter.DoubleToInt64Bits(392005)
                        );
                        BinaryPrimitives.WriteInt64BigEndian(
                            blob.AsSpan(53, 8),
                            BitConverter.DoubleToInt64Bits(5819990)
                        );
                        LowCompatibilityFixtures.Sql(
                            path,
                            "UPDATE gpkg_geometry_columns SET geometry_type_name='GEOMETRY' WHERE table_name='buildings'; UPDATE buildings SET geom=x'"
                                + Convert.ToHexString(blob)
                                + "' WHERE fid=1"
                        );
                        Require(
                            GeoPackageReader
                                .ReadFeatures(path, "buildings", context)
                                .Features.Single()
                                .Geometry.Points.Single() == new Coordinate2D(392005, 5819990),
                            "Byte order/envelope offsets changed coordinates."
                        );
                    })
            );
            await Check(
                "GeoPackage unclosed polygon rings are rejected without repair",
                () =>
                    Fails(
                        () =>
                            Sync(() =>
                            {
                                var path = LowCompatibilityFixtures.GeoPackage(
                                    Path.Combine(directory, "unclosed.gpkg")
                                );
                                var ring =
                                    new NetTopologySuite.Geometries.GeometryFactory().CreatePolygon(
                                        new[]
                                        {
                                            new NetTopologySuite.Geometries.Coordinate(
                                                392001,
                                                5819990
                                            ),
                                            new NetTopologySuite.Geometries.Coordinate(
                                                392002,
                                                5819990
                                            ),
                                            new NetTopologySuite.Geometries.Coordinate(
                                                392002,
                                                5819991
                                            ),
                                            new NetTopologySuite.Geometries.Coordinate(
                                                392001,
                                                5819990
                                            ),
                                        }
                                    );
                                var blob = LowCompatibilityFixtures.BinaryGeometry(ring, 25833);
                                BitConverter.GetBytes(392003d).CopyTo(blob, 8 + 13 + 3 * 16);
                                LowCompatibilityFixtures.Sql(
                                    path,
                                    "UPDATE buildings SET geom=x'"
                                        + Convert.ToHexString(blob)
                                        + "' WHERE fid=1"
                                );
                                GeoPackageReader.ReadFeatures(path, "buildings", context);
                            }),
                        "Unclosed"
                    )
            );
            await Check(
                "GeoPackage extended binary geometries rejected explicitly",
                () =>
                    Fails(
                        () =>
                            Sync(() =>
                            {
                                var path = LowCompatibilityFixtures.GeoPackage(
                                    Path.Combine(directory, "extended.gpkg")
                                );
                                var geometry =
                                    new NetTopologySuite.Geometries.GeometryFactory().CreatePoint(
                                        new NetTopologySuite.Geometries.Coordinate(392005, 5819990)
                                    );
                                var blob = LowCompatibilityFixtures.BinaryGeometry(geometry, 25833);
                                blob[3] |= 32;
                                LowCompatibilityFixtures.Sql(
                                    path,
                                    "UPDATE buildings SET geom=x'"
                                        + Convert.ToHexString(blob)
                                        + "' WHERE fid=1"
                                );
                                GeoPackageReader.ReadFeatures(path, "buildings", context);
                            }),
                        "Unsupported"
                    )
            );
            await Check(
                "GeoPackage declared feature type must match its WKB",
                () =>
                    Fails(
                        () =>
                            Sync(() =>
                            {
                                var path = LowCompatibilityFixtures.GeoPackage(
                                    Path.Combine(directory, "type-mismatch.gpkg")
                                );
                                LowCompatibilityFixtures.Sql(
                                    path,
                                    "UPDATE gpkg_geometry_columns SET geometry_type_name='POINT' WHERE table_name='buildings'"
                                );
                                GeoPackageReader.ReadFeatures(path, "buildings", context);
                            }),
                        "disagrees"
                    )
            );
            await Check(
                "GeoPackage unsupported declared geometry extensions rejected",
                () =>
                    Fails(
                        () =>
                            Sync(() =>
                            {
                                var path = LowCompatibilityFixtures.GeoPackage(
                                    Path.Combine(directory, "type-extension.gpkg")
                                );
                                LowCompatibilityFixtures.Sql(
                                    path,
                                    "UPDATE gpkg_geometry_columns SET geometry_type_name='CIRCULARSTRING' WHERE table_name='buildings'"
                                );
                                GeoPackageReader.ReadFeatures(path, "buildings", context);
                            }),
                        "Unsupported"
                    )
            );
            await Check(
                "GeoPackage source remains byte-identical after reads",
                () =>
                    Sync(() =>
                        Require(
                            before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(gpkg))),
                            "Read operation mutated the database."
                        )
                    )
            );
            Console.WriteLine($"Low compatibility checks passed: {_count}.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
