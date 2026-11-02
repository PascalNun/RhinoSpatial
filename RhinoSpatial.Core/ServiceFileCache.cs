using System.Security.Cryptography;
using System.Text.Json;
using BitMiracle.LibTiff.Classic;
using StbImageNext;

namespace RhinoSpatial.Core;

internal static class ServiceFileCache
{
    private sealed record Receipt(long Length, string Sha256, string ContentType);
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    internal static async Task<IDisposable> LockAsync(string path, CancellationToken token)
    {
        var gate = Gates[(uint)StringComparer.Ordinal.GetHashCode(Path.GetFullPath(path)) % (uint)Gates.Length];
        await gate.WaitAsync(token).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        internal Lease(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => _gate.Release();
    }

    internal static string Temporary(string final) => final + "." + Guid.NewGuid().ToString("N") + ".partial";

    internal static async Task<string?> ReadAsync(string path, int maximumBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var receiptPath = path + ".json";
        if (!File.Exists(path) || !File.Exists(receiptPath) || new FileInfo(receiptPath).Length > 4096) return null;
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(receiptPath, token).ConfigureAwait(false));
            if (receipt is null || receipt.Length <= 0 || receipt.Length > maximumBytes || new FileInfo(path).Length != receipt.Length) return null;
            await using var input = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false));
            return hash == receipt.Sha256 ? receipt.ContentType : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    internal static async Task PublishAsync(string temporary, string final, string contentType, CancellationToken token)
    {
        Receipt receipt;
        await using (var input = File.OpenRead(temporary))
            receipt = new Receipt(input.Length, Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)), contentType);
        var receiptTemporary = Temporary(final + ".json");
        try
        {
            await File.WriteAllTextAsync(receiptTemporary, JsonSerializer.Serialize(receipt), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, final, true);
            File.Move(receiptTemporary, final + ".json", true);
        }
        finally { File.Delete(receiptTemporary); }
    }

    internal static void ValidateImage(string path, int maximumPixels, CancellationToken token, bool decode = true)
    {
        token.ThrowIfCancellationRequested();
        using var input = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[4];
        input.ReadExactly(signature);
        input.Position = 0;
        if ((signature[0] == 'I' && signature[1] == 'I') || (signature[0] == 'M' && signature[1] == 'M'))
        {
            TiffWarningSuppression.EnsureInstalled();
            using var tiff = Tiff.Open(path, "r") ?? throw new InvalidDataException("The WMS image is not a readable TIFF.");
            var width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            var height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            CheckPixels(width, height, maximumPixels);
            if (!decode) return;
            var pixels = new int[width * height];
            if (!tiff.ReadRGBAImageOriented(width, height, pixels, Orientation.TOPLEFT, stopOnError: true))
                throw new InvalidDataException("The WMS TIFF image is incomplete.");
        }
        else
        {
            var info = ImageInfo.FromStream(input);
            CheckPixels(info.Width, info.Height, maximumPixels);
            input.Position = 0;
            if (decode) _ = ImageResult.FromStream(input, ColorComponents.RedGreenBlueAlpha);
            // STB tolerates a missing PNG terminator; a downloaded cache asset must be complete.
            if (signature[0] == 137 && signature[1] == 80)
            {
                if (input.Length < 12) throw new InvalidDataException("The WMS PNG is incomplete.");
                input.Position = input.Length - 12;
                Span<byte> tail = stackalloc byte[12];
                input.ReadExactly(tail);
                if (!tail.SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }))
                    throw new InvalidDataException("The WMS PNG is incomplete.");
            }
            else if (signature[0] == 255 && signature[1] == 216)
            {
                input.Position = input.Length - 2;
                if (input.ReadByte() != 255 || input.ReadByte() != 217)
                    throw new InvalidDataException("The WMS JPEG has no complete end marker.");
            }
            else if (signature[0] == 'G' && signature[1] == 'I' && signature[2] == 'F')
            {
                input.Position = input.Length - 1;
                if (input.ReadByte() != 59) throw new InvalidDataException("The WMS GIF is incomplete.");
            }
        }
        token.ThrowIfCancellationRequested();
    }

    private static void CheckPixels(int width, int height, int maximumPixels)
    {
        if (width <= 0 || height <= 0 || (long)width * height > maximumPixels)
            throw new ServiceLimitException("The WMS image exceeds the decoded pixel limit. Request a smaller image.");
    }
}
