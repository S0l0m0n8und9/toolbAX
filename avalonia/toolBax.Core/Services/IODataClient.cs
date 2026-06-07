using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBax.Core.Services;

/// <summary>Result of an HTTP call to F&amp;O OData (POST/PATCH/DELETE/GET) or the Dataverse Web API (GET).</summary>
public sealed record ODataResponse(int StatusCode, string ReasonPhrase, string Body, int ElapsedMs)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
    public string StatusLine => $"{StatusCode} {ReasonPhrase} · {ElapsedMs} ms";
}

/// <summary>OData write seam used by the POST Builder. The only place that issues HTTP.</summary>
public interface IODataClient
{
    Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default);

    /// <summary>
    /// Overload that can attach extra request headers (e.g. <c>If-Match</c> for optimistic concurrency
    /// on PATCH/DELETE). A default interface method that ignores the headers, so existing
    /// implementations (the in-memory fakes) need no change; the real client overrides it.
    /// </summary>
    Task<ODataResponse> SendAsync(string method, string path, string? body,
        IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
        => SendAsync(method, path, body, ct);
}
