using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IDualWriteCompareService"/> seeded from the prototype (DW_OPS_MAPS vs
/// AVC_TARGET). Design-mode returns the same illustrative diff set for any source≠target pair; it
/// covers every <see cref="DiffKind"/> bucket including an absent target.
/// TODO: compute the real diff from both environments' live map configs.
/// </summary>
public sealed class FakeDualWriteCompareService : IDualWriteCompareService
{
    // (fo, dv, direction, source side, target side-or-null)
    private static readonly (string Fo, string Dv, DwDirection Dir, DiffSide Source, DiffSide? Target)[] Seed =
    {
        ("CustomersV3", "account", DwDirection.Both, new(MapState.Running, "1.0.0.12", 14218), new(MapState.Running, "1.0.0.12", 13980)), // row delta
        ("VendorsV2", "msdyn_vendor", DwDirection.FoToDv, new(MapState.Running, "1.0.0.8", 4820), new(MapState.Running, "1.0.0.8", 4760)), // in sync
        ("ReleasedProductsV2", "product", DwDirection.Both, new(MapState.Paused, "1.0.0.21", 0), new(MapState.Running, "1.0.0.19", 240)), // version drift
        ("SalesOrderHeadersV2", "salesorder", DwDirection.DvToFo, new(MapState.Running, "1.0.0.15", 612), new(MapState.Paused, "1.0.0.15", 590)), // state differs
        ("PurchaseOrderHeadersV2", "msdyn_purchaseorder", DwDirection.Both, new(MapState.Running, "1.0.0.6", 188), null), // only in source
        ("ChartOfAccounts", "cdm_account", DwDirection.FoToDv, new(MapState.Running, "1.0.0.3", 14), new(MapState.Running, "1.0.0.3", 12)), // in sync
    };

    public Task<IReadOnlyList<CompareRow>> CompareAsync(string sourceEnvId, string targetEnvId, CancellationToken ct = default)
    {
        IReadOnlyList<CompareRow> rows = Seed
            .Select(s => new CompareRow(s.Fo, s.Dv, s.Dir, s.Source, s.Target, DiffClassifier.Classify(s.Source, s.Target)))
            .ToArray();
        return Task.FromResult(rows);
    }
}
