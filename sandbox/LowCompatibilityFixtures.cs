using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class LowCompatibilityFixtures
{
    private sealed record Tag(ushort Id, ushort Type, uint Count, byte[] Data);

    private sealed class Page
    {
        public int Width,
            Height,
            Channels,
            Bits,
            Flags;
        public byte[][] Tiles = Array.Empty<byte[]>();
        public uint[] Offsets = Array.Empty<uint>();
        public List<Tag> Tags = new();
        public uint Ifd;
    }

    internal static byte[] Cog(
        int size = 128,
        bool terrain = false,
        bool masks = true,
        bool alpha = false
    )
    {
        var pages = new List<Page>();
        foreach (var factor in new[] { 1, 2 })
        {
            pages.Add(
                new Page
                {
                    Width = size / factor,
                    Height = size / factor,
                    Channels =
                        terrain ? 1
                        : alpha ? 4
                        : 3,
                    Bits = terrain ? 32 : 8,
                    Flags = factor == 1 ? 0 : 1,
                }
            );
            if (masks)
                pages.Add(
                    new Page
                    {
                        Width = size / factor,
                        Height = size / factor,
                        Channels = 1,
                        Bits = 1,
                        Flags = factor == 1 ? 4 : 5,
                    }
                );
        }
        const int tile = 64;
        foreach (var p in pages)
        {
            var isMask = (p.Flags & 4) != 0;
            var tiles = new List<byte[]>();
            for (var y = 0; y < p.Height; y += tile)
            for (var x = 0; x < p.Width; x += tile)
            {
                var raw = new byte[tile * tile * p.Channels * p.Bits / 8];
                for (var ly = 0; ly < tile; ly++)
                for (var lx = 0; lx < tile; lx++)
                {
                    var xx = x + lx;
                    var yy = y + ly;
                    var index = ly * tile + lx;
                    var factor = size / p.Width;
                    if (isMask)
                    {
                        if (!(xx * factor is >= 8 and < 16 && yy * factor is >= 8 and < 16))
                            raw[ly * tile / 8 + lx / 8] |= (byte)(128 >> (lx % 8));
                    }
                    else if (terrain)
                        BitConverter
                            .GetBytes(
                                xx == 0 && yy == 0
                                    ? -9999f
                                    : 100f + xx * factor * .25f + yy * factor * .5f
                            )
                            .CopyTo(raw, index * 4);
                    else
                    {
                        raw[index * p.Channels] = (byte)(xx % 200 + 1);
                        raw[index * p.Channels + 1] = (byte)(yy % 200 + 1);
                        raw[index * p.Channels + 2] = 170;
                        if (alpha)
                            raw[index * p.Channels + 3] = 128;
                    }
                }
                using var compressed = new MemoryStream();
                using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true))
                    z.Write(raw);
                tiles.Add(compressed.ToArray());
            }
            p.Tiles = tiles.ToArray();
            p.Offsets = new uint[p.Tiles.Length];
        }
        static byte[] Shorts(params ushort[] values)
        {
            using var m = new MemoryStream();
            using var w = new BinaryWriter(m);
            foreach (var v in values)
                w.Write(v);
            return m.ToArray();
        }
        static byte[] Longs(IEnumerable<uint> values)
        {
            using var m = new MemoryStream();
            using var w = new BinaryWriter(m);
            foreach (var v in values)
                w.Write(v);
            return m.ToArray();
        }
        static byte[] Doubles(params double[] values)
        {
            using var m = new MemoryStream();
            using var w = new BinaryWriter(m);
            foreach (var v in values)
                w.Write(v);
            return m.ToArray();
        }
        foreach (var p in pages)
        {
            var isMask = (p.Flags & 4) != 0;
            var factor = (double)size / p.Width;
            void S(ushort tag, ushort value) => p.Tags.Add(new(tag, 3, 1, Shorts(value)));
            void L(ushort tag, uint value) => p.Tags.Add(new(tag, 4, 1, Longs(new[] { value })));
            L(254, (uint)p.Flags);
            L(256, (uint)p.Width);
            L(257, (uint)p.Height);
            p.Tags.Add(
                new(
                    258,
                    3,
                    (uint)p.Channels,
                    Shorts(Enumerable.Repeat((ushort)p.Bits, p.Channels).ToArray())
                )
            );
            S(259, 8);
            S(
                262,
                (ushort)(
                    isMask ? 4
                    : terrain ? 1
                    : 2
                )
            );
            S(274, 1);
            S(277, (ushort)p.Channels);
            S(284, 1);
            L(322, tile);
            L(323, tile);
            p.Tags.Add(new(324, 4, (uint)p.Tiles.Length, Longs(p.Offsets)));
            p.Tags.Add(
                new(325, 4, (uint)p.Tiles.Length, Longs(p.Tiles.Select(t => (uint)t.Length)))
            );
            S(339, (ushort)(terrain && !isMask ? 3 : 1));
            if (alpha && !isMask)
                S(338, 2);
            if (!isMask)
            {
                p.Tags.Add(new(33550, 12, 3, Doubles(factor, factor, 0)));
                p.Tags.Add(new(33922, 12, 6, Doubles(0, 0, 0, 392000, 5820000, 0)));
                var keys = Shorts(1, 1, 0, 3, 1024, 0, 1, 1, 1025, 0, 1, 1, 3072, 0, 1, 25833);
                p.Tags.Add(new(34735, 3, (uint)keys.Length / 2, keys));
                if (terrain)
                    p.Tags.Add(new(42113, 2, 6, Encoding.ASCII.GetBytes("-9999\0")));
            }
            p.Tags = p.Tags.OrderBy(t => t.Id).ToList();
        }
        uint location = 8;
        foreach (var p in pages)
        {
            p.Ifd = location;
            location += checked((uint)(2 + p.Tags.Count * 12 + 4));
        }
        var external = new Dictionary<(Page, ushort), uint>();
        foreach (var p in pages)
        foreach (var tag in p.Tags)
            if (tag.Data.Length > 4)
            {
                external[(p, tag.Id)] = location;
                location += checked((uint)tag.Data.Length);
                if (location % 2 != 0)
                    location++;
            }
        location = Math.Max(65536, location);
        foreach (var p in pages.OrderBy(p => p.Width))
            for (var i = 0; i < p.Tiles.Length; i++)
            {
                p.Offsets[i] = location;
                location += checked((uint)p.Tiles[i].Length);
            }
        using var output = new MemoryStream(new byte[location], true);
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0x4949);
        writer.Write((ushort)42);
        writer.Write(pages[0].Ifd);
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var p = pages[pageIndex];
            output.Position = p.Ifd;
            writer.Write((ushort)p.Tags.Count);
            foreach (var tag in p.Tags)
            {
                writer.Write(tag.Id);
                writer.Write(tag.Type);
                writer.Write(tag.Count);
                var data = tag.Id == 324 ? Longs(p.Offsets) : tag.Data;
                if (data.Length <= 4)
                {
                    writer.Write(data);
                    writer.Write(new byte[4 - data.Length]);
                }
                else
                    writer.Write(external[(p, tag.Id)]);
            }
            writer.Write(pageIndex + 1 < pages.Count ? pages[pageIndex + 1].Ifd : 0u);
            foreach (var tag in p.Tags)
                if (tag.Data.Length > 4)
                {
                    output.Position = external[(p, tag.Id)];
                    writer.Write(tag.Id == 324 ? Longs(p.Offsets) : tag.Data);
                }
            for (var i = 0; i < p.Tiles.Length; i++)
            {
                output.Position = p.Offsets[i];
                writer.Write(p.Tiles[i]);
            }
        }
        return output.ToArray();
    }

    internal static string GeoPackage(string path, bool index = true, bool customSrs = false)
    {
        try
        {
            _ = SQLitePCL.raw.sqlite3_libversion_number();
        }
        catch
        {
            if (OperatingSystem.IsWindows())
                SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
            else
                SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        }
        if (File.Exists(path))
            File.Delete(path);
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
        );
        connection.Open();
        var srs = customSrs ? 99001 : 25833;
        using var command = connection.CreateCommand();
        command.CommandText = $""""
            PRAGMA application_id=1196444487; PRAGMA user_version=10300;
            CREATE TABLE gpkg_spatial_ref_sys(srs_name TEXT NOT NULL,srs_id INTEGER PRIMARY KEY,organization TEXT NOT NULL,organization_coordsys_id INTEGER NOT NULL,definition TEXT NOT NULL,description TEXT);
            INSERT INTO gpkg_spatial_ref_sys VALUES('Undefined Cartesian',-1,'NONE',-1,'undefined',NULL),('Undefined geographic',0,'NONE',0,'undefined',NULL),('WGS84',4326,'EPSG',4326,'GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433]]',NULL);
            CREATE TABLE gpkg_contents(table_name TEXT PRIMARY KEY,data_type TEXT NOT NULL,identifier TEXT UNIQUE,description TEXT DEFAULT '',last_change DATETIME NOT NULL DEFAULT '2026-11-02T00:00:00.000Z',min_x DOUBLE,min_y DOUBLE,max_x DOUBLE,max_y DOUBLE,srs_id INTEGER);
            CREATE TABLE gpkg_geometry_columns(table_name TEXT NOT NULL,column_name TEXT NOT NULL,geometry_type_name TEXT NOT NULL,srs_id INTEGER NOT NULL,z TINYINT NOT NULL,m TINYINT NOT NULL,PRIMARY KEY(table_name,column_name),UNIQUE(table_name));
            CREATE TABLE buildings(fid INTEGER PRIMARY KEY,geom POLYGON,name TEXT,nullable TEXT,payload BLOB);
            CREATE TABLE "survey ""points"""(fid INTEGER PRIMARY KEY,geom POINT,name TEXT);
            INSERT INTO gpkg_contents(table_name,data_type,identifier,min_x,min_y,max_x,max_y,srs_id) VALUES('buildings','features','Example buildings',392000,5819872,392300,5820200,{srs}),('survey "points"','features','Survey points',392005,5819990,392005,5819990,{srs});
            INSERT INTO gpkg_geometry_columns VALUES('buildings','geom','POLYGON',{srs},0,0),('survey "points"','geom','POINT',{srs},0,0);
            """";
        command.ExecuteNonQuery();
        command.CommandText =
            "INSERT INTO gpkg_spatial_ref_sys VALUES('ETRS89 / UTM zone 33N',@srs,'EPSG',25833,@wkt,NULL)";
        command.Parameters.AddWithValue("@srs", srs);
        command.Parameters.AddWithValue("@wkt", MediumCompatibilityChecks.Wkt);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        var factory = new GeometryFactory();
        NetTopologySuite.Geometries.LinearRing Ring(double x, double y, double size) =>
            factory.CreateLinearRing(
                new[]
                {
                    new Coordinate(x, y),
                    new Coordinate(x + size, y),
                    new Coordinate(x + size, y + size),
                    new Coordinate(x, y + size),
                    new Coordinate(x, y),
                }
            );
        var polygon = factory.CreatePolygon(
            Ring(392000, 5819970, 30),
            new[] { Ring(392008, 5819978, 5) }
        );
        void Insert(string table, int id, Geometry? geometry, string name)
        {
            command.CommandText = $"INSERT INTO {table}(fid,geom,name) VALUES(@id,@geometry,@name)";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue(
                "@geometry",
                geometry is null ? DBNull.Value : BinaryGeometry(geometry, srs)
            );
            command.Parameters.AddWithValue("@name", name);
            command.ExecuteNonQuery();
        }
        Insert("buildings", 1, polygon, "courtyard");
        Insert("buildings", 2, factory.CreatePolygon(Ring(392200, 5820100, 20)), "outside");
        Insert("buildings", 3, null, "null geometry");
        Insert(
            "\"survey \"\"points\"\"\"",
            1,
            factory.CreatePoint(new Coordinate(392005, 5819990)),
            "marker"
        );
        command.CommandText = "UPDATE buildings SET payload=x'010203' WHERE fid=1";
        command.Parameters.Clear();
        command.ExecuteNonQuery();
        if (index)
        {
            command.CommandText = """
                CREATE TABLE gpkg_extensions(table_name TEXT,column_name TEXT,extension_name TEXT NOT NULL,definition TEXT NOT NULL,scope TEXT NOT NULL,UNIQUE(table_name,column_name,extension_name));
                INSERT INTO gpkg_extensions VALUES('buildings','geom','gpkg_rtree_index','http://www.geopackage.org/spec/#extension_rtree','write-only');
                CREATE VIRTUAL TABLE rtree_buildings_geom USING rtree(id,minx,maxx,miny,maxy);
                INSERT INTO rtree_buildings_geom VALUES(1,392000,392030,5819970,5820000),(2,392200,392220,5820100,5820120);
                CREATE TRIGGER rtree_buildings_geom_insert AFTER INSERT ON buildings WHEN NEW.geom IS NOT NULL AND NOT ST_IsEmpty(NEW.geom) BEGIN INSERT OR REPLACE INTO rtree_buildings_geom VALUES(NEW.fid,ST_MinX(NEW.geom),ST_MaxX(NEW.geom),ST_MinY(NEW.geom),ST_MaxY(NEW.geom)); END;
                CREATE TRIGGER rtree_buildings_geom_update1 AFTER UPDATE OF geom ON buildings WHEN OLD.fid=NEW.fid AND NEW.geom IS NOT NULL AND NOT ST_IsEmpty(NEW.geom) BEGIN INSERT OR REPLACE INTO rtree_buildings_geom VALUES(NEW.fid,ST_MinX(NEW.geom),ST_MaxX(NEW.geom),ST_MinY(NEW.geom),ST_MaxY(NEW.geom)); END;
                CREATE TRIGGER rtree_buildings_geom_update2 AFTER UPDATE OF geom ON buildings WHEN OLD.fid=NEW.fid AND (NEW.geom IS NULL OR ST_IsEmpty(NEW.geom)) BEGIN DELETE FROM rtree_buildings_geom WHERE id=OLD.fid; END;
                CREATE TRIGGER rtree_buildings_geom_update3 AFTER UPDATE ON buildings WHEN OLD.fid<>NEW.fid AND NEW.geom IS NOT NULL AND NOT ST_IsEmpty(NEW.geom) BEGIN DELETE FROM rtree_buildings_geom WHERE id=OLD.fid; INSERT OR REPLACE INTO rtree_buildings_geom VALUES(NEW.fid,ST_MinX(NEW.geom),ST_MaxX(NEW.geom),ST_MinY(NEW.geom),ST_MaxY(NEW.geom)); END;
                CREATE TRIGGER rtree_buildings_geom_update4 AFTER UPDATE ON buildings WHEN OLD.fid<>NEW.fid AND (NEW.geom IS NULL OR ST_IsEmpty(NEW.geom)) BEGIN DELETE FROM rtree_buildings_geom WHERE id IN(OLD.fid,NEW.fid); END;
                CREATE TRIGGER rtree_buildings_geom_delete AFTER DELETE ON buildings WHEN OLD.geom IS NOT NULL BEGIN DELETE FROM rtree_buildings_geom WHERE id=OLD.fid; END;
                """;
            command.ExecuteNonQuery();
        }
        return path;
    }

    internal static byte[] BinaryGeometry(Geometry geometry, int srs)
    {
        var wkb = new WKBWriter().Write(geometry);
        var blob = new byte[8 + wkb.Length];
        blob[0] = 71;
        blob[1] = 80;
        blob[3] = (byte)(geometry.IsEmpty ? 17 : 1);
        BitConverter.GetBytes(srs).CopyTo(blob, 4);
        wkb.CopyTo(blob, 8);
        return blob;
    }

    internal static void Sql(string path, string sql)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
        );
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DROP TRIGGER IF EXISTS rtree_buildings_geom_insert; DROP TRIGGER IF EXISTS rtree_buildings_geom_update1; DROP TRIGGER IF EXISTS rtree_buildings_geom_update2; DROP TRIGGER IF EXISTS rtree_buildings_geom_update3; DROP TRIGGER IF EXISTS rtree_buildings_geom_update4;";
        command.ExecuteNonQuery();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static void WriteExamples(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "imagery-mask.cog.tif"), Cog());
        File.WriteAllBytes(Path.Combine(directory, "elevation-mask.cog.tif"), Cog(128, true));
        GeoPackage(Path.Combine(directory, "local-layers.gpkg"));
        Console.WriteLine(
            "Wrote synthetic EPSG:25833 COG and GeoPackage examples to "
                + Path.GetFullPath(directory)
        );
    }
}
