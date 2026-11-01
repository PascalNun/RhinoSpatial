using StbImageNext;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace RhinoSpatial.Core;

public static class RasterImageCodec
{
    public static byte[] DecodeRgba(byte[] bytes, int width, int height)
    {
        using var input = new MemoryStream(bytes);
        var info = ImageInfo.FromStream(input);
        if (info.Width != width || info.Height != height || (long)width * height > 4194304) throw new InvalidDataException("Tile dimensions disagree with its declared matrix.");
        return ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha).Data;
    }
    public static void WritePng(string path, int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length) throw new InvalidDataException("Invalid RGBA image dimensions.");
        using var output = File.Create(path);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height); header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (var row = 0; row < height; row++) { zlib.WriteByte(0); zlib.Write(rgba, row * width * 4, width * 4); }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", Array.Empty<byte>());
    }
    private static void Chunk(Stream stream, string name, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); stream.Write(length);
        var type = Encoding.ASCII.GetBytes(name); stream.Write(type); stream.Write(bytes);
        uint crc = 0xffffffff;
        foreach (var b in type.Concat(bytes)) { crc ^= b; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); stream.Write(length);
    }
}
