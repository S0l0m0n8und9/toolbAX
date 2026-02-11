using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.OData;

/// <summary>
/// Non-streaming OData client used for write-like operations (POST/PATCH/DELETE).
/// Kept separate from <see cref="IODataClient"/> to avoid breaking the existing streaming read contract.
/// </summary>
public interface IODataWriteClient
{
    Task<ODataWriteResponse> SendAsync(ODataWriteRequest request, CancellationToken ct = default);
}

public sealed record ODataWriteRequest(
    HttpMethod Method,
    string Url,
    string? JsonBody = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Body = null,
    string? ContentType = null);

public sealed record ODataWriteResponse(
    int StatusCode,
    string? Body,
    IReadOnlyDictionary<string, string> Headers);
