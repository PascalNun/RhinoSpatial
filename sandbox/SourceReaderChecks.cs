using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class SourceReaderChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rhinospatial-reader-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var failures = new List<string>();
        var count = 0;
        try
        {
            void Check(string name, Action test)
            {
                count++;
                try
                {
                    test();
                    Console.WriteLine($"PASS: {name}");
                }
                catch (Exception exception)
                {
                    failures.Add(name);
                    Console.WriteLine($"FAIL: {name}: {exception.Message}");
                }
            }

            string Write(string name, string content)
            {
                var path = Path.Combine(directory, name);
                File.WriteAllText(path, content);
                return path;
            }

            const string cornerHeader = "ncols 2\nnrows 2\nxllcorner 100\nyllcorner 200\ncellsize 10\n";
            const string centerHeader = "ncols 2\nnrows 2\nxllcenter 105\nyllcenter 205\ncellsize 10\n";
            var cornerPath = Write("corner.asc", cornerHeader + "NODATA_value -9999\n10 20\n30 40\n");
            var centerPath = Write("center.asc", centerHeader + "NODATA_value -9999\n10 20\n30 40\n");

            foreach (var path in new[] { cornerPath, centerPath })
            {
                Check($"ASCII {Path.GetFileName(path)} sample placement", () =>
                {
                    var result = TerrainTextGridReader.ReadRaster(path, "test", "EPSG:25832");
                    Require(result.BoundingBox == new BoundingBox2D(105, 205, 115, 215), "Elevation samples must be positioned at cell centers.");
                    Require(result.Raster.Origin == new Coordinate2D(105, 215), "The first sample must be the top-left cell center.");
                    Require(result.Raster.Elevations.SequenceEqual(new float[] { 10, 20, 30, 40 }), "Sample order changed.");
                });
                Check($"ASCII {Path.GetFileName(path)} reference extent", () =>
                {
                    var metadata = TerrainTextGridReader.ReadSourceMetadata(path, "EPSG:25832");
                    Require(metadata.BoundingBox == new BoundingBox2D(100, 200, 120, 220), "Reference metadata must cover the full cell footprint.");
                });
            }

            Check("ASCII optional NoData header", () =>
            {
                var path = Write("default-nodata.asc", cornerHeader + "10 -9999\n30 40\n");
                var result = TerrainTextGridReader.ReadRaster(path, "test", "EPSG:25832");
                Require(result.Raster.Elevations.SequenceEqual(new float[] { 10, -9999, 30, 40 }), "The first data row was consumed as a header.");
                Require(result.Raster.NoDataValue == -9999, "The default NoData sentinel must be -9999.");
            });
            Check("ASCII wrapped rows", () =>
            {
                var path = Write("wrapped.asc", cornerHeader + "NODATA_value -9999\n10\n20 30 40\n");
                var result = TerrainTextGridReader.ReadRaster(path, "test", "EPSG:25832");
                Require(result.Raster.Elevations.SequenceEqual(new float[] { 10, 20, 30, 40 }), "NCOLS, rather than text line breaks, must determine raster rows.");
            });
            Check("ASCII truncated values rejected", () =>
                RequireInvalid(() => TerrainTextGridReader.ReadRaster(
                    Write("truncated.asc", cornerHeader + "NODATA_value -9999\n10\n30 40\n"), "test", "EPSG:25832"), "cell"));
            Check("ASCII extra values rejected", () =>
                RequireInvalid(() => TerrainTextGridReader.ReadRaster(
                    Write("extra.asc", cornerHeader + "NODATA_value -9999\n10 20 25\n30 40\n"), "test", "EPSG:25832"), "cell"));
            Check("ASCII invalid dimensions rejected", () =>
                RequireInvalid(() => TerrainTextGridReader.ReadSourceMetadata(
                    Write("dimensions.asc", cornerHeader.Replace("ncols 2", "ncols 0") + "NODATA_value -9999\n"), "EPSG:25832"), "ncols"));
            Check("ASCII invalid cell size rejected", () =>
                RequireInvalid(() => TerrainTextGridReader.ReadSourceMetadata(
                    Write("cellsize.asc", cornerHeader.Replace("cellsize 10", "cellsize -10") + "NODATA_value -9999\n"), "EPSG:25832"), "cellsize"));
            Check("ASCII mixed origin conventions rejected", () =>
                RequireInvalid(() => TerrainTextGridReader.ReadSourceMetadata(
                    Write("mixed.asc", cornerHeader.Replace("xllcorner", "xllcenter") + "NODATA_value -9999\n"), "EPSG:25832"), "origin"));

            Check("XYZ irregular spacing rejected", () =>
                RequireInvalid(() => TerrainTextGridReader.ReadRaster(
                    Write("irregular.xyz", "0 0 1\n1 0 2\n3 0 3\n0 1 4\n1 1 5\n3 1 6\n"), "test", "EPSG:25832"), "regular"));
            Check("XYZ sparse regular grid preserves missing cells", () =>
            {
                var result = TerrainTextGridReader.ReadRaster(
                    Write("sparse.xyz", "x,y,z\n0,0,1\n2,0,2\n0,2,3\n"), "test", "EPSG:25832");
                Require(result.Raster.Width == 2 && result.Raster.Height == 2, "Sparse grid dimensions changed.");
                Require(float.IsNaN(result.Raster.Elevations[1]), "A missing sample must remain NoData.");
            });
            Check("XYZ non-finite position rejected", () =>
                RequireInvalid(() => TerrainTextGridReader.ReadRaster(
                    Write("nonfinite.xyz", "0 0 1\n1 0 2\n0 1 3\nNaN 1 4\n"), "test", "EPSG:25832"), "finite"));

            Check("GeoJSON null attributes", () =>
            {
                var features = GeoJsonReader.ReadFeatures(Collection("{\"type\":\"Point\",\"coordinates\":[8,50]}" , "null"), "test");
                Require(features.Single().Attributes.Count == 0, "Null properties should mean no attributes.");
                Require(features.Single().Geometry.Points.Single() == new Coordinate2D(8, 50), "The valid point must survive.");
            });
            foreach (var type in new[] { "Point", "Polygon", "MultiPoint", "LineString", "MultiLineString", "MultiPolygon" })
            {
                Check($"GeoJSON empty {type}", () =>
                {
                    var feature = GeoJsonReader.ReadFeatures(Collection($"{{\"type\":\"{type}\",\"coordinates\":[]}}"), "test").Single();
                    Require(!SpatialFeatureFilter.TryGetFeatureBounds(feature, out _), "Empty coordinates must not fabricate geometry at (0,0).");
                });
            }
            Check("GeoJSON malformed point rejected", () =>
                RequireInvalid(() => GeoJsonReader.ReadFeatures(Collection("{\"type\":\"Point\",\"coordinates\":[8]}"), "test"), "coordinate"));
            foreach (var name in new[] { "EPSG:25832", "urn:ogc:def:crs:EPSG::25832", "urn:ogc:def:crs:EPSG:6.6:25832", "http://www.opengis.net/def/crs/EPSG/0/25832", "http://www.opengis.net/gml/srs/epsg.xml#25832" })
            {
                Check($"GeoJSON CRS {name}", () =>
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(new { crs = new { type = "name", properties = new { name } } });
                    Require(GeoJsonReader.TryReadSourceSrs(json) == "EPSG:25832", "A CRS version must not be mistaken for its EPSG code.");
                });
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Require(failures.Count == 0, $"{failures.Count} of {count} source reader checks failed: {string.Join(", ", failures)}");
        Console.WriteLine($"All {count} source reader checks passed.");
    }

    private static string Collection(string geometry, string properties = "{}") =>
        $"{{\"type\":\"FeatureCollection\",\"features\":[{{\"type\":\"Feature\",\"geometry\":{geometry},\"properties\":{properties}}}]}}";

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
