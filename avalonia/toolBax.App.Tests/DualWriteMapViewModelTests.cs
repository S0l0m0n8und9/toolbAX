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

public class DualWriteMapViewModelTests
{
    private static DualWriteMapViewModel MakeVm() => new(new FakeDualWriteMapService());

    // Holds run/error loads open until released, to exercise rapid map switching.
    private sealed class GatedMapService : IDualWriteMapService
    {
        private readonly FakeDualWriteMapService _inner = new();
        public readonly TaskCompletionSource Gate = new();

        public IReadOnlyList<DwMapSummary> GetMaps() => _inner.GetMaps();

        public DwMapDetail GetDetail(string mapId) => _inner.GetDetail(mapId);

        public async Task<IReadOnlyList<DwRun>> GetRunsAsync(string mapId, CancellationToken ct = default)
        {
            await Gate.Task;
            return await _inner.GetRunsAsync(mapId, ct);
        }

        public async Task<IReadOnlyList<DwError>> GetErrorsAsync(string mapId, CancellationToken ct = default)
        {
            await Gate.Task;
            return await _inner.GetErrorsAsync(mapId, ct);
        }
    }

    [Fact]
    public void Maps_are_listed_with_a_default_selection()
    {
        var vm = MakeVm();

        Assert.NotEmpty(vm.Maps);
        Assert.NotNull(vm.SelectedMap);
    }

    [Fact]
    public void Selecting_a_detailed_map_loads_bindings_value_maps_and_kpis()
    {
        var vm = MakeVm();

        vm.SelectedMap = vm.Maps.Single(m => m.Id == "cust-account");

        Assert.True(vm.HasBindings);
        Assert.NotEmpty(vm.Bindings);
        Assert.Contains(vm.Bindings, b => b.IsKey && b.FoField == "CustomerAccount");
        Assert.NotEmpty(vm.ValueMaps);
        Assert.NotEmpty(vm.Activity);
        Assert.False(string.IsNullOrWhiteSpace(vm.LatencyP95));
    }

    [Fact]
    public void Map_without_cached_bindings_shows_an_empty_state()
    {
        var vm = MakeVm();

        vm.SelectedMap = vm.Maps.Single(m => m.Id == "vend-account");

        Assert.False(vm.HasBindings);
        Assert.Empty(vm.Bindings);
    }

    [Fact]
    public void Search_filters_by_fo_or_dv_entity()
    {
        var vm = MakeVm();

        vm.Search = "Customers";
        Assert.Single(vm.Filtered);

        vm.Search = "account"; // dv "account" on cust-account
        Assert.NotEmpty(vm.Filtered);
    }

    [Fact]
    public void Filtering_out_the_selected_map_keeps_the_detail()
    {
        var vm = MakeVm();
        vm.SelectedMap = vm.Maps.Single(m => m.Id == "cust-account");
        Assert.True(vm.HasBindings);

        // The ListBox nulls SelectedItem when the current row leaves the filtered set; the detail
        // pane must not be wiped.
        vm.SelectedMap = null;

        Assert.Equal("cust-account", vm.DetailMap!.Id);
        Assert.True(vm.HasBindings);
        Assert.NotEmpty(vm.Bindings);
    }

    [Fact]
    public void Errors_indicator_reflects_the_selected_map_error_count()
    {
        var vm = MakeVm();

        vm.SelectedMap = vm.Maps.Single(m => m.Id == "so-salesorder");
        Assert.True(vm.HasErrors);

        vm.SelectedMap = vm.Maps.Single(m => m.Id == "vend-account");
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public async Task Selecting_a_failing_map_loads_runs_and_errors()
    {
        var vm = MakeVm();
        vm.SelectedMap = vm.Maps.Single(m => m.Id == "so-salesorder");

        await vm.LoadHistoryCommand.ExecuteAsync(null);

        Assert.True(vm.HasRuns);
        Assert.NotEmpty(vm.Runs);
        Assert.True(vm.HasErrorDetails);
        Assert.NotEmpty(vm.Errors);
        Assert.False(vm.IsLoadingHistory);
    }

    [Fact]
    public async Task Healthy_map_has_runs_but_no_error_details()
    {
        var vm = MakeVm();
        vm.SelectedMap = vm.Maps.Single(m => m.Id == "vend-account");

        await vm.LoadHistoryCommand.ExecuteAsync(null);

        Assert.True(vm.HasRuns);
        Assert.False(vm.HasErrorDetails);
        Assert.Empty(vm.Errors);
    }

    [Fact]
    public async Task Failing_map_errors_include_a_warning_severity()
    {
        var vm = MakeVm();
        vm.SelectedMap = vm.Maps.Single(m => m.Id == "so-salesorder");

        await vm.LoadHistoryCommand.ExecuteAsync(null);

        Assert.Contains(vm.Errors, e => e.IsWarning);
        Assert.Contains(vm.Errors, e => e.IsError);
    }

    [Fact]
    public async Task A_newer_selection_is_not_dropped_while_a_load_is_in_flight()
    {
        var svc = new GatedMapService();
        var vm = new DualWriteMapViewModel(svc); // the first selection's load is gated / in flight
        var target = vm.Maps.Single(m => m.Id == "so-salesorder");

        vm.SelectedMap = target; // newer selection arrives mid-flight

        svc.Gate.SetResult(); // release both loads
        await vm.LoadHistoryCommand.ExecutionTask!; // drain the latest load

        Assert.Equal("so-salesorder", vm.DetailMap!.Id);
        Assert.True(vm.HasRuns); // the newer selection's tabs are populated, not left blank
    }
}
