using System.Net.Http;
using System.Text;

namespace RhinoSpatial.Core;

internal static class SourceHttp
{
    public static async Task<(string Text, Uri Url, string MediaType)> ReadAsync(HttpClient http, string url,
        CancellationToken cancellationToken = default, int maximumBytes = 16 * 1024 * 1024)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("Source response exceeds the supported size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > maximumBytes) throw new InvalidDataException("Source response exceeds the supported size limit.");
            output.Write(buffer, 0, count);
        }
        var data = output.ToArray();
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        if (string.IsNullOrWhiteSpace(charset))
        {
            var declaration = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 256));
            var match = System.Text.RegularExpressions.Regex.Match(declaration, "^\\s*<\\?xml[^>]*encoding=['\"]([^'\"]+)['\"]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success) charset = match.Groups[1].Value;
        }
        var encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
        using var decoded = new StreamReader(new MemoryStream(data), encoding, true);
        return (await decoded.ReadToEndAsync().ConfigureAwait(false), response.RequestMessage?.RequestUri ?? new Uri(url),
            response.Content.Headers.ContentType?.MediaType ?? "");
    }
}
