using System;
using System.Collections.Generic;
using System.IO;
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

        vm.IsRawFilterMode = true; // raw $filter is now a mode; the typed text only applies in Raw mode
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
        vm.IsRawFilterMode = true; // raw $filter is now a mode
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
    public void Select_all_fields_selects_every_field_and_clear_deselects_all()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        vm.SelectAllFieldsCommand.Execute(null);
        Assert.All(vm.Fields, f => Assert.True(f.IsSelected));
        Assert.Equal($"{vm.Fields.Count} of {vm.Fields.Count} selected", vm.FieldSelectionLabel);

        vm.ClearFieldsCommand.Execute(null);
        Assert.All(vm.Fields, f => Assert.False(f.IsSelected));
        Assert.Equal($"0 of {vm.Fields.Count} selected", vm.FieldSelectionLabel);
        Assert.Contains("$select=*", vm.QueryUrl); // no fields selected → wildcard
    }

    [Fact]
    public void Select_all_after_a_search_selects_only_the_visible_fields()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.ClearFieldsCommand.Execute(null);

        vm.FieldSearch = "date";
        vm.SelectAllFieldsCommand.Execute(null);

        Assert.All(vm.Fields.Where(f => f.IsSelected),
            f => Assert.Contains("date", f.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.Fields, f => f.Name == "CreatedDateTime" && f.IsSelected);
        Assert.Contains(vm.Fields, f => f.Name == "OrganizationName" && !f.IsSelected);
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
    public void Navigation_properties_load_for_an_entity_that_exposes_them()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.True(vm.HasNavigations);
        Assert.Contains(vm.Navigations, n => n.Name == "PrimaryContact");
        Assert.Contains(vm.Navigations, n => n.Name == "SalesOrderHeaders");
    }

    [Fact]
    public void Joins_panel_is_collapsed_by_default_and_header_tracks_the_selection()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.False(vm.IsJoinsExpanded); // joins are secondary to $select → collapsed initially
        Assert.Equal($"Joins ($expand) · 0 of {vm.Navigations.Count}", vm.JoinsHeader);

        vm.Navigations.Single(n => n.Name == "PrimaryContact").IsSelected = true;

        Assert.Equal($"Joins ($expand) · 1 of {vm.Navigations.Count}", vm.JoinsHeader);
    }

    [Fact]
    public void Join_search_filters_the_navigation_list_case_insensitively()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.Equal(vm.Navigations.Count, vm.FilteredNavigations.Count); // unfiltered initially

        vm.JoinSearch = "SALES";

        Assert.All(vm.FilteredNavigations, n => Assert.Contains("sales", n.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.FilteredNavigations, n => n.Name == "SalesOrderHeaders");
        Assert.DoesNotContain(vm.FilteredNavigations, n => n.Name == "PrimaryContact");
        // Filtering only hides chips from the view; the master list (and selections) is untouched.
        Assert.Contains(vm.Navigations, n => n.Name == "PrimaryContact");
    }

    [Fact]
    public void Ticking_a_navigation_adds_expand_and_keeps_it_out_of_select()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        vm.Navigations.Single(n => n.Name == "PrimaryContact").IsSelected = true;

        Assert.Contains("$expand=PrimaryContact", vm.QueryUrl);
        // The nav joins via $expand, not $select.
        var select = vm.QueryUrl.Split('?')[1].Split('&').Single(p => p.StartsWith("$select=", StringComparison.Ordinal));
        Assert.DoesNotContain("PrimaryContact", select);
    }

    [Fact]
    public async Task Multiple_expands_keep_literal_commas_in_the_request()
    {
        var recorder = new RecordingODataClient();
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), recorder);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Navigations.Single(n => n.Name == "PrimaryContact").IsSelected = true;
        vm.Navigations.Single(n => n.Name == "SalesOrderHeaders").IsSelected = true;

        await vm.RunCommand.ExecuteAsync(null);

        // The item separator must stay a literal comma (not %2C, which would malform the $expand).
        Assert.Contains("$expand=PrimaryContact,SalesOrderHeaders", recorder.LastPath);
        Assert.DoesNotContain("%2C", recorder.LastPath!);
    }

    [Fact]
    public async Task An_expanded_navigation_becomes_a_result_column()
    {
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new RecordingODataClient());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.Navigations.Single(n => n.Name == "PrimaryContact").IsSelected = true;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Contains("PrimaryContact", vm.ResultColumns); // expanded data surfaces as a column
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

    [Fact]
    public async Task Run_switches_to_the_results_tab()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.Equal(0, vm.SelectedTabIndex); // Fields is the default tab

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(QueryBuilderViewModel.ResultsTabIndex, vm.SelectedTabIndex);
    }

    [Fact]
    public async Task Load_more_switches_to_the_results_tab()
    {
        const string page1 = "{\"@odata.nextLink\":\"https://x/data/E?$skiptoken=p2\",\"value\":[{\"CustomerAccount\":\"US-1\"}]}";
        const string page2 = "{\"value\":[{\"CustomerAccount\":\"US-2\"}]}";
        var client = new PagingODataClient(
            new ODataResponse(200, "OK", page1, 5),
            new ODataResponse(200, "OK", page2, 5));
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), client);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        await vm.RunCommand.ExecuteAsync(null);
        vm.SelectedTabIndex = 0; // pretend the user navigated back to Fields

        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(QueryBuilderViewModel.ResultsTabIndex, vm.SelectedTabIndex);
    }

    [Fact]
    public async Task Export_all_does_not_change_the_active_tab()
    {
        var fileSave = new FakeFileSaveService("C:/tmp/x.csv");
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(), fileSave: fileSave);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.Equal(0, vm.SelectedTabIndex);

        await vm.ExportAllCsvCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.SelectedTabIndex); // export writes a file; it must not jump to Results
    }

    [Fact]
    public void Fields_tab_header_tracks_selection_and_falls_back_when_uncached()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        // PK fields are selected by default (dataAreaId + CustomerAccount).
        Assert.Equal($"Fields · {vm.Fields.Count(f => f.IsSelected)}/{vm.Fields.Count}", vm.FieldsTabHeader);

        vm.ClearFieldsCommand.Execute(null);
        Assert.Equal($"Fields · 0/{vm.Fields.Count}", vm.FieldsTabHeader);

        vm.SelectAllFieldsCommand.Execute(null);
        Assert.Equal($"Fields · {vm.Fields.Count}/{vm.Fields.Count}", vm.FieldsTabHeader);

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2"); // no cached fields
        Assert.False(vm.HasFields);
        Assert.Equal("Fields", vm.FieldsTabHeader);
    }

    [Fact]
    public void Filter_tab_header_tracks_condition_count_and_raw_mode()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.Equal("Filter", vm.FilterTabHeader); // no conditions yet

        AddCondition(vm);
        Assert.Equal("Filter · 1", vm.FilterTabHeader);

        vm.IsRawFilterMode = true;
        Assert.Equal("Filter · raw", vm.FilterTabHeader);
    }

    [Fact]
    public void Joins_tab_header_tracks_selection_and_falls_back_when_none()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.Equal($"Joins · 0/{vm.Navigations.Count}", vm.JoinsTabHeader);

        vm.Navigations.Single(n => n.Name == "PrimaryContact").IsSelected = true;
        Assert.Equal($"Joins · 1/{vm.Navigations.Count}", vm.JoinsTabHeader);

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2"); // no navigations
        Assert.False(vm.HasNavigations);
        Assert.Equal("Joins", vm.JoinsTabHeader);
    }

    [Fact]
    public async Task Results_tab_header_shows_row_count_after_a_run()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.Equal("Results", vm.ResultsTabHeader); // before any run

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal($"Results · {vm.RowCount}", vm.ResultsTabHeader);
    }

    // --- Filter builder (nested AND/OR tree) ---

    private static QueryFilterOperator Op(string op) => QueryFilterOperator.All.Single(o => o.Op == op);

    private static QueryFilterCondition AddCondition(QueryBuilderViewModel vm)
    {
        vm.FilterRoot.AddConditionCommand.Execute(null);
        return (QueryFilterCondition)vm.FilterRoot.Children[^1];
    }

    [Fact]
    public void Builder_condition_renders_into_the_filter_and_url()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3"); // company-aware → cross-company on

        var cond = AddCondition(vm);
        cond.Field = "OrganizationName";
        cond.Operator = Op("eq");
        cond.Value = "Acme";

        Assert.Equal("OrganizationName eq 'Acme'", vm.BuilderFilter);
        Assert.Equal("OrganizationName eq 'Acme'", vm.EffectiveFilter); // cross-company on → no dataAreaId clause
        Assert.Contains("$filter=OrganizationName eq 'Acme'", vm.QueryUrl);
    }

    [Fact]
    public void Builder_function_operator_renders_as_a_function_call()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        var cond = AddCondition(vm);
        cond.Field = "OrganizationName";
        cond.Operator = Op("contains");
        cond.Value = "Contoso";

        Assert.Equal("contains(OrganizationName,'Contoso')", vm.BuilderFilter);
    }

    [Fact]
    public void Builder_numeric_value_is_emitted_unquoted_and_strings_quoted()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        var cond = AddCondition(vm);
        cond.Field = "CreditLimit"; // Decimal → numeric
        cond.Operator = Op("gt");
        cond.Value = "10000";
        Assert.True(cond.IsNumeric);
        Assert.Equal("CreditLimit gt 10000", vm.BuilderFilter);

        cond.Field = "OrganizationName"; // String → quoted (changing the field clears the value)
        cond.Value = "O'Brien";
        Assert.Equal("OrganizationName gt 'O''Brien'", vm.BuilderFilter); // embedded quote doubled
    }

    [Fact]
    public void Builder_nested_group_renders_with_parentheses_and_the_group_operator()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        var a = AddCondition(vm);
        a.Field = "OrganizationName"; a.Operator = Op("eq"); a.Value = "A";

        vm.FilterRoot.AddGroupCommand.Execute(null);
        var group = (QueryFilterGroup)vm.FilterRoot.Children[^1];
        group.SetOpCommand.Execute("or");
        group.AddConditionCommand.Execute(null);
        var b = (QueryFilterCondition)group.Children[0];
        b.Field = "CustomerGroupId"; b.Operator = Op("eq"); b.Value = "B";
        group.AddConditionCommand.Execute(null);
        var c = (QueryFilterCondition)group.Children[1];
        c.Field = "CurrencyCode"; c.Operator = Op("eq"); c.Value = "USD";

        // Root defaults to AND (no redundant outer parens); the nested group combines with OR.
        Assert.Equal("OrganizationName eq 'A' and (CustomerGroupId eq 'B' or CurrencyCode eq 'USD')", vm.BuilderFilter);
        Assert.Equal(3, vm.FilterRoot.ConditionCount);
    }

    [Fact]
    public void Enum_field_exposes_its_members_for_the_value_editor()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        var cond = AddCondition(vm);
        cond.Field = "IsOneTime"; // Enum<NoYes>

        Assert.True(cond.IsEnum);
        // Members still resolve off the LOCAL enum name, which is what the member cache is keyed by.
        Assert.Equal(new[] { "No", "Yes" }, cond.EnumMembers);

        cond.Value = "Yes";
        Assert.Equal("IsOneTime eq Microsoft.Dynamics.DataEntities.NoYes'Yes'", vm.BuilderFilter);
    }

    // --- Qualified enum literals (#179) ---
    // OData v4 requires an enum comparison to name the qualified type ahead of the quoted member. F&O 400s
    // on the bare 'Yes' form for a genuine enum property, so a filter the builder composed visually was
    // unrunnable the moment it touched an enum field.

    [Fact]
    public void Builder_renders_an_enum_condition_as_a_namespace_qualified_literal()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.CrossCompany = true; // keep the company clause out of the way

        var cond = AddCondition(vm);
        cond.Field = "BlockedForInvoice"; // Enum<CustVendorBlocked>
        cond.Operator = Op("ne");
        cond.Value = "Invoice";

        Assert.Equal(
            "BlockedForInvoice ne Microsoft.Dynamics.DataEntities.CustVendorBlocked'Invoice'",
            vm.BuilderFilter);
        // The literal reaches the request URL, not just the preview.
        Assert.Contains(
            "$filter=BlockedForInvoice ne Microsoft.Dynamics.DataEntities.CustVendorBlocked'Invoice'",
            vm.QueryUrl);
    }

    [Fact]
    public void Builder_doubles_an_apostrophe_inside_a_qualified_enum_literal()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.CrossCompany = true;

        var cond = AddCondition(vm);
        cond.Field = "IsOneTime";
        cond.Operator = Op("eq");
        cond.Value = "O'Brien"; // not a real member, but the escaping must survive the prefix

        // The type prefix sits OUTSIDE the quotes; the doubling stays inside them.
        Assert.Equal("IsOneTime eq Microsoft.Dynamics.DataEntities.NoYes'O''Brien'", vm.BuilderFilter);
    }

    // An enum field whose metadata never carried a qualified name — a legacy cache entry, or a fake that
    // only seeded the local name. Guessing a namespace would fabricate a type reference, so the old
    // bare-quoted rendering has to survive untouched.
    private sealed class UnqualifiedEnumMetadata : IMetadataService
    {
        private static readonly EntitySet[] Sets =
            { new("Things", string.Empty, 3, string.Empty, CompanyAware: false, "odata") };

        private static readonly EntityField[] Props =
        {
            new("Id", "String", false, IsKey: true, Length: 10),
            new("Status", "Enum", false, EnumType: "NoYes"), // local name only — no QualifiedEnumType
            new("Flag", "Enum", true, EnumType: "NoYes", QualifiedEnumType: "Contoso.Custom.NoYes"),
        };

        public IReadOnlyList<EntitySet> GetEntities() => Sets;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => Props;
        public IReadOnlyList<string>? GetEnumMembers(string enumType) =>
            string.Equals(enumType, "NoYes", StringComparison.OrdinalIgnoreCase) ? new[] { "No", "Yes" } : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public void An_enum_field_without_a_qualified_name_falls_back_to_the_bare_quoted_literal()
    {
        var vm = new QueryBuilderViewModel(new UnqualifiedEnumMetadata(), new FakeODataClient());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "Things");

        var cond = AddCondition(vm);
        cond.Field = "Status";
        cond.Operator = Op("eq");
        cond.Value = "Yes";

        Assert.True(cond.IsEnum);
        Assert.Equal(new[] { "No", "Yes" }, cond.EnumMembers); // still keyed by the local name
        Assert.Equal("Status eq 'Yes'", vm.BuilderFilter);     // unchanged from before #179
    }

    [Fact]
    public void A_qualified_enum_name_is_emitted_verbatim_whatever_the_namespace()
    {
        var vm = new QueryBuilderViewModel(new UnqualifiedEnumMetadata(), new FakeODataClient());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "Things");

        var cond = AddCondition(vm);
        cond.Field = "Flag";
        cond.Operator = Op("eq");
        cond.Value = "No";

        // The declared name goes through as-is — nothing hardcodes Microsoft.Dynamics.DataEntities.
        Assert.Equal("Flag eq Contoso.Custom.NoYes'No'", vm.BuilderFilter);
    }

    [Fact]
    public void Empty_or_incomplete_conditions_contribute_nothing()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        var cond = AddCondition(vm); // no value yet
        cond.Field = "OrganizationName";

        Assert.True(vm.CrossCompany);                 // company-aware default → no dataAreaId clause
        Assert.Equal(string.Empty, vm.BuilderFilter); // a value-less condition is skipped
        Assert.False(vm.HasEffectiveFilter);          // …so there's no effective filter at all
    }

    [Fact]
    public void Raw_filter_overrides_the_builder_only_in_raw_mode()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        var cond = AddCondition(vm);
        cond.Field = "OrganizationName"; cond.Operator = Op("eq"); cond.Value = "A";
        Assert.Equal("OrganizationName eq 'A'", vm.EffectiveFilter); // builder mode

        vm.IsRawFilterMode = true;
        vm.Filter = "CreditLimit gt 5000";
        Assert.Equal("CreditLimit gt 5000", vm.EffectiveFilter); // raw overrides the builder

        vm.Filter = "   "; // blank raw → falls back to the builder even in raw mode
        Assert.Equal("OrganizationName eq 'A'", vm.EffectiveFilter);
    }

    [Fact]
    public void Cross_company_off_injects_dataAreaId_for_a_company_aware_entity()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3"); // company-aware
        vm.CrossCompany = false;
        vm.Company = "usmf";

        Assert.Equal("dataAreaId eq 'usmf'", vm.EffectiveFilter);
        Assert.Contains("cross-company=false", vm.QueryUrl);

        var cond = AddCondition(vm);
        cond.Field = "OrganizationName"; cond.Operator = Op("eq"); cond.Value = "A";
        Assert.Equal("(dataAreaId eq 'usmf') and (OrganizationName eq 'A')", vm.EffectiveFilter);
    }

    [Fact]
    public void Cross_company_clause_is_not_injected_for_a_non_company_aware_entity()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "ChartOfAccounts"); // not company-aware
        // CrossCompany defaults to the entity's company-awareness (false here) but no dataAreaId applies.
        Assert.False(vm.CrossCompany);
        Assert.Equal(string.Empty, vm.EffectiveFilter);
        Assert.DoesNotContain("dataAreaId", vm.QueryUrl);
        Assert.Contains("cross-company=false", vm.QueryUrl);
    }

    [Fact]
    public async Task Copy_url_copies_the_query_url_to_the_clipboard()
    {
        var clipboard = new FakeClipboardService();
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(), clipboard);
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        await vm.CopyUrlCommand.ExecuteAsync(null);

        Assert.Equal(vm.QueryUrl, clipboard.LastText);
        Assert.Contains("copied", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Changing_the_entity_resets_the_filter_builder_and_mode()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        AddCondition(vm);
        Assert.Equal(1, vm.FilterRoot.ConditionCount);
        vm.IsRawFilterMode = true;
        vm.Filter = "x eq 'y'";

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "SalesOrderHeadersV2"); // different entity

        Assert.Equal(0, vm.FilterRoot.ConditionCount); // a fresh, empty builder
        Assert.False(vm.IsRawFilterMode);              // back to Builder mode
        Assert.Equal(string.Empty, vm.Filter);         // raw text cleared
    }

    [Fact]
    public void Switching_entity_notifies_the_filter_header_and_summary_so_they_cannot_go_stale()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        AddCondition(vm);
        AddCondition(vm);
        AddCondition(vm);
        Assert.Equal("Filter · 3", vm.FilterTabHeader);

        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is not null) raised.Add(e.PropertyName); };

        // Both entities are company-aware and we stay in builder mode with no raw text, so CrossCompany,
        // IsRawFilterMode and Filter don't change on the switch — only LoadFields/RebuildFilterContext run.
        // Without an explicit notification the bound Filter tab header would keep showing "Filter · 3".
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "SalesOrderHeadersV2");

        Assert.Equal(0, vm.FilterRoot.ConditionCount); // fresh, empty builder for the new entity
        Assert.Contains(nameof(QueryBuilderViewModel.FilterTabHeader), raised);
        Assert.Contains(nameof(QueryBuilderViewModel.FilterSummary), raised);
    }

    [Fact]
    public void Field_chips_carry_type_and_mandatory_metadata()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        var currency = vm.Fields.Single(f => f.Name == "CurrencyCode"); // mandatory, non-key String(3)
        Assert.True(currency.ShowReq);
        Assert.Equal("String(3)", currency.TypeDisplay);

        var isOneTime = vm.Fields.Single(f => f.Name == "IsOneTime");
        Assert.Equal("Enum<NoYes>", isOneTime.TypeDisplay);

        var key = vm.Fields.Single(f => f.Name == "CustomerAccount"); // key → PK marker, not REQ
        Assert.False(key.ShowReq);
    }

    [Fact]
    public void Function_operators_are_hidden_for_numeric_fields_and_a_selected_one_is_reset()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        var cond = AddCondition(vm);

        cond.Field = "OrganizationName"; // string → function ops offered
        Assert.Contains(cond.Operators, o => o.Op == "contains");
        cond.Operator = Op("contains");

        cond.Field = "CreditLimit"; // numeric → function ops hidden + the selected one falls back
        Assert.DoesNotContain(cond.Operators, o => o.IsFunction);
        Assert.False(cond.Operator.IsFunction);
    }

    [Fact]
    public void Whitespace_only_value_contributes_no_filter()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        var cond = AddCondition(vm);
        cond.Field = "OrganizationName";
        cond.Value = "   "; // whitespace is treated as empty

        Assert.Equal(string.Empty, vm.BuilderFilter);
    }

    [Fact]
    public void Root_group_omits_redundant_outer_parentheses()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        var a = AddCondition(vm); a.Field = "OrganizationName"; a.Operator = Op("eq"); a.Value = "A";
        var b = AddCondition(vm); b.Field = "CustomerGroupId"; b.Operator = Op("eq"); b.Value = "B";

        Assert.Equal("OrganizationName eq 'A' and CustomerGroupId eq 'B'", vm.BuilderFilter);
    }

    // --- Literal syntax per field type (#161) ---
    //
    // The old numeric test matched "Decimal|Int|Int32|Int64|Real" — "Int"/"Real" are WPF-era names the real
    // metadata mapper never emits — so every DateTime/Date/Boolean/Guid/Double/Int16 value was single-quoted
    // and F&O answered 400 ("incompatible types Edm.DateTimeOffset and Edm.String").

    [Theory]
    // Field, operator, typed value, expected rendering — all on WorkerV2, whose seeded fields cover the
    // non-string types the mapper produces.
    [InlineData("BirthDate", "eq", "2026-01-01", "BirthDate eq 2026-01-01")]
    [InlineData("EmploymentStartDateTime", "ge", "2026-01-01T00:00:00Z", "EmploymentStartDateTime ge 2026-01-01T00:00:00Z")]
    [InlineData("IsContractor", "eq", "true", "IsContractor eq true")]
    [InlineData("WorkerRecId", "eq", "6b1e1f77-9a1e-4c8e-9a4f-2f3d8a0c1b55", "WorkerRecId eq 6b1e1f77-9a1e-4c8e-9a4f-2f3d8a0c1b55")]
    [InlineData("PartyNumber", "gt", "100", "PartyNumber gt 100")]
    public void Builder_emits_bare_literals_for_non_string_types(string field, string op, string value, string expected)
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "WorkerV2");

        var cond = AddCondition(vm);
        cond.Field = field;
        cond.Operator = Op(op);
        cond.Value = value;

        Assert.Equal(expected, vm.BuilderFilter);
        Assert.DoesNotContain("'", vm.BuilderFilter); // no quotes anywhere — not even around part of it
    }

    [Theory]
    [InlineData("true", "IsContractor eq true")]
    [InlineData("True", "IsContractor eq true")]   // OData wants the lowercase form
    [InlineData("FALSE", "IsContractor eq false")]
    [InlineData("yes", "IsContractor eq yes")]     // not a boolean word → through as typed, server rejects it
    public void Builder_normalises_boolean_words_and_passes_anything_else_through(string value, string expected)
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "WorkerV2");

        var cond = AddCondition(vm);
        cond.Field = "IsContractor";
        cond.Operator = Op("eq");
        cond.Value = value;

        Assert.Equal(expected, vm.BuilderFilter);
    }

    [Fact]
    public void Builder_still_quotes_strings_and_enum_members()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        vm.CrossCompany = true; // keep the company clause out of the way

        var text = AddCondition(vm);
        text.Field = "OrganizationName"; text.Operator = Op("eq"); text.Value = "O'Brien";
        Assert.Equal("OrganizationName eq 'O''Brien'", vm.BuilderFilter); // embedded quote still doubled

        var enumCond = AddCondition(vm);
        enumCond.Field = "IsOneTime"; enumCond.Operator = Op("eq"); enumCond.Value = "Yes";
        // A string stays plainly quoted; the enum member gains its qualified type prefix (#179).
        Assert.Equal(
            "OrganizationName eq 'O''Brien' and IsOneTime eq Microsoft.Dynamics.DataEntities.NoYes'Yes'",
            vm.BuilderFilter);
    }

    [Fact]
    public void Function_operators_are_hidden_for_a_date_time_field_and_a_selected_one_is_reset()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "WorkerV2");
        var cond = AddCondition(vm);

        cond.Field = "Name"; // string → function ops offered
        Assert.Contains(cond.Operators, o => o.Op == "contains");
        cond.Operator = Op("contains");

        cond.Field = "EmploymentStartDateTime"; // DateTime → hidden, and the selected one falls back
        Assert.False(cond.SupportsFunctions);
        Assert.DoesNotContain(cond.Operators, o => o.IsFunction);
        Assert.False(cond.Operator.IsFunction);
    }

    [Theory]
    [InlineData("BirthDate")]
    [InlineData("IsContractor")]
    [InlineData("WorkerRecId")]
    [InlineData("PartyNumber")]
    public void Function_operators_are_offered_only_for_string_fields(string field)
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "WorkerV2");

        var cond = AddCondition(vm);
        cond.Field = field;

        Assert.DoesNotContain(cond.Operators, o => o.IsFunction);
    }

    // --- Company scoping against index-shaped metadata (#161) ---

    // Mirrors the real CoreMetadataService projection: entities come from the OData entity INDEX, which
    // carries no field data, so EntitySet.CompanyAware is hardcoded false there. Only the per-entity field
    // load reveals a dataAreaId — so that, not the flag, is what company scoping has to be gated on.
    private sealed class IndexShapedMetadata : IMetadataService
    {
        private static readonly EntitySet[] Sets =
        {
            new("CustomersV3", string.Empty, 2, string.Empty, CompanyAware: false, "odata"),
            new("SystemUsers", string.Empty, 2, string.Empty, CompanyAware: false, "odata"),
        };

        private static readonly Dictionary<string, IReadOnlyList<EntityField>> Fields = new()
        {
            ["CustomersV3"] = new EntityField[]
            {
                new("dataAreaId", "String", false, IsKey: true, Length: 4),
                new("CustomerAccount", "String", false, IsKey: true, Length: 20),
            },
            ["SystemUsers"] = new EntityField[]
            {
                new("UserId", "String", false, IsKey: true, Length: 20),
                new("Email", "String", true, Length: 80),
            },
        };

        public IReadOnlyList<EntitySet> GetEntities() => Sets;
        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            Fields.TryGetValue(entityName, out var fields) ? fields : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            Task.FromResult(Fields.ContainsKey(entityName));
    }

    [Fact]
    public void Company_scoping_is_gated_on_the_loaded_dataAreaId_field_not_the_entity_index()
    {
        var vm = new QueryBuilderViewModel(new IndexShapedMetadata(), new FakeODataClient());

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3"); // index says CompanyAware=false…
        Assert.True(vm.IsCompanyAware);                                      // …but its fields carry dataAreaId
        vm.CrossCompany = false;
        vm.Company = "usmf";

        Assert.Equal("dataAreaId eq 'usmf'", vm.EffectiveFilter);
        Assert.Contains("$filter=dataAreaId eq 'usmf'", vm.QueryUrl);
    }

    [Fact]
    public void Cross_company_defaults_to_the_entitys_real_company_awareness()
    {
        var vm = new QueryBuilderViewModel(new IndexShapedMetadata(), new FakeODataClient());

        // Company-aware → query across companies until the user opts into a single one (which is what
        // injects the dataAreaId clause); a global entity has nothing to scope.
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        Assert.True(vm.CrossCompany);

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "SystemUsers");
        Assert.False(vm.CrossCompany);
    }

    [Fact]
    public void Company_scoping_is_skipped_for_a_loaded_entity_without_a_dataAreaId_field()
    {
        var vm = new QueryBuilderViewModel(new IndexShapedMetadata(), new FakeODataClient());

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "SystemUsers"); // global entity
        Assert.False(vm.IsCompanyAware);
        Assert.False(vm.CrossCompany); // nothing to scope, so no cross-company default either
        vm.Company = "usmf";

        Assert.Equal(string.Empty, vm.EffectiveFilter);
        Assert.DoesNotContain("dataAreaId", vm.QueryUrl);
    }

    // As DeferredMetadata, but the entity that arrives is company-aware: nothing is served until a load
    // runs, then one entity whose FIELDS carry dataAreaId while its index flag stays false (production's
    // shape). Company-awareness therefore can't be known at selection time — only when the fields land.
    private sealed class DeferredCompanyMetadata : IMetadataService
    {
        private bool _loaded;
        private static readonly EntitySet[] Late =
            { new("CustomersV3", string.Empty, 2, string.Empty, CompanyAware: false, "odata") };
        private static readonly EntityField[] LateFields =
        {
            new("dataAreaId", "String", false, IsKey: true, Length: 4),
            new("CustomerAccount", "String", false, IsKey: true, Length: 20),
        };

        public IReadOnlyList<EntitySet> GetEntities() => _loaded ? Late : Array.Empty<EntitySet>();
        public IReadOnlyList<EntityField>? GetFields(string entityName) => _loaded ? LateFields : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) { _loaded = true; return Task.CompletedTask; }
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        { _loaded = true; return Task.FromResult(true); }
    }

    [Fact]
    public async Task Company_awareness_lands_with_the_fields_and_notifies_the_badge()
    {
        var vm = new QueryBuilderViewModel(new DeferredCompanyMetadata(), new FakeODataClient());
        Assert.False(vm.IsCompanyAware); // no fields yet → don't claim awareness

        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is not null) raised.Add(e.PropertyName); };

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.True(vm.IsCompanyAware);
        Assert.Contains(nameof(QueryBuilderViewModel.IsCompanyAware), raised); // the badge binds to this

        vm.CrossCompany = false;
        Assert.Equal("dataAreaId eq 'usmf'", vm.EffectiveFilter);
    }

    // --- Collection-typed fields are not scalar-filterable (#161) ---

    // MapType labels a Collection(...) property "Collection". No scalar comparison exists for one — OData
    // filters a collection only through an any()/all() lambda, which the visual builder can't compose — so
    // it must never reach the field dropdown; "Tags eq 'foo'" is a 400 from F&O. Shaped like the real
    // projection: scalars and collections side by side on one entity, plus an entity that's all collection.
    private sealed class CollectionFieldMetadata : IMetadataService
    {
        private static readonly EntitySet[] Sets =
        {
            new("Products", string.Empty, 4, string.Empty, CompanyAware: false, "odata"),
            new("TagBags", string.Empty, 1, string.Empty, CompanyAware: false, "odata"),
        };

        private static readonly Dictionary<string, IReadOnlyList<EntityField>> Fields = new()
        {
            ["Products"] = new EntityField[]
            {
                new("ItemNumber", "String", false, IsKey: true, Length: 20),
                new("Tags", "Collection", true),          // Collection(Edm.String)
                new("Price", "Decimal", true, Precision: 32, Scale: 2),
                new("AlternateKeys", "Collection", true), // Collection(Edm.Guid)
            },
            ["TagBags"] = new EntityField[]
            {
                new("Tags", "Collection", true),
            },
        };

        public IReadOnlyList<EntitySet> GetEntities() => Sets;
        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            Fields.TryGetValue(entityName, out var fields) ? fields : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            Task.FromResult(Fields.ContainsKey(entityName));
    }

    [Fact]
    public void Collection_typed_fields_are_not_offered_in_the_filter_builder()
    {
        var vm = new QueryBuilderViewModel(new CollectionFieldMetadata(), new FakeODataClient());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "Products");

        var cond = AddCondition(vm);

        // Nothing collection-typed reaches the dropdown, so no condition can be built on one…
        Assert.DoesNotContain("Tags", cond.FieldNames);
        Assert.DoesNotContain("AlternateKeys", cond.FieldNames);
        // …and a new condition defaults to the first *scalar*, never to a collection.
        Assert.Equal("ItemNumber", cond.Field);
    }

    [Fact]
    public void Scalar_fields_are_still_offered_alongside_excluded_collections()
    {
        var vm = new QueryBuilderViewModel(new CollectionFieldMetadata(), new FakeODataClient());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "Products");

        var cond = AddCondition(vm);

        // Only the collections are dropped; the scalars keep their metadata order and stay filterable.
        Assert.Equal(new[] { "ItemNumber", "Price" }, cond.FieldNames);

        cond.Field = "Price";
        cond.Operator = Op("gt");
        cond.Value = "10";
        Assert.Equal("Price gt 10", vm.BuilderFilter);
    }

    [Fact]
    public void A_condition_on_a_field_absent_from_the_context_renders_without_throwing()
    {
        // An excluded collection field — or a condition naming a field the selected entity doesn't have —
        // has no metadata in the context, so Meta() returns null, LiteralKind(null) quotes it and Render()
        // emits a plain string comparison. Missing metadata must stay a no-op, never a crash.
        var context = new QueryFilterContext(
            new EntityField[]
            {
                new("ItemNumber", "String", false, IsKey: true, Length: 20),
                new("Tags", "Collection", true),
            },
            _ => Array.Empty<string>());

        Assert.DoesNotContain("Tags", context.FieldNames);
        Assert.Null(context.Meta("Tags"));

        var cond = new QueryFilterCondition(context, () => { }) { Field = "Tags", Value = "foo" };

        Assert.Equal("Tags eq 'foo'", cond.Render());
    }

    [Fact]
    public void An_entity_whose_fields_are_all_collections_offers_nothing_to_filter_on()
    {
        var vm = new QueryBuilderViewModel(new CollectionFieldMetadata(), new FakeODataClient());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "TagBags");

        var cond = AddCondition(vm);

        Assert.Empty(cond.FieldNames);
        Assert.Null(cond.Field);   // nothing to default to…
        cond.Value = "foo";
        Assert.Equal(string.Empty, vm.BuilderFilter); // …so the condition contributes no filter
    }

    // --- Failing clipboard / file-save seams (#163) ---
    // A contended clipboard throws (COMException on Windows) and a locked file throws IOException. Left
    // unhandled, the generated AsyncRelayCommand rethrows the faulted task on the dispatcher, which takes
    // the whole app down; every one of these has to end as a status line instead.

    private sealed class ThrowingClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => throw new InvalidOperationException("clipboard is busy");
    }

    private sealed class ThrowingFileSave : IFileSaveService
    {
        private readonly Exception _ex;
        public ThrowingFileSave(Exception ex) => _ex = ex;
        public Task<string?> SaveTextAsync(string suggestedFileName, string content, CancellationToken ct = default) =>
            throw _ex;
    }

    [Fact]
    public async Task Copy_url_survives_a_failing_clipboard()
    {
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(), new ThrowingClipboard());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        await vm.CopyUrlCommand.ExecuteAsync(null); // awaiting proves the command task completed, not faulted

        Assert.Contains("clipboard", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clipboard is busy", vm.StatusText);
    }

    [Fact]
    public async Task Export_csv_to_the_clipboard_survives_a_failing_clipboard()
    {
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(), new ThrowingClipboard());
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        await vm.RunCommand.ExecuteAsync(null);

        await vm.ExportCsvCommand.ExecuteAsync(null);

        Assert.Contains("clipboard", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clipboard is busy", vm.StatusText);
    }

    [Fact]
    public async Task Export_csv_to_file_survives_a_locked_file()
    {
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(),
            fileSave: new ThrowingFileSave(new IOException("the file is in use")));
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        await vm.RunCommand.ExecuteAsync(null);

        await vm.ExportCsvFileCommand.ExecuteAsync(null);

        Assert.Contains("Export failed", vm.StatusText);
        Assert.Contains("the file is in use", vm.StatusText);
    }

    [Fact]
    public async Task Export_csv_to_file_reports_a_cancelled_save()
    {
        // A picker that throws OCE (rather than returning null) is still a cancellation, not a failure.
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(),
            fileSave: new ThrowingFileSave(new OperationCanceledException()));
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        await vm.RunCommand.ExecuteAsync(null);

        await vm.ExportCsvFileCommand.ExecuteAsync(null);

        Assert.Equal("Export cancelled.", vm.StatusText);
    }
}
