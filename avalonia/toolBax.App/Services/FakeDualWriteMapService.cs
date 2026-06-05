using System;
using System.Collections.Generic;
using System.Linq;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IDualWriteMapService"/> seeded from the design prototype (data.js DW_MAPS).
/// Only <c>cust-account</c> carries cached bindings/value maps — the rest exercise the "not cached"
/// empty state. Activity is generated deterministically per map (no RNG) so tests are stable.
/// </summary>
public sealed class FakeDualWriteMapService : IDualWriteMapService
{
    private static readonly IReadOnlyList<DwMapSummary> Maps = new[]
    {
        new DwMapSummary("cust-account", "CustomersV3", "account", "1.0.0.12", DwDirection.Both, MapState.Running, 14218, 3, "2m ago"),
        new DwMapSummary("vend-account", "VendorsV2", "msdyn_vendor", "1.0.0.8", DwDirection.FoToDv, MapState.Running, 4820, 0, "4m ago"),
        new DwMapSummary("prod-product", "ReleasedProductsV2", "product", "1.0.0.21", DwDirection.Both, MapState.Paused, 0, 0, "1h ago"),
        new DwMapSummary("so-salesorder", "SalesOrderHeadersV2", "salesorder", "1.0.0.15", DwDirection.DvToFo, MapState.Errored, 612, 41, "just now"),
        new DwMapSummary("soline-salesorderdetail", "SalesOrderLinesV2", "salesorderdetail", "1.0.0.15", DwDirection.DvToFo, MapState.Errored, 2211, 118, "just now"),
        new DwMapSummary("po-purchaseorder", "PurchaseOrderHeadersV2", "msdyn_purchaseorder", "1.0.0.6", DwDirection.Both, MapState.Running, 188, 0, "6m ago"),
    };

    private static readonly IReadOnlyList<DwBinding> CustBindings = new[]
    {
        new DwBinding(1, "CustomerAccount", "accountnumber", "none", true, true, false),
        new DwBinding(2, "OrganizationName", "name", "none", true, false, false),
        new DwBinding(3, "CurrencyCode", "transactioncurrencyid", "lookup → currency", true, false, false),
        new DwBinding(4, "CustomerGroupId", "cdm_customergroup", "value map · CUSTGROUP_MAP", false, false, false),
        new DwBinding(5, "PrimaryContactEmail", "emailaddress1", "none", false, false, false),
        new DwBinding(6, "CreditLimit", "creditlimit", "none", false, false, false),
        new DwBinding(7, "BlockedForInvoice", "cdm_blocked", "enum map · BLOCKED_ENUM", false, false, false),
        new DwBinding(8, "IsOneTime", "cdm_isonetime", "NoYes → bool", false, false, false),
        new DwBinding(9, "ModifiedDateTime", "modifiedon", "none", false, false, true),
    };

    private static readonly IReadOnlyList<DwValueMap> CustValueMaps = new[]
    {
        new DwValueMap("CUSTGROUP_MAP", new[]
        {
            new DwValueMapEntry("10", "Wholesale"),
            new DwValueMapEntry("20", "Retail"),
            new DwValueMapEntry("30", "Distribution"),
            new DwValueMapEntry("40", "Internal"),
        }, TotalSize: 14),
        new DwValueMap("BLOCKED_ENUM", new[]
        {
            new DwValueMapEntry("No", "false"),
            new DwValueMapEntry("Yes", "true"),
            new DwValueMapEntry("", "(null)"),
        }, TotalSize: 3),
    };

    public IReadOnlyList<DwMapSummary> GetMaps() => Maps;

    public DwMapDetail GetDetail(string mapId)
    {
        var summary = Maps.FirstOrDefault(m => m.Id == mapId) ?? Maps[0];
        var hasTemplate = summary.Id == "cust-account";

        return new DwMapDetail(
            summary,
            LatencyP95: "412 ms",
            Activity: Activity(summary.Id),
            Bindings: hasTemplate ? CustBindings : Array.Empty<DwBinding>(),
            ValueMaps: hasTemplate ? CustValueMaps : Array.Empty<DwValueMap>());
    }

    // Deterministic 24-point series seeded from the map id, mirroring the prototype's sine shape.
    private static IReadOnlyList<double> Activity(string mapId)
    {
        var seed = mapId[0] + mapId[^1];
        return Enumerable.Range(0, 24)
            .Select(i => 18 + Math.Abs(Math.Sin(i * 0.6 + seed)) * 80)
            .ToArray();
    }
}
