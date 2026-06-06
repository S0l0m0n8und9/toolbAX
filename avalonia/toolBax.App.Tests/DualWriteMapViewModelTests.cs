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

    private static readonly DwSolutionLoadResult NoSolutions = DwSolutionLoadResult.Ok(Array.Empty<DwSolution>());

    private sealed class ErrorReader : IDualWriteMapReader
    {
        private readonly string _error;
        public ErrorReader(string error) => _error = error;
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            Task.FromResult(DwMapLoadResult.Fail(_error));
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Task.FromResult(NoSolutions);
    }

    private sealed class EmptyReader : IDualWriteMapReader
    {
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            Task.FromResult(DwMapLoadResult.Ok(Array.Empty<DwMapRecord>()));
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Task.FromResult(NoSolutions);
    }

    private sealed class CountingReader : IDualWriteMapReader
    {
        public int Calls { get; private set; }
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(DwMapLoadResult.Ok(Array.Empty<DwMapRecord>()));
        }
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Task.FromResult(NoSolutions);
    }

    // Returns each queued map result in turn (last one repeats), to model successive loads/refreshes.
    private sealed class SequenceReader : IDualWriteMapReader
    {
        private readonly Queue<DwMapLoadResult> _results;
        public SequenceReader(params DwMapLoadResult[] results) => _results = new Queue<DwMapLoadResult>(results);
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Task.FromResult(NoSolutions);
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
    public async Task Reload_reloads_maps_after_initialize()
    {
        var counting = new CountingReader();
        var vm = MakeVm(counting);

        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.ReloadMapsCommand.ExecuteAsync(null);

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

        await vm.ReloadMapsCommand.ExecuteAsync(null);

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

        await vm.ReloadMapsCommand.ExecuteAsync(null);

        Assert.Equal("b", vm.DetailMap!.Id); // not reset to the first map
    }

    [Fact]
    public async Task Initialize_loads_solutions_and_publishers_with_all_sentinels()
    {
        var vm = MakeVm();

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(DwSolution.All, vm.Solutions[0]);   // "All solutions" first
        Assert.Equal(DwPublisher.All, vm.Publishers[0]); // "All publishers" first
        Assert.Contains(vm.Solutions, s => s.UniqueName == "dualwrite_core");
        Assert.Contains(vm.Publishers, p => p.UniqueName == "contoso");
        Assert.True(vm.SelectedSolution!.IsAll);
        Assert.True(vm.SelectedPublisher!.IsAll);
    }

    [Fact]
    public async Task Selecting_a_solution_filters_the_maps()
    {
        var vm = MakeVm();
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Maps.Count); // all maps from the fake

        vm.SelectedSolution = vm.Solutions.Single(s => s.UniqueName == "dualwrite_core");
        await vm.ReloadMapsCommand.ExecutionTask!;

        Assert.Equal(2, vm.Maps.Count); // only the two maps in that solution
        Assert.All(vm.Maps, m => Assert.Equal("aaaaaaaa-0000-0000-0000-000000000001", m.SolutionId));
    }

    [Fact]
    public async Task Selecting_all_solutions_restores_the_full_list()
    {
        var vm = MakeVm();
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedSolution = vm.Solutions.Single(s => s.UniqueName == "sales_ext");
        await vm.ReloadMapsCommand.ExecutionTask!;
        Assert.Single(vm.Maps);

        vm.SelectedSolution = DwSolution.All;
        await vm.ReloadMapsCommand.ExecutionTask!;

        Assert.Equal(3, vm.Maps.Count);
    }

    [Fact]
    public async Task Selecting_a_publisher_filters_the_solution_list()
    {
        var vm = MakeVm();
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.SelectedPublisher = vm.Publishers.Single(p => p.UniqueName == "contoso");

        // All sentinel + only the contoso solution remain.
        Assert.Equal(2, vm.Solutions.Count);
        Assert.Contains(vm.Solutions, s => s.UniqueName == "sales_ext");
        Assert.DoesNotContain(vm.Solutions, s => s.UniqueName == "dualwrite_core");
    }

    [Fact]
    public async Task Changing_publisher_resets_a_now_hidden_solution_to_all()
    {
        var vm = MakeVm();
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedSolution = vm.Solutions.Single(s => s.UniqueName == "dualwrite_core"); // publisher msdyn
        await vm.ReloadMapsCommand.ExecutionTask!;

        vm.SelectedPublisher = vm.Publishers.Single(p => p.UniqueName == "contoso");
        await vm.ReloadMapsCommand.ExecutionTask!;

        Assert.True(vm.SelectedSolution!.IsAll);  // the hidden solution fell back to "All"
        Assert.Equal(3, vm.Maps.Count);            // …and the maps reloaded unfiltered
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
