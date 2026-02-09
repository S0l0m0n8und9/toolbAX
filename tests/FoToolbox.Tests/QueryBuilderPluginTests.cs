using FoToolbox.Core.OData;
using FoToolbox.Core.Catalog;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilderPlugin;
using QB = QueryBuilderPlugin;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class QueryBuilderPluginTests
{
    private sealed class FakeContext : IPluginContext
    {
        public FakeContext()
        {
            CurrentEnv = new FoToolbox.Core.Models.FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");
            OData = new FakeODataClient();
            Catalog = new FakeCatalogService();
            Logger = NullLogger.Instance;
        }

        public FoToolbox.Core.Models.FoEnvironment CurrentEnv { get; set; }
        public IODataClient OData { get; }
        public ICatalogService Catalog { get; }
        public Microsoft.Extensions.Logging.ILogger Logger { get; }
    }

    private sealed class FakeODataClient : IODataClient
    {
        public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, System.Threading.CancellationToken cancellationToken = default)
            => ODataClientExtensions.EmptyPages(cancellationToken);
    }

    [Fact]
#pragma warning disable xUnit1031 // STA thread requires blocking init
    public void Plugin_Initializes_And_Creates_View()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var plugin = new QB.QueryBuilderPlugin();
                plugin.InitializeAsync(new FakeContext()).GetAwaiter().GetResult();
                Assert.Equal("fo.querybuilder", plugin.Id);
                Assert.Equal("fo.querybuilder", plugin.Manifest.Id);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null) throw failure;
    }
#pragma warning restore xUnit1031

    private sealed class FakeCatalogService : ICatalogService
    {
        public Task<TableCatalog> GetTablesAsync(FoToolbox.Core.Models.FoEnvironment env, CatalogRefreshMode mode, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new TableCatalog("test", "Test", System.DateTime.UtcNow, System.Array.Empty<TableInfo>()));

        public Task<ODataMetadata> GetODataMetadataAsync(FoToolbox.Core.Models.FoEnvironment env, CatalogRefreshMode mode, System.Threading.CancellationToken ct = default)
        {
            var entity = new ODataEntity("Customers", new[] { new ODataProperty("AccountNumber", "Edm.String", false), new ODataProperty("Name", "Edm.String", true) }, System.Array.Empty<ODataNavigationProperty>());
            return Task.FromResult(new ODataMetadata(new[] { entity }, System.Array.Empty<ODataEnumType>(), null));
        }

        public Task<CatalogSnapshot> GetSnapshotAsync(FoToolbox.Core.Models.FoEnvironment env, CatalogRefreshMode mode, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new CatalogSnapshot(env.Id, env.BaseUrl, new TableCatalog("test", "Test", System.DateTime.UtcNow, System.Array.Empty<TableInfo>()), new ODataMetadata(System.Array.Empty<ODataEntity>(), System.Array.Empty<ODataEnumType>(), null), System.DateTime.UtcNow));

        public Task RefreshAsync(FoToolbox.Core.Models.FoEnvironment env, CatalogRefreshScope scope, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<TableCatalog> ImportTableCatalogAsync(FoToolbox.Core.Models.FoEnvironment env, string json, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new TableCatalog("import", "UserImport", System.DateTime.UtcNow, System.Array.Empty<TableInfo>()));

        public Task<string> GetTableBrowserUrlTemplateAsync(System.Threading.CancellationToken ct = default)
            => Task.FromResult("{BaseUrl}/?mi=SysTableBrowser&table={TableName}");

        public Task SetTableBrowserUrlTemplateAsync(string template, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;

        public string BuildTableBrowserUrl(FoToolbox.Core.Models.FoEnvironment env, string tableName)
            => $"{env.BaseUrl}/?mi=SysTableBrowser&table={tableName}";

        public string BuildODataEntityUrl(FoToolbox.Core.Models.FoEnvironment env, string entityName)
            => $"{env.BaseUrl}/data/{entityName}";
    }
}
