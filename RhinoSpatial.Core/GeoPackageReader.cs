using System.Buffers.Binary;
using System.Globalization;
using Microsoft.Data.Sqlite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace RhinoSpatial.Core;

public record GeoPackageLayer(
    string Name,
    string Title,
    string GeometryColumn,
    int SrsId,
    string SourceSrs,
    BoundingBox2D? Bounds,
    string PrimaryKey
)
{
    public string GeometryType { get; init; } = string.Empty;
}

public record GeoPackageReadResult(
    List<WfsFeature> Features,
    int ScannedCount,
    bool UsedSpatialIndex,
    bool ReachedFeatureLimit
);

/// <summary>Read-only standard GeoPackage feature layers. No SQL supplied by the user is executed.</summary>
public static class GeoPackageReader
{
    private static readonly object ProviderLock = new();
    private const int MaxRows = 500000,
        MaxFeatures = 100000,
        MaxGeometryBytes = 16 * 1024 * 1024;

    private static void InitializeProvider()
    {
        lock (ProviderLock)
        {
            try
            {
                _ = SQLitePCL.raw.sqlite3_libversion_number();
            }
            catch (Exception ex) when (ex.Message.Contains("SetProvider", StringComparison.Ordinal))
            {
                if (OperatingSystem.IsWindows())
                    SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
                else
                    SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
            }
        }
    }

    private static SqliteConnection Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("GeoPackage was not found.", path);
        InitializeProvider();
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(path),
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString()
        );
        try
        {
            connection.Open();
            SQLitePCL.raw.sqlite3_limit(
                connection.Handle,
                SQLitePCL.raw.SQLITE_LIMIT_LENGTH,
                16 * 1024 * 1024
            );
            SQLitePCL.raw.sqlite3_limit(
                connection.Handle,
                SQLitePCL.raw.SQLITE_LIMIT_SQL_LENGTH,
                1024 * 1024
            );
            using var command = connection.CreateCommand();
            command.CommandText =
                "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF; PRAGMA application_id;";
            var id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (id != 0x47504B47 && id != 0x47503130)
                throw new InvalidDataException(
                    "This SQLite file does not declare a GeoPackage application ID."
                );
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static List<GeoPackageLayer> ReadLayers(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var connection = Open(path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var interrupt = deadline.Token.Register(() =>
            SQLitePCL.raw.sqlite3_interrupt(connection.Handle)
        );
        using var transaction = connection.BeginTransaction(deferred: true);
        return Layers(connection, transaction, deadline.Token);
    }

    private static List<GeoPackageLayer> Layers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken token
    )
    {
        foreach (
            var table in new[] { "gpkg_contents", "gpkg_geometry_columns", "gpkg_spatial_ref_sys" }
        )
            RequireOrdinaryTable(connection, transaction, table);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT substr(c.table_name,1,257),substr(coalesce(c.identifier,c.table_name),1,1025),substr(g.column_name,1,257),g.srs_id,
                   substr(s.organization,1,32),s.organization_coordsys_id,c.min_x,c.min_y,c.max_x,c.max_y,c.srs_id,substr(g.geometry_type_name,1,32)
            FROM gpkg_contents c LEFT JOIN gpkg_geometry_columns g ON g.table_name=c.table_name
            LEFT JOIN gpkg_spatial_ref_sys s ON s.srs_id=g.srs_id WHERE c.data_type='features' LIMIT 129
            """;
        var layers = new List<GeoPackageLayer>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (layers.Count >= 128)
                    throw new InvalidDataException(
                        "GeoPackage exceeds the 128 feature-layer metadata limit."
                    );
                if (
                    reader.IsDBNull(2)
                    || reader.IsDBNull(3)
                    || reader.IsDBNull(10)
                    || reader.GetInt32(3) != reader.GetInt32(10)
                )
                    throw new InvalidDataException(
                        "GeoPackage feature-layer catalogs disagree or are incomplete."
                    );
                var name = reader.GetString(0);
                var geometry = reader.GetString(2);
                if (
                    name.Length > 256
                    || geometry.Length > 256
                    || name.Length == 0
                    || geometry.Length == 0
                )
                    throw new InvalidDataException(
                        "GeoPackage has an invalid or oversized table/geometry name."
                    );
                var srs =
                    !reader.IsDBNull(4)
                    && reader.GetString(4).Equals("EPSG", StringComparison.OrdinalIgnoreCase)
                    && !reader.IsDBNull(5)
                    && reader.GetInt32(5) > 0
                        ? $"EPSG:{reader.GetInt32(5)}"
                        : string.Empty;
                BoundingBox2D? bounds = null;
                if (
                    !reader.IsDBNull(6)
                    && !reader.IsDBNull(7)
                    && !reader.IsDBNull(8)
                    && !reader.IsDBNull(9)
                )
                {
                    bounds = new(
                        reader.GetDouble(6),
                        reader.GetDouble(7),
                        reader.GetDouble(8),
                        reader.GetDouble(9)
                    );
                    if (!ValidBounds(bounds))
                        throw new InvalidDataException("GeoPackage catalog extent is invalid.");
                }
                layers.Add(
                    new(
                        name,
                        reader.GetString(1),
                        geometry,
                        reader.GetInt32(3),
                        srs,
                        bounds,
                        string.Empty
                    )
                    {
                        GeometryType = reader.IsDBNull(11)
                            ? string.Empty
                            : reader.GetString(11).ToUpperInvariant(),
                    }
                );
            }
        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            RequireOrdinaryTable(connection, transaction, layer.Name);
            var columns = Columns(connection, transaction, layer.Name);
            if (!columns.Any(c => c.Name == layer.GeometryColumn))
                throw new InvalidDataException("GeoPackage geometry column is missing.");
            var keys = columns.Where(c => c.Primary).ToList();
            if (
                keys.Count != 1
                || !keys[0].Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)
            )
                throw new InvalidDataException(
                    "GeoPackage feature table requires one INTEGER primary key."
                );
            layers[i] = layer with { PrimaryKey = keys[0].Name };
        }
        return layers;
    }

    public static GeoPackageReadResult ReadFeatures(
        string path,
        string layerName,
        SpatialContext2D context,
        int maxFeatures = 0,
        CancellationToken token = default
    )
    {
        if (maxFeatures < 0 || maxFeatures > MaxFeatures)
            throw new ArgumentOutOfRangeException(
                nameof(maxFeatures),
                "GeoPackage supports at most 100000 output features per layer."
            );
        token.ThrowIfCancellationRequested();
        using var connection = Open(path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var interrupt = deadline.Token.Register(() =>
            SQLitePCL.raw.sqlite3_interrupt(connection.Handle)
        );
        using var transaction = connection.BeginTransaction(deferred: true);
        var layers = Layers(connection, transaction, deadline.Token);
        var layer =
            string.IsNullOrWhiteSpace(layerName) && layers.Count == 1
                ? layers[0]
                : layers.SingleOrDefault(l => l.Name.Equals(layerName, StringComparison.Ordinal));
        if (layer is null)
            throw new InvalidDataException(
                "Select an exact feature-layer name from List WFS Layers; multi-layer GeoPackages require an explicit selection."
            );
        if (string.IsNullOrEmpty(layer.SourceSrs))
            throw new InvalidDataException(
                "GeoPackage layer does not declare a supported EPSG CRS; no CRS was guessed."
            );
        if (
            !new[]
            {
                "GEOMETRY",
                "POINT",
                "LINESTRING",
                "POLYGON",
                "MULTIPOINT",
                "MULTILINESTRING",
                "MULTIPOLYGON",
                "GEOMETRYCOLLECTION",
            }.Contains(layer.GeometryType)
        )
            throw new InvalidDataException(
                "Unsupported GeoPackage feature-layer geometry type or extension."
            );
        var bounds = Lod2AtomFeedReader.TransformBounds(
            new(context.ResolvedSrs, context.RequestBoundingBox),
            layer.SourceSrs
        );
        var columns = Columns(connection, transaction, layer.Name)
            .Where(c => c.Name != layer.GeometryColumn)
            .ToList();
        var rtree = "rtree_" + layer.Name + "_" + layer.GeometryColumn;
        var indexed = HasIndex(connection, transaction, layer, rtree);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var geom = "f." + Quote(layer.GeometryColumn);
        command.CommandText =
            $"SELECT f.{Quote(layer.PrimaryKey)},length({geom}),CASE WHEN length({geom})<={MaxGeometryBytes} THEN {geom} END"
            + string.Concat(
                columns.Select(c =>
                    $",CASE WHEN typeof(f.{Quote(c.Name)})='blob' THEN '[BLOB '||length(f.{Quote(c.Name)})||' bytes]' ELSE substr(CAST(f.{Quote(c.Name)} AS TEXT),1,16384) END"
                )
            )
            + $" FROM {Quote(layer.Name)} f"
            + (
                indexed
                    ? $" JOIN {Quote(rtree)} r ON r.id=f.{Quote(layer.PrimaryKey)} WHERE r.minx<=@maxx AND r.maxx>=@minx AND r.miny<=@maxy AND r.maxy>=@miny"
                    : ""
            )
            + $" ORDER BY f.{Quote(layer.PrimaryKey)} LIMIT {MaxRows + 1}";
        if (indexed)
        {
            command.Parameters.AddWithValue("@minx", bounds.MinX);
            command.Parameters.AddWithValue("@maxx", bounds.MaxX);
            command.Parameters.AddWithValue("@miny", bounds.MinY);
            command.Parameters.AddWithValue("@maxy", bounds.MaxY);
        }
        var output = new List<WfsFeature>();
        var scanned = 0;
        long bytes = 0,
            points = 0,
            attributeCharacters = 0;
        var reached = false;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (++scanned > MaxRows)
                throw new InvalidDataException(
                    "GeoPackage exceeded the 500000-row query budget. Add a standard RTree index or narrow the layer."
                );
            if (reader.IsDBNull(1))
                continue;
            var length = reader.GetInt64(1);
            bytes += length;
            if (length > MaxGeometryBytes || bytes > 128L * 1024 * 1024)
                throw new InvalidDataException("GeoPackage exceeded its geometry byte budget.");
            if (reader.GetValue(2) is not byte[] blob)
                throw new InvalidDataException(
                    "GeoPackage geometry is not a binary geometry value."
                );
            var geometry = Decode(blob, layer.SrsId, ref points);
            if (
                layer.GeometryType != "GEOMETRY"
                && !layer.GeometryType.Equals(
                    geometry.GeometryType,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidDataException(
                    "GeoPackage geometry type disagrees with its declared feature-layer type."
                );
            if (geometry.IsEmpty)
                continue;
            var e = geometry.EnvelopeInternal;
            if (
                e.MinX > bounds.MaxX
                || e.MaxX < bounds.MinX
                || e.MinY > bounds.MaxY
                || e.MaxY < bounds.MinY
            )
                continue;
            var converted = new WfsGeometry { Type = geometry.GeometryType };
            Append(geometry, converted, layer.SourceSrs, context.ResolvedSrs);
            var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 0; i < columns.Count; i++)
                attributes[columns[i].Name] = reader.IsDBNull(3 + i)
                    ? null
                    : reader.GetString(3 + i);
            attributeCharacters += attributes.Values.Sum(v => (long)(v?.Length ?? 0));
            if (attributeCharacters > 8 * 1024 * 1024)
                throw new InvalidDataException(
                    "GeoPackage exceeded its attribute text budget; narrow the area or select fewer fields upstream."
                );
            var feature = new WfsFeature(
                reader.GetValue(0).ToString()!,
                layer.Name,
                converted,
                attributes
            );
            if (
                SpatialFeatureFilter
                    .FilterToBounds(new[] { feature }, context.RequestBoundingBox)
                    .Features.Count == 0
            )
                continue;
            if (output.Count >= MaxFeatures)
                throw new InvalidDataException(
                    "GeoPackage exceeded its 100000-feature output budget; narrow the area."
                );
            output.Add(feature);
            if (maxFeatures > 0 && output.Count >= maxFeatures)
            {
                reached = true;
                break;
            }
        }
        return new(output, scanned, indexed, reached);
    }

    private static NtsGeometry Decode(byte[] blob, int srs, ref long points)
    {
        if (
            blob.Length < 8
            || blob[0] != 71
            || blob[1] != 80
            || blob[2] != 0
            || (blob[3] & 0xE0) != 0
        )
            throw new InvalidDataException(
                "Unsupported or malformed GeoPackage binary geometry header."
            );
        var little = (blob[3] & 1) != 0;
        var code = (blob[3] >> 1) & 7;
        var empty = (blob[3] & 16) != 0;
        var envelopeSize = code switch
        {
            0 => 0,
            1 => 32,
            2 or 3 => 48,
            4 => 64,
            _ => throw new InvalidDataException("Invalid GeoPackage envelope code."),
        };
        var header = 8 + envelopeSize;
        if (blob.Length < header || ReadInt(blob.AsSpan(4, 4), little) != srs || empty && code != 0)
            throw new InvalidDataException(
                "GeoPackage geometry header CRS/envelope does not match its layer."
            );
        var wkb = blob.AsSpan(header);
        var offset = 0;
        ValidateWkb(wkb, ref offset, ref points, 0);
        if (offset != wkb.Length)
            throw new InvalidDataException("Trailing bytes in GeoPackage WKB geometry.");
        var geometry = new WKBReader { RepairRings = false }.Read(wkb.ToArray());
        if (geometry.IsEmpty != empty)
            throw new InvalidDataException(
                "GeoPackage empty-geometry flag disagrees with its WKB."
            );
        return geometry;
    }

    // Validate counts before NTS allocates coordinate sequences from untrusted WKB.
    private static int ValidateWkb(
        ReadOnlySpan<byte> data,
        ref int offset,
        ref long points,
        int depth
    )
    {
        if (depth > 32 || offset > data.Length - 5 || data[offset] > 1)
            throw new InvalidDataException("Invalid or excessively nested GeoPackage WKB.");
        var little = data[offset++] != 0;
        var type = ReadInt(data.Slice(offset, 4), little);
        offset += 4;
        var kind = type % 1000;
        var dimensions = 2 + (type / 1000 is 1 or 3 ? 1 : 0) + (type / 1000 is 2 or 3 ? 1 : 0);
        if (type < 1 || type / 1000 > 3 || kind is < 1 or > 7)
            throw new InvalidDataException(
                "Only standard GeoPackage point, line, polygon and collection WKB geometries are supported."
            );
        if (kind == 1)
            Coordinates(data, ref offset, 1, dimensions, little, ref points, true);
        else
        {
            var count = CountValue(data, ref offset, little);
            if (kind == 2)
            {
                if (count == 1)
                    throw new InvalidDataException(
                        "A WKB line requires zero or at least two positions."
                    );
                Coordinates(data, ref offset, count, dimensions, little, ref points, false);
            }
            else if (kind == 3)
            {
                if (count > 100000)
                    throw new InvalidDataException("Excessive polygon ring count.");
                for (var i = 0; i < count; i++)
                {
                    var n = CountValue(data, ref offset, little);
                    if (n < 4)
                        throw new InvalidDataException(
                            "Polygon ring has fewer than four positions."
                        );
                    var start = offset;
                    Coordinates(data, ref offset, n, dimensions, little, ref points, false);
                    if (
                        ReadDouble(data.Slice(start, 8), little)
                            != ReadDouble(data.Slice(offset - dimensions * 8, 8), little)
                        || ReadDouble(data.Slice(start + 8, 8), little)
                            != ReadDouble(data.Slice(offset - dimensions * 8 + 8, 8), little)
                    )
                        throw new InvalidDataException(
                            "Unclosed WKB polygon ring; no geometry was repaired."
                        );
                }
            }
            else
            {
                if (count > 100000)
                    throw new InvalidDataException("Excessive geometry collection count.");
                for (var i = 0; i < count; i++)
                {
                    var child = ValidateWkb(data, ref offset, ref points, depth + 1);
                    if (kind is >= 4 and <= 6 && child != kind - 3)
                        throw new InvalidDataException("Invalid multi-geometry child type.");
                }
            }
        }
        return kind;
    }

    private static int CountValue(ReadOnlySpan<byte> data, ref int offset, bool little)
    {
        if (offset > data.Length - 4)
            throw new InvalidDataException("Truncated WKB count.");
        var n = ReadInt(data.Slice(offset, 4), little);
        offset += 4;
        if (n < 0)
            throw new InvalidDataException("Negative WKB count.");
        return n;
    }

    private static void Coordinates(
        ReadOnlySpan<byte> data,
        ref int offset,
        int count,
        int dimensions,
        bool little,
        ref long points,
        bool emptyPoint
    )
    {
        if (count > (data.Length - offset) / (8 * dimensions) || (points += count) > 4000000)
            throw new InvalidDataException("Truncated WKB or excessive coordinate count.");
        for (var i = 0; i < count; i++)
        {
            var x = ReadDouble(data.Slice(offset, 8), little);
            var y = ReadDouble(data.Slice(offset + 8, 8), little);
            if (
                (!double.IsFinite(x) || !double.IsFinite(y))
                && !(emptyPoint && double.IsNaN(x) && double.IsNaN(y))
            )
                throw new InvalidDataException("WKB contains non-finite XY coordinates.");
            offset += 8 * dimensions;
        }
    }

    private static int ReadInt(ReadOnlySpan<byte> value, bool little) =>
        little
            ? BinaryPrimitives.ReadInt32LittleEndian(value)
            : BinaryPrimitives.ReadInt32BigEndian(value);

    private static double ReadDouble(ReadOnlySpan<byte> value, bool little) =>
        BitConverter.Int64BitsToDouble(
            little
                ? BinaryPrimitives.ReadInt64LittleEndian(value)
                : BinaryPrimitives.ReadInt64BigEndian(value)
        );

    private static void Append(
        NtsGeometry geometry,
        WfsGeometry output,
        string source,
        string target
    )
    {
        List<Coordinate2D> Convert(NetTopologySuite.Geometries.Coordinate[] coordinates) =>
            coordinates
                .Select(c =>
                    SpatialReferenceTransform.TryTransformXY(
                        source,
                        target,
                        c.X,
                        c.Y,
                        out var x,
                        out var y
                    )
                    && double.IsFinite(x)
                    && double.IsFinite(y)
                        ? new Coordinate2D(x, y)
                        : throw new InvalidDataException(
                            $"Cannot transform GeoPackage coordinates from '{source}' to '{target}'."
                        )
                )
                .ToList();
        switch (geometry)
        {
            case Polygon polygon:
                output.OuterRings.Add(new(Convert(polygon.ExteriorRing.Coordinates)));
                for (var i = 0; i < polygon.NumInteriorRings; i++)
                    output.OuterRings.Add(new(Convert(polygon.GetInteriorRingN(i).Coordinates)));
                break;
            case NetTopologySuite.Geometries.LineString line:
                output.LineStrings.Add(new(Convert(line.Coordinates)));
                break;
            case Point point:
                output.Points.AddRange(Convert(point.Coordinates));
                break;
            case GeometryCollection collection:
                for (var i = 0; i < collection.NumGeometries; i++)
                    Append(collection.GetGeometryN(i), output, source, target);
                break;
            default:
                throw new InvalidDataException("Unsupported GeoPackage geometry type.");
        }
    }

    private record Column(string Name, string Type, bool Primary);

    private static List<Column> Columns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({Quote(table)})";
        using var reader = command.ExecuteReader();
        var columns = new List<Column>();
        while (reader.Read())
        {
            if (columns.Count >= 256)
                throw new InvalidDataException("GeoPackage exceeds its 256-column limit.");
            columns.Add(new(reader.GetString(1), reader.GetString(2), reader.GetInt32(5) > 0));
        }
        return columns;
    }

    private static bool HasIndex(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GeoPackageLayer layer,
        string rtree
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@name AND lower(sql) LIKE 'create virtual table%using rtree%';";
        command.Parameters.AddWithValue("@name", rtree);
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
            return false;
        command.CommandText =
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='gpkg_extensions'";
        command.Parameters.Clear();
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
            return false;
        RequireOrdinaryTable(connection, transaction, "gpkg_extensions");
        command.CommandText = "SELECT sqlite_compileoption_used('ENABLE_RTREE')";
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            return false;
        command.CommandText =
            "SELECT count(*) FROM gpkg_extensions WHERE table_name=@table AND column_name=@column AND extension_name='gpkg_rtree_index'";
        command.Parameters.AddWithValue("@table", layer.Name);
        command.Parameters.AddWithValue("@column", layer.GeometryColumn);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static void RequireOrdinaryTable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@name AND lower(sql) NOT LIKE 'create virtual table%'";
        command.Parameters.AddWithValue("@name", table);
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
            throw new InvalidDataException(
                "GeoPackage catalog/feature table is missing or is not an ordinary SQLite table."
            );
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static bool ValidBounds(BoundingBox2D b) =>
        double.IsFinite(b.MinX)
        && double.IsFinite(b.MinY)
        && double.IsFinite(b.MaxX)
        && double.IsFinite(b.MaxY)
        && b.MinX <= b.MaxX
        && b.MinY <= b.MaxY;
}
