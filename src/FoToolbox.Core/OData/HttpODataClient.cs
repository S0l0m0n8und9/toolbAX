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
    private readonly ReadRetryPolicy _readRetryPolicy;
    private static readonly MediaTypeWithQualityHeaderValue JsonAccept = new("application/json");

    public HttpODataClient(HttpClient httpClient, ReadRetryPolicy? readRetryPolicy = null)
    {
        _httpClient = httpClient;
        _readRetryPolicy = readRetryPolicy ?? new ReadRetryPolicy();
    }

    public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var next = request.Url;
        var visits = new PageVisitTracker();
        // The initial request defines the trusted origin: its absolute URL, or the HttpClient's
        // BaseAddress when the request URL is relative. A server-supplied @odata.nextLink must stay on
        // that origin, so the (possibly auth-bearing) HttpClient never follows a page off-origin. Every
        // nextLink is resolved against the client's actual BaseAddress before the check (see
        // IsSameOriginNextLink); if no request target can be resolved, the continuation is refused.
        var origin = PageVisitTracker.ResolveRequestUri(request.Url, _httpClient.BaseAddress);
        while (!string.IsNullOrWhiteSpace(next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visits.TryVisit(next, _httpClient.BaseAddress, out var resolvedRequestUri))
            {
                throw new InvalidOperationException(
                    "Paging stopped because the service returned a previously visited request target. Results are incomplete.");
            }

            var requestUri = resolvedRequestUri ?? new Uri(next, UriKind.RelativeOrAbsolute);
            HttpRequestMessage CreateRequest()
            {
                var message = new HttpRequestMessage(HttpMethod.Get, requestUri);
                message.Headers.Accept.Add(JsonAccept);
                return message;
            }

            HttpResponseMessage response;
            try
            {
                response = await _readRetryPolicy.SendBufferedAsync(
                    _httpClient,
                    CreateRequest,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (AuthRecoveryException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw BuildPluginFriendlyException(ex);
            }

            using (response)
            {
                ODataPage page;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = response.Content is null
                            ? null
                            : await response.Content.ReadAsStringAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
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
                    cancellationToken.ThrowIfCancellationRequested();
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var root = doc.RootElement;

                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("value", out var value) ||
                        value.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidOperationException(
                            "Paging stopped because the service returned an invalid collection response. Results are incomplete.");
                    }

                    var rows = new List<IReadOnlyDictionary<string, object?>>();
                    foreach (var element in value.EnumerateArray())
                    {
                        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in element.EnumerateObject())
                        {
                            dict[prop.Name] = JsonElementToObject(prop.Value);
                        }
                        rows.Add(dict);
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

                    if (!root.TryGetProperty("@odata.nextLink", out var nlElement) ||
                        nlElement.ValueKind == JsonValueKind.Null)
                    {
                        next = null;
                    }
                    else if (nlElement.ValueKind == JsonValueKind.String)
                    {
                        next = nlElement.GetString();
                        if (string.IsNullOrWhiteSpace(next))
                        {
                            throw new InvalidOperationException(
                                "Paging stopped because the service returned an invalid continuation. Results are incomplete.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "Paging stopped because the service returned an invalid continuation. Results are incomplete.");
                    }

                    var resolvedNext = next is null
                        ? null
                        : PageVisitTracker.ResolveRequestUri(next, _httpClient.BaseAddress);
                    if (next is not null && !IsSameOriginNextLink(origin, resolvedNext))
                    {
                        throw new InvalidOperationException(
                            "Refusing to follow an @odata.nextLink that points to a different origin than the request: " +
                            $"'{next}' does not resolve to '{origin?.GetLeftPart(UriPartial.Authority) ?? "(unknown origin)"}'.");
                    }

                    page = new ODataPage(rows, next, odataCount, headers, odataContext);
                }
                catch
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }

                cancellationToken.ThrowIfCancellationRequested();
                yield return page;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    /// <summary>
    /// True when a server-supplied <c>@odata.nextLink</c> is safe to follow on the (token-bearing) client.
    /// </summary>
    /// <remarks>
    /// The candidate has already been resolved against the actual <see cref="HttpClient.BaseAddress"/>
    /// used for dispatch. Comparing that exact URI prevents validating a relative continuation against
    /// an absolute initial request while the client would send it through a different configured base.
    /// </remarks>
    private static bool IsSameOriginNextLink(Uri? origin, Uri? resolved)
    {
        if (origin is null || resolved is null)
        {
            // No trusted origin to compare against, so nothing can be shown safe: fail closed. (Not
            // reachable in practice — a relative request URL with no BaseAddress fails on the first send.)
            return false;
        }

        return RequestOriginGuard.IsSameOrigin(origin, resolved);
    }

    private static Exception BuildPluginFriendlyException(Exception exception)
    {
        if (exception is AuthRecoveryException or TimeoutException)
            return exception;
        if (exception is OperationCanceledException)
            return new TimeoutException("The OData request timed out.", exception);

        return new InvalidOperationException(
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
