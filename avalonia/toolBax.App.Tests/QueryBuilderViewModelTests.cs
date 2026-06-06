using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class QueryBuilderViewModelTests
{
    private static QueryBuilderViewModel MakeVm() =>
        new(new FakeMetadataService(), new FakeODataClient());

    // GET → 404, to exercise the failed-run path.
    private sealed class FailingODataClient : IODataClient
    {
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
            => Task.FromResult(new ODataResponse(404, "Not Found", "{\"error\":{}}", 20));
    }

    // Holds a run open until the gate is released, so concurrent-run guards can be observed.
    private sealed class GatedODataClient : IODataClient
    {
        public readonly TaskCompletionSource Gate = new();

        public async Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            await Gate.Task;
            return new ODataResponse(200, "OK", "{\"value\":[]}", 10);
        }
    }

    [Fact]
    public void Cached_entity_populates_field_chips_and_query_url()
    {
        var vm = MakeVm();

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.True(vm.HasFields);
        Assert.NotEmpty(vm.Fields);
        Assert.Contains("/data/CustomersV3", vm.QueryUrl);
        Assert.Contains("$select=", vm.QueryUrl);
        // PK fields are selected by default, so the URL carries a key.
        Assert.Contains("CustomerAccount", vm.QueryUrl);
    }

    [Fact]
    public void Toggling_a_field_updates_the_select_clause_both_ways()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        var chip = vm.Fields.Single(f => f.Name == "OrganizationName");

        vm.ToggleFieldCommand.Execute(chip);
        Assert.True(chip.IsSelected);
        Assert.Contains("OrganizationName", vm.QueryUrl);

        vm.ToggleFieldCommand.Execute(chip);
        Assert.False(chip.IsSelected);
        Assert.DoesNotContain("OrganizationName", vm.QueryUrl);
    }

    [Fact]
    public void Uncached_entity_hides_fields_and_shows_run_once_hint()
    {
        var vm = MakeVm();

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2");

        Assert.False(vm.HasFields);
        Assert.Empty(vm.Fields);
        Assert.Contains("run once", vm.NotCachedMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_populates_result_rows_columns_and_status()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.HasRun);
        Assert.NotEmpty(vm.ResultRows);
        Assert.Equal(vm.RowCount, vm.ResultRows.Count);
        Assert.Contains("200", vm.StatusText);
        // Result columns mirror the currently selected fields, in order.
        Assert.Equal(vm.Fields.Where(f => f.IsSelected).Select(f => f.Name), vm.ResultColumns);
        Assert.True(vm.RunSucceeded);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Failed_run_reports_error_without_a_success_badge()
    {
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FailingODataClient());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.HasRun);
        Assert.False(vm.RunSucceeded);
        Assert.Empty(vm.ResultRows);
        Assert.Contains("404", vm.StatusText);
    }

    [Fact]
    public void Csv_builder_quotes_fields_with_commas_and_doubles_quotes()
    {
        var columns = new[] { "Name", "Note" };
        var rows = new[]
        {
            new QueryResultRow(new Dictionary<string, string> { ["Name"] = "Acme, Inc.", ["Note"] = "say \"hi\"" }),
        };

        var csv = QueryCsv.Build(columns, rows);

        Assert.Equal("Name,Note\n\"Acme, Inc.\",\"say \"\"hi\"\"\"", csv);
    }

    [Fact]
    public async Task Export_csv_copies_header_and_rows_to_the_clipboard()
    {
        var clipboard = new FakeClipboardService();
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(), clipboard);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.ExportCsvCommand.CanExecute(null));
        await vm.ExportCsvCommand.ExecuteAsync(null);

        Assert.NotNull(clipboard.LastText);
        var lines = clipboard.LastText!.Split('\n');
        Assert.Equal(string.Join(",", vm.ResultColumns), lines[0]); // header
        Assert.Equal(vm.ResultRows.Count + 1, lines.Length);        // header + one line per row
        Assert.Contains("CSV", vm.StatusText);
    }

    [Fact]
    public void Export_csv_is_disabled_before_a_run()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.False(vm.ExportCsvCommand.CanExecute(null));
    }

    [Fact]
    public async Task Run_is_disabled_while_a_run_is_in_flight()
    {
        var client = new GatedODataClient();
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), client);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        var run = vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.IsBusy);
        Assert.False(vm.RunCommand.CanExecute(null));

        client.Gate.SetResult();
        await run;

        Assert.False(vm.IsBusy);
        Assert.True(vm.RunCommand.CanExecute(null));
    }
}
