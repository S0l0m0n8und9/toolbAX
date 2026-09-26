using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using FoToolbox.Core.Net;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IODataClient"/>: issues the request against the active environment's F&amp;O OData
/// endpoint with a bearer token from <see cref="IAuthService"/>. Failures (no active env, auth error,
/// HTTP error) are returned as an <see cref="ODataResponse"/>. Mutation cancellation carries dispatch
/// and observed response evidence; a failed body read can retain a 2xx acknowledgment without success.
/// </summary>
public sealed class CoreODataClient : IODataClient, IDisposable
{
    private readonly IAuthService _auth;
    private readonly Func<EnvProfile?> _activeEnv;
    private readonly HttpClient _http;
    private readonly ReadRetryPolicy _readRetryPolicy;
    // We own (and therefore must dispose) the HttpClient only when we allocated it; an injected one
    // belongs to the caller. Guards a future multi-instance refactor from exhausting sockets.
    private readonly bool _ownsHttp;

    public CoreODataClient(
        IAuthService auth,
        Func<EnvProfile?> activeEnv,
        HttpClient? http = null,
        ReadRetryPolicy? readRetryPolicy = null)
    {
        _auth = auth;
        _activeEnv = activeEnv;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
        _readRetryPolicy = readRetryPolicy ?? new ReadRetryPolicy();
    }

    public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        => SendAsync(method, path, body, headers: null, ct);

    public async Task<ODataResponse> SendAsync(string method, string path, string? body,
        IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
    {
        var read = string.Equals(method.Trim(), "GET", StringComparison.OrdinalIgnoreCase);
        if (read) ct.ThrowIfCancellationRequested();
        var response = await SendCoreAsync(method, path, body, headers, ct).ConfigureAwait(false);
        if (read) ct.ThrowIfCancellationRequested();

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
        var mutation = method.Trim().ToUpperInvariant() is "POST" or "PATCH" or "PUT" or "DELETE";
        var read = string.Equals(method.Trim(), "GET", StringComparison.OrdinalIgnoreCase);
        if (read) ct.ThrowIfCancellationRequested();
        ODataResponse LocalFailure(int status, string reason, string detail) =>
            new(status, reason, detail, (int)sw.ElapsedMilliseconds) { DispatchStarted = mutation ? false : null };
        if (mutation && ct.IsCancellationRequested)
            throw new ODataWriteCanceledException(false, null, new OperationCanceledException(ct), ct);

        var env = _activeEnv();
        if (env is null)
        {
            return LocalFailure(0, "No active environment", "Select an environment first.");
        }

        var identity = EnvironmentIdentity.Create(env);
        var normalizedBaseUrl = ResourceUrlNormalizer.NormalizeFoBaseUrl(env.Url);
        Uri uri;
        try { uri = BuildUri(normalizedBaseUrl, path); }
        catch (Exception ex) when (mutation && ex is ArgumentException or UriFormatException)
        { return LocalFailure(0, "Invalid request", ex.GetType().Name); }

        // A server-driven paging link is used verbatim, but only if it stays on the captured environment's
        // origin. Decide this from the same immutable profile snapshot used for auth and dispatch.
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !RequestOriginGuard.IsSameOrigin(normalizedBaseUrl, uri))
        {
            return LocalFailure(0, "Refused", "The paging link points to a different origin than the environment.");
        }

        string token;
        try
        {
            token = await _auth.AcquireFoTokenAsync(env, ct).ConfigureAwait(false);
            if (read) ct.ThrowIfCancellationRequested();
        }
        // Cancelling mid-sign-in is not an authentication failure: reporting "401 Unauthorized" told the
        // user their credentials had been rejected when in fact they pressed Cancel. Rethrow so the
        // caller's cancellation path runs — see the note on the send handler below (#168).
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            if (mutation) throw new ODataWriteCanceledException(false, null, ex, ct);
            throw;
        }
        catch (Exception ex)
        {
            if (read) ct.ThrowIfCancellationRequested();
            return LocalFailure(401, "Unauthorized", ex.Message);
        }

        if (!identity.IsCurrent(_activeEnv()))
        {
            return LocalFailure(0, "Environment changed", "The active environment changed before the request was sent.");
        }

        var dispatchStarted = false;
        ODataResponse? observed = null;
        var headerSnapshot = headers is null
            ? Array.Empty<KeyValuePair<string, string>>()
            : new List<KeyValuePair<string, string>>(headers).ToArray();
        HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(read ? HttpMethod.Get : new HttpMethod(method), uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            foreach (var header in headerSnapshot)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var verb = method.Trim().ToUpperInvariant();
            if (body is not null && verb is "POST" or "PATCH" or "PUT")
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return request;
        }

        try
        {
            using var request = read ? null : CreateRequest();
            // Headers-first mutation reads must retain HttpClient's original total deadline and buffer limit.
            using var deadline = mutation ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
            if (deadline is not null && _http.Timeout != Timeout.InfiniteTimeSpan) deadline.CancelAfter(_http.Timeout);
            var exchangeToken = deadline?.Token ?? ct;
            exchangeToken.ThrowIfCancellationRequested();
            dispatchStarted = true; // The injected handler pipeline is opaque; this never proves delivery.
            using var response = read
                ? await _readRetryPolicy.SendAsync(
                    _http,
                    CreateRequest,
                    HttpCompletionOption.ResponseContentRead,
                    ct,
                    () =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!identity.IsCurrent(_activeEnv()))
                            throw new InvalidOperationException("The active environment changed before the request was sent.");
                    }).ConfigureAwait(false)
                : await _http.SendAsync(
                    request!,
                    mutation ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                    exchangeToken).ConfigureAwait(false);
            if (read) ct.ThrowIfCancellationRequested();
            if (mutation)
            {
                observed = new ODataResponse((int)response.StatusCode, response.ReasonPhrase ?? string.Empty,
                    string.Empty, (int)sw.ElapsedMilliseconds, CollectHeaders(response))
                    { DispatchStarted = true, BodyComplete = false };
                await response.Content.LoadIntoBufferAsync(_http.MaxResponseContentBufferSize, exchangeToken).ConfigureAwait(false);
            }
            var responseBody = await response.Content.ReadAsStringAsync(exchangeToken).ConfigureAwait(false);
            if (read) ct.ThrowIfCancellationRequested();
            sw.Stop();
            return new ODataResponse((int)response.StatusCode, response.ReasonPhrase ?? string.Empty,
                responseBody, (int)sw.ElapsedMilliseconds, observed?.Headers ?? CollectHeaders(response))
                { DispatchStarted = mutation ? true : null };
        }
        // A cancelled request is not a failed request. Reporting it as one meant the view models' own
        // `catch (OperationCanceledException)` handlers — the Query Builder's clean "Export cancelled.",
        // for one — could never run in production, and the user was told the request had failed instead.
        // Mutations retain cancellation/timeout evidence in an OCE-compatible exception. Reads keep
        // the existing distinction: caller cancellation propagates; an HTTP timeout becomes a failure result.
        catch (OperationCanceledException ex) when (mutation)
        {
            throw new ODataWriteCanceledException(dispatchStarted,
                observed is null ? null : observed with { BodyReadError = ex.GetType().Name, ElapsedMs = (int)sw.ElapsedMilliseconds },
                ex, ct.IsCancellationRequested ? ct : ex.CancellationToken);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (read) ct.ThrowIfCancellationRequested();
            if (read && !identity.IsCurrent(_activeEnv()))
                return LocalFailure(0, "Environment changed", "The active environment changed before the request was sent.");
            if (observed is not null)
                return observed with { BodyReadError = ex.GetType().Name, ElapsedMs = (int)sw.ElapsedMilliseconds };
            return new ODataResponse(0, "Request failed", mutation ? ex.GetType().Name : ex.Message, (int)sw.ElapsedMilliseconds)
                { DispatchStarted = mutation ? dispatchStarted : null, BodyComplete = !mutation };
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
    private static Uri BuildUri(string normalizedBaseUrl, string path)
    {
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(path);
        }

        return new Uri($"{normalizedBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
