using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.OData;

public sealed class HttpODataWriteClient : IODataWriteClient
{
    private readonly HttpClient _httpClient;

    public HttpODataWriteClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ODataWriteResponse> SendAsync(ODataWriteRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Url)) throw new ArgumentException("Url is required.", nameof(request));

        using var msg = new HttpRequestMessage(request.Method, request.Url);

        // Per-request headers to avoid polluting the shared HttpClient used by CatalogService.
        msg.Headers.Accept.ParseAdd("application/json");

        if (request.Headers is not null)
        {
            foreach (var kvp in request.Headers)
            {
                // Prefer request-specific headers. This is intentionally lenient:
                // allow custom headers (e.g. If-Match) without hardcoding.
                msg.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
            }
        }

        if (request.Body is not null)
        {
            var ctValue = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType;
            msg.Content = new StringContent(request.Body, Encoding.UTF8, ctValue);
        }
        else if (request.JsonBody is not null)
        {
            msg.Content = new StringContent(request.JsonBody, Encoding.UTF8, "application/json");
        }

        using var resp = await _httpClient.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var body = resp.Content is null ? null : await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in resp.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }
        if (resp.Content is not null)
        {
            foreach (var header in resp.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        return new ODataWriteResponse((int)resp.StatusCode, body, headers);
    }
}
