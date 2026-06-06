using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Compares the dual-write maps of two environments (Dual-Write Compare §5), returning a diff row
/// per map. Async because it reaches both environments' configuration via the shared Data Integrator.
/// </summary>
public interface IDualWriteCompareService
{
    Task<IReadOnlyList<CompareRow>> CompareAsync(string sourceEnvId, string targetEnvId, CancellationToken ct = default);
}
