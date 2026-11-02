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
        var text = await ServiceRequests.TextAsync(response, maximumBytes, timeout.Token).ConfigureAwait(false);
        return (text, response.RequestMessage?.RequestUri ?? new Uri(url), response.Content.Headers.ContentType?.MediaType ?? "");
    }
}
