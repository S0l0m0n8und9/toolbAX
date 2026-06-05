using System.Threading;
using System.Threading.Tasks;

namespace ToolBax.Core.Services;

/// <summary>Result of an OData write (POST/PATCH/DELETE).</summary>
public sealed record ODataResponse(int StatusCode, string ReasonPhrase, string Body, int ElapsedMs)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
    public string StatusLine => $"{StatusCode} {ReasonPhrase} · {ElapsedMs} ms";
}

/// <summary>OData write seam used by the POST Builder. The only place that issues HTTP.</summary>
public interface IODataClient
{
    Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default);
}
