using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDualWriteMapReader"/>: pages through <c>msdyn_dualwriteentitymap</c> via the
/// <see cref="IDataverseClient"/> (following each <c>@odata.nextLink</c>) and reshapes the records with
/// <see cref="DualWriteMapParser"/>. A non-2xx response on any page stops the load and is returned as an
/// error (rather than thrown) so the Map Browser can surface it.
/// </summary>
public sealed class CoreDualWriteMapReader : IDualWriteMapReader
{
    private readonly IDataverseClient _dataverse;

    public CoreDualWriteMapReader(IDataverseClient dataverse) => _dataverse = dataverse;

    public async Task<DwMapLoadResult> GetMapsAsync(CancellationToken ct = default)
    {
        var all = new List<DwMapRecord>();
        string? pathOrUrl = DualWriteMapParser.MapsPath();

        while (pathOrUrl is not null)
        {
            var response = await _dataverse.GetAsync(pathOrUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return DwMapLoadResult.Fail(DescribeFailure(response));
            }

            var page = DualWriteMapParser.ParsePage(response.Body);
            all.AddRange(page.Records);
            pathOrUrl = page.NextLink; // absolute URL; the client uses it verbatim
        }

        return DwMapLoadResult.Ok(all);
    }

    private static string DescribeFailure(ODataResponse response)
    {
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? $"HTTP {response.StatusCode}"
            : response.ReasonPhrase;
        var body = response.Body.Trim();
        if (string.IsNullOrEmpty(body))
        {
            return $"Couldn't load dual-write maps — {reason}.";
        }

        // Keep the banner readable; the full Dataverse error JSON can be long.
        if (body.Length > 500)
        {
            body = body[..500] + "…";
        }

        return $"Couldn't load dual-write maps — {reason}: {body}";
    }
}
