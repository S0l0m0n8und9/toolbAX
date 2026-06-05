using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IDualWriteGateway"/> seeded from the design prototype (data.js). Used for
/// design-mode and tests until the live gateway client is wired in. Mirrors the prototype simulation:
/// <see cref="SubmitActionAsync"/> records the call and returns a request id; successive
/// <see cref="GetStatusAsync"/> calls move one map at a time from the verb state to the result state,
/// then report <see cref="RequestPhase.Succeeded"/>.
/// </summary>
public sealed class FakeDualWriteGateway : IDualWriteGateway
{
    private readonly List<DwMap> _maps;
    private readonly Dictionary<string, Pending> _pending = new();
    private int _requestSeq;

    /// <summary>Number of <see cref="SubmitActionAsync"/> calls — lets tests assert no silent mutation.</summary>
    public int SubmitCount { get; private set; }

    public FakeDualWriteGateway(IEnumerable<DwMap>? maps = null) =>
        _maps = (maps ?? SeedMaps()).ToList();

    public Task<GatewayInfo> ResolveEnvironmentAsync(string identifier, CancellationToken ct) =>
        Task.FromResult(SeedGateway());

    public Task<IReadOnlyList<DwMap>> GetMapsAsync(string cid, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DwMap>>(_maps);

    public Task<string> SubmitActionAsync(string cid, DwAction action, IReadOnlyList<string> tableIds, CancellationToken ct)
    {
        SubmitCount++;
        var requestId = $"req-{++_requestSeq:000}";
        _pending[requestId] = new Pending(tableIds.ToList(), DwActions.VerbState(action), DwActions.ResultState(action));
        return Task.FromResult(requestId);
    }

    public Task<GatewayStatus> GetStatusAsync(string requestId, CancellationToken ct)
    {
        var pending = _pending[requestId];
        if (pending.Revealed < pending.Ids.Count)
        {
            pending.Revealed++;
        }

        var states = new Dictionary<string, MapState>();
        for (var i = 0; i < pending.Ids.Count; i++)
        {
            states[pending.Ids[i]] = i < pending.Revealed ? pending.Result : pending.Verb;
        }

        var phase = pending.Revealed >= pending.Ids.Count ? RequestPhase.Succeeded : RequestPhase.InProgress;
        return Task.FromResult(new GatewayStatus(requestId, phase, states));
    }

    private sealed class Pending
    {
        public Pending(List<string> ids, MapState verb, MapState result)
        {
            Ids = ids;
            Verb = verb;
            Result = result;
        }

        public List<string> Ids { get; }
        public MapState Verb { get; }
        public MapState Result { get; }
        public int Revealed { get; set; }
    }

    public static GatewayInfo SeedGateway() => new(
        "contoso.operations.dynamics.com",
        "australiaeast",
        "projectmanagementservice.au-il102.gateway.prod.island.powerapps.com",
        "0e7b1f44-3c2a-4d9e-9f01-2b6a8c5d7e10",
        "Contoso (AUMF · APAC Prod)",
        "2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b",
        new AuthSnapshot("interactive", "ops.svc@contoso.com", System.TimeSpan.FromMinutes(47)));

    public static IReadOnlyList<DwMap> SeedMaps() => new[]
    {
        new DwMap("cust-account", "Customers V3", "CustomersV3", "account", DwDirection.Both, MapState.Running, "1.0.0.12", "Microsoft", 14218, 3),
        new DwMap("vend-account", "Vendors V2", "VendorsV2", "msdyn_vendor", DwDirection.FoToDv, MapState.Running, "1.0.0.8", "Microsoft", 4820, 0),
        new DwMap("prod-product", "Released products V2", "ReleasedProductsV2", "product", DwDirection.Both, MapState.Paused, "1.0.0.21", "contoso.it", 0, 0),
        new DwMap("so-header", "Sales order headers", "SalesOrderHeadersV2", "salesorder", DwDirection.DvToFo, MapState.Running, "1.0.0.15", "contoso.it", 612, 41),
        new DwMap("so-line", "Sales order lines", "SalesOrderLinesV2", "salesorderdetail", DwDirection.DvToFo, MapState.Running, "1.0.0.15", "contoso.it", 2211, 118),
        new DwMap("po-header", "Purchase order headers", "PurchaseOrderHeadersV2", "msdyn_purchaseorder", DwDirection.Both, MapState.Running, "1.0.0.6", "Microsoft", 188, 0),
        new DwMap("coa", "Chart of accounts", "ChartOfAccounts", "msdyn_coa", DwDirection.FoToDv, MapState.Stopped, "1.0.0.3", "Microsoft", 0, 0),
        new DwMap("exch-rate", "Exchange rates", "ExchangeRates", "transactioncurrency", DwDirection.FoToDv, MapState.Idle, "1.0.0.2", "Microsoft", 0, 0),
    };
}
