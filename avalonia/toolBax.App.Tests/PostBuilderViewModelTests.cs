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

        Assert.Contains("/data/CustomersV3(dataAreaId='USMF',CustomerAccount='US-001')", vm.RequestUrl);
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
        vm.Fields.Single(f => f.Name == "dataAreaId").Value = "USMF";
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";
        var org = vm.Fields.Single(f => f.Name == "OrganizationName");
        org.Include = true;
        org.Value = "Acme";

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

        Assert.Equal("/data/CustomersV3(dataAreaId='USMF',CustomerAccount='US-1')", recorder.LastPath);
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
    [InlineData("String", "Edm.String")]
    [InlineData("Enum", "Edm.String")] // enum members are sent as JSON strings (member-name)
    public void Friendly_types_map_to_edm(string friendly, string edm) =>
        Assert.Equal(edm, PostPayloadMapper.ToEdmType(friendly));

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

        var org = vm.Fields.Single(f => f.Name == "OrganizationName");
        org.Include = true;
        org.Value = "Acme";
        Assert.Contains("OrganizationName", vm.RequestBody);

        org.Include = false;
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

        // PATCH relaxes mandatory enforcement; with the key values supplied (to target the record) the
        // same selection becomes valid.
        vm.Method = "PATCH";
        vm.Fields.Single(f => f.Name == "dataAreaId").Value = "USMF";
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";
        Assert.False(vm.HasPayloadIssues);
    }

    [Fact]
    public void Keyed_write_with_an_incomplete_key_is_flagged_and_send_is_blocked()
    {
        var vm = MakeGridVm();
        vm.UseFieldGrid = true;
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Method = "PATCH";
        vm.Fields.Single(f => f.Name == "dataAreaId").Value = "USMF"; // CustomerAccount still blank

        // The record is identified by neither a complete URL predicate nor the (key-excluded) body,
        // so the keyed write is flagged and Send is disabled until every key value is present.
        Assert.True(vm.HasPayloadIssues);
        Assert.Contains("key", vm.PayloadIssues, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.SendCommand.CanExecute(null));

        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";
        Assert.False(vm.HasPayloadIssues);
        Assert.True(vm.SendCommand.CanExecute(null));
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
        vm.Fields.Single(f => f.Name == "dataAreaId").Value = "USMF";       // …and the keys target the record
        vm.Fields.Single(f => f.Name == "CustomerAccount").Value = "US-1";

        Assert.False(vm.HasPayloadIssues);
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Raw_mode_send_is_never_blocked_by_payload_issues()
    {
        var vm = MakeVm(); // raw mode (no field grid)

        Assert.True(vm.SendCommand.CanExecute(null));
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
}
