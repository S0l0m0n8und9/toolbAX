using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IDualWriteCompareService"/> for design-mode/tests. Returns an illustrative diff
/// that exercises every <see cref="DualWriteComparisonVerdict"/> (identical / version mismatch / state
/// mismatch / only-in-source / only-in-target / cannot-compare) for any source≠target pair.
/// </summary>
public sealed class FakeDualWriteCompareService : IDualWriteCompareService
{
    private static readonly DualWriteMapComparisonRow[] Seed =
    {
        Row("Customers V3", true, true, "1.0.0.12", "1.0.0.12", "Running", "Running", DualWriteComparisonVerdict.Identical),
        Row("Released products V2", true, true, "1.0.0.21", "1.0.0.19", "Paused", "Running", DualWriteComparisonVerdict.VersionMismatch),
        Row("Sales order headers", true, true, "1.0.0.15", "1.0.0.15", "Running", "Paused", DualWriteComparisonVerdict.StateMismatch),
        Row("Purchase order headers", true, false, "1.0.0.6", "", "Running", "", DualWriteComparisonVerdict.OnlyInLeft),
        Row("Exchange rates", false, true, "", "1.0.0.2", "", "Stopped", DualWriteComparisonVerdict.OnlyInRight),
        // Unpairable (#160): two source maps share this name + CE target, so neither can be lined up
        // against the target's one map with confidence — each is its own row, no version/state verdict
        // reached. Note text mirrors DualWriteMapComparer.DuplicateNote's wording.
        Row("Purchase requisition lines", true, false, "1.0.0.4", "", "Running", "", DualWriteComparisonVerdict.Ambiguous,
            "2 map(s) in source and 1 in target share this name and CE target, so they cannot be paired. " +
            "Each is listed on its own row and no version/state verdict was reached."),
    };

    public Task<IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(EnvProfile source, EnvProfile target, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DualWriteMapComparisonRow>>(Seed);

    private static DualWriteMapComparisonRow Row(string name, bool inLeft, bool inRight,
        string leftVer, string rightVer, string leftState, string rightState, DualWriteComparisonVerdict verdict,
        string note = "") =>
        new(name, inLeft, inRight, leftVer, rightVer, leftState, rightState, verdict) { Note = note };
}
