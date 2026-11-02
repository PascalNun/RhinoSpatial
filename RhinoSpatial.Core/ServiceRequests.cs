using System.Buffers;
using System.Text;

namespace RhinoSpatial.Core;

internal static class ServiceRequests
{
    internal const int MaximumErrorBytes = 64 * 1024;

    internal static async Task<T> RunAsync<T>(ServiceRequestPolicy policy, CancellationToken caller,
        Func<CancellationToken, Task<T>> operation)
    {
        caller.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller);
        deadline.CancelAfter(policy.OperationTimeout);
        try
        {
            var result = await operation(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception ex) when (caller.IsCancellationRequested)
        { throw new OperationCanceledException("Service loading was canceled.", ex, caller); }
        catch (Exception ex) when (deadline.IsCancellationRequested)
        { throw new TimeoutException("The service operation exceeded its time limit. Reduce the study area or retry later.", ex); }
    }

    internal static async Task<T> SendAsync<T>(HttpClient http, HttpRequestMessage request, ServiceRequestPolicy policy,
        CancellationToken token, Func<HttpResponseMessage, CancellationToken, Task<T>> read)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(policy.RequestTimeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            var value = await read(response, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return value;
        }
        catch (Exception ex) when (token.IsCancellationRequested)
        { throw new OperationCanceledException("Service loading was canceled.", ex, token); }
        catch (Exception ex) when (deadline.IsCancellationRequested)
        { throw new TimeoutException("The service request timed out while receiving its response.", ex); }
        catch (OperationCanceledException ex)
        { throw new TimeoutException("The HTTP client timed out while receiving the service response.", ex); }
    }

    internal static async Task CopyAsync(HttpContent content, Stream output, int maximumBytes, CancellationToken token)
    {
        if (content.Headers.ContentLength > maximumBytes)
            throw new ServiceLimitException("The service response exceeds its byte limit. Request a smaller area.");
        await using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        long total = 0;
        try
        {
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximumBytes - total + 1)), token).ConfigureAwait(false)) > 0)
            {
                total += count;
                if (total > maximumBytes) throw new ServiceLimitException("The service response exceeds its byte limit. Request a smaller area.");
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            if (content.Headers.ContentLength is { } expected && expected != total)
                throw new InvalidDataException("The service transfer was incomplete; no cached result was published.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal static async Task<string> TextAsync(HttpResponseMessage response, int maximumBytes, CancellationToken token)
    {
        using var output = new MemoryStream();
        await CopyAsync(response.Content, output, maximumBytes, token).ConfigureAwait(false);
        var data = output.GetBuffer();
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        if (string.IsNullOrWhiteSpace(charset))
        {
            var prefix = Encoding.ASCII.GetString(data, 0, (int)Math.Min(output.Length, 256));
            var match = System.Text.RegularExpressions.Regex.Match(prefix, "^\\s*<\\?xml[^>]*encoding=['\"]([^'\"]+)['\"]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success) charset = match.Groups[1].Value;
        }
        using var input = new MemoryStream(data, 0, (int)output.Length, false);
        using var reader = new StreamReader(input, string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset), true);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return text;
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        // Keep diagnostics bounded even if an error page is larger than the actual data request.
        string detail;
        try { detail = await TextAsync(response, MaximumErrorBytes, token).ConfigureAwait(false); }
        catch (ServiceLimitException) { detail = "Error response exceeded the diagnostic limit."; }
        if (detail.Length > 1024) detail = detail[..1024];
        throw new HttpRequestException($"The service returned {(int)response.StatusCode} {response.ReasonPhrase}. {detail}", null, response.StatusCode);
    }

    internal static async Task<(string Text, int Bytes)> MetadataAsync(HttpClient http, string url,
        ServiceRequestPolicy policy, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendAsync(http, request, policy, token, async (response, ct) =>
        {
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var text = await TextAsync(response, policy.MaximumMetadataBytes, ct).ConfigureAwait(false);
            return (text, checked(text.Length * sizeof(char)));
        }).ConfigureAwait(false);
    }
}

/// <summary>Completed values only. Waiter cancellation never becomes cached task state.</summary>
internal sealed class ServiceMetadataCache<T>
{
    private sealed record Entry(T Value, int Weight, DateTimeOffset Stored);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly object _sync = new();
    private long _weight;
    private const long MaximumWeight = 128L * 1024 * 1024;

    internal async Task<T> GetAsync(string key, CancellationToken token, Func<CancellationToken, Task<(T Value, int Weight)>> load)
    {
        token.ThrowIfCancellationRequested();
        var gate = _gates[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)_gates.Length];
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_entries.TryGetValue(key, out var entry) && DateTimeOffset.UtcNow - entry.Stored < TimeSpan.FromMinutes(5))
                    return entry.Value;
            }
            var loaded = await load(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_entries.Remove(key, out var old)) _weight -= old.Weight;
                while (_entries.Count > 0 && (_entries.Count >= 64 || _weight + loaded.Weight > MaximumWeight))
                {
                    var oldest = _entries.MinBy(p => p.Value.Stored);
                    _entries.Remove(oldest.Key);
                    _weight -= oldest.Value.Weight;
                }
                if (loaded.Weight <= MaximumWeight)
                {
                    _entries[key] = new Entry(loaded.Value, loaded.Weight, DateTimeOffset.UtcNow);
                    _weight += loaded.Weight;
                }
            }
            return loaded.Value;
        }
        finally { gate.Release(); }
    }
}
