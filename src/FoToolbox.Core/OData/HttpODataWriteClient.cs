using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
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
            // Default to JSON, not application/octet-stream: every endpoint this client talks to is an
            // OData one, so octet-stream is a guaranteed 415 rather than a useful fallback.
            var ctValue = string.IsNullOrWhiteSpace(request.ContentType) ? "application/json" : request.ContentType;

            // Parse the content type instead of passing it as StringContent's "mediaType" argument: that
            // argument goes through new MediaTypeHeaderValue(mediaType), which rejects any value carrying
            // parameters (FormatException). ODataBatchBuilder.BuildWriteBatch produces exactly such a
            // value — "multipart/mixed; boundary=batch_..." — so the parameterised form has to survive
            // for the two halves of the write path to compose.
            var contentType = MediaTypeHeaderValue.Parse(ctValue);

            // The body below is written as UTF-8, so say so — unless the caller already pinned a charset,
            // or this is a multipart body, whose parts carry their own encodings (and whose Content-Type
            // must stay byte-identical to the boundary the body was built with).
            if (contentType.CharSet is null &&
                contentType.MediaType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) != true)
            {
                contentType.CharSet = Encoding.UTF8.WebName;
            }

            msg.Content = new StringContent(request.Body, Encoding.UTF8);
            msg.Content.Headers.ContentType = contentType;
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
