using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Net;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Exercises <see cref="CoreDualWriteMapReader"/> over a fake <see cref="IDataverseClient"/>: the maps +
/// solutions query paths, server-driven paging (nextLink), accumulation, solution filtering, and error
/// surfacing. No network.
/// </summary>
public class CoreDualWriteMapReaderTests
{
    private sealed class FakeDataverseClient : IDataverseClient
    {
        private readonly Queue<ODataResponse> _responses;
        public List<string> Requested { get; } = new();

        public FakeDataverseClient(params ODataResponse[] responses) => _responses = new Queue<ODataResponse>(responses);

        public Task<ODataResponse> GetAsync(string pathOrUrl, CancellationToken ct = default)
        {
            Requested.Add(pathOrUrl);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static ODataResponse Ok(string body) => new(200, "OK", body, 1);

    private const string OneMap = """
    { "value": [ { "msdyn_dualwriteentitymapid": "a", "msdyn_name": "alpha", "msdyn_displayname": "Alpha" } ] }
    """;

    [Fact]
    public async Task GetMaps_queries_the_dualwrite_map_entity_set()
    {
        var dv = new FakeDataverseClient(Ok(OneMap));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Maps);
        Assert.Equal("Alpha", result.Maps[0].DisplayName);
        Assert.StartsWith("msdyn_dualwriteentitymaps?", dv.Requested[0]);
    }

    [Fact]
    public async Task GetMaps_follows_next_link_and_accumulates_pages()
    {
        const string page1 = """
        { "@odata.nextLink": "https://x/api/data/v9.2/page2", "value": [ { "msdyn_dualwriteentitymapid": "a", "msdyn_name": "alpha" } ] }
        """;
        const string page2 = """
        { "value": [ { "msdyn_dualwriteentitymapid": "b", "msdyn_name": "beta" } ] }
        """;
        var dv = new FakeDataverseClient(Ok(page1), Ok(page2));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Maps.Count);
        Assert.Equal("https://x/api/data/v9.2/page2", dv.Requested[1]); // the nextLink, used verbatim
    }

    [Fact]
    public async Task A_non_success_response_surfaces_as_an_error()
    {
        var dv = new FakeDataverseClient(new ODataResponse(401, "Unauthorized", "token denied", 1));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Maps);
        Assert.Contains("Unauthorized", result.Error);
    }

    [Fact]
    public async Task A_failure_on_a_later_page_surfaces_as_an_error()
    {
        const string page1 = """
        { "@odata.nextLink": "https://x/api/data/v9.2/page2", "value": [ { "msdyn_dualwriteentitymapid": "a", "msdyn_name": "alpha" } ] }
        """;
        var dv = new FakeDataverseClient(Ok(page1), new ODataResponse(500, "Server Error", "boom", 1));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("Server Error", result.Error);
    }

    [Fact]
    public async Task GetSolutions_queries_the_solutions_entity_set()
    {
        const string solutions = """
        { "value": [ { "solutionid": "55555555-5555-5555-5555-555555555555", "uniquename": "cust", "friendlyname": "Cust", "publisherid": { "uniquename": "p", "friendlyname": "Pub" } } ] }
        """;
        var dv = new FakeDataverseClient(Ok(solutions));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetSolutionsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Solutions);
        Assert.Equal("cust", result.Solutions[0].UniqueName);
        Assert.StartsWith("solutions?", dv.Requested[0]);
    }

    [Fact]
    public async Task GetMaps_for_a_solution_filters_to_its_components()
    {
        // First request = solution components (object ids); second = all maps; result keeps only matches.
        const string components = """
        { "value": [ { "objectid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" } ] }
        """;
        const string maps = """
        { "value": [
            { "msdyn_dualwriteentitymapid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "msdyn_name": "in-solution" },
            { "msdyn_dualwriteentitymapid": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "msdyn_name": "not-in-solution" } ] }
        """;
        var dv = new FakeDataverseClient(Ok(components), Ok(maps));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync("my_solution", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Maps);
        Assert.Equal("in-solution", result.Maps[0].Name);
        Assert.StartsWith("solutioncomponents?", dv.Requested[0]); // components fetched first
    }

    [Fact]
    public async Task GetCeRowCount_returns_the_odata_count()
    {
        var dv = new FakeDataverseClient(Ok("{\"@odata.count\":3120,\"value\":[]}"));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetCeRowCountAsync("accounts", "accounttype eq 'vendor'", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(3120, result.Count);
        Assert.StartsWith("accounts?$top=1&$count=true", dv.Requested[0]);
    }

    [Fact]
    public async Task GetCeRowCount_surfaces_a_request_error()
    {
        var dv = new FakeDataverseClient(new ODataResponse(404, "Not Found", "no such entity", 1));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetCeRowCountAsync("nope", null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("Not Found", result.Error);
    }

    [Fact]
    public async Task GetCeRowCount_fails_when_no_count_is_returned()
    {
        var dv = new FakeDataverseClient(Ok("{\"value\":[]}"));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
    }

    // ── #210: an UNFILTERED capped CE count is upgraded to the platform's snapshot total ──────────────
    // RetrieveTotalRecordCount has no 5,000-row ceiling, but takes no filter, answers from a ≤24h snapshot,
    // and wants logical names — so the upgrade is conditional, two extra requests, and strictly optional.

    private const string CappedCount = """
    { "@odata.count": 5000, "@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded": true, "value": [] }
    """;

    private const string AccountLogicalName = """
    { "value": [ { "LogicalName": "account", "MetadataId": "11111111-1111-1111-1111-111111111111" } ] }
    """;

    // The documented response: an SDK EntityRecordCountCollection, whose data contract serializes as
    // parallel Keys/Values arrays.
    private const string SnapshotTotal = """
    { "@odata.context": "https://x/api/data/v9.2/$metadata#Microsoft.Dynamics.CRM.RetrieveTotalRecordCountResponse",
      "EntityRecordCountCollection": {
        "Count": 1, "IsReadOnly": false, "Keys": [ "account" ], "Values": [ 42317 ] } }
    """;

    private static int EntityDefinitionRequests(IEnumerable<string> requested) =>
        requested.Count(r => r.Contains("/EntityDefinitions?", StringComparison.Ordinal));

    [Fact]
    public async Task GetCeRowCount_upgrades_a_capped_unfiltered_count_to_the_snapshot_total()
    {
        var dv = new FakeDataverseClient(Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, Env1);

        var result = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(42_317, result.Count);
        Assert.True(result.Snapshot);
        Assert.False(result.Capped);   // the snapshot total REPLACES the ceiling, it doesn't annotate it
        Assert.Equal(3, dv.Requested.Count);
        Assert.Contains("$select=LogicalName", dv.Requested[1]);
        Assert.Contains("EntitySetName eq 'accounts'", Uri.UnescapeDataString(dv.Requested[1]));
        Assert.Equal(Env1Api + "RetrieveTotalRecordCount(EntityNames=@p1)?@p1=[\"account\"]",
            Uri.UnescapeDataString(dv.Requested[2]));
    }

    [Fact]
    public async Task GetCeRowCount_leaves_a_capped_FILTERED_count_alone()
    {
        // The function accepts no filter, so a filtered leg's ceiling cannot be upgraded — it keeps today's
        // floor (and, at the row, today's Unknown verdict). No metadata lookup is even attempted.
        var dv = new FakeDataverseClient(Ok(CappedCount));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetCeRowCountAsync(
            "accounts", "accounttype eq 'customer'", TestContext.Current.CancellationToken);

        Assert.Equal(5000, result.Count);
        Assert.True(result.Capped);
        Assert.False(result.Snapshot);
        Assert.Single(dv.Requested);
    }

    [Fact]
    public async Task GetCeRowCount_does_not_reach_for_a_snapshot_when_the_live_count_was_exact()
    {
        // An exact live count is strictly better than a ≤24h snapshot, so nothing else is requested.
        var dv = new FakeDataverseClient(Ok("{\"@odata.count\":3120,\"value\":[]}"));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.Equal(3120, result.Count);
        Assert.False(result.Capped);
        Assert.False(result.Snapshot);
        Assert.Single(dv.Requested);
    }

    [Fact]
    public async Task GetCeRowCount_degrades_to_the_capped_count_when_the_logical_name_lookup_fails()
    {
        // The upgrade is never a new failure mode: a caller with no metadata read privilege still gets the
        // count it asked for, exactly as before #210.
        var dv = new FakeDataverseClient(
            Ok(CappedCount), new ODataResponse(403, "Forbidden", "no metadata read", 1));
        var reader = new CoreDualWriteMapReader(dv, Env1);

        var result = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(5000, result.Count);
        Assert.True(result.Capped);
        Assert.False(result.Snapshot);
        Assert.Equal(2, dv.Requested.Count);   // the function is never called without a logical name
    }

    [Fact]
    public async Task GetCeRowCount_degrades_to_the_capped_count_when_the_snapshot_function_fails()
    {
        var dv = new FakeDataverseClient(
            Ok(CappedCount), Ok(AccountLogicalName), new ODataResponse(500, "Server Error", "boom", 1));
        var reader = new CoreDualWriteMapReader(dv, Env1);

        var result = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(5000, result.Count);
        Assert.True(result.Capped);
        Assert.False(result.Snapshot);
    }

    [Fact]
    public async Task GetCeRowCount_degrades_to_the_capped_count_when_the_snapshot_body_names_no_total()
    {
        // A 200 that doesn't mention this table is as much a miss as a 500, and degrades the same way.
        var dv = new FakeDataverseClient(Ok(CappedCount), Ok(AccountLogicalName), Ok("""
            { "EntityRecordCountCollection": { "Count": 0, "IsReadOnly": false, "Keys": [], "Values": [] } }
            """));
        var reader = new CoreDualWriteMapReader(dv, Env1);

        var result = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(5000, result.Count);
        Assert.True(result.Capped);
        Assert.False(result.Snapshot);
    }

    [Fact]
    public async Task GetCeRowCount_reads_a_snapshot_total_from_a_dictionary_shaped_collection()
    {
        // A key-value collection is as plausibly serialized as a plain object as it is as parallel arrays,
        // and betting on one shape must not cost the upgrade — so both are read.
        var dv = new FakeDataverseClient(Ok(CappedCount), Ok(AccountLogicalName),
            Ok("""{ "EntityRecordCountCollection": { "account": 42317 } }"""));
        var reader = new CoreDualWriteMapReader(dv, Env1);

        var result = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(result.Snapshot);
        Assert.Equal(42_317, result.Count);
    }

    [Fact]
    public async Task GetCeRowCount_resolves_the_logical_name_once_per_entity_set()
    {
        // The lookup is metadata, not data: caching it keeps the upgrade at one extra request per count
        // after the first, rather than two.
        var dv = new FakeDataverseClient(
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal),
            Ok(CappedCount), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, Env1);

        await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        var second = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(second.Snapshot);
        Assert.Equal(42_317, second.Count);
        Assert.Equal(5, dv.Requested.Count);
        Assert.Equal(1, EntityDefinitionRequests(dv.Requested));
    }

    private static EnvProfile CountEnv(string id, string name, string? dataverseUrl = null) =>
        new(id, name, $"https://{name}.operations.dynamics.com", "tenant", "AUMF", "Tier 2", EnvStatus.Connected,
            DataverseUrl: dataverseUrl ?? $"https://{name}.crm.dynamics.com");

    // The #210 snapshot upgrade addresses the PINNED environment's endpoint, so these tests need one.
    private static EnvProfile? Env1() => CountEnv("env1", "contoso");

    private const string Env1Api = "https://contoso.crm.dynamics.com/api/data/v9.2/";

    // Mutable active-environment source: the shell switching environments under the app-lifetime reader.
    private sealed class EnvSwitch
    {
        public EnvProfile? Current { get; set; } = CountEnv("env1", "contoso");
        public EnvProfile? Get() => Current;
    }

    [Fact]
    public async Task The_logical_name_cache_does_not_cross_environments()
    {
        // The #151 bug class: this reader is built once and outlives an environment switch, so a set →
        // logical mapping cached for environment A must not answer for environment B. Eviction-on-rejection
        // cannot cover this — a logical name that also EXISTS in B is accepted, and the row then shows the
        // WRONG TABLE'S total with no failure anywhere to notice.
        var env = new EnvSwitch();
        var dv = new FakeDataverseClient(
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal),
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        env.Current = CountEnv("env2", "fabrikam");
        var afterSwitch = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(afterSwitch.Snapshot);
        Assert.Equal(2, EntityDefinitionRequests(dv.Requested));   // re-resolved for the environment now active
    }

    [Fact]
    public async Task The_logical_name_cache_still_serves_a_repeat_count_in_the_same_environment()
    {
        // The environment dimension must not defeat the cache it keys: an unchanged environment still gets
        // one EntityDefinitions GET for the life of the reader.
        var env = new EnvSwitch();
        var dv = new FakeDataverseClient(
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal),
            Ok(CappedCount), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        var second = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(second.Snapshot);
        Assert.Equal(42_317, second.Count);
        Assert.Equal(1, EntityDefinitionRequests(dv.Requested));
    }

    [Fact]
    public async Task Returning_to_an_environment_reuses_the_name_cached_for_it()
    {
        // The key is the environment's identity, not "the last one seen": going back to A must hit A's entry.
        var env = new EnvSwitch();
        var dv = new FakeDataverseClient(
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal),   // env1: resolves
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal),   // env2: resolves separately
            Ok(CappedCount), Ok(SnapshotTotal));                          // back on env1: cached
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        env.Current = CountEnv("env2", "fabrikam");
        await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        env.Current = CountEnv("env1", "contoso");
        var back = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(back.Snapshot);
        Assert.Equal(2, EntityDefinitionRequests(dv.Requested));   // two environments, two lookups — not three
    }

    // As FakeDataverseClient, but parks the FIRST request whose path starts with a given prefix, so an
    // environment switch can land INSIDE one of the two snapshot GETs — the window the cache key alone can't
    // close, because the client resolves the active environment per call.
    private sealed class GatedDataverseClient : IDataverseClient
    {
        private readonly Queue<ODataResponse> _responses;
        private readonly string _parkOnPrefix;
        private bool _parked;

        public List<string> Requested { get; } = new();
        public TaskCompletionSource Entered { get; } = new();
        public TaskCompletionSource Gate { get; } = new();

        public GatedDataverseClient(string parkOnPrefix, params ODataResponse[] responses)
        {
            _parkOnPrefix = parkOnPrefix;
            _responses = new Queue<ODataResponse>(responses);
        }

        public async Task<ODataResponse> GetAsync(string pathOrUrl, CancellationToken ct = default)
        {
            Requested.Add(pathOrUrl);
            // Matched anywhere in the url, not just at the start: the snapshot lookups are absolute, rooted
            // at the pinned environment's api base (#210).
            if (!_parked && pathOrUrl.Contains(_parkOnPrefix, StringComparison.Ordinal))
            {
                _parked = true;
                Entered.TrySetResult();
                await Gate.Task;
            }

            return _responses.Dequeue();
        }
    }

    [Fact]
    public async Task A_switch_during_the_logical_name_fetch_discards_the_result_and_caches_nothing()
    {
        // #170's commit-generation discard. The client resolves the ACTIVE environment inside this GET, so
        // the name coming back may be environment B's; caching it under A's key would poison that key for
        // good, because the entry outlives the switch and still answers when the user returns to A.
        var env = new EnvSwitch();
        var dv = new GatedDataverseClient("EntityDefinitions?",
            Ok(CappedCount), Ok(AccountLogicalName),                      // count on env1, then the parked lookup
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal));  // back on env1: must resolve AGAIN
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        var counting = reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        await dv.Entered.Task;                        // parked inside the EntityDefinitions GET
        env.Current = CountEnv("env2", "fabrikam");   // the shell switches while it is in flight
        dv.Gate.SetResult();
        var duringSwitch = await counting;

        // The leg keeps exactly the display it would have had without the upgrade…
        Assert.True(duringSwitch.IsSuccess);
        Assert.Equal(5000, duringSwitch.Count);
        Assert.True(duringSwitch.Capped);
        Assert.False(duringSwitch.Snapshot);
        Assert.Equal(2, dv.Requested.Count);          // the function was never called with a suspect name

        // …and nothing was written under env1's key: counting there again re-resolves from scratch.
        env.Current = CountEnv("env1", "contoso");
        var backOnEnv1 = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(backOnEnv1.Snapshot);
        Assert.Equal(42_317, backOnEnv1.Count);
        Assert.Equal(2, EntityDefinitionRequests(dv.Requested)); // resolved again — no poisoned entry served
    }

    [Fact]
    public async Task A_switch_during_the_snapshot_function_call_discards_the_total()
    {
        // The second GET carries the same exposure: the total coming back may be environment B's, and pairing
        // it with environment A's leg is precisely the wrong-number outcome the capped display avoids.
        var env = new EnvSwitch();
        var dv = new GatedDataverseClient("RetrieveTotalRecordCount",
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        var counting = reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        await dv.Entered.Task;
        env.Current = CountEnv("env2", "fabrikam");
        dv.Gate.SetResult();
        var result = await counting;

        Assert.Equal(5000, result.Count);
        Assert.True(result.Capped);
        Assert.False(result.Snapshot);   // no foreign total on this leg
    }

    [Fact]
    public async Task A_rejection_that_arrives_after_a_switch_does_not_evict_the_previous_environments_entry()
    {
        // The eviction path takes the same discard: after a switch the rejection is a verdict on environment
        // B, so it says nothing about the name cached for A. Acting on it would bin correct metadata.
        var env = new EnvSwitch();
        var dv = new GatedDataverseClient("RetrieveTotalRecordCount",
            Ok(CappedCount), Ok(AccountLogicalName), new ODataResponse(404, "Not Found", "no such entity", 1),
            Ok(CappedCount), Ok(SnapshotTotal));   // back on env1: the entry survived, so no second lookup
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        var counting = reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        await dv.Entered.Task;
        env.Current = CountEnv("env2", "fabrikam");
        dv.Gate.SetResult();
        await counting;

        env.Current = CountEnv("env1", "contoso");
        var backOnEnv1 = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(backOnEnv1.Snapshot);
        Assert.Equal(42_317, backOnEnv1.Count);
        Assert.Equal(1, EntityDefinitionRequests(dv.Requested));  // env1's entry outlived the foreign rejection
    }

    [Fact]
    public async Task Without_a_switch_a_gated_lookup_still_upgrades_and_caches()
    {
        // The discard must not misfire on an unchanged environment: same parked GET, no switch.
        var env = new EnvSwitch();
        var dv = new GatedDataverseClient("EntityDefinitions?",
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal),
            Ok(CappedCount), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        var counting = reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        await dv.Entered.Task;
        dv.Gate.SetResult();
        var first = await counting;
        var second = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(first.Snapshot);
        Assert.Equal(42_317, first.Count);
        Assert.True(second.Snapshot);
        Assert.Equal(1, EntityDefinitionRequests(dv.Requested));   // cached exactly as before
    }

    // ── #210: the operation is PINNED to one environment instance, not re-checked by id ────────────────

    [Fact]
    public async Task The_snapshot_lookups_are_addressed_to_the_pinned_environments_endpoint()
    {
        // The pinning contract this class owns: both lookups go out as ABSOLUTE urls rooted at the captured
        // environment's Dataverse endpoint. That is what lets CoreDataverseClient's origin guard adjudicate
        // them against whichever environment is active when they are actually issued.
        var dv = new FakeDataverseClient(Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, Env1);

        await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.StartsWith(Env1Api + "accounts?", dv.Requested[0]);
        Assert.StartsWith(Env1Api + "EntityDefinitions?", dv.Requested[1]);
        Assert.StartsWith(Env1Api + "RetrieveTotalRecordCount", dv.Requested[2]);
    }

    // As GatedDataverseClient, but ALSO enforcing CoreDataverseClient's origin rule via the production
    // RequestOriginGuard: an absolute request is refused unless it matches the ACTIVE environment's Dataverse
    // origin, and the check runs when the request is issued (before the parked round-trip), exactly as the
    // real client orders it. Without this the reader's pinning would be unobservable at this seam — the
    // refusal is the client's behaviour, so the double reproduces that one rule and nothing else.
    private sealed class OriginGuardingDataverseClient : IDataverseClient
    {
        private readonly Queue<ODataResponse> _responses;
        private readonly Func<EnvProfile?> _activeEnv;
        private readonly string _parkOn;
        private bool _parked;

        public List<string> Requested { get; } = new();
        public TaskCompletionSource Entered { get; } = new();
        public TaskCompletionSource Gate { get; } = new();

        public OriginGuardingDataverseClient(Func<EnvProfile?> activeEnv, string parkOn, params ODataResponse[] responses)
        {
            _activeEnv = activeEnv;
            _parkOn = parkOn;
            _responses = new Queue<ODataResponse>(responses);
        }

        public async Task<ODataResponse> GetAsync(string pathOrUrl, CancellationToken ct = default)
        {
            Requested.Add(pathOrUrl);

            if (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !RequestOriginGuard.IsSameOrigin(_activeEnv()?.DataverseUrl, new Uri(pathOrUrl)))
            {
                return new ODataResponse(0, "Refused",
                    "The paging link points to a different origin than the Dataverse environment.", 1);
            }

            if (!_parked && pathOrUrl.Contains(_parkOn, StringComparison.Ordinal))
            {
                _parked = true;
                Entered.TrySetResult();
                await Gate.Task;
            }

            return _responses.Dequeue();
        }
    }

    [Fact]
    public async Task An_A_to_B_to_A_switch_around_the_lookup_neither_upgrades_nor_caches()
    {
        // The ABA hole an id re-check cannot see: with the switch back in place by the time the operation
        // ends, "is the active id still what I captured?" answers YES — while the lookup went out under
        // env2. Pinning settles it earlier and elsewhere: the request carries env1's origin, env2 is active
        // when it is issued, and the origin guard refuses it. Nothing is upgraded and nothing is cached.
        var env = new EnvSwitch();
        var dv = new OriginGuardingDataverseClient(env.Get, "accounts?",
            Ok(CappedCount),                                       // run 1's count (env1 still active)
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal));  // run 2, back on env1
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        var counting = reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        await dv.Entered.Task;                        // parked in the count GET; env1 is what got pinned
        env.Current = CountEnv("env2", "fabrikam");   // A → B, so the lookup is issued under env2 …
        dv.Gate.SetResult();
        var duringAba = await counting;
        env.Current = CountEnv("env1", "contoso");    // … B → A, which an id re-check would find unchanged

        Assert.True(duringAba.IsSuccess);
        Assert.Equal(5000, duringAba.Count);
        Assert.True(duringAba.Capped);
        Assert.False(duringAba.Snapshot);   // no total from an environment this count didn't come from

        // And nothing was cached for env1: counting there again has to resolve from scratch.
        var afterwards = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(afterwards.Snapshot);
        Assert.Equal(42_317, afterwards.Count);
        Assert.Equal(2, EntityDefinitionRequests(dv.Requested));  // the refused one, then a real one
    }

    [Fact]
    public async Task A_profile_repointed_at_another_organisation_does_not_reuse_the_old_endpoints_name()
    {
        // Why the key carries the endpoint as well as the id — the same reason CatalogService.CacheKey (#151)
        // does. A profile is editable in place: the id survives while the Dataverse URL moves to a different
        // organisation, and an id-only key would answer the new endpoint with the old one's metadata.
        var env = new EnvSwitch();
        var dv = new FakeDataverseClient(
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal),
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        env.Current = CountEnv("env1", "contoso", dataverseUrl: "https://contoso-uat.crm.dynamics.com");
        var afterEdit = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(afterEdit.Snapshot);
        Assert.Equal(2, EntityDefinitionRequests(dv.Requested));          // resolved afresh for the new org
        Assert.StartsWith("https://contoso-uat.crm.dynamics.com/", dv.Requested[4]);  // and addressed there
    }

    [Fact]
    public async Task A_cosmetic_url_difference_is_still_the_same_endpoint()
    {
        // The key normalizes like CatalogService.CacheKey — lower-invariant, scheme-defaulted, no trailing
        // slash — so casing or a trailing slash is not mistaken for a different organisation and does not
        // silently double the lookups.
        var env = new EnvSwitch();
        var dv = new FakeDataverseClient(
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal),
            Ok(CappedCount), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, env.Get);

        await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        env.Current = CountEnv("env1", "contoso", dataverseUrl: "HTTPS://Contoso.crm.dynamics.com/");
        var second = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(second.Snapshot);
        Assert.Equal(1, EntityDefinitionRequests(dv.Requested));
    }

    [Fact]
    public async Task A_leg_counted_without_a_dataverse_url_keeps_todays_relative_request_and_error()
    {
        // Nothing to pin to. The count still goes out exactly as it does today — a relative path, so the
        // client answers with its own "No Dataverse URL" diagnosis — and the upgrade is simply not attempted.
        var dv = new FakeDataverseClient(Ok(CappedCount));
        var reader = new CoreDualWriteMapReader(dv, () => CountEnv("env1", "contoso", dataverseUrl: ""));

        var result = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.StartsWith("accounts?", dv.Requested[0]);   // relative, never a fabricated absolute url
        Assert.True(result.Capped);
        Assert.False(result.Snapshot);
        Assert.Single(dv.Requested);
    }

    [Fact]
    public async Task A_cached_logical_name_the_function_rejects_is_re_resolved_next_time()
    {
        // What an environment switch looks like from inside the reader: the cached name is metadata from an
        // environment that is no longer active, so the function rejects it. Dropping the entry lets the next
        // run recover instead of repeating a dead request for the life of the app.
        var dv = new FakeDataverseClient(
            Ok(CappedCount), Ok(AccountLogicalName), new ODataResponse(404, "Not Found", "no such entity", 1),
            Ok(CappedCount), Ok(AccountLogicalName), Ok(SnapshotTotal));
        var reader = new CoreDualWriteMapReader(dv, Env1);

        var first = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);
        var second = await reader.GetCeRowCountAsync("accounts", null, TestContext.Current.CancellationToken);

        Assert.True(first.Capped);
        Assert.False(first.Snapshot);
        Assert.True(second.Snapshot);
        Assert.Equal(42_317, second.Count);
        Assert.Equal(2, EntityDefinitionRequests(dv.Requested));
    }

    [Fact]
    public async Task GetMaps_for_a_solution_surfaces_a_component_fetch_error()
    {
        var dv = new FakeDataverseClient(new ODataResponse(403, "Forbidden", "no access", 1));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync("my_solution", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("Forbidden", result.Error);
    }

    // ── Solution filtering, against the REAL two-request path (#167) ──────────────────────────────────
    // The fake reader filters on each map's own SolutionId, which the real reader never reads: it asks
    // solutioncomponents for the solution's dual-write-map object ids first, then keeps only the maps whose
    // id is in that set. VM-level tests over the fake therefore prove nothing about this path, so the
    // request shape and the set-intersection edges are covered here.

    [Fact]
    public async Task GetMaps_for_a_solution_scopes_the_components_request_to_that_solution_and_component_type()
    {
        var dv = new FakeDataverseClient(Ok("""{ "value": [] }"""), Ok(OneMap));
        var reader = new CoreDualWriteMapReader(dv);

        await reader.GetMapsAsync("my_solution", TestContext.Current.CancellationToken);

        // The filter is URL-escaped on the wire; assert on its decoded form so the test reads like the query.
        var componentsRequest = Uri.UnescapeDataString(dv.Requested[0]);
        Assert.StartsWith("solutioncomponents?", componentsRequest);
        Assert.Contains("$select=objectid", componentsRequest);
        Assert.Contains("componenttype eq 500", componentsRequest);              // dual-write map components only
        Assert.Contains("solutionid/uniquename eq 'my_solution'", componentsRequest);
    }

    [Fact]
    public async Task GetMaps_for_a_solution_with_no_components_returns_no_maps()
    {
        // An empty solution must yield an empty list, NOT the unfiltered set — the failure mode where a
        // "no maps in this solution" answer silently becomes "every map in the environment".
        var dv = new FakeDataverseClient(Ok("""{ "value": [] }"""), Ok(OneMap));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync("empty_solution", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Maps);
        Assert.Equal(2, dv.Requested.Count);   // components, then maps: the filter is applied, not skipped
    }

    [Fact]
    public async Task GetMaps_for_a_solution_whose_component_matches_no_map_returns_no_maps()
    {
        // A component id that is a valid guid but names no map (a stale component row, or a map deleted
        // out from under the solution) must drop every map rather than fall through to an unfiltered list.
        const string components = """
        { "value": [ { "objectid": "cccccccc-cccc-cccc-cccc-cccccccccccc" } ] }
        """;
        const string maps = """
        { "value": [
            { "msdyn_dualwriteentitymapid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "msdyn_name": "alpha" },
            { "msdyn_dualwriteentitymapid": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "msdyn_name": "beta" } ] }
        """;
        var dv = new FakeDataverseClient(Ok(components), Ok(maps));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync("my_solution", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Maps);
    }

    [Fact]
    public async Task GetMaps_for_a_solution_pages_the_components_before_filtering()
    {
        // The component ids are themselves server-paged; a filter built from page 1 alone would drop every
        // map whose component landed on page 2.
        const string componentsPage1 = """
        { "@odata.nextLink": "https://x/api/data/v9.2/components2",
          "value": [ { "objectid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" } ] }
        """;
        const string componentsPage2 = """
        { "value": [ { "objectid": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" } ] }
        """;
        const string maps = """
        { "value": [
            { "msdyn_dualwriteentitymapid": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "msdyn_name": "alpha" },
            { "msdyn_dualwriteentitymapid": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "msdyn_name": "beta" },
            { "msdyn_dualwriteentitymapid": "cccccccc-cccc-cccc-cccc-cccccccccccc", "msdyn_name": "gamma" } ] }
        """;
        var dv = new FakeDataverseClient(Ok(componentsPage1), Ok(componentsPage2), Ok(maps));
        var reader = new CoreDualWriteMapReader(dv);

        var result = await reader.GetMapsAsync("my_solution", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "alpha", "beta" }, result.Maps.Select(m => m.Name).ToArray());
        Assert.Equal("https://x/api/data/v9.2/components2", dv.Requested[1]);
    }
}
