using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Reads the CE environment's virtual-table catalogue for the Virtual Tables screen (#23): queries
/// Dataverse <c>EntityDefinitions</c> metadata and reshapes the virtual (external) tables into
/// <see cref="VirtualTableInfo"/>s, classified by data source. Read-only — it surfaces what the platform
/// reports and never generates or mutates virtual tables. Failures come back in the result's
/// <c>Error</c> rather than thrown, so the screen can show a banner.
/// </summary>
public interface IVirtualTableReader
{
    Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default);
}
