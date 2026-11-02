using System.Net;
using System.Net.Http.Headers;

namespace RhinoSpatial.Core;

/// <summary>A bounded, representation-pinned seekable view over an HTTP byte-range resource.</summary>
public sealed class HttpRangeStream : Stream
{
    private const int BlockSize = 65536;
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly long _budget;
    private readonly int _requestBudget;
    private readonly CancellationTokenSource _timeout;
    private readonly CancellationToken _token;
    private readonly Dictionary<long, byte[]> _blocks = new();
    private readonly Queue<long> _order = new();
    private long _length,
        _position;
    private string? _etag;
    private DateTimeOffset? _modified;
    private bool _disposed;
    public long TransferredBytes { get; private set; }
    public int RequestCount { get; private set; }
    public string Validator => _etag ?? _modified?.ToString("R") ?? "none";
    public Exception? Failure { get; private set; }

    private HttpRangeStream(
        HttpClient http,
        string url,
        long budget,
        int requests,
        TimeSpan timeout,
        CancellationToken token
    )
    {
        if (
            !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0
        )
            throw new ArgumentException(
                "Remote rasters require an HTTP(S) URL without embedded credentials."
            );
        _http = http;
        _url = url;
        _budget = budget;
        _requestBudget = requests;
        _timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        _timeout.CancelAfter(timeout);
        _token = _timeout.Token;
    }

    public static async Task<HttpRangeStream> OpenAsync(
        HttpClient http,
        string url,
        CancellationToken token = default,
        long maximumBytes = 64L * 1024 * 1024,
        int maximumRequests = 512
    )
    {
        var result = new HttpRangeStream(
            http,
            url,
            maximumBytes,
            maximumRequests,
            TimeSpan.FromMinutes(2),
            token
        );
        try
        {
            await result.BlockAsync(0).ConfigureAwait(false);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private async Task<byte[]> BlockAsync(long start)
    {
        _token.ThrowIfCancellationRequested();
        if (_blocks.TryGetValue(start, out var cached))
            return cached;
        if (++RequestCount > _requestBudget)
            throw new InvalidDataException(
                "Remote raster exceeded its range-request budget; reduce the area."
            );
        if (start > 0 && _etag is null && _modified is null)
            throw new InvalidDataException(
                "The range server supplies no strong ETag or Last-Modified validator; a consistent raster snapshot cannot be verified."
            );
        var end = _length == 0 ? BlockSize - 1 : Math.Min(_length - 1, start + BlockSize - 1);
        using var request = new HttpRequestMessage(HttpMethod.Get, _url);
        request.Headers.Range = new RangeHeaderValue(start, end);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        if (_etag is not null)
            request.Headers.IfRange = new RangeConditionHeaderValue(
                new EntityTagHeaderValue(_etag)
            );
        else if (_modified.HasValue)
            request.Headers.IfRange = new RangeConditionHeaderValue(_modified.Value);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _token)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new InvalidDataException(
                $"Remote raster requires HTTP 206 byte ranges (received {(int)response.StatusCode}); no whole-file fallback was used."
            );
        if (
            response.Content.Headers.ContentEncoding.Any(e =>
                !e.Equals("identity", StringComparison.OrdinalIgnoreCase)
            )
        )
            throw new InvalidDataException(
                "Encoded HTTP ranges cannot be used as TIFF byte offsets."
            );
        var range = response.Content.Headers.ContentRange;
        if (
            range is null
            || range.Unit != "bytes"
            || !range.HasLength
            || !range.HasRange
            || range.From != start
            || range.To < start
            || range.To > end
            || range.Length <= 0
            || range.Length > 1024L * 1024 * 1024 * 1024
            || range.To != Math.Min(end, range.Length.GetValueOrDefault() - 1)
        )
            throw new InvalidDataException("The server returned an inconsistent Content-Range.");
        var strong = response.Headers.ETag is { IsWeak: false } tag ? tag.Tag : null;
        var modified = response.Content.Headers.LastModified;
        if (_length == 0)
        {
            _length = range.Length.GetValueOrDefault();
            _etag = strong;
            _modified = modified;
        }
        else if (
            range.Length != _length
            || (_etag is not null && strong != _etag)
            || (_etag is null && _modified.HasValue && modified != _modified)
        )
            throw new InvalidDataException(
                "The remote raster changed during reading; retry to obtain one consistent snapshot."
            );
        var expected = checked((int)(range.To!.Value - start + 1));
        if (TransferredBytes + expected > _budget)
            throw new InvalidDataException(
                "Remote raster exceeded its byte budget; reduce the area or use a coarser source."
            );
        if (
            response.Content.Headers.ContentLength.HasValue
            && response.Content.Headers.ContentLength != expected
        )
            throw new InvalidDataException("Truncated or oversized HTTP range.");
        await using var body = await response
            .Content.ReadAsStreamAsync(_token)
            .ConfigureAwait(false);
        var data = new byte[expected];
        var read = 0;
        while (read < expected)
        {
            var n = await body.ReadAsync(data.AsMemory(read, expected - read), _token)
                .ConfigureAwait(false);
            if (n == 0)
                throw new InvalidDataException("Truncated HTTP byte range.");
            read += n;
            TransferredBytes += n;
        }
        if (await body.ReadAsync(new byte[1], _token).ConfigureAwait(false) != 0)
            throw new InvalidDataException("HTTP range exceeded its declared size.");
        while (_blocks.Count >= 256)
            _blocks.Remove(_order.Dequeue());
        _blocks[start] = data;
        _order.Enqueue(start);
        return data;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HttpRangeStream));
        try
        {
            _token.ThrowIfCancellationRequested();
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));
            var total = (int)Math.Min(count, Math.Max(0, _length - _position));
            var copied = 0;
            while (copied < total)
            {
                var start = _position / BlockSize * BlockSize;
                var data = BlockAsync(start).GetAwaiter().GetResult();
                var index = (int)(_position - start);
                var n = Math.Min(total - copied, data.Length - index);
                if (n <= 0)
                    throw new EndOfStreamException("Invalid TIFF range offset.");
                Buffer.BlockCopy(data, index, buffer, offset + copied, n);
                copied += n;
                _position += n;
            }
            return copied;
        }
        catch (Exception ex)
        {
            Failure = ex;
            throw;
        }
    }

    public void ThrowIfFailed()
    {
        if (Failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Failure).Throw();
        _token.ThrowIfCancellationRequested();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(_length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (position < 0)
            throw new IOException("Negative TIFF seek.");
        return _position = position;
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush() { }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _timeout.Dispose();
            _blocks.Clear();
        }
        base.Dispose(disposing);
    }
}
