using FoToolbox.Core.Auth;
using FoToolbox.Core.Net;
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
        // The initial request defines the trusted origin: its absolute URL, or the HttpClient's
        // BaseAddress when the request URL is relative. A server-supplied @odata.nextLink must stay on
        // that origin, so the (possibly auth-bearing) HttpClient never follows a page off-origin. If no
        // origin can be determined, any absolute nextLink is refused (fail closed).
        var origin = Uri.TryCreate(request.Url, UriKind.Absolute, out var seed) ? seed : _httpClient.BaseAddress;
        while (!string.IsNullOrWhiteSpace(next))
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, next);
            msg.Headers.Accept.Clear();
            msg.Headers.Accept.Add(JsonAccept);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (AuthRecoveryException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw BuildPluginFriendlyException(ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var body = response.Content is null
                        ? null
                        : await response.Content.ReadAsStringAsync(cancellationToken);
                    throw BuildPluginFriendlyException(response, body);
                }

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

                if (!string.IsNullOrWhiteSpace(next)
                    && Uri.TryCreate(next, UriKind.Absolute, out var nextUri)
                    && (origin is null || !RequestOriginGuard.IsSameOrigin(origin, nextUri)))
                {
                    throw new InvalidOperationException(
                        "Refusing to follow an @odata.nextLink that points to a different origin than the request.");
                }

                yield return new ODataPage(rows, next, odataCount, headers, odataContext);
            }
        }
    }

    private static Exception BuildPluginFriendlyException(Exception exception)
    {
        return exception is AuthRecoveryException
            ? exception
            : new InvalidOperationException(
                "Authentication needs to be refreshed before the plugin can continue. Re-authenticate in Profiles and retry the operation.",
                exception);
    }

    private static Exception BuildPluginFriendlyException(HttpResponseMessage response, string? body)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return new InvalidOperationException(
                "Authentication needs to be refreshed before the plugin can continue. Re-authenticate in Profiles and retry the operation.");
        }

        var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;
        return new HttpRequestException(
            $"OData request failed with {(int)response.StatusCode} {response.ReasonPhrase}. {detail}".Trim(),
            null,
            response.StatusCode);
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
