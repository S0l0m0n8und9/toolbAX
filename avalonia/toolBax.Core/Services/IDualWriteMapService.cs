using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Source for the Dual-Write Map Browser (§4): the map catalogue, each map's cached "template"
/// detail (KPIs, 24h activity, field bindings, value maps), and its live run history + errors
/// (loaded async, since those come from run-history / dead-letter endpoints). The only write is the
/// targeted dead-letter <see cref="RetryErrorAsync"/> — map lifecycle mutations stay on Operations.
/// </summary>
public interface IDualWriteMapService
{
    IReadOnlyList<DwMapSummary> GetMaps();

    DwMapDetail GetDetail(string mapId);

    Task<IReadOnlyList<DwRun>> GetRunsAsync(string mapId, CancellationToken ct = default);

    Task<IReadOnlyList<DwError>> GetErrorsAsync(string mapId, CancellationToken ct = default);

    /// <summary>Re-submits a dead-lettered record. Returns true when the retry was accepted.</summary>
    Task<bool> RetryErrorAsync(string mapId, DwError error, CancellationToken ct = default);
}
