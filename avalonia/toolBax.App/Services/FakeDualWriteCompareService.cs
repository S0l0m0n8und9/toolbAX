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
    /// <summary>
    /// Shared verbatim across every row in the collision below (mirrors <c>AmbiguousRows</c>, which is
    /// handed one note string per collision and stamps it onto each row it emits — never a per-row
    /// variant). Wording mirrors <c>DualWriteMapComparer.DuplicateNote</c>'s.
    /// </summary>
    private const string PurchaseRequisitionLinesNote =
        "2 map(s) in source and 1 in target share this name and CE target, so they cannot be paired. " +
        "Each is listed on its own row and no version/state verdict was reached.";

    private static readonly DualWriteMapComparisonRow[] Seed =
    {
        Row("Customers V3", true, true, "1.0.0.12", "1.0.0.12", "Running", "Running", DualWriteComparisonVerdict.Identical),
        Row("Released products V2", true, true, "1.0.0.21", "1.0.0.19", "Paused", "Running", DualWriteComparisonVerdict.VersionMismatch),
        Row("Sales order headers", true, true, "1.0.0.15", "1.0.0.15", "Running", "Paused", DualWriteComparisonVerdict.StateMismatch),
        Row("Purchase order headers", true, false, "1.0.0.6", "", "Running", "", DualWriteComparisonVerdict.OnlyInLeft),
        Row("Exchange rates", false, true, "", "1.0.0.2", "", "Stopped", DualWriteComparisonVerdict.OnlyInRight),
        // Unpairable (#160): two source maps and one target map all share this name + CE target, so no
        // pairing among them is defensible — each is listed on its own row, on the side it actually came
        // from, with no version/state verdict reached (mirrors DualWriteMapComparer.AmbiguousRows, which
        // emits one row per map — left maps first, then right — under the collision's single note).
        Row("Purchase requisition lines", true, false, "1.0.0.4", "", "Running", "", DualWriteComparisonVerdict.Ambiguous,
            PurchaseRequisitionLinesNote),
        Row("Purchase requisition lines", true, false, "1.0.0.5", "", "Paused", "", DualWriteComparisonVerdict.Ambiguous,
            PurchaseRequisitionLinesNote),
        Row("Purchase requisition lines", false, true, "", "1.0.0.4", "", "Running", DualWriteComparisonVerdict.Ambiguous,
            PurchaseRequisitionLinesNote),
    };

    public Task<IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(EnvProfile source, EnvProfile target, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DualWriteMapComparisonRow>>(Seed);

    private static DualWriteMapComparisonRow Row(string name, bool inLeft, bool inRight,
        string leftVer, string rightVer, string leftState, string rightState, DualWriteComparisonVerdict verdict,
        string note = "") =>
        new(name, inLeft, inRight, leftVer, rightVer, leftState, rightState, verdict) { Note = note };
}
