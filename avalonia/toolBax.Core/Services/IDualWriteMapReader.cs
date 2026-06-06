using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Reads the dual-write map catalogue for the Map Browser (§4): queries <c>msdyn_dualwriteentitymap</c>
/// records from the Dataverse Web API (following server-driven paging) and reshapes them into
/// <see cref="DwMapRecord"/>s, plus the solution list used to filter them. Read-only — acting on a map
/// is the Operations screen's job. Failures come back in the result's <c>Error</c> rather than thrown,
/// so the screen can show a banner.
/// </summary>
public interface IDualWriteMapReader
{
    /// <summary>
    /// Loads the dual-write maps. When <paramref name="solutionUniqueName"/> is set, only the maps that
    /// belong to that solution (its dual-write-map solution components) are returned.
    /// </summary>
    Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default);

    /// <summary>Loads the Dataverse solutions (with publisher info) for the "filter by solution" picker.</summary>
    Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default);
}
