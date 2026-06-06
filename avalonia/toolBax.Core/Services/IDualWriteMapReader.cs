using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Reads the dual-write map catalogue for the Map Browser (§4): queries <c>msdyn_dualwriteentitymap</c>
/// records from the Dataverse Web API (following server-driven paging) and reshapes them into
/// <see cref="DwMapRecord"/>s. Read-only — acting on a map is the Operations screen's job. Failures come
/// back in <see cref="DwMapLoadResult.Error"/> rather than thrown, so the screen can show a banner.
/// </summary>
public interface IDualWriteMapReader
{
    Task<DwMapLoadResult> GetMapsAsync(CancellationToken ct = default);
}
