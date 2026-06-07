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

    // Returns each queued response in turn, recording the requested paths (for paging tests).
    private sealed class PagingODataClient : IODataClient
    {
        private readonly Queue<ODataResponse> _responses;
        public List<string> Requested { get; } = new();
        public PagingODataClient(params ODataResponse[] responses) => _responses = new Queue<ODataResponse>(responses);
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            Requested.Add(path);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    // Records the path passed to SendAsync, to assert on the request the Run command issues.
    private sealed class RecordingODataClient : IODataClient
    {
        public string? LastPath { get; private set; }
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            LastPath = path;
            return Task.FromResult(new ODataResponse(200, "OK", "{\"value\":[]}", 5));
        }
    }

    // Mimics the real service: nothing until LoadEntitiesAsync runs, then one entity with fields.
    private sealed class DeferredMetadata : IMetadataService
    {
        private bool _loaded;
        private static readonly EntitySet[] Late = { new("LateEntity", "M", 1, "k", false, "odata") };
        private static readonly EntityField[] LateFields =
        {
            new("Id", "String", false, IsKey: true, Length: 10),
            new("Name", "String", true, Length: 50),
        };

        public IReadOnlyList<EntitySet> GetEntities() => _loaded ? Late : Array.Empty<EntitySet>();
        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            _loaded && entityName == "LateEntity" ? LateFields : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) { _loaded = true; return Task.CompletedTask; }
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        { _loaded = true; return Task.FromResult(entityName == "LateEntity"); }
    }

    [Fact]
    public async Task Initialize_loads_entities_and_field_chips_unavailable_at_construction()
    {
        var vm = new QueryBuilderViewModel(new DeferredMetadata(), new FakeODataClient());
        Assert.Empty(vm.Entities); // the real service starts empty until InitializeAsync

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Contains(vm.Entities, e => e.Name == "LateEntity");
        Assert.True(vm.HasFields);
        Assert.NotEmpty(vm.Fields);
        Assert.Contains("/data/LateEntity", vm.QueryUrl);
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
    public void Query_options_feed_the_url()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        vm.Filter = "CustomerGroupId eq 'DOM'";
        vm.OrderBy = "OrganizationName desc";
        vm.Top = "25";
        vm.Skip = "10";
        vm.Count = true;

        Assert.Contains("$filter=CustomerGroupId eq 'DOM'", vm.QueryUrl);
        Assert.Contains("$orderby=OrganizationName desc", vm.QueryUrl);
        Assert.Contains("$top=25", vm.QueryUrl);
        Assert.Contains("$skip=10", vm.QueryUrl);
        Assert.Contains("$count=true", vm.QueryUrl);
    }

    [Fact]
    public void Non_numeric_top_is_omitted_not_silently_mismatched()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        vm.Top = "abc"; // invalid → no $top clause (rather than a silent binding-conversion mismatch)

        Assert.DoesNotContain("$top=", vm.QueryUrl);
        Assert.Equal("abc", vm.Top); // the text the user typed is preserved
    }

    [Fact]
    public void Cross_company_toggle_feeds_the_url()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        vm.CrossCompany = true;
        Assert.Contains("cross-company=true", vm.QueryUrl);

        vm.CrossCompany = false;
        Assert.DoesNotContain("cross-company=true", vm.QueryUrl);
    }

    [Fact]
    public async Task Run_url_encodes_the_filter()
    {
        var recorder = new RecordingODataClient();
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), recorder);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Filter = "Name eq 'A B'";

        await vm.RunCommand.ExecuteAsync(null);

        Assert.NotNull(recorder.LastPath);
        Assert.Contains(Uri.EscapeDataString("Name eq 'A B'"), recorder.LastPath); // encoded for the request
        Assert.DoesNotContain("Name eq 'A B'", recorder.LastPath);                 // raw (spaced) form not sent
    }

    [Fact]
    public async Task Run_parses_count_and_next_link()
    {
        const string page1 = "{\"@odata.count\":120,\"@odata.nextLink\":\"https://x/data/E?$skiptoken=p2\",\"value\":[{\"CustomerAccount\":\"US-1\"}]}";
        var vm = new QueryBuilderViewModel(new FakeMetadataService(),
            new PagingODataClient(new ODataResponse(200, "OK", page1, 5)));
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(120, vm.TotalCount);
        Assert.True(vm.HasMore);
        Assert.Contains("of 120", vm.StatusText);
    }

    [Fact]
    public async Task Load_more_appends_the_next_page_via_the_next_link()
    {
        const string page1 = "{\"@odata.count\":3,\"@odata.nextLink\":\"https://x/data/E?$skiptoken=p2\",\"value\":[{\"CustomerAccount\":\"US-1\"}]}";
        const string page2 = "{\"value\":[{\"CustomerAccount\":\"US-2\"},{\"CustomerAccount\":\"US-3\"}]}";
        var client = new PagingODataClient(
            new ODataResponse(200, "OK", page1, 5),
            new ODataResponse(200, "OK", page2, 5));
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), client);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Single(vm.ResultRows);

        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.ResultRows.Count);
        Assert.False(vm.HasMore);                                      // page 2 had no nextLink
        Assert.Equal("https://x/data/E?$skiptoken=p2", client.Requested[1]); // the nextLink was requested
    }

    [Fact]
    public async Task A_failed_load_more_clears_the_success_badge()
    {
        const string page1 = "{\"@odata.nextLink\":\"https://x/data/E?$skiptoken=p2\",\"value\":[{\"CustomerAccount\":\"US-1\"}]}";
        var client = new PagingODataClient(
            new ODataResponse(200, "OK", page1, 5),
            new ODataResponse(500, "Server Error", "{}", 5));
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), client);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        await vm.RunCommand.ExecuteAsync(null);
        Assert.True(vm.RunSucceeded);

        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.False(vm.RunSucceeded); // the failed page clears the stale success badge
    }

    [Fact]
    public void Entity_search_filters_the_displayed_list_case_insensitively()
    {
        var vm = MakeVm();
        Assert.Equal(vm.Entities.Count, vm.FilteredEntities.Count); // unfiltered initially
        // Select a matching entity so the always-pinned active selection is itself a match.
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "SalesOrderHeadersV2");

        vm.EntitySearch = "ORDER"; // case-insensitive substring on the entity name

        Assert.All(vm.FilteredEntities, e => Assert.Contains("order", e.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.FilteredEntities, e => e.Name == "SalesOrderHeadersV2");
        Assert.Contains(vm.FilteredEntities, e => e.Name == "PurchaseOrderHeadersV2");
        Assert.DoesNotContain(vm.FilteredEntities, e => e.Name == "CustomersV3");
    }

    [Fact]
    public void Clearing_entity_search_restores_the_full_list()
    {
        var vm = MakeVm();
        vm.SelectedEntity = null; // no selection → nothing pinned, so a no-match term yields an empty list
        vm.EntitySearch = "zzz-no-match";
        Assert.Empty(vm.FilteredEntities);

        vm.EntitySearch = "   "; // whitespace-only is treated as no filter

        Assert.Equal(vm.Entities.Count, vm.FilteredEntities.Count);
    }

    [Fact]
    public void Entity_search_excluding_the_selection_keeps_it_pinned_and_preserves_fields()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.NotEmpty(vm.Fields);

        vm.EntitySearch = "order"; // CustomersV3 does NOT contain "order"

        // The active selection stays pinned in the filtered list so the bound ListBox can't null it
        // (which would otherwise wipe the field selection + query URL); selection + fields survive.
        Assert.Contains(vm.FilteredEntities, e => e.Name == "CustomersV3");
        Assert.Equal("CustomersV3", vm.SelectedEntity!.Name);
        Assert.NotEmpty(vm.Fields);
        Assert.Contains("/data/CustomersV3", vm.QueryUrl);
    }

    [Fact]
    public void Field_search_filters_the_chips_case_insensitively()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.Equal(vm.Fields.Count, vm.FilteredFields.Count); // unfiltered initially

        vm.FieldSearch = "DATE";

        Assert.All(vm.FilteredFields, f => Assert.Contains("date", f.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.FilteredFields, f => f.Name == "CreatedDateTime");
        Assert.Contains(vm.FilteredFields, f => f.Name == "ModifiedDateTime");
        Assert.DoesNotContain(vm.FilteredFields, f => f.Name == "OrganizationName");
    }

    [Fact]
    public void Field_search_hides_chips_from_the_view_but_keeps_them_in_the_select_clause()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Fields.Single(f => f.Name == "OrganizationName").IsSelected = true;

        vm.FieldSearch = "date"; // OrganizationName no longer shown…

        Assert.DoesNotContain(vm.FilteredFields, f => f.Name == "OrganizationName");
        Assert.Contains("OrganizationName", vm.QueryUrl); // …but the selection still drives $select
    }

    [Fact]
    public void Changing_the_entity_reapplies_the_active_field_search()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2"); // no cached fields
        vm.FieldSearch = "name";

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3"); // fields appear, filtered

        Assert.NotEmpty(vm.FilteredFields);
        Assert.All(vm.FilteredFields, f => Assert.Contains("name", f.Name, StringComparison.OrdinalIgnoreCase));
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

        Assert.Equal("Name,Note\r\n\"Acme, Inc.\",\"say \"\"hi\"\"\"", csv);
    }

    [Theory]
    [InlineData("=1+2")]
    [InlineData("+44")]
    [InlineData("-5")]
    [InlineData("@SUM(A1)")]
    public void Csv_builder_neutralises_formula_injection(string dangerous)
    {
        var rows = new[] { new QueryResultRow(new Dictionary<string, string> { ["C"] = dangerous }) };

        var cell = QueryCsv.Build(new[] { "C" }, rows).Split("\r\n")[1];

        // Quoted and apostrophe-prefixed so a spreadsheet treats it as literal text.
        Assert.Equal($"\"'{dangerous}\"", cell);
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
        var lines = clipboard.LastText!.Split("\r\n");
        // Header matches the escaped builder output (invariant to column escaping).
        Assert.Equal(QueryCsv.Build(vm.ResultColumns, Enumerable.Empty<QueryResultRow>()), lines[0]);
        Assert.Equal(vm.ResultRows.Count + 1, lines.Length); // header + one line per row
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
    public async Task Export_csv_to_file_writes_header_and_rows_and_reports_the_path()
    {
        var fileSave = new FakeFileSaveService("C:/tmp/CustomersV3.csv");
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(), fileSave: fileSave);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.ExportCsvFileCommand.CanExecute(null));
        await vm.ExportCsvFileCommand.ExecuteAsync(null);

        Assert.Equal("CustomersV3.csv", fileSave.LastSuggestedName);
        Assert.NotNull(fileSave.LastContent);
        var lines = fileSave.LastContent!.Split("\r\n");
        Assert.Equal(QueryCsv.Build(vm.ResultColumns, Enumerable.Empty<QueryResultRow>()), lines[0]);
        Assert.Equal(vm.ResultRows.Count + 1, lines.Length);
        Assert.Contains("Saved", vm.StatusText);
        Assert.Contains("CustomersV3.csv", vm.StatusText);
    }

    [Fact]
    public async Task Export_csv_to_file_cancel_does_not_report_a_save()
    {
        var fileSave = new FakeFileSaveService(resultPath: null); // user cancels the dialog
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(), fileSave: fileSave);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        await vm.RunCommand.ExecuteAsync(null);

        await vm.ExportCsvFileCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Saved", vm.StatusText);
        Assert.Contains("cancel", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_csv_to_file_is_disabled_before_a_run()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.False(vm.ExportCsvFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task Export_all_pages_through_every_row_and_saves_one_file()
    {
        const string page1 = "{\"@odata.nextLink\":\"https://x/data/E?$skiptoken=p2\",\"value\":[{\"CustomerAccount\":\"US-1\"}]}";
        const string page2 = "{\"value\":[{\"CustomerAccount\":\"US-2\"},{\"CustomerAccount\":\"US-3\"}]}";
        var client = new PagingODataClient(
            new ODataResponse(200, "OK", page1, 5),
            new ODataResponse(200, "OK", page2, 5));
        var fileSave = new FakeFileSaveService("C:/tmp/all.csv");
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), client, fileSave: fileSave);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.True(vm.ExportAllCsvCommand.CanExecute(null));
        await vm.ExportAllCsvCommand.ExecuteAsync(null);

        Assert.NotNull(fileSave.LastContent);
        var lines = fileSave.LastContent!.Split("\r\n");
        Assert.Equal(4, lines.Length);                                  // header + 3 rows across 2 pages
        Assert.Equal(2, client.Requested.Count);                       // base query + one nextLink
        Assert.Equal("https://x/data/E?$skiptoken=p2", client.Requested[1]);
        Assert.DoesNotContain("$top=", client.Requested[0]);           // export-all is unbounded
        Assert.Contains("Saved", vm.StatusText);
    }

    // Runs a callback on each request, to simulate the user changing the selection mid-export.
    private sealed class CallbackODataClient : IODataClient
    {
        private readonly Queue<ODataResponse> _responses;
        public Action? OnRequest { get; set; }
        public CallbackODataClient(params ODataResponse[] responses) => _responses = new Queue<ODataResponse>(responses);
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            OnRequest?.Invoke();
            return Task.FromResult(_responses.Dequeue());
        }
    }

    // Simulates a cancelled request (e.g. via CancelExportAllCsvCommand).
    private sealed class CancelingODataClient : IODataClient
    {
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
            => throw new OperationCanceledException();
    }

    [Fact]
    public async Task Export_all_names_the_file_for_the_entity_in_effect_when_it_started()
    {
        const string page1 = "{\"@odata.nextLink\":\"https://x/data/E?$skiptoken=p2\",\"value\":[{\"CustomerAccount\":\"US-1\"}]}";
        const string page2 = "{\"value\":[{\"CustomerAccount\":\"US-2\"}]}";
        var client = new CallbackODataClient(
            new ODataResponse(200, "OK", page1, 5),
            new ODataResponse(200, "OK", page2, 5));
        var fileSave = new FakeFileSaveService("C:/tmp/out.csv");
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), client, fileSave: fileSave);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        // The user switches entity while the export is still paging.
        client.OnRequest = () => vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2");

        await vm.ExportAllCsvCommand.ExecuteAsync(null);

        Assert.Equal("CustomersV3.csv", fileSave.LastSuggestedName); // the entity in effect at start, not VendorsV2
    }

    [Fact]
    public async Task Export_all_reports_cancellation_cleanly()
    {
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new CancelingODataClient(),
            fileSave: new FakeFileSaveService());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        await vm.ExportAllCsvCommand.ExecuteAsync(null);

        Assert.Equal("Export cancelled.", vm.StatusText); // not "Export failed: …canceled"
        Assert.False(vm.IsBusy);
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
