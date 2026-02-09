using FoToolbox.Core.Models;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using FoToolbox.Core.Export;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilderPlugin;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class QueryBuilderViewModelTests
{
    private sealed class FakeContext : IPluginContext
    {
        public FakeContext()
        {
            CurrentEnv = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");
            OData = new FakeODataClient();
            Catalog = new FakeCatalogService();
            Logger = NullLogger.Instance;
        }

        public FoEnvironment CurrentEnv { get; set; }
        public IODataClient OData { get; set; }
        public ICatalogService Catalog { get; }
        public Microsoft.Extensions.Logging.ILogger Logger { get; }
    }

    private sealed class FakeODataClient : IODataClient
    {
        public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, System.Threading.CancellationToken cancellationToken = default)
            => ODataClientExtensions.EmptyPages(cancellationToken);
    }

    private sealed class PagedODataClient : IODataClient
    {
        private readonly Queue<ODataPage> _pages;

        public PagedODataClient()
        {
            _pages = new Queue<ODataPage>(new[]
            {
                new ODataPage(new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { { "AccountNumber", "A1" } } }, "next"),
                new ODataPage(new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { { "AccountNumber", "A2" } } }, null)
            });
        }

        public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            if (_pages.Count > 0)
            {
                yield return _pages.Dequeue();
            }
        }
    }

    private sealed class FakeCatalogService : ICatalogService
    {
        public Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, System.Threading.CancellationToken ct = default)
        {
            var catalog = new TableCatalog("test", "Test", System.DateTime.UtcNow, System.Array.Empty<TableInfo>());
            return Task.FromResult(catalog);
        }

        public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, System.Threading.CancellationToken ct = default)
        {
            var entity = new ODataEntity("Customers",
                new[] { new ODataProperty("AccountNumber", "Edm.String", false), new ODataProperty("dataAreaId", "Edm.String", true) },
                new[] { new ODataNavigationProperty("SalesOrders", "Collection(Default.SalesOrder)") });
            return Task.FromResult(new ODataMetadata(new[] { entity }, System.Array.Empty<ODataEnumType>(), null));
        }

        public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, System.Threading.CancellationToken ct = default)
        {
            var tables = new TableCatalog("test", "Test", System.DateTime.UtcNow, System.Array.Empty<TableInfo>());
            var metadata = new ODataMetadata(System.Array.Empty<ODataEntity>(), System.Array.Empty<ODataEnumType>(), null);
            return Task.FromResult(new CatalogSnapshot(env.Id, env.BaseUrl, tables, metadata, System.DateTime.UtcNow));
        }

        public Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, System.Threading.CancellationToken ct = default)
        {
            var catalog = new TableCatalog("import", "UserImport", System.DateTime.UtcNow, System.Array.Empty<TableInfo>());
            return Task.FromResult(catalog);
        }

        public Task<string> GetTableBrowserUrlTemplateAsync(System.Threading.CancellationToken ct = default)
            => Task.FromResult("{BaseUrl}/?mi=SysTableBrowser&table={TableName}");

        public Task SetTableBrowserUrlTemplateAsync(string template, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;

        public string BuildTableBrowserUrl(FoEnvironment env, string tableName)
            => $"{env.BaseUrl}/?mi=SysTableBrowser&table={tableName}";

        public string BuildODataEntityUrl(FoEnvironment env, string entityName)
            => $"{env.BaseUrl}/data/{entityName}";
    }

    [Fact]
    public async Task BuildQueryRequest_Uses_Metadata_Fields_And_Filter()
    {
        var vm = new QueryBuilderViewModel(new FakeContext());
        await vm.LoadEntitiesCommand.ExecuteAsync();
        vm.SelectedEntity = "Customers";
        vm.UpdateSelectedFields(new List<string> { "AccountNumber", "SalesOrders" });
        vm.CrossCompany = false;
        vm.Company = "USMF";
        vm.FilterText = "AccountNumber eq 'A0001'";
        vm.Count = true;
        vm.OrderBy = "AccountNumber asc";

        var req = vm.BuildQueryRequest();
        Assert.Equal("https://contoso.operations.dynamics.com/data/Customers?$select=AccountNumber,SalesOrders&$filter=%28dataAreaId%20eq%20%27USMF%27%29%20and%20%28AccountNumber%20eq%20%27A0001%27%29&$orderby=AccountNumber%20asc&$count=true", req.Url);
    }

    [Fact]
    public async Task LoadMore_Appends_Rows_When_NextLink_Exists()
    {
        var ctx = new FakeContext();
        ctx.OData = new PagedODataClient();
        var vm = new QueryBuilderViewModel(ctx);
        await vm.LoadEntitiesCommand.ExecuteAsync();
        vm.SelectedEntity = "Customers";
        vm.UpdateSelectedFields(new List<string> { "AccountNumber" });

        await vm.PreviewCommand.ExecuteAsync();
        var table = vm.PreviewTable?.Table;
        Assert.NotNull(table);
        Assert.Equal(1, table!.Rows.Count);

        await vm.LoadMoreCommand.ExecuteAsync();
        table = vm.PreviewTable?.Table;
        Assert.NotNull(table);
        Assert.Equal(2, table!.Rows.Count);
    }

    [Fact]
    public async Task Invalid_Expand_Path_Blocks_Request()
    {
        var vm = new QueryBuilderViewModel(new FakeContext());
        await vm.LoadEntitiesCommand.ExecuteAsync();
        vm.SelectedEntity = "Customers";
        vm.ExpandPath = "BadNav";
        var ok = vm.TryBuildQueryRequest(out _);
        Assert.False(ok);
        Assert.NotNull(vm.ValidationWarning);
    }

    [Fact]
    public async Task Raw_Filter_Overrides_Builder_Errors()
    {
        var vm = new QueryBuilderViewModel(new FakeContext());
        await vm.LoadEntitiesCommand.ExecuteAsync();
        vm.SelectedEntity = "Customers";
        vm.RootGroup.Children.Add(new FilterConditionViewModel { Field = string.Empty, Operator = "eq", Value = string.Empty });
        vm.FilterText = "AccountNumber eq 'A0001'";

        var ok = vm.TryBuildQueryRequest(out var request);

        Assert.True(ok);
        Assert.Contains("$filter=AccountNumber%20eq%20%27A0001%27", request.Url);
    }
}
