using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IODataClient"/> for design-mode + tests: echoes a plausible response per verb
/// without issuing HTTP (POST→201 Created, PATCH/DELETE→204 No Content, empty body→400).
/// </summary>
public sealed class FakeODataClient : IODataClient
{
    public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
    {
        var verb = (method ?? string.Empty).Trim().ToUpperInvariant();

        if (verb is "POST" or "PATCH" && string.IsNullOrWhiteSpace(body))
        {
            return Task.FromResult(new ODataResponse(400, "Bad Request",
                "{\"error\":{\"message\":\"A request body is required.\"}}", 12));
        }

        return Task.FromResult(verb switch
        {
            "POST" => new ODataResponse(201, "Created", body ?? "{}", 142),
            "PATCH" => new ODataResponse(204, "No Content", string.Empty, 96),
            "DELETE" => new ODataResponse(204, "No Content", string.Empty, 88),
            _ => new ODataResponse(405, "Method Not Allowed", string.Empty, 8),
        });
    }
}
