using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.OData;

/// <summary>
/// Minimal HttpClient-based OData client that follows @odata.nextLink.
/// </summary>
public sealed class HttpODataClient : IODataClient
{
    private readonly HttpClient _httpClient;

    public HttpODataClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var next = request.Url;
        while (!string.IsNullOrWhiteSpace(next))
        {
            var response = await _httpClient.GetAsync(next, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

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

            root.TryGetProperty("@odata.nextLink", out var nlElement);
            next = nlElement.ValueKind == JsonValueKind.String ? nlElement.GetString() : null;

            yield return new ODataPage(rows, next);
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
