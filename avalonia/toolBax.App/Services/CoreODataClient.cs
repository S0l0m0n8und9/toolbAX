using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IODataClient"/>: issues the request against the active environment's F&amp;O OData
/// endpoint with a bearer token from <see cref="IAuthService"/>. Failures (no active env, auth error,
/// HTTP error) are returned as a non-2xx <see cref="ODataResponse"/> rather than thrown, so the POST
/// Builder / Query Builder surface them in their status line.
/// </summary>
public sealed class CoreODataClient : IODataClient, IDisposable
{
    private readonly IAuthService _auth;
    private readonly Func<EnvProfile?> _activeEnv;
    private readonly HttpClient _http;
    // We own (and therefore must dispose) the HttpClient only when we allocated it; an injected one
    // belongs to the caller. Guards a future multi-instance refactor from exhausting sockets.
    private readonly bool _ownsHttp;

    public CoreODataClient(IAuthService auth, Func<EnvProfile?> activeEnv, HttpClient? http = null)
    {
        _auth = auth;
        _activeEnv = activeEnv;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
    }

    public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        => SendAsync(method, path, body, headers: null, ct);

    public async Task<ODataResponse> SendAsync(string method, string path, string? body,
        IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var env = _activeEnv();
        if (env is null)
        {
            return new ODataResponse(0, "No active environment", "Select an environment first.", (int)sw.ElapsedMilliseconds);
        }

        string token;
        try
        {
            token = await _auth.AcquireFoTokenAsync(env, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ODataResponse(401, "Unauthorized", ex.Message, (int)sw.ElapsedMilliseconds);
        }

        var uri = BuildUri(env.Url, path);

        // A server-driven paging link is used verbatim, but only if it stays on the environment's host —
        // otherwise the env-scoped bearer (and its claims) would be sent to a foreign origin.
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !SameHost(env.Url, uri))
        {
            return new ODataResponse(0, "Refused",
                "The paging link points to a different host than the environment.", (int)sw.ElapsedMilliseconds);
        }

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (headers is not null)
            {
                foreach (var header in headers)
                {
                    // TryAddWithoutValidation: callers may pass header values OData allows but
                    // HttpClient's strict parser would reject (e.g. a weak ETag for If-Match).
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            var verb = method.Trim().ToUpperInvariant();
            if (body is not null && verb is "POST" or "PATCH" or "PUT")
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            sw.Stop();
            return new ODataResponse((int)response.StatusCode, response.ReasonPhrase ?? string.Empty,
                responseBody, (int)sw.ElapsedMilliseconds, CollectHeaders(response));
        }
        catch (Exception ex)
        {
            return new ODataResponse(0, "Request failed", ex.Message, (int)sw.ElapsedMilliseconds);
        }
    }

    // Flattens the response + content headers into a name→value map (multi-value headers are joined),
    // so the POST Builder can surface useful ones like ETag, OData-EntityId, Location, Content-Type.
    private static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
        {
            headers[h.Key] = string.Join(", ", h.Value);
        }

        foreach (var h in response.Content.Headers)
        {
            headers[h.Key] = string.Join(", ", h.Value);
        }

        return headers;
    }

    // env.Url may be a bare host ("contoso.operations.dynamics.com") or a full URL; path is "/data/…".
    // An absolute path (a server-driven @odata.nextLink) is used verbatim for paging.
    private static Uri BuildUri(string envUrl, string path)
    {
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(path);
        }

        var baseUrl = envUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? envUrl : $"https://{envUrl}";
        return new Uri($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
    }

    // True when the request URI is on the same host as the environment (guards paging-link redirects).
    private static bool SameHost(string envUrl, Uri requestUri)
    {
        var normalized = envUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? envUrl : $"https://{envUrl}";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var envUri)
            && string.Equals(envUri.Host, requestUri.Host, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
