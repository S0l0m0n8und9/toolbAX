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
        // that origin, so the (possibly auth-bearing) HttpClient never follows a page off-origin. Every
        // nextLink is resolved against that origin before the check (see IsSameOriginNextLink); if no
        // origin can be determined, any nextLink is refused (fail closed).
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

                if (!string.IsNullOrWhiteSpace(next) && !IsSameOriginNextLink(origin, next!))
                {
                    throw new InvalidOperationException(
                        "Refusing to follow an @odata.nextLink that points to a different origin than the request: " +
                        $"'{next}' does not resolve to '{origin?.GetLeftPart(UriPartial.Authority) ?? "(unknown origin)"}'.");
                }

                yield return new ODataPage(rows, next, odataCount, headers, odataContext);
            }
        }
    }

    /// <summary>
    /// True when a server-supplied <c>@odata.nextLink</c> is safe to follow on the (token-bearing) client.
    /// </summary>
    /// <remarks>
    /// The link is resolved the same way <see cref="HttpClient"/> will resolve it (RFC 3986 §5.3) and the
    /// resolved origin is then compared with the trusted one, rather than only checking links that parse
    /// as absolute URIs. That matters for a <c>//host/path</c> network-path reference (RFC 3986 §4.2):
    /// it keeps the base scheme but <em>replaces the authority</em>, and whether
    /// <c>Uri.TryCreate(.., UriKind.Absolute, ..)</c> accepts it is platform-dependent — on Windows it
    /// becomes an implicit UNC <c>file://</c> URI, elsewhere it stays "relative" and an absolute-only
    /// check waves it through, after which <c>BaseAddress</c> resolution sends the bearer to
    /// <c>//attacker.example/steal</c>. Resolving first removes that platform dependency.
    /// </remarks>
    private static bool IsSameOriginNextLink(Uri? origin, string next)
    {
        if (origin is null)
        {
            // No trusted origin to compare against, so nothing can be shown safe: fail closed. (Not
            // reachable in practice — a relative request URL with no BaseAddress fails on the first send.)
            return false;
        }

        return Uri.TryCreate(origin, next, out var resolved) && RequestOriginGuard.IsSameOrigin(origin, resolved);
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
