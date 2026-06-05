using System.Collections.Generic;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Read-only source for the Dual-Write Map Browser (§4): the map catalogue and each map's cached
/// "template" detail (KPIs, 24h activity, field bindings, value maps). Live run history + errors load
/// via a separate async surface in the §4 follow-up.
/// </summary>
public interface IDualWriteMapService
{
    IReadOnlyList<DwMapSummary> GetMaps();

    DwMapDetail GetDetail(string mapId);
}
