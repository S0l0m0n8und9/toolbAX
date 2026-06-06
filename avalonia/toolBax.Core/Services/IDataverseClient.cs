using System.Threading;
using System.Threading.Tasks;

namespace ToolBax.Core.Services;

/// <summary>
/// Read seam for the Dataverse Web API (<c>{dataverse}/api/data/v9.2</c>), used by the Dual-Write Map
/// Browser to query <c>msdyn_dualwriteentitymap</c> records. Authenticates with the environment's
/// Dataverse service principal (client credentials) — separate from the F&amp;O <see cref="IODataClient"/>.
/// Failures (no active env, no Dataverse URL, auth error, HTTP error) come back as a non-2xx
/// <see cref="ODataResponse"/> rather than thrown, so the screen can surface them in a status banner.
/// </summary>
public interface IDataverseClient
{
    /// <summary>
    /// Issues an authenticated GET. <paramref name="pathOrUrl"/> is either a path relative to the
    /// Dataverse API base (e.g. <c>msdyn_dualwriteentitymaps?$select=…</c>) or an absolute URL such as
    /// a server-driven paging <c>@odata.nextLink</c>.
    /// </summary>
    Task<ODataResponse> GetAsync(string pathOrUrl, CancellationToken ct = default);
}
