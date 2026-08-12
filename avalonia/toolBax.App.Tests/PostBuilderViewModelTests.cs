using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class PostBuilderViewModelTests
{
    private static PostBuilderViewModel MakeVm() => new(new FakeODataClient());

    // A POST Builder backed by the seeded metadata (CustomersV3 has fields), for field-grid tests.
    private static PostBuilderViewModel MakeGridVm() =>
        new(new FakeODataClient(), metadata: new FakeMetadataService());

    // Wraps the seeded metadata to count GetFields calls (to assert the grid isn't rebuilt twice).
    private sealed class CountingMetadata : IMetadataService
    {
        private readonly FakeMetadataService _inner = new();
        public int GetFieldsCalls { get; private set; }
        public IReadOnlyList<EntitySet> GetEntities() => _inner.GetEntities();
        public IReadOnlyList<EntityField>? GetFields(string entityName)
        {
            GetFieldsCalls++;
            return _inner.GetFields(entityName);
        }
        public Task LoadEntitiesAsync(CancellationToken ct = default) => _inner.LoadEntitiesAsync(ct);
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            _inner.LoadFieldsAsync(entityName, ct);
    }

    // Mimics the real (environment-scoped) service: nothing is cached until the Load* calls run, and the
    // catalogue it publishes can be swapped to stand in for an environment switch. The pre-seeded
    // FakeMetadataService can express neither — its entities and CustomersV3 fields are always there.
    private sealed class DeferredMetadata : IMetadataService
    {
        private static readonly EntityField[] Schema =
        {
            new("Id", "String", false, IsKey: true, Length: 10),
            new("Name", "String", true, Length: 50),
        };

        private readonly HashSet<string> _fieldsLoaded = new(StringComparer.Ordinal);
        private string[] _catalogue;
        private string[] _published = Array.Empty<string>();

        public DeferredMetadata(params string[] catalogue) => _catalogue = catalogue;

        /// <summary>How many field fetches the VM asked for (i.e. went through EntityCatalogLoader).</summary>
        public int FieldLoads { get; private set; }

        /// <summary>Swaps what the NEXT LoadEntitiesAsync publishes (an environment switch).</summary>
        public void SwitchCatalogue(params string[] catalogue) => _catalogue = catalogue;

        public IReadOnlyList<EntitySet> GetEntities() =>
            _published.Select(n => new EntitySet(n, "M", 2, "Id", false, "odata")).ToList();

        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            _fieldsLoaded.Contains(entityName) ? Schema : null;

        public Task LoadEntitiesAsync(CancellationToken ct = default)
        {
            _published = _catalogue;
            return Task.CompletedTask;
        }

        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        {
            FieldLoads++;
            var known = _published.Contains(entityName);
            if (known)
            {
                _fieldsLoaded.Add(entityName);
            }

            return Task.FromResult(known);
        }
    }

    // Metadata carrying a date-only field, to prove an Edm.Date column reaches the payload builder's
    // date-only branch instead of being widened to a timestamp. FakeMetadataService has no Date field and
    // is app (not test) code, so the shape lives here.
    private sealed class DateFieldMetadata : IMetadataService
    {
        private static readonly IReadOnlyList<EntitySet> Sets =
            new[] { new EntitySet("WorkerV2", "HR", 2, "PersonnelNumber", false, "hr") };

        private static readonly IReadOnlyList<EntityField> Fields = new[]
        {
            new EntityField("PersonnelNumber", "String", false, IsKey: true, Length: 20),
            new EntityField("BirthDate", "Date", true),
        };

        public IReadOnlyList<EntitySet> GetEntities() => Sets;
        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            entityName == "WorkerV2" ? Fields : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            Task.FromResult(entityName == "WorkerV2");
    }

    // An entity keyed on a number, with one nullable non-key field. Serves two purposes: proving non-string
    // key types are left raw in the predicate (digits carry no URL syntax), and giving the POST/clear-field
    // tests a minimal entity whose only mandatory field is the key — so a blank optional field is the only
    // thing under test.
    private sealed class NumericKeyMetadata : IMetadataService
    {
        private static readonly IReadOnlyList<EntitySet> Sets =
            new[] { new EntitySet("Counters", "SYS", 2, "Id", false, "system") };

        private static readonly IReadOnlyList<EntityField> Fields = new[]
        {
            new EntityField("Id", "Int32", false, IsKey: true),
            new EntityField("Label", "String", true, Length: 20),
        };

        public IReadOnlyList<EntitySet> GetEntities() => Sets;
        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            entityName == "Counters" ? Fields : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            Task.FromResult(entityName == "Counters");
    }

    // A grid on WorkerV2 (single String key) with the key filled, for the key-literal encoding tests.
    private static PostBuilderViewModel KeyGrid(string keyValue)
    {
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: new DateFieldMetadata())
        {
            Method = "PATCH",
            UseFieldGrid = true,
        };
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "WorkerV2");
        vm.Fields.Single(f => f.Name == "PersonnelNumber").Value = keyValue;
        return vm;
    }

    // A grid on Counters (Int32 key + nullable Label), for the clear-field / empty-body tests.
    private static PostBuilderViewModel CounterGrid(string method)
    {
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: new NumericKeyMetadata())
        {
            Method = method,
            UseFieldGrid = true,
        };
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "Counters");
        vm.Fields.Single(f => f.Name == "Id").Value = "7";
        return vm;
    }

    // Makes a seeded-CustomersV3 PATCH valid under clear-field semantics (#158): its non-nullable non-key
    // fields are pre-included but blank, which is now an issue ("enter a value or exclude it"), and a PATCH
    // left with nothing in the body is blocked as a no-op — so drop the blanks and patch one real field.
    private static void PatchOneField(PostBuilderViewModel vm, string field = "OrganizationName", string value = "Acme")
    {
        foreach (var row in vm.Fields.Where(r => !r.IsKey && string.IsNullOrWhiteSpace(r.Value)).ToList())
        {
            row.Include = false;
        }

        var target = vm.Fields.Single(r => r.Name == field);
        target.Include = true;
        target.Value = value;
    }

    private sealed class RecordingODataClient : IODataClient
    {
        public string? LastPath { get; private set; }
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            LastPath = path;
            return Task.FromResult(new ODataResponse(204, "No Content", string.Empty, 3));
        }
    }

    // Captures the headers passed to the 5-arg overload (for If-Match assertions).
    private sealed class HeaderCapturingClient : IODataClient
    {
        public IReadOnlyDictionary<string, string>? LastHeaders { get; private set; }

        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
            => SendAsync(method, path, body, null, ct);

        public Task<ODataResponse> SendAsync(string method, string path, string? body,
            IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
        {
            LastHeaders = headers;
            return Task.FromResult(new ODataResponse(204, "No Content", string.Empty, 1));
        }
    }

    [Fact]
    public async Task Post_returns_201_and_echoes_the_body()
    {
        var vm = MakeVm();
        vm.Method = "POST";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("201", vm.StatusText);
        Assert.Contains("CustomerAccount", vm.ResponseBody);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Delete_returns_204()
    {
        var vm = MakeVm();
        vm.Method = "DELETE";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("204", vm.StatusText);
    }

    [Fact]
    public async Task Empty_post_body_is_rejected_with_400()
    {
        var vm = MakeVm();
        vm.Method = "POST";
        vm.RequestBody = "   ";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("400", vm.StatusText);
    }

    [Fact]
    public async Task Patch_returns_204()
    {
        var vm = MakeVm();
        vm.Method = "PATCH";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("204", vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Cross_company_appends_to_the_request_url()
    {
        var vm = MakeVm();
        vm.Method = "PATCH";
        vm.Path = "/data/CustomersV3(...)";

        Assert.DoesNotContain("cross-company", vm.RequestUrl);

        vm.CrossCompany = true;

        Assert.Equal("PATCH /data/CustomersV3(...)?cross-company=true", vm.RequestUrl);
    }

    [Fact]
    public async Task Send_uses_the_cross_company_effective_path()
    {
        var recorder = new RecordingODataClient();
        var vm = new PostBuilderViewModel(recorder) { Method = "DELETE", Path = "/data/E(1)", CrossCompany = true };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("/data/E(1)?cross-company=true", recorder.LastPath);
    }

    [Fact]
    public void Grid_patch_targets_the_record_via_a_composite_key_predicate()
    {
        var vm = MakeGridVm();
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Method = "PATCH";

        vm.Fields.Single(f => f.Name == "dataAreaId").Value = "USMF";
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-001";

        // %27 is the quote delimiting a string key literal — percent-encoded so a value carrying URL syntax
        // can't reshape the request (issue #157).
        Assert.Contains("/data/CustomersV3(dataAreaId=%27USMF%27,CustomerAccount=%27US-001%27)", vm.RequestUrl);
    }

    [Fact]
    public void Post_validation_summary_reflects_the_issue_count()
    {
        var vm = MakeGridVm();
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Method = "POST"; // enforces mandatory fields → at least one is unfilled

        Assert.True(vm.HasPayloadIssues);
        Assert.True(vm.PayloadIssueCount >= 1);
        // The count matches the detail lines, and the concise header carries it (so the panel can stay
        // capped + scrollable instead of rendering one sentence per field as a wall).
        Assert.Equal(vm.PayloadIssueCount, vm.PayloadIssues.Split(System.Environment.NewLine).Length);
        Assert.Contains(vm.PayloadIssueCount.ToString(), vm.PayloadIssueSummary);
        Assert.Contains("resolve before sending", vm.PayloadIssueSummary);
    }

    [Fact]
    public void Grid_post_targets_the_collection_without_a_key_predicate()
    {
        var vm = MakeGridVm();
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Method = "POST";
        vm.Fields.Single(f => f.Name == "dataAreaId").Value = "USMF";

        Assert.Contains("/data/CustomersV3", vm.RequestUrl);
        Assert.DoesNotContain("(dataAreaId=", vm.RequestUrl);
    }

    [Fact]
    public void Grid_patch_omits_the_predicate_until_every_key_value_is_present()
    {
        var vm = MakeGridVm();
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Method = "PATCH";
        vm.Fields.Single(f => f.Name == "dataAreaId").Value = "USMF"; // CustomerAccount still blank

        Assert.DoesNotContain("(dataAreaId=", vm.RequestUrl); // incomplete key → no partial predicate
    }

    [Fact]
    public void Grid_patch_body_excludes_key_fields()
    {
        var vm = MakeGridVm();
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Method = "PATCH";
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1"; // dataAreaId is seeded
        PatchOneField(vm);

        Assert.Contains("OrganizationName", vm.RequestBody);
        Assert.DoesNotContain("dataAreaId", vm.RequestBody);     // keys live in the URL predicate, not the body
        Assert.DoesNotContain("CustomerAccount", vm.RequestBody);
    }

    [Fact]
    public async Task Grid_delete_sends_to_the_keyed_url()
    {
        var recorder = new RecordingODataClient();
        var vm = new PostBuilderViewModel(recorder, metadata: new FakeMetadataService());
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Method = "DELETE";
        vm.Fields.Single(f => f.Name == "dataAreaId").Value = "USMF";
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("/data/CustomersV3(dataAreaId=%27USMF%27,CustomerAccount=%27US-1%27)", recorder.LastPath);
    }

    // Returns a response carrying headers (to exercise the response-header surface).
    private sealed class HeaderedResponseClient : IODataClient
    {
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
            => Task.FromResult(new ODataResponse(201, "Created", "{}", 2,
                new Dictionary<string, string> { ["OData-EntityId"] = "https://x/data/E(1)", ["ETag"] = "W/\"9\"" }));
    }

    [Fact]
    public async Task Send_surfaces_response_headers()
    {
        var vm = new PostBuilderViewModel(new HeaderedResponseClient()) { Method = "POST" };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(vm.HasResponseHeaders);
        Assert.Contains("OData-EntityId: https://x/data/E(1)", vm.ResponseHeaders);
        Assert.Contains("ETag: W/\"9\"", vm.ResponseHeaders);
        // Sorted by name: ETag precedes OData-EntityId.
        Assert.StartsWith("ETag: W/\"9\"", vm.ResponseHeaders);
        Assert.True(vm.ResponseHeaders.IndexOf("ETag:", StringComparison.Ordinal)
            < vm.ResponseHeaders.IndexOf("OData-EntityId:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Patch_sends_if_match_header_when_enabled()
    {
        var client = new HeaderCapturingClient();
        var vm = new PostBuilderViewModel(client) { Method = "PATCH", Path = "/data/E(1)", UseIfMatch = true };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.NotNull(client.LastHeaders);
        Assert.Equal("*", client.LastHeaders!["If-Match"]);
    }

    [Fact]
    public async Task Patch_omits_if_match_header_when_disabled()
    {
        var client = new HeaderCapturingClient();
        var vm = new PostBuilderViewModel(client) { Method = "PATCH", Path = "/data/E(1)", UseIfMatch = false };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Null(client.LastHeaders);
    }

    [Fact]
    public async Task Post_never_sends_if_match_header()
    {
        var client = new HeaderCapturingClient();
        var vm = new PostBuilderViewModel(client) { Method = "POST", UseIfMatch = true };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Null(client.LastHeaders); // If-Match is meaningless for a create
        Assert.False(vm.ShowIfMatch);    // …and the controls are hidden for POST
    }

    [Fact]
    public async Task A_custom_etag_is_sent_as_the_if_match_value()
    {
        var client = new HeaderCapturingClient();
        var vm = new PostBuilderViewModel(client) { Method = "DELETE", Path = "/data/E(1)", UseIfMatch = true, IfMatch = "W/\"42\"" };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("W/\"42\"", client.LastHeaders!["If-Match"]);
        Assert.True(vm.ShowIfMatch);
    }

    [Fact]
    public async Task Send_sets_the_success_badge()
    {
        var vm = MakeVm();
        vm.Method = "POST";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(vm.SendSucceeded);
        Assert.Contains("201", vm.StatusBadge);
    }

    [Fact]
    public async Task Copy_url_and_payload_write_to_the_clipboard()
    {
        var clipboard = new FakeClipboardService();
        var vm = new PostBuilderViewModel(new FakeODataClient(), clipboard) { Path = "/data/E", CrossCompany = true };

        await vm.CopyUrlCommand.ExecuteAsync(null);
        Assert.Equal("/data/E?cross-company=true", clipboard.LastText);

        await vm.CopyPayloadCommand.ExecuteAsync(null);
        Assert.Equal(vm.RequestBody, clipboard.LastText);
    }

    // --- Field-grid (metadata-driven payload builder) ---

    [Theory]
    [InlineData("Boolean", "Edm.Boolean")]
    [InlineData("Int16", "Edm.Int16")]
    [InlineData("Int32", "Edm.Int32")]
    [InlineData("Int64", "Edm.Int64")]
    [InlineData("Decimal", "Edm.Decimal")]
    [InlineData("Double", "Edm.Double")]
    [InlineData("Single", "Edm.Single")]
    [InlineData("Guid", "Edm.Guid")]
    [InlineData("DateTime", "Edm.DateTimeOffset")]
    [InlineData("Date", "Edm.Date")] // date-only fields keep their own EDM type
    [InlineData("String", "Edm.String")]
    [InlineData("Enum", "Edm.String")] // enum members are sent as JSON strings (member-name)
    public void Friendly_types_map_to_edm(string friendly, string edm) =>
        Assert.Equal(edm, PostPayloadMapper.ToEdmType(friendly));

    // The grid row whose value drives the payload, for the date end-to-end tests below.
    private static (PostBuilderViewModel Vm, PostFieldRow Row) DateGrid()
    {
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: new DateFieldMetadata());
        vm.Method = "PATCH"; // key fields move to the URL predicate, so only BirthDate is in the body
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "WorkerV2");

        // A keyed write needs its key value or the incomplete-key issue masks the coercion result.
        vm.Fields.Single(f => f.Name == "PersonnelNumber").Value = "000123";

        var row = vm.Fields.Single(f => f.Name == "BirthDate");
        row.Include = true;
        return (vm, row);
    }

    [Fact]
    public void A_date_field_serialises_as_a_bare_date_not_a_timestamp()
    {
        var (vm, birthDate) = DateGrid();

        birthDate.Value = "2026-08-11";

        Assert.False(vm.HasPayloadIssues);
        Assert.Contains("\"2026-08-11\"", vm.RequestBody);
        Assert.DoesNotContain("T00:00:00", vm.RequestBody); // not widened to a DateTimeOffset
    }

    [Fact]
    public void A_date_field_is_validated_rather_than_passed_through_as_a_string()
    {
        var (vm, birthDate) = DateGrid();

        // Before Edm.Date was reachable a date cell was typed as string/DateTimeOffset, so an ambiguous
        // locale date reached F&O unchallenged (or as 8 November).
        birthDate.Value = "11/08/2026";

        Assert.True(vm.HasPayloadIssues);
        Assert.Contains("yyyy-MM-dd", vm.PayloadIssues, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_construction_does_not_disturb_the_raw_body_or_path()
    {
        // Grid mode is opt-in; a freshly-built VM keeps the seeded raw JSON and default path.
        var vm = MakeGridVm();

        Assert.False(vm.UseFieldGrid);
        Assert.False(vm.IsBodyReadOnly);
        Assert.Contains("CustomerAccount", vm.RequestBody);
        Assert.Null(vm.SelectedEntity);
        Assert.NotEmpty(vm.Entities);
    }

    [Fact]
    public void Selecting_an_entity_in_grid_mode_pre_includes_key_and_mandatory_fields()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH"; // avoid mandatory-blank issues for this structural check
        vm.UseFieldGrid = true;

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.NotEmpty(vm.Fields);
        Assert.True(vm.Fields.Single(f => f.Name == "CustomerAccount").Include);  // key → pre-included
        Assert.True(vm.Fields.Single(f => f.Name == "CustomerAccount").Mandatory);
        Assert.True(vm.Fields.Single(f => f.Name == "CurrencyCode").Include);     // non-nullable → pre-included
        Assert.False(vm.Fields.Single(f => f.Name == "OrganizationName").Include); // optional → not pre-included
        Assert.Equal("/data/CustomersV3", vm.Path);
    }

    [Fact]
    public void Editing_a_value_rebuilds_the_payload_with_type_coercion()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        foreach (var f in vm.Fields)
        {
            f.Include = false;
        }

        var credit = vm.Fields.Single(f => f.Name == "CreditLimit"); // Edm.Decimal
        credit.Include = true;
        credit.Value = "100.5";

        Assert.Contains("CreditLimit", vm.RequestBody);
        Assert.Contains("100.5", vm.RequestBody);
        Assert.DoesNotContain("\"100.5\"", vm.RequestBody); // a number, not a quoted string
    }

    [Fact]
    public void Excluding_a_field_omits_it_from_the_payload()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";

        PatchOneField(vm);
        Assert.Contains("OrganizationName", vm.RequestBody);

        vm.Fields.Single(f => f.Name == "OrganizationName").Include = false;
        Assert.DoesNotContain("OrganizationName", vm.RequestBody);
    }

    [Fact]
    public void Post_enforces_mandatory_fields_but_patch_does_not()
    {
        var vm = MakeGridVm();
        vm.Method = "POST";
        vm.UseFieldGrid = true;

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        // Mandatory fields are pre-included but blank, so a POST surfaces validation issues.
        Assert.True(vm.HasPayloadIssues);
        Assert.Contains("mandatory", vm.PayloadIssues, StringComparison.OrdinalIgnoreCase);

        // PATCH relaxes mandatory enforcement; with the key values supplied (to target the record) and one
        // real field to write, the same selection becomes valid. The blanks still have to be unchecked —
        // under PATCH an included blank means "clear this field", which a non-nullable column can't be.
        vm.Method = "PATCH";
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";
        PatchOneField(vm);
        Assert.False(vm.HasPayloadIssues);
    }

    [Fact]
    public void Keyed_write_with_an_incomplete_key_is_flagged_and_send_is_blocked()
    {
        var vm = MakeGridVm();
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Method = "PATCH";
        PatchOneField(vm); // a valid body, so the key is the only thing left to be wrong

        // The record is identified by neither a complete URL predicate nor the (key-excluded) body,
        // so the keyed write is flagged and Send is disabled until every key value is present.
        // (dataAreaId is seeded; CustomerAccount is still blank.)
        Assert.True(vm.HasPayloadIssues);
        Assert.Contains("key", vm.PayloadIssues, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.SendCommand.CanExecute(null));

        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";
        Assert.False(vm.HasPayloadIssues);
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    // An entity whose metadata declares NO key fields at all (some F&O services expose these unkeyed).
    private sealed class KeylessMetadata : IMetadataService
    {
        private static readonly IReadOnlyList<EntitySet> Sets =
            new[] { new EntitySet("LogEntries", "SYS", 1, string.Empty, false, "system") };

        private static readonly IReadOnlyList<EntityField> Fields = new[]
        {
            new EntityField("Message", "String", true, Length: 200),
        };

        public IReadOnlyList<EntitySet> GetEntities() => Sets;
        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            entityName == "LogEntries" ? Fields : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            Task.FromResult(entityName == "LogEntries");
    }

    [Fact]
    public void Keyed_write_against_a_keyless_entity_is_blocked_not_sent()
    {
        // Red-check: before the fix, BuildKeyPredicate returns null for a keyless entity, BasePath falls
        // back to the bare collection URL, and — because the old guard only fired when keyNames.Count > 0 —
        // no issue was raised there either, leaving Send enabled for a DELETE against a whole collection
        // (F&O would 405 it, but the confirm dialog still promised "the targeted record will be removed").
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: new KeylessMetadata());
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "LogEntries");
        vm.Method = "DELETE";

        Assert.Equal("DELETE /data/LogEntries", vm.RequestUrl); // no predicate — falls back to the collection
        Assert.True(vm.HasPayloadIssues);
        Assert.Contains("no key fields", vm.PayloadIssues, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Toggling_grid_mode_drives_the_body_read_only_state()
    {
        var vm = MakeGridVm();

        vm.UseFieldGrid = true;
        Assert.True(vm.IsBodyReadOnly);

        vm.UseFieldGrid = false;
        Assert.False(vm.IsBodyReadOnly);
    }

    [Fact]
    public void Entity_search_filters_the_displayed_list_case_insensitively()
    {
        var vm = MakeGridVm();
        Assert.Equal(vm.Entities.Count, vm.FilteredEntities.Count);

        vm.EntitySearch = "ledger"; // matches LedgerJournalHeaders only; CustomersV3 not selected here

        Assert.NotEmpty(vm.FilteredEntities);
        Assert.All(vm.FilteredEntities, e => Assert.Contains("ledger", e.Name, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.FilteredEntities, e => e.Name == "CustomersV3");
    }

    [Fact]
    public void Invalid_grid_payload_clears_the_body_and_blocks_send()
    {
        var vm = MakeGridVm();
        vm.Method = "POST";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.True(vm.HasPayloadIssues);              // mandatory fields are blank under POST
        Assert.Equal(string.Empty, vm.RequestBody);    // no stale body left behind to send
        Assert.False(vm.SendCommand.CanExecute(null));  // Send is disabled while the payload is invalid

        vm.Method = "PATCH";                            // PATCH relaxes mandatory…
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";  // …and the keys target the record
        PatchOneField(vm);                              // (dataAreaId is seeded)

        Assert.False(vm.HasPayloadIssues);
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Raw_mode_send_is_never_blocked_by_payload_issues()
    {
        var vm = MakeVm(); // raw mode (no field grid)

        Assert.True(vm.SendCommand.CanExecute(null));
    }

    // --- Field metadata that hasn't loaded (issue #155) ---

    [Fact]
    public void Switching_to_an_entity_without_loaded_fields_clears_the_body_and_blocks_send()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3"); // the only seeded entity with fields
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";
        PatchOneField(vm);
        Assert.False(vm.HasPayloadIssues);
        Assert.Contains("Acme", vm.RequestBody); // a real CustomersV3 body, which must not go stale

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2"); // no cached fields

        Assert.Empty(vm.Fields);
        Assert.Equal(string.Empty, vm.RequestBody);     // the CustomersV3 body must not survive the switch…
        Assert.True(vm.HasPayloadIssues);               // …the reason is stated…
        Assert.Equal(1, vm.PayloadIssueCount);
        Assert.Contains("VendorsV2", vm.PayloadIssues);
        Assert.False(vm.SendCommand.CanExecute(null));  // …so a CustomersV3 payload can't POST to /data/VendorsV2
    }

    [Fact]
    public void Delete_is_blocked_while_the_selected_entitys_field_metadata_has_not_loaded()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Fields.Single(f => f.Name == "dataAreaId").Value = "USMF";
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2"); // no cached fields
        vm.Method = "DELETE";

        // With no fields there are no key values, so the target falls back to the collection URL and the
        // keyed-write guard (which needs the key names) never runs. CanSend is the only thing between the
        // confirm dialog's "targeted delete" wording and a DELETE against the whole collection.
        Assert.Equal("DELETE /data/VendorsV2", vm.RequestUrl);
        Assert.True(vm.HasPayloadIssues);
        Assert.False(vm.SendCommand.CanExecute(null));
    }

    // --- LoadError banner surfaces catalogue/field load failures in BOTH modes (issue #168) ---

    // Fails the catalogue load itself (token acquisition, unreachable endpoint, SQLite I/O — whatever the
    // real service's failure mode is), the way Initialize would encounter it.
    private sealed class FailingCatalogueMetadata : IMetadataService
    {
        public IReadOnlyList<EntitySet> GetEntities() => Array.Empty<EntitySet>();
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("catalogue endpoint unreachable");
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    // Entities load fine, but the selected entity's own field fetch fails — the EnsureFields path rather
    // than Initialize's catalogue load.
    private sealed class FailingFieldMetadata : IMetadataService
    {
        private static readonly IReadOnlyList<EntitySet> Sets =
            new[] { new EntitySet("Widgets", "SYS", 1, "Id", false, "system") };

        public IReadOnlyList<EntitySet> GetEntities() => Sets;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null; // never cached
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            throw new InvalidOperationException("field fetch failed");
    }

    [Fact]
    public async Task Raw_mode_surfaces_a_catalogue_load_failure_via_the_load_error_banner()
    {
        // Before the fix, raw mode had no signal at all for an Initialize failure — the picker (and its
        // issue panel) is hidden there, so the load error went nowhere.
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: new FailingCatalogueMetadata());
        Assert.False(vm.UseFieldGrid);

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.NotNull(vm.LoadError);
        Assert.Contains("catalogue endpoint unreachable", vm.LoadError);
        Assert.Equal(string.Empty, vm.PayloadIssues); // kept out of the payload-issues plumbing
    }

    [Fact]
    public async Task Grid_mode_also_surfaces_the_same_catalogue_load_failure_via_the_banner()
    {
        // Grid mode already names the load failure as the cause inside its own block message (BlockPayload);
        // the banner is an independent, additional signal — both should be true at once.
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: new FailingCatalogueMetadata())
        {
            UseFieldGrid = true,
        };

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.NotNull(vm.LoadError);
        Assert.Contains("catalogue endpoint unreachable", vm.LoadError);
    }

    [Fact]
    public async Task Field_fetch_failure_also_feeds_the_load_error_banner()
    {
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: new FailingFieldMetadata())
        {
            Method = "PATCH",
        };
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "Widgets");
        vm.UseFieldGrid = true; // auto-triggers a (failing) field fetch via ReloadGrid

        await vm.EnsureFieldsCommand.ExecuteAsync(null); // deterministic re-run of the already-failed fetch

        Assert.NotNull(vm.LoadError);
        Assert.Contains("field fetch failed", vm.LoadError);
    }

    [Fact]
    public async Task Initialize_loads_the_catalogue_so_grid_mode_becomes_usable()
    {
        var meta = new DeferredMetadata("LateEntity");
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: meta) { Method = "PATCH" };
        // Nothing is cached at construction, so the ctor snapshot alone leaves the picker empty all session.
        Assert.Empty(vm.Entities);

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "LateEntity" }, vm.Entities.Select(e => e.Name));
        Assert.Equal(new[] { "LateEntity" }, vm.FilteredEntities.Select(e => e.Name));
        Assert.Null(vm.SelectedEntity); // grid mode stays opt-in: the raw path/body are untouched

        vm.UseFieldGrid = true; // auto-selects the loaded entity, whose fields arrive via the loader

        Assert.Equal("LateEntity", vm.SelectedEntity!.Name);
        Assert.Equal(1, meta.FieldLoads); // fetched, not pre-seeded
        Assert.Equal(new[] { "Id", "Name" }, vm.Fields.Select(f => f.Name));

        vm.Fields.Single(f => f.Name == "Id").Value = "A1";
        // Id is the key, so it lives in the predicate rather than the body — a PATCH needs one real field in
        // the body too, or it's the no-op #158 now blocks.
        var name = vm.Fields.Single(f => f.Name == "Name");
        name.Include = true;
        name.Value = "Late";

        Assert.False(vm.HasPayloadIssues);
        Assert.True(vm.SendCommand.CanExecute(null));
        Assert.Equal("PATCH /data/LateEntity(Id=%27A1%27)", vm.RequestUrl);
    }

    [Fact]
    public async Task Re_initializing_refreshes_the_catalogue_and_keeps_the_selection_by_name()
    {
        var meta = new DeferredMetadata("Alpha", "Beta");
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: meta) { Method = "PATCH" };
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "Beta");
        Assert.NotEmpty(vm.Fields);

        // An environment switch republishes a different catalogue; Initialize runs on every Loaded, so the
        // list refreshes — and Beta stays selected because it exists in the new environment too.
        meta.SwitchCatalogue("Beta", "Gamma");
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Beta", "Gamma" }, vm.Entities.Select(e => e.Name));
        Assert.Equal("Beta", vm.SelectedEntity!.Name);
        Assert.NotEmpty(vm.Fields);

        // …and when the selection is gone from the new environment, grid mode falls back to the first entity
        // (it can't sit on an entity the environment doesn't have) and loads its fields.
        meta.SwitchCatalogue("Gamma", "Delta");
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal("Gamma", vm.SelectedEntity!.Name);
        Assert.Equal(new[] { "Id", "Name" }, vm.Fields.Select(f => f.Name));
        Assert.Equal("/data/Gamma", vm.Path);
    }

    [Fact]
    public void Fake_metadata_exposes_enum_members()
    {
        var meta = new FakeMetadataService();

        Assert.Equal(new[] { "No", "Yes" }, meta.GetEnumMembers("NoYes"));
        Assert.Equal(new[] { "No", "Yes" }, meta.GetEnumMembers("noyes")); // case-insensitive, like the real service
        Assert.Null(meta.GetEnumMembers("NotAnEnum"));
    }

    [Fact]
    public void Post_field_row_bool_value_round_trips_to_the_string_value()
    {
        var row = new PostFieldRow("B", "Boolean", mandatory: false, isKey: false, include: true,
            PostFieldEditor.Bool, Array.Empty<string>());

        row.BoolValue = true;
        Assert.Equal("true", row.Value);
        row.BoolValue = false;
        Assert.Equal("false", row.Value);
        row.BoolValue = null;
        Assert.Equal(string.Empty, row.Value);

        row.Value = "true"; // external (payload-rebuild) writes flow back to the checkbox
        Assert.True(row.BoolValue);
    }

    [Fact]
    public void Grid_rows_get_the_right_editor_kind_and_enum_members()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        var enumRow = vm.Fields.Single(f => f.Name == "IsOneTime"); // Enum<NoYes>
        Assert.Equal(PostFieldEditor.Enum, enumRow.Editor);
        Assert.Equal(new[] { "No", "Yes" }, enumRow.EnumMembers);

        var textRow = vm.Fields.Single(f => f.Name == "OrganizationName"); // String
        Assert.Equal(PostFieldEditor.Text, textRow.Editor);
        Assert.Empty(textRow.EnumMembers);
    }

    [Fact]
    public void Mapper_treats_keys_and_non_nullable_fields_as_mandatory()
    {
        Assert.True(PostPayloadMapper.ToProperty(new EntityField("k", "String", Nullable: true, IsKey: true)).Mandatory);
        Assert.True(PostPayloadMapper.ToProperty(new EntityField("n", "String", Nullable: false)).Mandatory);
        Assert.False(PostPayloadMapper.ToProperty(new EntityField("o", "String", Nullable: true)).Mandatory);
    }

    [Fact]
    public void Entering_grid_mode_builds_the_field_set_once()
    {
        var meta = new CountingMetadata();
        var vm = new PostBuilderViewModel(new FakeODataClient(), metadata: meta);
        vm.Method = "PATCH";

        vm.UseFieldGrid = true; // auto-selects the first entity and builds the grid + payload

        Assert.NotEmpty(vm.Fields);
        // LoadFields + RebuildPayload, once each — not twice (no double-build when entering grid mode).
        Assert.Equal(2, meta.GetFieldsCalls);
    }

    [Fact]
    public void Entity_search_excluding_the_selection_keeps_it_pinned_and_preserves_the_grid()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.NotEmpty(vm.Fields);

        vm.EntitySearch = "order"; // CustomersV3 does NOT contain "order"

        // The active selection stays pinned in the filtered list so the bound ComboBox can't null it
        // (which would otherwise wipe the field grid); selection + fields survive the search.
        Assert.Contains(vm.FilteredEntities, e => e.Name == "CustomersV3");
        Assert.Equal("CustomersV3", vm.SelectedEntity!.Name);
        Assert.NotEmpty(vm.Fields);
    }

    // --- Confirm-on-mutation + behavioural refinements ---

    // Records the ConfirmRequest and returns a fixed decision (confirm/cancel).
    private sealed class CapturingDialogs : IDialogService
    {
        private readonly bool _confirm;
        public ConfirmRequest? Last { get; private set; }
        public CapturingDialogs(bool confirm) => _confirm = confirm;
        public Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            Last = request;
            return Task.FromResult(_confirm);
        }
    }

    [Fact]
    public async Task Send_is_blocked_when_the_confirm_dialog_is_declined()
    {
        var recorder = new RecordingODataClient();
        var dialogs = new CapturingDialogs(confirm: false);
        var vm = new PostBuilderViewModel(recorder, dialogs: dialogs) { Method = "DELETE", Path = "/data/E(1)" };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Null(recorder.LastPath); // the gateway is never called when the user cancels
        Assert.Contains("cancelled", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(dialogs.Last); // the confirm was actually presented
    }

    [Fact]
    public async Task Confirmed_send_proceeds_to_the_client()
    {
        var recorder = new RecordingODataClient();
        var vm = new PostBuilderViewModel(recorder, dialogs: new CapturingDialogs(confirm: true))
        { Method = "DELETE", Path = "/data/E(1)" };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("/data/E(1)", recorder.LastPath);
    }

    [Fact]
    public async Task Destructive_methods_confirm_with_a_danger_caveat()
    {
        var dialogs = new CapturingDialogs(confirm: false);
        var vm = new PostBuilderViewModel(new RecordingODataClient(), dialogs: dialogs)
        { Method = "DELETE", Path = "/data/E(1)" };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(dialogs.Last!.IsDanger);
        Assert.Contains("permanent", dialogs.Last!.Caveat, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Send DELETE", dialogs.Last!.ConfirmLabel);
    }

    [Fact]
    public void Switching_to_a_keyed_method_auto_enables_if_match()
    {
        var vm = MakeVm(); // POST by default
        Assert.False(vm.UseIfMatch);

        vm.Method = "PATCH";
        Assert.True(vm.UseIfMatch); // PATCH/DELETE default to optimistic concurrency

        vm.Method = "POST";
        Assert.False(vm.UseIfMatch); // a create has no If-Match
    }

    [Fact]
    public void Grid_seeds_the_company_code_default()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.Equal("USMF", vm.Fields.Single(f => f.Name == "dataAreaId").Value);
    }

    [Fact]
    public void Included_field_count_and_summary_track_the_grid()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        var before = vm.IncludedFieldCount;
        vm.Fields.Single(f => f.Name == "OrganizationName").Include = true;

        Assert.Equal(before + 1, vm.IncludedFieldCount);
        Assert.Contains("CustomersV3", vm.GridSummary);
        Assert.Contains(vm.IncludedFieldCount.ToString(), vm.GridSummary);
    }

    // --- OData key literals are percent-encoded (issue #157) ---

    // The part of the URL between "PersonnelNumber=" and the closing ")" — i.e. the key literal itself.
    private static string KeyLiteral(string requestUrl)
    {
        const string marker = "PersonnelNumber=";
        var start = requestUrl.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return requestUrl[start..requestUrl.LastIndexOf(')')];
    }

    [Theory]
    [InlineData("1000/A")]  // '/' became an extra path segment → 404 against a URL the user never typed
    [InlineData("A#1")]     // '#' started the URI *fragment* → request truncated, query string dropped
    [InlineData("A?1")]     // '?' started the query string
    [InlineData("50%")]     // '%' opened a broken escape sequence
    [InlineData("O'Brien")] // the OData '' escape, which has to survive encoding
    [InlineData("A 1")]     // a space
    public void Key_values_carrying_url_syntax_are_percent_encoded_in_the_predicate(string keyValue)
    {
        var vm = KeyGrid(keyValue);
        vm.CrossCompany = true;

        var literal = KeyLiteral(vm.RequestUrl);

        // No URL-significant character survives as syntax inside the key literal…
        Assert.DoesNotContain("#", literal, StringComparison.Ordinal);
        Assert.DoesNotContain("?", literal, StringComparison.Ordinal);
        Assert.DoesNotContain("/", literal, StringComparison.Ordinal);
        Assert.DoesNotContain("'", literal, StringComparison.Ordinal);
        // …and the query option the '#' case used to swallow whole is still on the request.
        Assert.EndsWith("?cross-company=true", vm.RequestUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hash_in_a_key_no_longer_truncates_the_request_at_the_uri_fragment()
    {
        var vm = KeyGrid("A#1");
        vm.CrossCompany = true;

        // The path is interpolated into the request URL and handed to new Uri(...) by
        // CoreODataClient.BuildUri, so a raw '#' began the fragment: everything after it left the request —
        // including "?cross-company=true" — and the key that reached F&O was "A", not "A#1". No error was
        // raised anywhere; a cross-company write silently became a single-company one against a wrong key.
        var path = vm.RequestUrl["PATCH ".Length..];
        var uri = new Uri("https://host.example" + path);

        Assert.Equal(string.Empty, uri.Fragment);
        Assert.Equal("?cross-company=true", uri.Query);
        Assert.Contains("%23", uri.AbsolutePath, StringComparison.Ordinal); // the '#' is data, not syntax
    }

    [Fact]
    public void A_slash_in_a_key_stays_inside_the_key_segment()
    {
        var vm = KeyGrid("1000/A");

        var uri = new Uri("https://host.example" + vm.RequestUrl["PATCH ".Length..]);

        // "/data/WorkerV2(...)" and nothing more: raw, "1000/A" added a segment and the request 404'd.
        Assert.Equal(3, uri.Segments.Length);
        Assert.EndsWith(")", uri.Segments[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_key_stays_readable_with_only_the_quotes_encoded()
    {
        // Encoding is not allowed to turn an ordinary predicate into noise — %27 delimiters, value verbatim.
        Assert.Contains("/data/WorkerV2(PersonnelNumber=%27000123%27)", KeyGrid("000123").RequestUrl,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_string_key_is_left_raw()
    {
        // Digits, GUIDs and booleans carry no URL syntax, and OData wants them unquoted — encoding them
        // would only make the predicate harder to read.
        Assert.Contains("/data/Counters(Id=7)", CounterGrid("PATCH").RequestUrl, StringComparison.Ordinal);
    }

    // --- An empty PATCH is blocked; blank-but-included clears the field (issue #158) ---

    [Fact]
    public void Patch_with_only_the_keys_filled_is_blocked_as_an_empty_body()
    {
        var vm = CounterGrid("PATCH"); // Id (key, excluded from the body); Label nullable → not pre-included

        // Every included field is a key, and keys live in the URL predicate — so the body was "{}", F&O
        // answered 204 and the badge went green over a request that changed nothing.
        Assert.Equal("{}", vm.RequestBody);
        Assert.True(vm.HasPayloadIssues);
        Assert.Contains("empty body", vm.PayloadIssues, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.SendCommand.CanExecute(null));

        // Including a real field unblocks it.
        var label = vm.Fields.Single(f => f.Name == "Label");
        label.Include = true;
        label.Value = "counted";

        Assert.False(vm.HasPayloadIssues);
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void A_blank_included_nullable_field_on_patch_clears_it_and_the_preview_shows_the_null()
    {
        var vm = CounterGrid("PATCH");
        var label = vm.Fields.Single(f => f.Name == "Label");

        label.Include = true; // included and blank → clear the field

        Assert.Contains("\"Label\": null", vm.RequestBody, StringComparison.Ordinal);
        Assert.False(vm.HasPayloadIssues); // the body isn't empty any more, so the no-op guard stands down
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Unchecking_a_field_on_patch_omits_it_rather_than_clearing_it()
    {
        var vm = CounterGrid("PATCH");
        var label = vm.Fields.Single(f => f.Name == "Label");
        label.Include = true;
        Assert.Contains("Label", vm.RequestBody, StringComparison.Ordinal);

        label.Include = false; // the other half of the distinction: leave this field alone

        Assert.DoesNotContain("Label", vm.RequestBody, StringComparison.Ordinal);
        Assert.Contains("empty body", vm.PayloadIssues, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_literal_text_null_still_sends_json_null_on_patch()
    {
        var vm = CounterGrid("PATCH");
        var label = vm.Fields.Single(f => f.Name == "Label");
        label.Include = true;
        label.Value = "null";

        Assert.Contains("\"Label\": null", vm.RequestBody, StringComparison.Ordinal);
        Assert.False(vm.HasPayloadIssues);
    }

    [Fact]
    public void A_blank_included_non_nullable_field_on_patch_is_flagged_rather_than_dropped()
    {
        var vm = MakeGridVm();
        vm.Method = "PATCH";
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1"; // dataAreaId is seeded

        // CurrencyCode is non-nullable and pre-included; blank, it can be neither written nor cleared, so
        // the user is told which of the two to do instead of the value being silently dropped.
        Assert.True(vm.HasPayloadIssues);
        Assert.Contains("CurrencyCode", vm.PayloadIssues, StringComparison.Ordinal);
        Assert.Contains("isn't nullable", vm.PayloadIssues, StringComparison.Ordinal);
        Assert.False(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void A_blank_included_field_on_post_is_still_omitted()
    {
        // POST is unchanged: an absent property lets the service apply its own default, so a blank included
        // field is an omission there — not a request to write null.
        var vm = CounterGrid("POST");
        vm.Fields.Single(f => f.Name == "Label").Include = true;

        Assert.False(vm.HasPayloadIssues);
        Assert.DoesNotContain("Label", vm.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("null", vm.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_is_exempt_from_both_new_body_rules()
    {
        // The send path drops the body for DELETE outright, so there is nothing to clear and no no-op to
        // block — a keyed DELETE whose grid carries only the key must stay sendable.
        var vm = CounterGrid("DELETE");
        vm.Fields.Single(f => f.Name == "Label").Include = true; // included and blank

        Assert.False(vm.HasPayloadIssues);
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task Delete_sends_no_body_at_all()
    {
        // The premise of the DELETE exemption above, asserted rather than assumed.
        var recorder = new BodyRecordingClient();
        var vm = new PostBuilderViewModel(recorder) { Method = "DELETE", Path = "/data/E(1)" };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Null(recorder.LastBody);
    }

    private sealed class BodyRecordingClient : IODataClient
    {
        public string? LastBody { get; private set; }
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            LastBody = body;
            return Task.FromResult(new ODataResponse(204, "No Content", string.Empty, 1));
        }
    }

    // --- Failing clipboard (#163) ---
    // A contended clipboard throws (COMException on Windows) and the generated AsyncRelayCommand rethrows
    // the faulted task on the dispatcher, killing the app; a failed copy must end as a status line.

    private sealed class ThrowingClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => throw new InvalidOperationException("clipboard is busy");
    }

    [Fact]
    public async Task Copy_url_survives_a_failing_clipboard()
    {
        var vm = new PostBuilderViewModel(new FakeODataClient(), new ThrowingClipboard()) { Path = "/data/E" };

        await vm.CopyUrlCommand.ExecuteAsync(null); // awaiting proves the command task completed, not faulted

        Assert.Contains("clipboard", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clipboard is busy", vm.StatusText);
    }

    [Fact]
    public async Task Copy_payload_survives_a_failing_clipboard()
    {
        var vm = new PostBuilderViewModel(new FakeODataClient(), new ThrowingClipboard()) { Path = "/data/E" };

        await vm.CopyPayloadCommand.ExecuteAsync(null);

        Assert.Contains("clipboard", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clipboard is busy", vm.StatusText);
    }

    // --- A cancelled send is reported as cancelled, not a failure (issue #168) ---
    //
    // CoreODataClient doesn't yet rethrow genuine cancellation on main (that lands separately, alongside
    // the Query Builder's own cancellation fix) — it currently folds an OperationCanceledException into a
    // "Request failed" response like any other exception. These tests drive the OCE through the
    // IODataClient test seam directly, so they're valid regardless of merge order: once CoreODataClient
    // rethrows for real, the same catch clause covers the end-to-end path too.

    // Holds the request open, then honours the caller's token the way CoreODataClient will once it
    // rethrows genuine cancellation instead of folding it into a response — mirrors the Query Builder's
    // own CancellableGatedODataClient test seam.
    private sealed class GatedSendClient : IODataClient
    {
        public readonly TaskCompletionSource Gate = new();

        public async Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            await Gate.Task;
            ct.ThrowIfCancellationRequested();
            return new ODataResponse(204, "No Content", string.Empty, 5);
        }
    }

    // An OperationCanceledException that arrives with OUR token still live — the shape an HTTP/socket
    // timeout takes. Must NOT be reported as "Send cancelled." (that phrase means the user pressed
    // Cancel); it's a genuine failure and has to fall through to the general handler.
    private sealed class TimeoutShapedODataClient : IODataClient
    {
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
            => throw new OperationCanceledException("socket timeout");
    }

    [Fact]
    public async Task Cancelling_a_send_reports_cancellation_not_a_request_failure()
    {
        // Red-check: before the fix, Send's bare `catch (Exception ex)` reported this as
        // "Request failed." — a lie, since nothing about the request failed; the user asked to stop it.
        var client = new GatedSendClient();
        var vm = new PostBuilderViewModel(client) { Method = "POST" };

        var send = vm.SendCommand.ExecuteAsync(null);
        vm.SendCancelCommand.Execute(null); // the generated cancel command — now actually bound in the view
        client.Gate.SetResult();
        await send;

        Assert.Equal("Send cancelled.", vm.StatusText); // not "Request failed."
        Assert.False(vm.SendSucceeded);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_timeout_shaped_cancellation_still_reports_as_a_failure()
    {
        // Guard: an OperationCanceledException whose token was never cancelled (a timeout, not a user
        // Cancel) must not be swallowed by the new "Send cancelled." branch — it's a real failure.
        var vm = new PostBuilderViewModel(new TimeoutShapedODataClient()) { Method = "POST" };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("Request failed.", vm.StatusText);
        Assert.False(vm.SendSucceeded);
        Assert.Contains("socket timeout", vm.ResponseBody);
    }
}
