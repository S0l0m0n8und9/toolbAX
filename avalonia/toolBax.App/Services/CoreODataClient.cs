using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Net;
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
        var response = await SendCoreAsync(method, path, body, headers, ct).ConfigureAwait(false);

        // Every failure below is returned rather than thrown, so it used to live only in a tool's status
        // line. Mirror it to Trace for the session log (#168) — see RequestTrace for exactly how little a
        // trace line is allowed to say. A cancelled request throws out of SendCoreAsync and never gets
        // here: cancelling is not a failure.
        if (!response.IsSuccess)
        {
            RequestTrace.Failure("F&O", method, path, response);
        }

        return response;
    }

    private async Task<ODataResponse> SendCoreAsync(string method, string path, string? body,
        IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
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
        // Cancelling mid-sign-in is not an authentication failure: reporting "401 Unauthorized" told the
        // user their credentials had been rejected when in fact they pressed Cancel. Rethrow so the
        // caller's cancellation path runs — see the note on the send handler below (#168).
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ODataResponse(401, "Unauthorized", ex.Message, (int)sw.ElapsedMilliseconds);
        }

        var uri = BuildUri(env.Url, path);

        // A server-driven paging link is used verbatim, but only if it stays on the environment's origin
        // (scheme + host + port) — otherwise the env-scoped bearer (and its claims) would be sent to a
        // foreign origin, or downgraded to plaintext on the same host.
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !RequestOriginGuard.IsSameOrigin(env.Url, uri))
        {
            return new ODataResponse(0, "Refused",
                "The paging link points to a different origin than the environment.", (int)sw.ElapsedMilliseconds);
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
        // A cancelled request is not a failed request. Reporting it as one meant the view models' own
        // `catch (OperationCanceledException)` handlers — the Query Builder's clean "Export cancelled.",
        // for one — could never run in production, and the user was told the request had failed instead.
        // An HTTP/socket timeout also surfaces as an OperationCanceledException but with the caller's
        // token still live, so gate on the token: only that means the caller asked to stop. A timeout
        // falls through and keeps its non-success response.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
