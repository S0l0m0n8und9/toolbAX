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

public class MetadataViewModelTests
{
    private static MetadataViewModel MakeVm() => new(new FakeMetadataService());

    // Mimics the real service: nothing is available until LoadEntitiesAsync runs, then one entity with
    // fields appears — so InitializeAsync (not the ctor) is what populates the browser.
    private sealed class DeferredMetadata : IMetadataService
    {
        private bool _loaded;
        private static readonly EntitySet[] Late = { new("LateEntity", "M", 1, "k", false, "odata") };
        private static readonly EntityField[] LateFields = { new("Id", "String", false, IsKey: true, Length: 10) };

        public IReadOnlyList<EntitySet> GetEntities() => _loaded ? Late : Array.Empty<EntitySet>();
        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            _loaded && entityName == "LateEntity" ? LateFields : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) { _loaded = true; return Task.CompletedTask; }
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        { _loaded = true; return Task.FromResult(entityName == "LateEntity"); }
    }

    [Fact]
    public async Task Initialize_loads_entities_and_fields_unavailable_at_construction()
    {
        var vm = new MetadataViewModel(new DeferredMetadata());
        Assert.Empty(vm.Entities); // nothing seeded at ctor — the real service starts empty

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Contains(vm.Entities, e => e.Name == "LateEntity");
        Assert.True(vm.IsCached);           // the first entity's fields were auto-fetched
        Assert.NotEmpty(vm.Fields);
    }

    // Mimics a live failure (token denied / OData unreachable) so the fetch can't silently no-op.
    private sealed class ThrowingMetadata : IMetadataService
    {
        public IReadOnlyList<EntitySet> GetEntities() => Array.Empty<EntitySet>();
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("metadata endpoint unreachable"));
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            Task.FromException<bool>(new InvalidOperationException("metadata endpoint unreachable"));
    }

    [Fact]
    public async Task Initialize_surfaces_a_load_failure_as_LoadError()
    {
        var vm = new MetadataViewModel(new ThrowingMetadata());

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Contains("unreachable", vm.LoadError);
        Assert.Empty(vm.Entities);
    }

    // Records the forceRefresh flag of every load, so Refresh can be shown to bypass the caches rather
    // than just re-reading them.
    private sealed class RecordingMetadata : IMetadataService
    {
        private static readonly EntitySet[] All =
        {
            new("Alpha", "M", 1, "k", false, "odata"),
            new("Beta", "M", 1, "k", false, "odata"),
        };
        private static readonly EntityField[] Props = { new("Id", "String", false, IsKey: true, Length: 10) };

        public List<bool> EntityLoads { get; } = new();
        public List<(string Entity, bool Force)> FieldLoads { get; } = new();

        public IReadOnlyList<EntitySet> GetEntities() => All;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => Props;

        public Task LoadEntitiesAsync(CancellationToken ct = default) => LoadEntitiesAsync(false, ct);

        public Task LoadEntitiesAsync(bool forceRefresh, CancellationToken ct = default)
        {
            EntityLoads.Add(forceRefresh);
            return Task.CompletedTask;
        }

        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            LoadFieldsAsync(entityName, false, ct);

        public Task<bool> LoadFieldsAsync(string entityName, bool forceRefresh, CancellationToken ct = default)
        {
            FieldLoads.Add((entityName, forceRefresh));
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Refresh_forces_a_reload_of_the_entity_list_and_the_selected_fields()
    {
        var metadata = new RecordingMetadata();
        var vm = new MetadataViewModel(metadata);
        vm.Selected = vm.Entities.Single(e => e.Name == "Beta");

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(true, metadata.EntityLoads);
        Assert.Contains(metadata.FieldLoads, l => l.Entity == "Beta" && l.Force);
        Assert.Equal("Beta", vm.Selected?.Name);   // the selection survives the reload
        Assert.Null(vm.LoadError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Refresh_reloads_through_the_service_and_clears_busy()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.IsBusy);
        Assert.True(vm.RefreshCommand.CanExecute(null));   // re-runnable once it finishes
        Assert.Equal("CustomersV3", vm.Selected?.Name);
        Assert.True(vm.IsCached);
        Assert.NotEmpty(vm.Fields);
    }

    // Serves a catalogue until a forced refresh, after which the environment legitimately has no entities
    // (a profile repointed at an environment without OData metadata, or a cache emptied by the switch).
    private sealed class EmptyingMetadata : IMetadataService
    {
        private static readonly EntitySet[] Initial = { new("Alpha", "M", 1, "k", false, "odata") };
        private static readonly EntityField[] Props = { new("Id", "String", false, IsKey: true, Length: 10) };
        private bool _emptied;

        public IReadOnlyList<EntitySet> GetEntities() => _emptied ? Array.Empty<EntitySet>() : Initial;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => _emptied ? null : Props;

        public Task LoadEntitiesAsync(CancellationToken ct = default) => LoadEntitiesAsync(false, ct);

        public Task LoadEntitiesAsync(bool forceRefresh, CancellationToken ct = default)
        {
            if (forceRefresh)
            {
                _emptied = true;
            }

            return Task.CompletedTask;
        }

        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            LoadFieldsAsync(entityName, false, ct);

        public Task<bool> LoadFieldsAsync(string entityName, bool forceRefresh, CancellationToken ct = default) =>
            Task.FromResult(!_emptied);
    }

    // A refresh that succeeds but returns nothing has to empty the browser: keeping the previous list
    // while LoadError is cleared shows another environment's entities as if they were current.
    [Fact]
    public async Task Refresh_returning_no_entities_empties_the_list()
    {
        var vm = new MetadataViewModel(new EmptyingMetadata());
        Assert.Single(vm.Entities);   // the pre-refresh catalogue

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(vm.Entities);
        Assert.Empty(vm.Filtered);
        Assert.Null(vm.Selected);
        Assert.Empty(vm.Fields);
        Assert.False(vm.IsCached);
        Assert.Null(vm.LoadError);   // an empty result is not a failure
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Refresh_surfaces_a_failure_as_LoadError_and_clears_busy()
    {
        var vm = new MetadataViewModel(new ThrowingMetadata());

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("unreachable", vm.LoadError);
        Assert.False(vm.IsBusy);
    }

    // Holds a field fetch open so the in-flight state is observable. The real service is a live $metadata
    // read against a document that can be tens of MB, which is the whole reason the pane needs an indicator.
    private sealed class BlockingFieldsMetadata : IMetadataService
    {
        private static readonly EntitySet[] All =
        {
            new("Alpha", "M", 1, "k", false, "odata"),
            new("Beta", "M", 1, "k", false, "odata"),
        };
        private static readonly EntityField[] Props = { new("Id", "String", false, IsKey: true, Length: 10) };
        private readonly HashSet<string> _loaded = new();

        /// <summary>Completed by the test to let the pending fetch finish (or fail).</summary>
        public TaskCompletionSource<bool> Gate { get; } = new();

        public IReadOnlyList<EntitySet> GetEntities() => All;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => _loaded.Contains(entityName) ? Props : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        {
            await Gate.Task;
            _loaded.Add(entityName);
            return true;
        }
    }

    [Fact]
    public async Task Fetching_an_uncached_entitys_fields_shows_a_loading_indicator_until_it_completes()
    {
        var metadata = new BlockingFieldsMetadata();
        var vm = new MetadataViewModel(metadata);
        Assert.False(vm.IsLoadingFields);
        Assert.True(vm.ShowNotCachedHint);

        var fetch = vm.LoadSelectedFieldsCommand.ExecuteAsync(null);

        Assert.True(vm.IsLoadingFields);
        Assert.Equal("Loading Alpha…", vm.FieldsLoadingMessage);
        // "open it in Query Builder" is wrong advice while the fetch that would populate them is running.
        Assert.False(vm.ShowNotCachedHint);

        metadata.Gate.SetResult(true);
        await fetch;

        Assert.False(vm.IsLoadingFields);
        Assert.True(vm.IsCached);
        Assert.NotEmpty(vm.Fields);
        Assert.False(vm.ShowNotCachedHint);
    }

    [Fact]
    public async Task A_failed_fields_fetch_clears_the_indicator_and_restores_the_not_cached_hint()
    {
        var metadata = new BlockingFieldsMetadata();
        var vm = new MetadataViewModel(metadata);

        var fetch = vm.LoadSelectedFieldsCommand.ExecuteAsync(null);
        Assert.True(vm.IsLoadingFields);

        metadata.Gate.SetException(new InvalidOperationException("metadata endpoint unreachable"));
        await fetch;

        Assert.False(vm.IsLoadingFields);   // never left spinning on a failure
        Assert.False(vm.IsCached);
        Assert.True(vm.ShowNotCachedHint);  // now the hint is the right advice
        Assert.Contains("unreachable", vm.LoadError);
    }

    // Per-entity gates, so a test can hold one entity's fetch open, switch selection, then release — the
    // mid-flight entity change the browser has to survive. LoadFieldsAsync honours the cancellation token
    // like the real service, so a superseded fetch actually unwinds instead of hanging.
    private sealed class GatedFieldsMetadata : IMetadataService
    {
        private static readonly EntitySet[] All =
        {
            new("Alpha", "M", 1, "k", false, "odata"),
            new("Beta", "M", 1, "k", false, "odata"),
        };
        private static readonly EntityField[] Props = { new("Id", "String", false, IsKey: true, Length: 10) };
        private readonly Dictionary<string, TaskCompletionSource<bool>> _gates = new();
        private readonly HashSet<string> _loaded = new();
        // A superseded fetch unwinds on a pool thread while the new selection runs on the test thread, so
        // the recorded state is guarded rather than left to chance.
        private readonly object _sync = new();

        /// <summary>Every entity a fetch was actually started for, in order.</summary>
        public List<string> FieldLoads { get; } = new();

        public TaskCompletionSource<bool> Gate(string entityName)
        {
            lock (_sync)
            {
                if (!_gates.TryGetValue(entityName, out var gate))
                {
                    _gates[entityName] = gate = new TaskCompletionSource<bool>();
                }

                return gate;
            }
        }

        /// <summary>Pre-caches an entity so selecting it needs no fetch.</summary>
        public void MarkCached(string entityName)
        {
            lock (_sync)
            {
                _loaded.Add(entityName);
            }
        }

        public IReadOnlyList<EntitySet> GetEntities() => All;

        public IReadOnlyList<EntityField>? GetFields(string entityName)
        {
            lock (_sync)
            {
                return _loaded.Contains(entityName) ? Props : null;
            }
        }

        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        {
            lock (_sync)
            {
                FieldLoads.Add(entityName);
            }

            await Gate(entityName).Task.WaitAsync(ct);
            MarkCached(entityName);
            return true;
        }

        public IReadOnlyList<string> StartedFetches()
        {
            lock (_sync)
            {
                return FieldLoads.ToList();
            }
        }
    }

    // Awaits whatever field-fetch executions are pending. Selection changes start the command
    // fire-and-forget, so ExecutionTask is the only handle a test has on them.
    private static async Task DrainFieldFetchesAsync(MetadataViewModel vm)
    {
        for (var i = 0; i < 5 && vm.LoadSelectedFieldsCommand.ExecutionTask is { IsCompleted: false } running; i++)
        {
            await running;
        }
    }

    [Fact]
    public void Changing_entity_mid_fetch_starts_the_new_entitys_fetch()
    {
        // The load command is fired fire-and-forget on every selection change. If it refused to re-enter
        // while the previous entity's fetch was still running, the click would be silently swallowed — and
        // the indicator's ownership handover (below) would have nothing to hand over to.
        var metadata = new GatedFieldsMetadata();
        var vm = new MetadataViewModel(metadata);

        vm.LoadSelectedFieldsCommand.Execute(null);
        Assert.Equal(new[] { "Alpha" }, metadata.StartedFetches());

        vm.Selected = vm.Entities.Single(e => e.Name == "Beta");

        Assert.Contains("Beta", metadata.StartedFetches());
    }

    [Fact]
    public async Task Changing_entity_mid_fetch_still_loads_the_new_entity_and_clears_the_indicator()
    {
        var metadata = new GatedFieldsMetadata();
        var vm = new MetadataViewModel(metadata);   // Alpha selected, fields not cached

        vm.LoadSelectedFieldsCommand.Execute(null);   // fire-and-forget, exactly as the view does
        var supersededFetch = vm.LoadSelectedFieldsCommand.ExecutionTask!;
        Assert.True(vm.IsLoadingFields);

        // The user clicks Beta while Alpha's fetch is still in flight.
        vm.Selected = vm.Entities.Single(e => e.Name == "Beta");

        // Open both gates before waiting on anything: the superseded fetch may already have unwound via
        // cancellation, and the new selection's fetch is only reachable through ExecutionTask.
        metadata.Gate("Alpha").TrySetResult(true);
        metadata.Gate("Beta").TrySetResult(true);
        await supersededFetch;
        await DrainFieldFetchesAsync(vm);

        // The click must not be dropped, and nothing may be left spinning once the dust settles.
        Assert.Contains("Beta", metadata.StartedFetches());
        Assert.False(vm.IsLoadingFields);
        Assert.True(vm.IsCached);
        Assert.NotEmpty(vm.Fields);
    }

    [Fact]
    public async Task Clearing_the_selection_mid_fetch_cannot_leave_the_indicator_stuck()
    {
        // What Refresh does when the environment comes back with no entities at all (a profile repointed at
        // an environment without OData metadata): the selection is cleared. The replacement execution then
        // bails out before it can take over the indicator, so the fetch already in flight is the only one
        // left that can lower it — and it must, even though "its" entity is no longer selected.
        var metadata = new GatedFieldsMetadata();
        var vm = new MetadataViewModel(metadata);

        vm.LoadSelectedFieldsCommand.Execute(null);
        var supersededFetch = vm.LoadSelectedFieldsCommand.ExecutionTask!;
        Assert.True(vm.IsLoadingFields);

        vm.Selected = null;

        metadata.Gate("Alpha").TrySetResult(true);
        await supersededFetch;
        await DrainFieldFetchesAsync(vm);

        Assert.False(vm.IsLoadingFields);
    }

    [Fact]
    public async Task Changing_to_a_cached_entity_mid_fetch_clears_the_indicator()
    {
        // The new selection needs no fetch of its own, so there is no later slow completion to piggyback
        // the clear onto — the superseded fetch must not leave the pane spinning on its way out.
        var metadata = new GatedFieldsMetadata();
        metadata.MarkCached("Beta");
        var vm = new MetadataViewModel(metadata);

        vm.LoadSelectedFieldsCommand.Execute(null);
        var supersededFetch = vm.LoadSelectedFieldsCommand.ExecutionTask!;
        Assert.True(vm.IsLoadingFields);

        vm.Selected = vm.Entities.Single(e => e.Name == "Beta");
        metadata.Gate("Alpha").TrySetResult(true);
        await supersededFetch;
        await DrainFieldFetchesAsync(vm);

        Assert.False(vm.IsLoadingFields);
        Assert.True(vm.IsCached);           // Beta's cached fields are on screen
        Assert.False(vm.ShowNotCachedHint);
    }

    [Fact]
    public async Task A_cached_entity_leaves_no_loading_indicator_behind()
    {
        var vm = MakeVm();

        await vm.LoadSelectedFieldsCommand.ExecuteAsync(null);   // CustomersV3 is already cached

        Assert.False(vm.IsLoadingFields);
        Assert.True(vm.IsCached);
    }

    [Fact]
    public void Cached_entity_populates_fields()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.True(vm.IsCached);
        Assert.NotEmpty(vm.Fields);
        Assert.Contains(vm.Fields, f => f.Name == "CustomerAccount" && f.IsKey);
    }

    [Fact]
    public void Uncached_entity_shows_no_fields_and_a_fetch_hint()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "VendorsV2");

        Assert.False(vm.IsCached);
        Assert.Empty(vm.Fields);
        Assert.Contains("Query Builder", vm.NotCachedMessage);
    }

    [Fact]
    public void Filter_matches_name_or_module()
    {
        var vm = MakeVm();

        vm.Search = "CustomersV3";
        Assert.Single(vm.Filtered);

        vm.Search = "GL";
        Assert.NotEmpty(vm.Filtered);
        Assert.All(vm.Filtered, e => Assert.Equal("GL", e.Module));
    }

    [Fact]
    public void Type_display_formats_string_enum_and_decimal()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.Equal("String(20)", vm.Fields.Single(f => f.Name == "CustomerAccount").TypeDisplay);
        Assert.Equal("Enum<NoYes>", vm.Fields.Single(f => f.Name == "IsOneTime").TypeDisplay);
        Assert.Equal("Decimal(32)", vm.Fields.Single(f => f.Name == "CreditLimit").TypeDisplay);
    }

    [Fact]
    public void Property_search_filters_the_field_grid_case_insensitively()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.Equal(vm.Fields.Count, vm.FilteredFields.Count); // unfiltered initially

        vm.FieldSearch = "DATE";

        Assert.All(vm.FilteredFields, f => Assert.Contains("date", f.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.FilteredFields, f => f.Name == "CreatedDateTime");
        Assert.DoesNotContain(vm.FilteredFields, f => f.Name == "OrganizationName");
    }

    [Fact]
    public void Property_search_reapplies_when_the_entity_changes()
    {
        var vm = MakeVm();
        // Move off the default selection (CustomersV3) to an entity with no cached fields, set a search,
        // then switch back — so selecting CustomersV3 is a real change that fires OnSelectedChanged.
        vm.Selected = vm.Entities.Single(e => e.Name == "VendorsV2");
        vm.FieldSearch = "name";
        Assert.Empty(vm.FilteredFields); // VendorsV2 has no cached fields

        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.NotEmpty(vm.FilteredFields);
        Assert.All(vm.FilteredFields, f => Assert.Contains("name", f.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Field_metadata_exposes_mandatory_precision_scale_and_range()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");

        var credit = vm.Fields.Single(f => f.Name == "CreditLimit");
        Assert.Equal("32/4", credit.PrecisionScale);
        Assert.Equal("0 .. 9999999", credit.Range);

        Assert.True(vm.Fields.Single(f => f.Name == "CurrencyCode").Mandatory);
        Assert.False(vm.Fields.Single(f => f.Name == "OrganizationName").Mandatory);

        // No precision/range info → blank cells (not "—/—" noise).
        var name = vm.Fields.Single(f => f.Name == "OrganizationName");
        Assert.Equal(string.Empty, name.PrecisionScale);
        Assert.Equal(string.Empty, name.Range);
    }
}
