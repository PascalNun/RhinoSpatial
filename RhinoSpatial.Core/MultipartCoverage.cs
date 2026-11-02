using System.Buffers;
using System.Net.Http.Headers;
using System.Text;

namespace RhinoSpatial.Core;

/// <summary>Find a single coverage in a bounded spool file without copying the whole response into RAM.</summary>
internal static class MultipartCoverage
{
    private sealed record Boundary(long Start, long After, bool Closing);

    internal static async Task<string> ExtractAsync(string spool, string output, MediaTypeHeaderValue? contentType, CancellationToken token)
    {
        var boundary = contentType?.Parameters.FirstOrDefault(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"');
        if (string.IsNullOrEmpty(boundary) || boundary.Length > 70 || boundary.Any(c => c < 32 || c > 126))
            throw new InvalidDataException("The WCS multipart boundary is missing or invalid.");
        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        await using var input = new FileStream(spool, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var current = await FindAsync(input, delimiter, 0, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The WCS multipart response has no boundary.");
        (long Start, long Length, string Type)? coverage = null;
        var parts = 0;
        while (!current.Closing)
        {
            token.ThrowIfCancellationRequested();
            if (++parts > 32) throw new ServiceLimitException("The WCS multipart response exceeds 32 parts.");
            input.Position = current.After;
            var headers = ReadHeaders(input, token);
            var start = input.Position;
            var next = await FindAsync(input, delimiter, start, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("The WCS multipart response is incomplete.");
            var end = next.Start;
            // Remove only MIME framing, never all trailing CR/LF bytes from a binary TIFF.
            input.Position = end - 1;
            if (end > start && input.ReadByte() == 10)
            {
                end--;
                if (end > start) { input.Position = end - 1; if (input.ReadByte() == 13) end--; }
            }
            var type = headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value ?? "application/octet-stream";
            var declared = type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                headers.Any(h => h.Key.Equals("Content-Description", StringComparison.OrdinalIgnoreCase) && h.Value.Contains("coverage", StringComparison.OrdinalIgnoreCase)) ||
                headers.Any(h => h.Key.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase) && h.Value.Contains(".tif", StringComparison.OrdinalIgnoreCase));
            if (declared)
            {
                if (coverage is not null || end <= start) throw new InvalidDataException("The WCS multipart coverage is empty or ambiguous.");
                coverage = (start, end - start, type);
            }
            current = next;
        }
        if (coverage is not { } selected) throw new InvalidDataException("The WCS multipart response has no readable coverage part.");
        input.Position = selected.Start;
        await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            var remaining = selected.Length;
            while (remaining > 0)
            {
                var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token).ConfigureAwait(false);
                if (count == 0) throw new InvalidDataException("The WCS multipart coverage is truncated.");
                await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                remaining -= count;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        return selected.Type;
    }

    private static Dictionary<string, string> ReadHeaders(Stream input, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var previous = -1;
        while (bytes.Length <= 65536)
        {
            if (bytes.Length % 256 == 0) token.ThrowIfCancellationRequested();
            var value = input.ReadByte();
            if (value < 0) throw new InvalidDataException("The WCS multipart headers are incomplete.");
            bytes.WriteByte((byte)value);
            if (value == 10)
            {
                var data = bytes.GetBuffer();
                var length = (int)bytes.Length;
                if (previous == 10 || (length >= 4 && data[length - 4] == 13 && data[length - 3] == 10 && previous == 13))
                {
                    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in Encoding.ASCII.GetString(data, 0, length).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var separator = line.IndexOf(':');
                        if (separator > 0) result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                    }
                    return result;
                }
            }
            previous = value;
        }
        throw new ServiceLimitException("The WCS multipart headers exceed 64 KiB.");
    }

    private static async Task<Boundary?> FindAsync(FileStream input, byte[] delimiter, long start, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            while (start < input.Length)
            {
                token.ThrowIfCancellationRequested();
                input.Position = start;
                var count = await input.ReadAtLeastAsync(buffer.AsMemory(),
                    (int)Math.Min(delimiter.Length + 8, input.Length - start), false, token).ConfigureAwait(false);
                if (count < delimiter.Length) return null;
                var index = 0;
                while (index <= count - delimiter.Length)
                {
                    var match = buffer.AsSpan(index, count - index).IndexOf(delimiter);
                    if (match < 0) break;
                    var offset = start + index + match;
                    input.Position = Math.Max(0, offset - 1);
                    var lineStart = offset == 0 || input.ReadByte() == 10;
                    input.Position = offset + delimiter.Length;
                    var first = input.ReadByte();
                    var second = input.ReadByte();
                    if (lineStart && first == '-' && second == '-')
                    {
                        var tail = input.ReadByte();
                        if (tail < 0 || tail == 10 || (tail == 13 && input.ReadByte() == 10))
                            return new Boundary(offset, input.Position, true);
                    }
                    if (lineStart && first == 13 && second == 10) return new Boundary(offset, input.Position, false);
                    if (lineStart && first == 10) return new Boundary(offset, offset + delimiter.Length + 1, false);
                    index += match + 1;
                }
                if (start + count >= input.Length) return null;
                start += count - delimiter.Length - 4;
            }
            return null;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
