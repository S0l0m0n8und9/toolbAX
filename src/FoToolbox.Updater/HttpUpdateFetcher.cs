using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Updater;

public sealed class HttpUpdateFetcher : IUpdateFetcher
{
    private readonly HttpClient _httpClient;

    public HttpUpdateFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Stream> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return stream;
    }
}
