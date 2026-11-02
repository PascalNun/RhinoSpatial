using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BitMiracle.LibTiff.Classic;

namespace RhinoSpatial.Core;

public record CogImageResult(
    GeoReferencedRasterInfo Image,
    int RangeRequests,
    long TransferredBytes,
    int OverviewWidth
);

public record CogTerrainResult(
    TerrainRasterData Raster,
    int RangeRequests,
    long TransferredBytes,
    int OverviewWidth
);

/// <summary>Windowed classic-TIFF COG access using LibTiff codecs over bounded HTTP ranges.</summary>
public sealed class CogReader
{
    private static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _http;

    public CogReader(HttpClient? http = null)
    {
        _http = http ?? SharedHttp;
    }

    public static bool LooksLike(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && new[] { ".tif", ".tiff" }.Contains(
            Path.GetExtension(uri.AbsolutePath),
            StringComparer.OrdinalIgnoreCase
        );

    private sealed record ImageDirectory(long Offset, GeoReferencedRasterInfo Info, int Flags);

    private sealed record Window(int X, int Y, int Width, int Height);

    public async Task<GeoReferencedRasterInfo> ReadMetadataAsync(
        string source,
        CancellationToken token = default
    )
    {
        using var stream = await HttpRangeStream
            .OpenAsync(_http, source, token, 4 * 1024 * 1024, 64)
            .ConfigureAwait(false);
        using var tiff = Open(stream);
        var info = GeoTiffReader.ReadImageInfo(tiff, source, stream.Length);
        stream.ThrowIfFailed();
        return info;
    }

    public async Task<CogImageResult> ReadImageAsync(
        string source,
        SpatialContext2D context,
        CancellationToken token = default
    )
    {
        using var stream = await HttpRangeStream
            .OpenAsync(_http, source, token)
            .ConfigureAwait(false);
        using var tiff = Open(stream);
        var directories = Directories(tiff, source, stream);
        var selected = Select(directories, context);
        var image = selected.Image;
        var window = selected.Window;
        SetDirectory(tiff, image.Offset, stream);
        var tile = TileDimensions(tiff);
        var noData =
            NoData(tiff)
            ?? (
                image.Offset == directories[0].Offset
                    ? null
                    : ReadBaseNoData(tiff, directories[0], image, stream)
            );
        var samples = Value(tiff, TiffTag.SAMPLESPERPIXEL, 1);
        var bits = Value(tiff, TiffTag.BITSPERSAMPLE, 1);
        var format = (SampleFormat)Value(tiff, TiffTag.SAMPLEFORMAT, (int)SampleFormat.UINT);
        if (
            noData.HasValue
            && (
                bits is not (8 or 16)
                || Value(tiff, TiffTag.PLANARCONFIG, 1) != 1
                || Value(tiff, TiffTag.PHOTOMETRIC, 1) == (int)Photometric.YCBCR
            )
        )
            throw new InvalidDataException(
                "Imagery NoData requires contiguous 8/16-bit gray, palette or RGB samples."
            );
        var mask = directories.SingleOrDefault(d =>
            (d.Flags & 4) != 0
            && d.Info.Width == image.Info.Width
            && d.Info.Height == image.Info.Height
        );
        if (directories.Any(d => (d.Flags & 4) != 0) && mask is null)
            throw new InvalidDataException(
                "No matching-resolution internal mask is available for this overview."
            );
        var rgba = new byte[checked(window.Width * window.Height * 4)];
        var raster = new int[checked(tile.Width * tile.Height)];
        foreach (var (x, y) in Tiles(window, tile.Width, tile.Height))
        {
            token.ThrowIfCancellationRequested();
            if (!tiff.ReadRGBATile(x, y, raster))
            {
                stream.ThrowIfFailed();
                throw new InvalidDataException("The TIFF imagery tile could not be decoded.");
            }
            byte[]? raw = null;
            if (noData.HasValue)
                raw = ReadTile(tiff, x, y, stream);
            CopyPixels(
                window,
                tile.Width,
                tile.Height,
                x,
                y,
                (lx, ly, destination) =>
                {
                    var pixel = raster[(tile.Height - 1 - ly) * tile.Width + lx];
                    rgba[destination * 4] = (byte)Tiff.GetR(pixel);
                    rgba[destination * 4 + 1] = (byte)Tiff.GetG(pixel);
                    rgba[destination * 4 + 2] = (byte)Tiff.GetB(pixel);
                    rgba[destination * 4 + 3] = (byte)Tiff.GetA(pixel);
                    var alpha = rgba[destination * 4 + 3];
                    if (alpha is > 0 and < 255)
                        for (var c = 0; c < 3; c++)
                            rgba[destination * 4 + c] = (byte)
                                Math.Min(
                                    255,
                                    (rgba[destination * 4 + c] * 255 + alpha / 2) / alpha
                                );
                    if (raw is not null)
                    {
                        var missing = true;
                        for (var c = 0; c < Math.Min(samples, 3); c++)
                            missing &= IsNoData(
                                TerrainRasterReader.ReadSample(
                                    raw,
                                    (ly * tile.Width + lx) * samples + c,
                                    bits,
                                    format
                                ),
                                noData!.Value
                            );
                        if (missing)
                            rgba[destination * 4 + 3] = 0;
                    }
                }
            );
        }
        if (mask is not null)
        {
            SetDirectory(tiff, mask.Offset, stream);
            var mt = TileDimensions(tiff);
            var mb = Value(tiff, TiffTag.BITSPERSAMPLE, 1);
            if (
                mb is not (1 or 8)
                || Value(tiff, TiffTag.SAMPLESPERPIXEL, 1) != 1
                || Value(tiff, TiffTag.FILLORDER, 1) != 1
            )
                throw new InvalidDataException(
                    "Unsupported internal mask encoding (requires one 1/8-bit, MSB-first band)."
                );
            foreach (var (x, y) in Tiles(window, mt.Width, mt.Height))
            {
                token.ThrowIfCancellationRequested();
                var raw = ReadTile(tiff, x, y, stream);
                var stride = (mt.Width * mb + 7) / 8;
                CopyPixels(
                    window,
                    mt.Width,
                    mt.Height,
                    x,
                    y,
                    (lx, ly, destination) =>
                    {
                        var visible =
                            mb == 8
                                ? raw[ly * stride + lx] != 0
                                : (raw[ly * stride + lx / 8] & (128 >> (lx % 8))) != 0;
                        if (!visible)
                            rgba[destination * 4 + 3] = 0;
                    }
                );
            }
        }
        stream.ThrowIfFailed();
        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhinoSpatial",
            "CogWindows"
        );
        Directory.CreateDirectory(cache);
        // The cache contains the selected PNG window only, never a full remote TIFF or a plaintext URL.
        var signature = source + "\n" + stream.Validator + "\n" + image.Offset + "\n" + window;
        var filename =
            Convert
                .ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            signature + "\n" + Convert.ToHexString(SHA256.HashData(rgba))
                        )
                    )
                )
                .ToLowerInvariant() + ".png";
        var path = Path.Combine(cache, filename);
        if (!File.Exists(path))
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                RasterImageCodec.WritePng(temporary, window.Width, window.Height, rgba);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        return new(
            Crop(image.Info, window, path),
            stream.RequestCount,
            stream.TransferredBytes,
            image.Info.Width
        );
    }

    public async Task<CogTerrainResult> ReadTerrainAsync(
        string source,
        SpatialContext2D context,
        CancellationToken token = default
    )
    {
        using var stream = await HttpRangeStream
            .OpenAsync(_http, source, token)
            .ConfigureAwait(false);
        using var tiff = Open(stream);
        var directories = Directories(tiff, source, stream);
        var selected = Select(directories, context);
        var image = selected.Image;
        var window = selected.Window;
        SetDirectory(tiff, image.Offset, stream);
        var tile = TileDimensions(tiff);
        if (
            Value(tiff, TiffTag.SAMPLESPERPIXEL, 1) != 1
            || Value(tiff, TiffTag.PLANARCONFIG, 1) != 1
        )
            throw new InvalidDataException(
                "Remote terrain requires one contiguous elevation band."
            );
        var bits = Value(tiff, TiffTag.BITSPERSAMPLE, 1);
        var format = (SampleFormat)Value(tiff, TiffTag.SAMPLEFORMAT, (int)SampleFormat.UINT);
        if (
            bits is not (8 or 16 or 32 or 64)
            || format is not (SampleFormat.UINT or SampleFormat.INT or SampleFormat.IEEEFP)
            || bits == 64 && format != SampleFormat.IEEEFP
            || format == SampleFormat.IEEEFP && bits is not (32 or 64)
        )
            throw new InvalidDataException("Unsupported remote DEM sample type.");
        var noData =
            NoData(tiff)
            ?? (
                image.Offset == directories[0].Offset
                    ? null
                    : ReadBaseNoData(tiff, directories[0], image, stream)
            );
        var elevations = new float[checked(window.Width * window.Height)];
        foreach (var (x, y) in Tiles(window, tile.Width, tile.Height))
        {
            token.ThrowIfCancellationRequested();
            var raw = ReadTile(tiff, x, y, stream);
            CopyPixels(
                window,
                tile.Width,
                tile.Height,
                x,
                y,
                (lx, ly, destination) =>
                    elevations[destination] = TerrainRasterReader.ReadSample(
                        raw,
                        ly * tile.Width + lx,
                        bits,
                        format
                    )
            );
        }
        var mask = directories.SingleOrDefault(d =>
            (d.Flags & 4) != 0
            && d.Info.Width == image.Info.Width
            && d.Info.Height == image.Info.Height
        );
        if (directories.Any(d => (d.Flags & 4) != 0) && mask is null)
            throw new InvalidDataException(
                "No matching-resolution internal terrain mask is available."
            );
        if (mask is not null)
        {
            SetDirectory(tiff, mask.Offset, stream);
            var mt = TileDimensions(tiff);
            var mb = Value(tiff, TiffTag.BITSPERSAMPLE, 1);
            if (
                mb is not (1 or 8)
                || Value(tiff, TiffTag.SAMPLESPERPIXEL, 1) != 1
                || Value(tiff, TiffTag.FILLORDER, 1) != 1
            )
                throw new InvalidDataException("Unsupported internal terrain mask encoding.");
            foreach (var (x, y) in Tiles(window, mt.Width, mt.Height))
            {
                token.ThrowIfCancellationRequested();
                var raw = ReadTile(tiff, x, y, stream);
                var stride = (mt.Width * mb + 7) / 8;
                CopyPixels(
                    window,
                    mt.Width,
                    mt.Height,
                    x,
                    y,
                    (lx, ly, destination) =>
                    {
                        if (
                            mb == 8
                                ? raw[ly * stride + lx] == 0
                                : (raw[ly * stride + lx / 8] & (128 >> (lx % 8))) == 0
                        )
                            elevations[destination] = float.NaN;
                    }
                );
            }
        }
        stream.ThrowIfFailed();
        var info = Crop(image.Info, window, source);
        var g = info.GeoTransform!;
        return new(
            new(
                "remote-cog",
                info.SrsName,
                info.Width,
                info.Height,
                g.At(.5, .5),
                new(g.A, g.D),
                new(g.B, g.E),
                noData,
                elevations
            )
            {
                GeoTransform = g,
            },
            stream.RequestCount,
            stream.TransferredBytes,
            image.Info.Width
        );
    }

    private static Tiff Open(HttpRangeStream stream)
    {
        var header = new byte[4];
        if (stream.Read(header, 0, 4) != 4)
            throw new InvalidDataException("Truncated TIFF header.");
        stream.Position = 0;
        var magic = header[0] == 73 ? header[2] | header[3] << 8 : header[2] << 8 | header[3];
        if (magic == 43)
            throw new InvalidDataException(
                "BigTIFF is not supported by this decoder; use a classic TIFF COG."
            );
        var firstOffset =
            header[0] == 73
                ? BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(stream, 4, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(stream, 4, 4));
        PreflightDirectory(stream, firstOffset);
        stream.Position = 0;
        TiffWarningSuppression.EnsureInstalled();
        var tiff = Tiff.ClientOpen("remote COG", "r", stream, new TiffStream());
        stream.ThrowIfFailed();
        return tiff
            ?? throw new InvalidDataException("The ranged resource is not a supported TIFF.");
    }

    private static List<ImageDirectory> Directories(
        Tiff tiff,
        string source,
        HttpRangeStream stream
    )
    {
        var first = GeoTiffReader.ReadImageInfo(tiff, source, stream.Length);
        var output = new List<ImageDirectory>();
        var seen = new HashSet<long>();
        var pending = new Queue<long>();
        void AddCurrent(bool subIfd)
        {
            stream.ThrowIfFailed();
            var offset = tiff.CurrentDirOffset();
            if (!seen.Add(offset))
                throw new InvalidDataException("Cyclic or duplicate TIFF image directory.");
            if (seen.Count > 64)
                throw new InvalidDataException(
                    "Remote TIFF exceeds its 64-directory metadata budget."
                );
            var flags = Value(tiff, TiffTag.SUBFILETYPE, 0);
            var width = Value(tiff, TiffTag.IMAGEWIDTH, 0);
            var height = Value(tiff, TiffTag.IMAGELENGTH, 0);
            if (width <= 0 || height <= 0)
                throw new InvalidDataException("Invalid TIFF directory dimensions.");
            if (output.Count == 0 && (flags & 4) != 0)
                throw new InvalidDataException(
                    "The primary TIFF directory is a mask, not an image."
                );
            if (output.Count > 0 && (flags & 5) == 0 && !subIfd)
                throw new InvalidDataException("Multi-page TIFFs are not a COG overview pyramid.");
            var sub = tiff.GetField(TiffTag.SUBIFD);
            if (sub is not null)
            {
                var offsets =
                    sub[^1].TolongArray()
                    ?? throw new InvalidDataException("Invalid TIFF SubIFD offsets.");
                if (offsets.Length > 64 || pending.Count + offsets.Length > 64)
                    throw new InvalidDataException("Too many TIFF SubIFDs.");
                foreach (var child in offsets)
                    pending.Enqueue(child);
            }
            GeoReferencedRasterInfo info;
            if (
                tiff.GetField(TiffTag.GEOTIFF_MODELPIXELSCALETAG) is not null
                || tiff.GetField(TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG) is not null
            )
                info = GeoTiffReader.ReadImageInfo(tiff, source, stream.Length, first.SrsName);
            else
            {
                if (
                    first.Width % width != 0
                    || first.Height % height != 0
                    || first.Width / width != first.Height / height
                )
                    return; // Skip ambiguous unreferenced rounded overview grids; retain exact levels.
                var factor = (double)first.Width / width;
                var g = first.GeoTransform!;
                var scaled = new RasterGeoTransform(
                    g.A * factor,
                    g.B * factor,
                    g.C,
                    g.D * factor,
                    g.E * factor,
                    g.F
                );
                info = new(
                    source,
                    first.SrsName,
                    width,
                    height,
                    scaled.Bounds(width, height),
                    stream.Length
                )
                {
                    GeoTransform = scaled,
                };
            }
            if (info.SrsName != first.SrsName || width > first.Width || height > first.Height)
                throw new InvalidDataException(
                    "TIFF overview CRS/dimensions disagree with the primary image."
                );
            var expected = first.GeoTransform!;
            var actual = info.GeoTransform!;
            var a = expected.Bounds(first.Width, first.Height);
            var b = actual.Bounds(width, height);
            var tolerance = Math.Max(1, Math.Max(a.MaxX - a.MinX, a.MaxY - a.MinY)) * 1e-8;
            if (
                Math.Abs(a.MinX - b.MinX) > tolerance
                || Math.Abs(a.MinY - b.MinY) > tolerance
                || Math.Abs(a.MaxX - b.MaxX) > tolerance
                || Math.Abs(a.MaxY - b.MaxY) > tolerance
            )
                throw new InvalidDataException("TIFF overview and base footprints disagree.");
            _ = TileDimensions(tiff);
            output.Add(new(offset, info, flags));
        }
        AddCurrent(false);
        var next = PreflightDirectory(stream, tiff.CurrentDirOffset());
        while (next != 0)
        {
            SetDirectory(tiff, next, stream);
            AddCurrent(false);
            next = PreflightDirectory(stream, next);
        }
        stream.ThrowIfFailed();
        while (pending.Count > 0)
        {
            SetDirectory(tiff, pending.Dequeue(), stream);
            AddCurrent(true);
        }
        return output;
    }

    private static (ImageDirectory Image, Window Window) Select(
        List<ImageDirectory> directories,
        SpatialContext2D context
    )
    {
        var choices = directories
            .Where(d => (d.Flags & 4) == 0)
            .OrderByDescending(d => d.Info.Width)
            .Select(d => (Image: d, Window: WindowFor(d.Info, context)))
            .ToList();
        var result = choices.FirstOrDefault(c => c.Window.Width <= 1024 && c.Window.Height <= 1024);
        if (result.Image is null)
            result = choices[^1];
        if (result.Window.Width <= 0 || result.Window.Height <= 0)
            throw new InvalidDataException(
                "Remote raster does not overlap the shared Spatial Context."
            );
        if ((long)result.Window.Width * result.Window.Height > 4 * 1024 * 1024)
            throw new InvalidDataException(
                "Remote raster window exceeds four million pixels; narrow the area or use a source with overviews."
            );
        return result;
    }

    private static Window WindowFor(GeoReferencedRasterInfo info, SpatialContext2D context)
    {
        var bounds = Lod2AtomFeedReader.TransformBounds(
            new(context.ResolvedSrs, context.PlacementBoundingBox),
            info.SrsName
        );
        var g = info.GeoTransform!;
        var p = new[]
        {
            g.Pixel(bounds.MinX, bounds.MinY),
            g.Pixel(bounds.MaxX, bounds.MinY),
            g.Pixel(bounds.MaxX, bounds.MaxY),
            g.Pixel(bounds.MinX, bounds.MaxY),
        };
        var x = (int)Math.Clamp(Math.Floor(p.Min(c => c.X)), 0, info.Width);
        var y = (int)Math.Clamp(Math.Floor(p.Min(c => c.Y)), 0, info.Height);
        var right = (int)Math.Clamp(Math.Ceiling(p.Max(c => c.X)), 0, info.Width);
        var bottom = (int)Math.Clamp(Math.Ceiling(p.Max(c => c.Y)), 0, info.Height);
        return new(x, y, right - x, bottom - y);
    }

    private static GeoReferencedRasterInfo Crop(
        GeoReferencedRasterInfo info,
        Window window,
        string path
    )
    {
        var g = info.GeoTransform!;
        var origin = g.At(window.X, window.Y);
        var crop = new RasterGeoTransform(g.A, g.B, origin.X, g.D, g.E, origin.Y);
        return new(
            path,
            info.SrsName,
            window.Width,
            window.Height,
            crop.Bounds(window.Width, window.Height),
            info.FileSizeBytes
        )
        {
            GeoTransform = crop,
        };
    }

    private static (int Width, int Height) TileDimensions(Tiff tiff)
    {
        if (!tiff.IsTiled())
            throw new InvalidDataException(
                "Remote raster is not tiled; provide a COG rather than a striped TIFF."
            );
        if (Value(tiff, TiffTag.ORIENTATION, 1) != 1)
            throw new InvalidDataException("Remote TIFF requires top-left orientation.");
        var width = Value(tiff, TiffTag.TILEWIDTH, 0);
        var height = Value(tiff, TiffTag.TILELENGTH, 0);
        var bytes = tiff.TileSize();
        if (
            width <= 0
            || height <= 0
            || (long)width * height > 1024 * 1024
            || bytes <= 0
            || bytes > 32 * 1024 * 1024
        )
            throw new InvalidDataException("Remote raster tile exceeds the decoded tile budget.");
        return (width, height);
    }

    private static IEnumerable<(int X, int Y)> Tiles(Window w, int tw, int th)
    {
        var count = 0;
        for (var y = w.Y / th * th; y < w.Y + w.Height; y += th)
        for (var x = w.X / tw * tw; x < w.X + w.Width; x += tw)
        {
            if (++count > 256)
                throw new InvalidDataException(
                    "Remote window exceeds 256 tiles; narrow the area or use overviews."
                );
            yield return (x, y);
        }
    }

    private static void CopyPixels(
        Window w,
        int tw,
        int th,
        int x,
        int y,
        Action<int, int, int> copy
    )
    {
        for (var row = Math.Max(y, w.Y); row < Math.Min(y + th, w.Y + w.Height); row++)
        for (var col = Math.Max(x, w.X); col < Math.Min(x + tw, w.X + w.Width); col++)
            copy(col - x, row - y, (row - w.Y) * w.Width + col - w.X);
    }

    private static byte[] ReadTile(Tiff tiff, int x, int y, HttpRangeStream stream)
    {
        var buffer = new byte[tiff.TileSize()];
        var read = tiff.ReadEncodedTile(tiff.ComputeTile(x, y, 0, 0), buffer, 0, -1);
        stream.ThrowIfFailed();
        if (read != buffer.Length)
            throw new InvalidDataException("Truncated decoded TIFF tile.");
        return buffer;
    }

    // Bound TIFF field allocations before handing a directory to the decoder.
    private static long PreflightDirectory(HttpRangeStream stream, long offset)
    {
        var little = ReadBytes(stream, 0, 2)[0] == 73;
        if (offset < 8 || offset > stream.Length - 2)
            throw new InvalidDataException("Invalid TIFF directory offset.");
        var countBytes = ReadBytes(stream, offset, 2);
        var count = little
            ? BinaryPrimitives.ReadUInt16LittleEndian(countBytes)
            : BinaryPrimitives.ReadUInt16BigEndian(countBytes);
        if (count > 1024)
            throw new InvalidDataException("Remote TIFF directory exceeds 1024 tags.");
        var entries = ReadBytes(stream, offset + 2, checked(count * 12 + 4));
        long total = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = entries.AsSpan(i * 12, 12);
            var type = little
                ? BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(2, 2))
                : BinaryPrimitives.ReadUInt16BigEndian(entry.Slice(2, 2));
            var n = little
                ? BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(4, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(4, 4));
            var size = type switch
            {
                1 or 2 or 6 or 7 => 1,
                3 or 8 => 2,
                4 or 9 or 11 or 13 => 4,
                5 or 10 or 12 => 8,
                _ => throw new InvalidDataException("Unsupported TIFF field type."),
            };
            var bytes = (long)n * size;
            total += bytes;
            if (bytes > 8 * 1024 * 1024 || total > 16 * 1024 * 1024)
                throw new InvalidDataException(
                    "Remote TIFF directory exceeds its field allocation budget."
                );
            if (bytes > 4)
            {
                var location = little
                    ? BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4))
                    : BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(8, 4));
                if (location > stream.Length - bytes)
                    throw new InvalidDataException("TIFF field points outside the remote file.");
            }
        }
        var tail = entries.AsSpan(count * 12, 4);
        return little
            ? BinaryPrimitives.ReadUInt32LittleEndian(tail)
            : BinaryPrimitives.ReadUInt32BigEndian(tail);
    }

    private static byte[] ReadBytes(HttpRangeStream stream, long offset, int length)
    {
        if (offset < 0 || offset > stream.Length - length)
            throw new InvalidDataException("Truncated TIFF directory.");
        var saved = stream.Position;
        try
        {
            stream.Position = offset;
            var bytes = new byte[length];
            if (stream.Read(bytes, 0, length) != length)
                throw new InvalidDataException("Truncated TIFF directory.");
            return bytes;
        }
        finally
        {
            stream.Position = saved;
        }
    }

    private static int Value(Tiff tiff, TiffTag tag, int fallback) =>
        tiff.GetField(tag)?[0].ToInt() ?? fallback;

    private static void SetDirectory(Tiff tiff, long offset, HttpRangeStream stream)
    {
        PreflightDirectory(stream, offset);
        if (!tiff.SetSubDirectory(offset))
        {
            stream.ThrowIfFailed();
            throw new InvalidDataException("Cannot read TIFF image directory.");
        }
        stream.ThrowIfFailed();
    }

    private static double? NoData(Tiff tiff) => TerrainRasterReader.ReadNoDataValue(tiff);

    private static double? ReadBaseNoData(
        Tiff tiff,
        ImageDirectory first,
        ImageDirectory selected,
        HttpRangeStream stream
    )
    {
        SetDirectory(tiff, first.Offset, stream);
        var value = NoData(tiff);
        SetDirectory(tiff, selected.Offset, stream);
        return value;
    }

    private static bool IsNoData(float value, double noData) =>
        double.IsNaN(noData) ? float.IsNaN(value) : value == (float)noData;
}
