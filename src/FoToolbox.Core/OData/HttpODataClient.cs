using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;

namespace FoToolbox.Core.OData;

/// <summary>
/// Minimal HttpClient-based OData client that follows @odata.nextLink.
/// </summary>
public sealed class HttpODataClient : IODataClient
{
    private readonly HttpClient _httpClient;
    private static readonly MediaTypeWithQualityHeaderValue JsonAccept = new("application/json");

    public HttpODataClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var next = request.Url;
        while (!string.IsNullOrWhiteSpace(next))
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, next);
            msg.Headers.Accept.Clear();
            msg.Headers.Accept.Add(JsonAccept);

            using var response = await _httpClient.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
            foreach (var header in response.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            if (root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in value.EnumerateArray())
                {
                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in element.EnumerateObject())
                    {
                        dict[prop.Name] = JsonElementToObject(prop.Value);
                    }
                    rows.Add(dict);
                }
            }

            long? odataCount = null;
            if (root.TryGetProperty("@odata.count", out var countEl))
            {
                if (countEl.ValueKind == JsonValueKind.Number && countEl.TryGetInt64(out var c))
                {
                    odataCount = c;
                }
                else if (countEl.ValueKind == JsonValueKind.String && long.TryParse(countEl.GetString(), out var cs))
                {
                    odataCount = cs;
                }
            }

            string? odataContext = null;
            if (root.TryGetProperty("@odata.context", out var ctxEl) && ctxEl.ValueKind == JsonValueKind.String)
            {
                odataContext = ctxEl.GetString();
            }

            root.TryGetProperty("@odata.nextLink", out var nlElement);
            next = nlElement.ValueKind == JsonValueKind.String ? nlElement.GetString() : null;

            yield return new ODataPage(rows, next, odataCount, headers, odataContext);
        }
    }

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText()
    };
}
