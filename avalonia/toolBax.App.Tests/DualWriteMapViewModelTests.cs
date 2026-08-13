using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) => Task.FromResult(DwCountResult.Ok(0));
    }

    private sealed class EmptyReader : IDualWriteMapReader
    {
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            Task.FromResult(DwMapLoadResult.Ok(Array.Empty<DwMapRecord>()));
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Task.FromResult(NoSolutions);
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) => Task.FromResult(DwCountResult.Ok(0));
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
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) => Task.FromResult(DwCountResult.Ok(0));
    }

    // Returns each queued map result in turn (last one repeats), to model successive loads/refreshes.
    private sealed class SequenceReader : IDualWriteMapReader
    {
        private readonly Queue<DwMapLoadResult> _results;
        public SequenceReader(params DwMapLoadResult[] results) => _results = new Queue<DwMapLoadResult>(results);
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Task.FromResult(NoSolutions);
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) => Task.FromResult(DwCountResult.Ok(0));
    }

    // Gates GetCeRowCountAsync so a map switch can be interleaved with an in-flight count.
    private sealed class GatedCountReader : IDualWriteMapReader
    {
        private readonly FakeDualWriteMapReader _inner = new();
        public TaskCompletionSource Gate { get; } = new();
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            _inner.GetMapsAsync(solutionUniqueName, ct);
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => _inner.GetSolutionsAsync(ct);
        public async Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default)
        {
            await Gate.Task.WaitAsync(ct);
            return DwCountResult.Ok(5);
        }
    }

    // Holds each map load open on a per-call gate, so overlapping reloads can be orchestrated.
    private sealed class GatedReader : IDualWriteMapReader
    {
        public List<TaskCompletionSource> Gates { get; } = new();
        public async Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default)
        {
            var gate = new TaskCompletionSource();
            Gates.Add(gate);
            await gate.Task;
            return DwMapLoadResult.Ok(Array.Empty<DwMapRecord>());
        }
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Task.FromResult(NoSolutions);
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) => Task.FromResult(DwCountResult.Ok(0));
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
    public async Task IsLoading_stays_true_until_the_last_overlapping_reload_finishes()
    {
        var reader = new GatedReader();
        var vm = MakeVm(reader);

        var initTask = vm.InitializeCommand.ExecuteAsync(null); // solutions (sync) then map load A (gated)
        Assert.Single(reader.Gates);
        Assert.True(vm.IsLoading);

        vm.ReloadMapsCommand.Execute(null);                     // overlapping map load B (gated)
        Assert.Equal(2, reader.Gates.Count);

        reader.Gates[0].SetResult();                            // finish A while B is still in flight
        await initTask;
        Assert.True(vm.IsLoading);                              // must NOT flip off — B is still loading

        reader.Gates[1].SetResult();                            // finish B
        await vm.ReloadMapsCommand.ExecutionTask!;
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Export_markdown_writes_the_selected_map_and_reports_the_path()
    {
        var save = new FakeFileSaveService(resultPath: "C:/out/map.md");
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(), save);
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        await vm.ExportMarkdownCommand.ExecuteAsync(null);

        Assert.NotNull(save.LastContent);
        Assert.Contains("# Customers V3 to Accounts", save.LastContent);
        Assert.EndsWith(".md", save.LastSuggestedName);
        Assert.Equal(SaveFileType.Markdown, save.LastFileType); // the picker offers *.md for this export
        Assert.Equal("Exported to C:/out/map.md", vm.ExportStatus);
    }

    [Fact]
    public async Task Export_markdown_reports_a_cancelled_dialog()
    {
        var save = new FakeFileSaveService(resultPath: null); // user cancels the save dialog
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(), save);
        await vm.InitializeCommand.ExecuteAsync(null);

        await vm.ExportMarkdownCommand.ExecuteAsync(null);

        Assert.Equal("Export cancelled.", vm.ExportStatus);
    }

    [Fact]
    public async Task Switching_the_selected_map_clears_a_stale_export_status()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(), new FakeFileSaveService("x.md"));
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");
        await vm.ExportMarkdownCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.ExportStatus);

        vm.SelectedMap = vm.Maps.Single(m => m.Name == "vendorsv2_account");

        Assert.Equal(string.Empty, vm.ExportStatus);
    }

    [Fact]
    public async Task Switching_maps_during_a_count_does_not_crash()
    {
        var reader = new GatedCountReader();
        var vm = new DualWriteMapViewModel(reader);
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        var counting = vm.CountAllRowsCommand.ExecuteAsync(null);             // begins, awaits the gate
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "vendorsv2_account");  // clears + rebuilds CountRows mid-count
        reader.Gate.SetResult();

        await counting; // must not throw (no "collection modified during enumeration")
    }

    [Fact]
    public void Export_markdown_is_disabled_without_a_selection()
    {
        var vm = MakeVm(new EmptyReader());

        Assert.False(vm.ExportMarkdownCommand.CanExecute(null)); // nothing loaded/selected yet
    }

    [Fact]
    public async Task Selecting_a_map_builds_an_uncounted_row_per_leg()
    {
        var vm = MakeVm();
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        Assert.Equal(vm.DetailMap!.Legs.Count, vm.CountRows.Count);
        Assert.All(vm.CountRows, r => Assert.Null(r.CeCount)); // not counted until requested
    }

    private sealed class CountODataClient : IODataClient
    {
        private readonly long _count;
        private readonly Action<int>? _afterCall;
        public string? LastPath { get; private set; }
        public int Calls { get; private set; }

        /// <param name="afterCall">Invoked with the 1-based call ordinal once the response is prepared —
        /// lets a test switch the active environment "during" an F&amp;O count.</param>
        public CountODataClient(long count, Action<int>? afterCall = null)
        {
            _count = count;
            _afterCall = afterCall;
        }

        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            LastPath = path;
            Calls++;
            _afterCall?.Invoke(Calls);
            return Task.FromResult(new ODataResponse(200, "OK", $"{{\"@odata.count\":{_count},\"value\":[]}}", 1));
        }
    }

    [Fact]
    public async Task Count_rows_fills_each_legs_ce_count()
    {
        var vm = MakeVm();
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.CountRows);
        Assert.All(vm.CountRows, r => Assert.NotNull(r.CeCount));
        Assert.Equal(250, vm.CountRows[0].CeCount); // filtered leg → the fake's filtered count
    }

    [Fact]
    public async Task Count_rows_compares_fo_and_ce_counts()
    {
        var odata = new CountODataClient(250); // F&O returns 250; CE for the filtered customer leg is 250
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(), odata: odata);
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        var row = vm.CountRows[0];
        Assert.Equal(250, row.FoCount);
        Assert.Equal(250, row.CeCount);
        Assert.Equal("Match", row.ComparisonLabel);
        Assert.StartsWith("/data/", odata.LastPath); // counted via the F&O OData endpoint
    }

    // Metadata service exposing a fixed entity catalogue for F&O-entity resolution.
    private sealed class StubMetadataService : IMetadataService
    {
        private readonly List<EntitySet> _entities;
        public StubMetadataService(params string[] names) =>
            _entities = names.Select(n => new EntitySet(n, "Module", 0, "Id", false, "Table")).ToList();
        public IReadOnlyList<EntitySet> GetEntities() => _entities;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) => Task.FromResult(false);
    }

    [Fact]
    public async Task Fo_entity_default_is_resolved_from_the_metadata_catalogue()
    {
        // The customer leg's source schema is CustCustomerV3Entity → resolves to the CustCustomerV3 set.
        var metadata = new StubMetadataService("CustCustomerV3", "VendVendorV2", "SalesOrderHeadersV2");
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(), metadata: metadata);
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        Assert.Equal("CustCustomerV3", vm.CountRows[0].FoEntity);
    }

    [Fact]
    public async Task Fo_entity_default_falls_back_to_a_guess_without_metadata()
    {
        // Empty catalogue → no confident match → the simple "drop Entity suffix" guess.
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(), metadata: new StubMetadataService());
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        // No confident catalogue match → the simple "drop Entity suffix" guess.
        Assert.Equal("CustCustomerV3", vm.CountRows[0].FoEntity);
    }

    [Fact]
    public async Task Editing_the_fo_entity_clears_a_stale_fo_count()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(), odata: new CountODataClient(250));
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");
        await vm.CountAllRowsCommand.ExecuteAsync(null);
        var row = vm.CountRows[0];
        Assert.NotNull(row.FoCount);

        row.FoEntity = "ADifferentEntity"; // the prior count no longer applies

        Assert.Null(row.FoCount);
        Assert.Equal("—", row.ComparisonLabel);
    }

    [Fact]
    public async Task Count_rows_reports_a_mismatch()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(), odata: new CountODataClient(999));
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        Assert.Equal("Mismatch", vm.CountRows[0].ComparisonLabel); // F&O 999 vs CE 250
    }

    // --- #159: a capped Dataverse count reaches the row as capped, not as a total ---

    // Real maps, but every CE count comes back flagged as the platform's 5,000-row ceiling.
    private sealed class CappedCeCountReader : IDualWriteMapReader
    {
        private readonly FakeDualWriteMapReader _inner = new();
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            _inner.GetMapsAsync(solutionUniqueName, ct);
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => _inner.GetSolutionsAsync(ct);
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) =>
            Task.FromResult(DwCountResult.Ok(5000, capped: true));
    }

    [Fact]
    public async Task A_capped_ce_count_suppresses_the_verdict_instead_of_reporting_a_mismatch()
    {
        // F&O reports the real 5,000 rows of a table Dataverse could only count up to its ceiling. Before
        // the fix this read as "Match" (equal numbers) or "Mismatch" (any other F&O count) — both bogus.
        var vm = new DualWriteMapViewModel(new CappedCeCountReader(), odata: new CountODataClient(5000));
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        var row = vm.CountRows[0];
        Assert.True(row.CeCountCapped);
        Assert.Equal("5,000+", row.CeCountLabel);
        Assert.Equal("Unknown (CE count capped)", row.ComparisonLabel);
    }

    // --- #152: counts are gated on the environment the displayed maps were loaded from ---

    // Real maps + counts, but records every CE count call so "no request was issued" is assertable.
    private sealed class CallCountingReader : IDualWriteMapReader
    {
        private readonly FakeDualWriteMapReader _inner = new();
        public int CeCountCalls { get; private set; }
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            _inner.GetMapsAsync(solutionUniqueName, ct);
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => _inner.GetSolutionsAsync(ct);
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default)
        {
            CeCountCalls++;
            return _inner.GetCeRowCountAsync(entitySet, odataFilter, ct);
        }
    }

    // Holds the map read open on a gate (and records CE counts), so an environment switch can be
    // interleaved with an in-flight load. Entered completes once the read is actually parked, so the test
    // never switches before the VM has captured the environment it is loading for.
    private sealed class GatedLoadReader : IDualWriteMapReader
    {
        private readonly FakeDualWriteMapReader _inner = new();
        public TaskCompletionSource Entered { get; } = new();
        public TaskCompletionSource Gate { get; } = new();
        public int CeCountCalls { get; private set; }

        public async Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Gate.Task;
            return await _inner.GetMapsAsync(solutionUniqueName, ct);
        }

        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => _inner.GetSolutionsAsync(ct);

        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default)
        {
            CeCountCalls++;
            return _inner.GetCeRowCountAsync(entitySet, odataFilter, ct);
        }
    }

    // A single map with three legs, so a count run has several rows (and several awaits) to trip through.
    private sealed class ThreeLegReader : IDualWriteMapReader
    {
        private readonly Action<int>? _afterCeCount;
        public int CeCountCalls { get; private set; }

        /// <param name="afterCeCount">Invoked with the 1-based CE-count ordinal — lets a test switch the
        /// active environment "during" a leg's Dataverse count.</param>
        public ThreeLegReader(Action<int>? afterCeCount = null) => _afterCeCount = afterCeCount;

        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            Task.FromResult(DwMapLoadResult.Ok(ThreeLegMap()));

        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Task.FromResult(NoSolutions);

        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default)
        {
            CeCountCalls++;
            _afterCeCount?.Invoke(CeCountCalls);
            return Task.FromResult(DwCountResult.Ok(100 * CeCountCalls));
        }
    }

    private static IReadOnlyList<DwMapRecord> ThreeLegMap() => DualWriteMapParser.ParsePage("""
        { "value": [ {
            "msdyn_dualwriteentitymapid": "m1",
            "msdyn_name": "threelegs",
            "msdyn_displayname": "Three legs",
            "msdyn_mapping": "{\"id\":\"map-3\",\"legs\":[{\"id\":\"leg-1\",\"sourceSchema\":\"AlphaEntity\",\"destinationSchema\":\"alphas\",\"fieldMappings\":[]},{\"id\":\"leg-2\",\"sourceSchema\":\"BetaEntity\",\"destinationSchema\":\"betas\",\"fieldMappings\":[]},{\"id\":\"leg-3\",\"sourceSchema\":\"GammaEntity\",\"destinationSchema\":\"gammas\",\"fieldMappings\":[]}]}"
        } ] }
        """).Records;

    private static EnvProfile MapEnv(string id, string name) =>
        new(id, name, $"https://{name}.operations.dynamics.com", "tenant", "AUMF", "Tier 2", EnvStatus.Connected);

    // Mutable active-environment source: the shell switching environments under this cached VM.
    private sealed class EnvSwitch
    {
        public EnvProfile? Current { get; set; } = MapEnv("env1", "contoso");
        public EnvProfile? Get() => Current;
    }

    [Fact]
    public async Task Counting_after_an_environment_switch_is_refused_until_the_maps_reload()
    {
        var reader = new CallCountingReader();
        var odata = new CountODataClient(250);
        var env = new EnvSwitch();
        var vm = new DualWriteMapViewModel(reader, odata: odata, activeEnv: env.Get);
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        env.Current = MapEnv("env2", "fabrikam");   // the grid still holds env1's maps

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        Assert.Equal(0, reader.CeCountCalls);                   // no Dataverse count
        Assert.Null(odata.LastPath);                            // no F&O count
        Assert.All(vm.CountRows, r => Assert.Null(r.CeCount));
        Assert.Contains("reload maps", vm.LoadError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reloading_the_maps_under_the_new_environment_re_enables_counting()
    {
        var reader = new CallCountingReader();
        var env = new EnvSwitch();
        var vm = new DualWriteMapViewModel(reader, odata: new CountODataClient(250), activeEnv: env.Get);
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");
        env.Current = MapEnv("env2", "fabrikam");
        await vm.CountAllRowsCommand.ExecuteAsync(null);        // refused
        Assert.Equal(0, reader.CeCountCalls);

        await vm.ReloadMapsCommand.ExecuteAsync(null);          // re-stamps to env2
        await vm.CountAllRowsCommand.ExecuteAsync(null);

        Assert.True(reader.CeCountCalls > 0);
        Assert.NotEmpty(vm.CountRows);
        Assert.All(vm.CountRows, r => Assert.NotNull(r.CeCount));
        Assert.False(vm.HasLoadError);                          // the refusal banner cleared on reload
    }

    [Fact]
    public async Task Counting_works_without_any_environment_switch()
    {
        var reader = new CallCountingReader();
        var env = new EnvSwitch();
        var vm = new DualWriteMapViewModel(reader, odata: new CountODataClient(250), activeEnv: env.Get);
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        Assert.True(reader.CeCountCalls > 0);
        Assert.All(vm.CountRows, r => Assert.NotNull(r.CeCount));
        Assert.False(vm.HasLoadError);
    }

    // The guards above are entry-only; these cover a switch that lands ACROSS the awaits of one operation.

    [Fact]
    public async Task A_mid_load_switch_stamps_the_environment_the_maps_were_read_for()
    {
        var env = new EnvSwitch();
        var reader = new GatedLoadReader();
        var odata = new CountODataClient(250);
        var vm = new DualWriteMapViewModel(reader, odata: odata, activeEnv: env.Get);

        var loading = vm.InitializeCommand.ExecuteAsync(null);
        await reader.Entered.Task;                      // parked inside GetMapsAsync (env1 already captured)
        env.Current = MapEnv("env2", "fabrikam");       // the shell switches while the load is in flight
        reader.Gate.SetResult();
        await loading;

        // The load itself still succeeds — a switch mid-load is not an error…
        Assert.NotEmpty(vm.Maps);
        Assert.False(vm.HasLoadError);

        // …but these maps are stamped with the environment they were READ for (env1), not whatever became
        // active while the read was in flight — so counting them under env2 is refused until a reload.
        await vm.CountAllRowsCommand.ExecuteAsync(null);

        Assert.Equal(0, reader.CeCountCalls);
        Assert.Equal(0, odata.Calls);
        Assert.Contains("reload maps", vm.LoadError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_switch_mid_count_run_stops_it_and_marks_the_abandoned_leg_skipped()
    {
        var env = new EnvSwitch();
        var reader = new ThreeLegReader();
        // Switch as the first row's F&O count returns: no later row may be requested.
        var odata = new CountODataClient(500, afterCall: n =>
        {
            if (n == 1)
            {
                env.Current = MapEnv("env2", "fabrikam");
            }
        });
        var vm = new DualWriteMapViewModel(reader, odata: odata, activeEnv: env.Get);
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.CountRows.Count);

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        Assert.Equal(1, reader.CeCountCalls);                 // only the first row's Dataverse count
        Assert.Equal(1, odata.Calls);                         // only the first row's F&O count
        Assert.Equal(100, vm.CountRows[0].CeCount);           // fully-counted row keeps its numbers…
        Assert.Equal(500, vm.CountRows[0].FoCount);           // …they were consistent when taken
        Assert.Contains("Skipped", vm.CountRows[1].CeStatus);  // the row the run stopped at says why
        Assert.Contains("Skipped", vm.CountRows[1].FoStatus);
        Assert.Null(vm.CountRows[1].CeCount);
        Assert.Null(vm.CountRows[2].CeCount);                  // never reached
        Assert.Null(vm.CountRows[2].FoCount);
        Assert.Contains("reload maps", vm.LoadError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_switch_between_a_rows_two_legs_skips_its_fo_count_and_keeps_the_ce_number()
    {
        var env = new EnvSwitch();
        // Switch as the first row's Dataverse count returns — its F&O leg is a separate await, and pairing
        // env1's CE number with env2's F&O number in one row is exactly what must not happen.
        var reader = new ThreeLegReader(afterCeCount: n =>
        {
            if (n == 1)
            {
                env.Current = MapEnv("env2", "fabrikam");
            }
        });
        var odata = new CountODataClient(500);
        var vm = new DualWriteMapViewModel(reader, odata: odata, activeEnv: env.Get);
        await vm.InitializeCommand.ExecuteAsync(null);

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        Assert.Equal(1, reader.CeCountCalls);
        Assert.Equal(0, odata.Calls);                          // the F&O leg was never issued
        Assert.Equal(100, vm.CountRows[0].CeCount);            // the number it did take stays
        Assert.Equal(string.Empty, vm.CountRows[0].CeStatus);   // that leg succeeded, it wasn't skipped
        Assert.Null(vm.CountRows[0].FoCount);                  // no second environment's number beside it
        Assert.Contains("Skipped", vm.CountRows[0].FoStatus);
        Assert.Equal("—", vm.CountRows[0].ComparisonLabel);    // and therefore no bogus Match/Mismatch
        Assert.Contains("reload maps", vm.LoadError, StringComparison.OrdinalIgnoreCase);
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

    // ---- #22: dual-write map direct link ----

    private static DwMapRecord MapWithId(string id) => new(
        Id: id, SolutionId: "sol", Name: "name", DisplayName: "Display", Version: "1.0",
        State: "", Status: "", Owner: "",
        CreatedOn: null, ModifiedOn: null,
        SummaryRows: Array.Empty<DwMapSummaryRow>(), Legs: Array.Empty<DwMapLeg>(),
        Fields: Array.Empty<DwMapField>(), ValueTransforms: Array.Empty<DwMapValueTransform>(),
        Properties: Array.Empty<DwMapProperty>(), RawMapping: null, RawProperties: null);

    private static EnvProfile EnvWithDataverse(string? dataverseUrl) =>
        new("env1", "Env", "contoso.operations.dynamics.com", "tenant", "USMF", "Tier 1",
            EnvStatus.Connected, DataverseUrl: dataverseUrl);

    [Fact]
    public async Task Selecting_a_map_builds_an_openable_and_copyable_dataverse_link()
    {
        var launcher = new FakeUrlLauncher();
        var clipboard = new FakeClipboardService();
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(),
            activeEnv: () => EnvWithDataverse("https://contoso.crm.dynamics.com"),
            clipboard: clipboard, launcher: launcher);

        vm.SelectedMap = MapWithId("11111111-1111-1111-1111-111111111111");

        Assert.True(vm.HasMapLink);
        var url = vm.MapRecordUrl;
        Assert.NotNull(url);
        Assert.Contains("msdyn_dualwriteentitymap", url!);
        Assert.Empty(vm.MapLinkUnavailableReason);

        await vm.OpenMapLinkCommand.ExecuteAsync(null);
        Assert.Equal(url, launcher.LastUrl);

        await vm.CopyMapLinkCommand.ExecuteAsync(null);
        Assert.Equal(url, clipboard.LastText);
    }

    [Fact]
    public void No_dataverse_url_disables_the_link_with_an_explanation()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(),
            activeEnv: () => EnvWithDataverse(null));

        vm.SelectedMap = MapWithId("11111111-1111-1111-1111-111111111111");

        Assert.False(vm.HasMapLink);
        Assert.Null(vm.MapRecordUrl);
        Assert.False(vm.OpenMapLinkCommand.CanExecute(null));
        Assert.Contains("Dataverse URL", vm.MapLinkUnavailableReason);
    }

    [Fact]
    public void An_invalid_map_id_disables_the_link_with_a_record_id_explanation()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(),
            activeEnv: () => EnvWithDataverse("https://contoso.crm.dynamics.com"));

        vm.SelectedMap = MapWithId("not-a-guid");

        Assert.False(vm.HasMapLink);
        Assert.Null(vm.MapRecordUrl);
        Assert.False(vm.OpenMapLinkCommand.CanExecute(null));
        Assert.Contains("record id", vm.MapLinkUnavailableReason);
    }

    // --- Commands must not fault the dispatcher (#163) ---
    // Every command below is an AsyncRelayCommand: a faulted task is rethrown on the dispatcher, which
    // kills the app. Each has to end as a status/error line instead.

    // Throws rather than returning a failure result — the reader seam's other failure mode.
    private sealed class ThrowingReader : IDualWriteMapReader
    {
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("Dataverse said no");
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Dataverse said no");
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) =>
            Task.FromResult(DwCountResult.Ok(0));
    }

    private sealed class ThrowingClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => throw new InvalidOperationException("clipboard is busy");
    }

    private sealed class ThrowingFileSave : IFileSaveService
    {
        public Task<string?> SaveTextAsync(string suggestedFileName, string content, SaveFileType fileType,
            CancellationToken ct = default) => throw new IOException("the file is in use");
    }

    // Gates the SOLUTIONS load (Initialize's first await, the one with no OCE handling of its own —
    // LoadMapsAsync already catches cancellation) and honours the token, so a cancelled run really throws.
    private sealed class GatedSolutionReader : IDualWriteMapReader
    {
        public List<TaskCompletionSource> Gates { get; } = new();
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) =>
            Task.FromResult(DwMapLoadResult.Ok(Array.Empty<DwMapRecord>()));
        public async Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default)
        {
            var gate = new TaskCompletionSource();
            Gates.Add(gate);
            await gate.Task.WaitAsync(ct);
            return NoSolutions;
        }
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) => Task.FromResult(DwCountResult.Ok(0));
    }

    [Fact]
    public async Task Re_entering_initialize_cancels_the_first_run_without_faulting()
    {
        // The view fires InitializeCommand on every Loaded, and AsyncRelayCommand cancels the previous
        // token when re-entered — so navigate-away-and-back must complete the first run, not fault it.
        var reader = new GatedSolutionReader();
        var vm = MakeVm(reader);

        var first = vm.InitializeCommand.ExecuteAsync(null);
        var second = vm.InitializeCommand.ExecuteAsync(null); // cancels the first command's token
        foreach (var gate in reader.Gates.ToList())
        {
            gate.TrySetResult();
        }

        await first;  // must complete, not throw OperationCanceledException
        await second;
    }

    [Fact]
    public async Task Initialize_reports_a_reader_failure_instead_of_faulting()
    {
        var vm = MakeVm(new ThrowingReader());

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.True(vm.HasLoadError);
        Assert.Contains("Dataverse said no", vm.LoadError);
    }

    [Fact]
    public async Task Export_markdown_survives_a_locked_file()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(), new ThrowingFileSave());
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedMap = vm.Maps.Single(m => m.Name == "customersv3_account");

        await vm.ExportMarkdownCommand.ExecuteAsync(null);

        Assert.Contains("Export failed", vm.ExportStatus);
        Assert.Contains("the file is in use", vm.ExportStatus);
    }

    [Fact]
    public async Task Copy_map_link_survives_a_failing_clipboard()
    {
        var vm = new DualWriteMapViewModel(new FakeDualWriteMapReader(),
            activeEnv: () => EnvWithDataverse("https://contoso.crm.dynamics.com"),
            clipboard: new ThrowingClipboard());
        vm.SelectedMap = MapWithId("11111111-1111-1111-1111-111111111111");

        await vm.CopyMapLinkCommand.ExecuteAsync(null);

        Assert.Contains("clipboard", vm.ExportStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clipboard is busy", vm.ExportStatus);
    }

    // --- #204: the F&O count leg goes out with a filter F&O can actually parse ---

    // A single-leg map with a caller-supplied source schema and X++ source filter, so a test can drive the
    // count path with the exact strings #204's live probe used.
    private sealed class FilterLegReader : IDualWriteMapReader
    {
        private readonly string _sourceSchema;
        private readonly string _sourceFilter;

        public FilterLegReader(string sourceSchema, string sourceFilter)
        {
            _sourceSchema = sourceSchema;
            _sourceFilter = sourceFilter;
        }

        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default)
        {
            var mapping =
                $"{{\"id\":\"map-1\",\"legs\":[{{\"id\":\"leg-1\"," +
                $"\"sourceSchema\":{JsonSerializer.Serialize(_sourceSchema)}," +
                "\"destinationSchema\":\"accounts\"," +
                $"\"sourceFilter\":{JsonSerializer.Serialize(_sourceFilter)}," +
                "\"reversedSourceFilter\":\"\",\"fieldMappings\":[]}]}";
            var page =
                "{\"value\":[{\"msdyn_dualwriteentitymapid\":\"m1\",\"msdyn_name\":\"oneleg\"," +
                $"\"msdyn_displayname\":\"One leg\",\"msdyn_mapping\":{JsonSerializer.Serialize(mapping)}}}]}}";
            return Task.FromResult(DwMapLoadResult.Ok(DualWriteMapParser.ParsePage(page).Records));
        }

        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Task.FromResult(NoSolutions);

        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default) =>
            Task.FromResult(DwCountResult.Ok(7));
    }

    // Metadata whose fields are NOT cached until they're loaded — the live shape, so the count path has to
    // fetch them for the counted entity like it does against a real environment.
    private sealed class FieldMetadataService : IMetadataService
    {
        private readonly string _entity;
        private readonly IReadOnlyList<EntityField> _fields;
        private bool _loaded;

        public int FieldLoads { get; private set; }

        public FieldMetadataService(string entity, params string[] fieldNames)
        {
            _entity = entity;
            _fields = fieldNames.Select(n => new EntityField(n, "String", true)).ToList();
        }

        public IReadOnlyList<EntitySet> GetEntities() =>
            new[] { new EntitySet(_entity, "Module", 0, "Id", false, "Table") };

        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            _loaded && string.Equals(entityName, _entity, StringComparison.Ordinal) ? _fields : null;

        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        {
            FieldLoads++;
            _loaded |= string.Equals(entityName, _entity, StringComparison.Ordinal);
            return Task.FromResult(_loaded);
        }
    }

    [Fact]
    public async Task An_fo_count_is_issued_with_qualified_enum_literals_and_corrected_field_casing()
    {
        // #204's live matrix: this leg's converted filter 400s in every spelling except the one asserted
        // below, so the count request has to carry that exact string.
        var reader = new FilterLegReader("CustomerV3Entity", "(ISONETIMECUSTOMER != NoYes::Yes)");
        var metadata = new FieldMetadataService("CustomerV3", "IsOneTimeCustomer", "CustomerAccount");
        var odata = new CountODataClient(42);
        var vm = new DualWriteMapViewModel(reader, odata: odata, metadata: metadata);
        await vm.InitializeCommand.ExecuteAsync(null);

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        var row = vm.CountRows.Single();
        Assert.Equal("CustomerV3", row.FoEntity);
        Assert.Equal(
            "/data/CustomerV3?$top=1&$count=true&cross-company=true&$filter=" +
            Uri.EscapeDataString("(IsOneTimeCustomer ne Microsoft.Dynamics.DataEntities.NoYes'Yes')"),
            odata.LastPath);
        Assert.Equal(42, row.FoCount);
        Assert.Empty(row.FoStatus);
        Assert.True(metadata.FieldLoads > 0); // the entity's fields were fetched to do the correcting
    }

    [Fact]
    public async Task A_leg_whose_filter_names_a_field_the_entity_lacks_is_reported_instead_of_counted()
    {
        // Honest-verdict doctrine (#159): a 400 the user can't act on is worse than naming the field.
        var reader = new FilterLegReader("CustomerV3Entity", "(QUOTATIONNUMBER == 'Q-1')");
        var metadata = new FieldMetadataService("CustomerV3", "IsOneTimeCustomer", "CustomerAccount");
        var odata = new CountODataClient(42);
        var vm = new DualWriteMapViewModel(reader, odata: odata, metadata: metadata);
        await vm.InitializeCommand.ExecuteAsync(null);

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        var row = vm.CountRows.Single();
        Assert.Equal(0, odata.Calls);      // no known-doomed request
        Assert.Null(odata.LastPath);
        Assert.Null(row.FoCount);
        Assert.Contains("QUOTATIONNUMBER", row.FoStatus);
        Assert.Contains("CustomerV3", row.FoStatus);
        Assert.Equal(7, row.CeCount);      // the Dataverse side of the row still counted
    }

    [Fact]
    public async Task A_leg_with_no_resolvable_fo_entity_is_reported_instead_of_counted()
    {
        // A blank resolved entity used to produce "/data/?…" — the service document, which answers 200 and
        // yields no count at all, i.e. a silent null instead of a reason.
        var reader = new FilterLegReader(string.Empty, string.Empty);
        var odata = new CountODataClient(42);
        var vm = new DualWriteMapViewModel(reader, odata: odata, metadata: new StubMetadataService());
        await vm.InitializeCommand.ExecuteAsync(null);

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        var row = vm.CountRows.Single();
        Assert.Equal(string.Empty, row.FoEntity);
        Assert.Equal(0, odata.Calls);
        Assert.Null(row.FoCount);
        Assert.Contains("not resolved", row.FoStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_count_still_goes_out_when_the_entitys_fields_cannot_be_loaded()
    {
        // No fields to case-correct against is not a reason to refuse the count: the enum token is already
        // fixed by the converter, so the request is strictly better than before #204 — never worse.
        var reader = new FilterLegReader("CustomerV3Entity", "(ISONETIMECUSTOMER != NoYes::Yes)");
        var odata = new CountODataClient(42);
        var vm = new DualWriteMapViewModel(reader, odata: odata,
            metadata: new FieldMetadataService("SomeOtherEntity"));
        await vm.InitializeCommand.ExecuteAsync(null);

        await vm.CountAllRowsCommand.ExecuteAsync(null);

        var row = vm.CountRows.Single();
        Assert.Equal(1, odata.Calls);
        Assert.Contains(
            Uri.EscapeDataString("(ISONETIMECUSTOMER ne Microsoft.Dynamics.DataEntities.NoYes'Yes')"),
            odata.LastPath!);
        Assert.Equal(42, row.FoCount);
        Assert.Empty(row.FoStatus);
    }
}
