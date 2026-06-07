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

        // PATCH relaxes mandatory enforcement, so the same selection becomes valid.
        vm.Method = "PATCH";
        Assert.False(vm.HasPayloadIssues);
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

        vm.Method = "PATCH";                            // PATCH relaxes mandatory → valid again

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
}
