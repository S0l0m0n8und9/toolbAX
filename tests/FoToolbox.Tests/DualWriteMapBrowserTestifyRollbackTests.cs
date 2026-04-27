using DualWriteMapBrowserPlugin;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Net.Http;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class DualWriteMapBrowserTestifyRollbackTests
{
    [Fact]
    public async Task TryDeleteTestifyRecordAsync_Treats404AsSuccessAndClearsPersistedState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-rollback.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var config = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            var instanceUrl = "https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-0001',dataAreaId='USMF')?cross-company=true";
            config.LastRunToken = "TESTIFY-123";
            config.LastEntityInstanceUrl = instanceUrl;
            await store.SaveAsync(config, CancellationToken.None);

            var deleteClient = new RecordingODataWriteClient(new ODataWriteResponse(404, null, new Dictionary<string, string>()));
            var viewModel = new DualWriteMapBrowserViewModel(new FakeWriteContext(deleteClient), store);

            var deleted = await viewModel.TryDeleteTestifyRecordAsync(
                "Map A",
                "map-a",
                config,
                instanceUrl,
                "Rollback",
                CancellationToken.None);

            Assert.True(deleted);
            Assert.Single(deleteClient.Requests);
            Assert.Equal(HttpMethod.Delete, deleteClient.Requests[0].Method);
            Assert.Equal(instanceUrl, deleteClient.Requests[0].Url);

            var reloaded = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            Assert.Null(reloaded.LastEntityInstanceUrl);
            Assert.Null(reloaded.LastRunToken);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task TryDeleteTestifyRecordAsync_KeepsPersistedStateWhenDeleteFails()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-rollback.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var config = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            var instanceUrl = "https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-0001',dataAreaId='USMF')?cross-company=true";
            config.LastRunToken = "TESTIFY-123";
            config.LastEntityInstanceUrl = instanceUrl;
            await store.SaveAsync(config, CancellationToken.None);

            var deleteClient = new RecordingODataWriteClient(new ODataWriteResponse(500, "Server error", new Dictionary<string, string>()));
            var viewModel = new DualWriteMapBrowserViewModel(new FakeWriteContext(deleteClient), store);

            var deleted = await viewModel.TryDeleteTestifyRecordAsync(
                "Map A",
                "map-a",
                config,
                instanceUrl,
                "Rollback",
                CancellationToken.None);

            Assert.False(deleted);

            var reloaded = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            Assert.Equal(instanceUrl, reloaded.LastEntityInstanceUrl);
            Assert.Equal("TESTIFY-123", reloaded.LastRunToken);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task FinalizeTestifyFailureAsync_SkipsRollbackForReusedRecord()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-rollback.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var config = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            var instanceUrl = "https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-0001',dataAreaId='USMF')?cross-company=true";
            config.LastRunToken = "TESTIFY-123";
            config.LastEntityInstanceUrl = instanceUrl;
            await store.SaveAsync(config, CancellationToken.None);

            var deleteClient = new RecordingODataWriteClient(new ODataWriteResponse(204, null, new Dictionary<string, string>()));
            var viewModel = new DualWriteMapBrowserViewModel(new FakeWriteContext(deleteClient), store);

            var status = await viewModel.FinalizeTestifyFailureAsync(
                "Map A",
                "map-a",
                config,
                createdThisRun: false,
                "PATCH step 1 failed.",
                CancellationToken.None);

            Assert.Equal("PATCH step 1 failed.", status);
            Assert.Empty(deleteClient.Requests);

            var reloaded = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            Assert.Equal(instanceUrl, reloaded.LastEntityInstanceUrl);
            Assert.Equal("TESTIFY-123", reloaded.LastRunToken);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class FakeWriteContext : IPluginContext, IPluginContextWrite
    {
        public FakeWriteContext(IODataWriteClient writeClient)
        {
            CurrentEnv = new FoEnvironment("env-1", "Env 1", "https://contoso.operations.dynamics.com", "tenant", "USMF");
            OData = new FakeODataClient();
            Catalog = new FakeCatalogService();
            Logger = NullLogger.Instance;
            ODataWrite = writeClient;
        }

        public FoEnvironment CurrentEnv { get; set; }
        public IODataClient OData { get; }
        public ICatalogService Catalog { get; }
        public Microsoft.Extensions.Logging.ILogger Logger { get; }
        public IODataWriteClient ODataWrite { get; }
    }

    private sealed class RecordingODataWriteClient : IODataWriteClient
    {
        private readonly ODataWriteResponse _response;

        public RecordingODataWriteClient(ODataWriteResponse response)
        {
            _response = response;
        }

        public List<ODataWriteRequest> Requests { get; } = new();

        public Task<ODataWriteResponse> SendAsync(ODataWriteRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_response);
        }
    }

    private sealed class FakeODataClient : IODataClient
    {
        public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, CancellationToken cancellationToken = default) =>
            ODataClientExtensions.EmptyPages(cancellationToken);
    }

    private sealed class FakeCatalogService : ICatalogService
    {
        public Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default) =>
            Task.FromResult(new TableCatalog("test", "Test", DateTime.UtcNow, Array.Empty<TableInfo>()));

        public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default) =>
            Task.FromResult(new ODataMetadata(Array.Empty<ODataEntity>(), Array.Empty<ODataEnumType>(), null));

        public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default) =>
            Task.FromResult(new CatalogSnapshot(env.Id, env.BaseUrl, new TableCatalog("test", "Test", DateTime.UtcNow, Array.Empty<TableInfo>()), new ODataMetadata(Array.Empty<ODataEntity>(), Array.Empty<ODataEnumType>(), null), DateTime.UtcNow));

        public Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default) => Task.CompletedTask;

        public Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default) =>
            Task.FromResult(new TableCatalog("import", "Import", DateTime.UtcNow, Array.Empty<TableInfo>()));

        public Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default) =>
            Task.FromResult("{BaseUrl}/?mi=SysTableBrowser&table={TableName}");

        public Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default) => Task.CompletedTask;

        public string BuildTableBrowserUrl(FoEnvironment env, string tableName) =>
            $"{env.BaseUrl}/?mi=SysTableBrowser&table={tableName}";

        public string BuildODataEntityUrl(FoEnvironment env, string entityName) =>
            $"{env.BaseUrl}/data/{entityName}";
    }
}
