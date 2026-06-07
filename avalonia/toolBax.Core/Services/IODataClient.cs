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
    /// on PATCH/DELETE). This is a default interface method that <b>silently discards the headers</b> by
    /// forwarding to the 3-arg overload — convenient so the in-memory fakes need no change.
    /// <para><b>Implementors:</b> any client that actually issues HTTP (or otherwise needs the headers,
    /// e.g. <c>CoreODataClient</c>) MUST override this method; relying on the default will drop
    /// <c>If-Match</c> and any other caller-supplied headers at runtime with no compile-time signal.</para>
    /// </summary>
    Task<ODataResponse> SendAsync(string method, string path, string? body,
        IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
        => SendAsync(method, path, body, ct);
}
