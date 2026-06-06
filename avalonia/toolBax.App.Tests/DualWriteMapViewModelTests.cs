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

/// <summary>
/// Drives the redesigned Dual-Write Map Browser VM (real msdyn_* shape): async load + selection,
/// search, error banner, and empty state. Backed by fake <see cref="IDualWriteMapReader"/>s.
/// </summary>
public class DualWriteMapViewModelTests
{
    private static DualWriteMapViewModel MakeVm(IDualWriteMapReader? reader = null) =>
        new(reader ?? new FakeDualWriteMapReader());

    private sealed class ErrorReader : IDualWriteMapReader
    {
        private readonly string _error;
        public ErrorReader(string error) => _error = error;
        public Task<DwMapLoadResult> GetMapsAsync(CancellationToken ct = default) =>
            Task.FromResult(DwMapLoadResult.Fail(_error));
    }

    private sealed class EmptyReader : IDualWriteMapReader
    {
        public Task<DwMapLoadResult> GetMapsAsync(CancellationToken ct = default) =>
            Task.FromResult(DwMapLoadResult.Ok(Array.Empty<DwMapRecord>()));
    }

    private sealed class CountingReader : IDualWriteMapReader
    {
        public int Calls { get; private set; }
        public Task<DwMapLoadResult> GetMapsAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(DwMapLoadResult.Ok(Array.Empty<DwMapRecord>()));
        }
    }

    // Returns each queued result in turn (last one repeats), to model successive loads/refreshes.
    private sealed class SequenceReader : IDualWriteMapReader
    {
        private readonly Queue<DwMapLoadResult> _results;
        public SequenceReader(params DwMapLoadResult[] results) => _results = new Queue<DwMapLoadResult>(results);
        public Task<DwMapLoadResult> GetMapsAsync(CancellationToken ct = default) =>
            Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
    }

    // Two distinct records (fresh instances each call, so refresh restores by Id, not reference).
    private static IReadOnlyList<DwMapRecord> TwoMaps() => DualWriteMapParser.ParsePage("""
        { "value": [
            { "msdyn_dualwriteentitymapid": "a", "msdyn_name": "alpha", "msdyn_displayname": "Alpha" },
            { "msdyn_dualwriteentitymapid": "b", "msdyn_name": "beta", "msdyn_displayname": "Beta" } ] }
        """).Records;

    [Fact]
    public void Starts_empty_before_initialize()
    {
        var vm = MakeVm();

        Assert.Empty(vm.Maps);
        Assert.Null(vm.DetailMap);
    }

    [Fact]
    public async Task Initialize_loads_maps_and_selects_the_first()
    {
        var vm = MakeVm();

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.Maps);
        Assert.True(vm.HasMaps);
        Assert.NotNull(vm.DetailMap);
        Assert.True(vm.HasSelection);
        Assert.False(vm.HasLoadError);
    }

    [Fact]
    public async Task Initialize_only_loads_once()
    {
        var counting = new CountingReader();
        var vm = MakeVm(counting);

        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(1, counting.Calls);
    }

    [Fact]
    public async Task Refresh_reloads_after_initialize()
    {
        var counting = new CountingReader();
        var vm = MakeVm(counting);

        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, counting.Calls);
    }

    [Fact]
    public async Task Selecting_a_map_exposes_its_legs_fields_and_value_transforms()
    {
        var vm = MakeVm();
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        Assert.NotEmpty(vm.DetailMap!.Legs);
        Assert.Contains(vm.DetailMap.Fields, f => f.SourceField == "CustomerAccount");
        Assert.Contains(vm.DetailMap.ValueTransforms, t => t.TransformType == "ValueMap");
        Assert.Contains(vm.DetailMap.Properties, p => p.Key == "IntegrationKey");
    }

    [Fact]
    public async Task Search_filters_by_display_name_or_schema()
    {
        var vm = MakeVm();
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.Search = "Vendors";
        Assert.Single(vm.Filtered);

        vm.Search = "salesorders"; // destination schema of the sales-order map
        Assert.Single(vm.Filtered);

        vm.Search = "accounts"; // destination schema shared by customer + vendor maps
        Assert.Equal(2, vm.Filtered.Count());
    }

    [Fact]
    public async Task Filtering_out_the_selected_map_keeps_the_detail()
    {
        var vm = MakeVm();
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        // The ListBox nulls SelectedItem when the current row leaves the filtered set; the detail
        // pane must not be wiped.
        vm.SelectedMap = null;

        Assert.Equal("customersv3_account", vm.DetailMap!.Name);
        Assert.NotEmpty(vm.DetailMap.Legs);
    }

    [Fact]
    public async Task A_load_failure_surfaces_an_error_banner()
    {
        var vm = MakeVm(new ErrorReader("Couldn't load dual-write maps — Unauthorized."));

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.True(vm.HasLoadError);
        Assert.Contains("Unauthorized", vm.LoadError);
        Assert.Empty(vm.Maps);
        Assert.False(vm.ShowEmptyState);   // an error is shown instead of the empty state
        Assert.False(vm.ShowSelectPrompt); // …and instead of the "select a map" prompt (no overlap)
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_previously_loaded_maps()
    {
        var reader = new SequenceReader(
            DwMapLoadResult.Ok(TwoMaps()),
            DwMapLoadResult.Fail("Couldn't load dual-write maps — Unauthorized."));
        var vm = MakeVm(reader);
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Maps.Count);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Maps.Count);   // stale-but-useful catalogue retained, not wiped
        Assert.True(vm.HasLoadError);
        Assert.NotNull(vm.DetailMap);      // selection retained
    }

    [Fact]
    public async Task Refresh_preserves_the_current_selection_when_still_present()
    {
        var reader = new SequenceReader(DwMapLoadResult.Ok(TwoMaps()), DwMapLoadResult.Ok(TwoMaps()));
        var vm = MakeVm(reader);
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Id == "b");

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("b", vm.DetailMap!.Id); // not reset to the first map
    }

    [Fact]
    public async Task A_successful_but_empty_load_shows_the_empty_state()
    {
        var vm = MakeVm(new EmptyReader());

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.HasLoadError);
        Assert.False(vm.HasMaps);
    }
}
