using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
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
            Logger = NullLogger.Instance;
        }

        public FoEnvironment CurrentEnv { get; set; }
        public IODataClient OData { get; set; }
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

    private sealed class FakeMetadataProvider : IMetadataProvider
    {
        public Task<ODataMetadata> GetMetadataAsync(string envId, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            var entity = new ODataEntity("Customers",
                new[] { new ODataProperty("AccountNumber", "Edm.String", false), new ODataProperty("dataAreaId", "Edm.String", true) },
                new[] { new ODataNavigationProperty("SalesOrders", "Collection(Default.SalesOrder)") });
            return Task.FromResult(new ODataMetadata(new[] { entity }, null));
        }
    }

    [Fact]
    public async Task BuildQueryRequest_Uses_Metadata_Fields_And_Filter()
    {
        var vm = new QueryBuilderViewModel(new FakeContext(), new FakeMetadataProvider());
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
        var vm = new QueryBuilderViewModel(ctx, new FakeMetadataProvider());
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
        var vm = new QueryBuilderViewModel(new FakeContext(), new FakeMetadataProvider());
        await vm.LoadEntitiesCommand.ExecuteAsync();
        vm.SelectedEntity = "Customers";
        vm.ExpandPath = "BadNav";
        var ok = vm.TryBuildQueryRequest(out _);
        Assert.False(ok);
        Assert.NotNull(vm.ValidationWarning);
    }
}
