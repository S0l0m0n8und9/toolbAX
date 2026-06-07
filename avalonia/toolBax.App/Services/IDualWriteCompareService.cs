using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// Compares the dual-write maps of two environments (Dual-Write Compare §5), returning one
/// <see cref="DualWriteMapComparisonRow"/> per map (matched by name, classified by presence / active
/// version / state). Reaches each environment's gateway, so it's async + can fail. Lives in the app
/// layer (alongside <see cref="IDualWriteConnector"/>) because it depends on FoToolbox.Core's gateway types.
/// </summary>
public interface IDualWriteCompareService
{
    Task<IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(EnvProfile source, EnvProfile target, CancellationToken ct = default);
}
