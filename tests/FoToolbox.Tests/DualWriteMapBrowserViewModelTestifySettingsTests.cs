using DualWriteMapBrowserPlugin;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class DualWriteMapBrowserViewModelTestifySettingsTests
{
    [Fact]
    public async Task SaveAndReload_PersistsSelectedMapSettingsAcrossViewModelInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-settings.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var seeded = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            seeded.OmitCreateFields = new HashSet<string>(new[] { "FieldA" }, StringComparer.OrdinalIgnoreCase);
            seeded.PreferredCreateValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CurrencyCode"] = "USD"
            };
            seeded.CePollTimeoutMinutes = 7;
            seeded.AllowPartialEnumCoverage = true;
            await store.SaveAsync(seeded, CancellationToken.None);

            var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), store);

            viewModel.SelectedRecord = CreateRecord("map-a", "Map A");
            await WaitForAsync(() => viewModel.TestifyCePollTimeoutMinutesText == "7");

            Assert.Equal("FieldA", viewModel.TestifyOmitCreateFieldsText);
            Assert.Equal("CurrencyCode=USD", viewModel.TestifyPreferredCreateValuesText);
            Assert.Equal("7", viewModel.TestifyCePollTimeoutMinutesText);
            Assert.True(viewModel.TestifyAllowPartialEnumCoverage);

            viewModel.SelectedRecord = CreateRecord("map-b", "Map B");
            await WaitForAsync(() => viewModel.TestifyCePollTimeoutMinutesText == "5");

            Assert.Equal(string.Empty, viewModel.TestifyOmitCreateFieldsText);
            Assert.Equal(string.Empty, viewModel.TestifyPreferredCreateValuesText);
            Assert.False(viewModel.TestifyAllowPartialEnumCoverage);

            viewModel.TestifyOmitCreateFieldsText = "FieldB\r\nFieldC";
            viewModel.TestifyPreferredCreateValuesText = "NumberSequenceGroup=STD";
            viewModel.TestifyCePollTimeoutMinutesText = "11";
            viewModel.TestifyAllowPartialEnumCoverage = true;

            await viewModel.SaveTestifySettingsCommand.ExecuteAsync();

            var reloadedViewModel = new DualWriteMapBrowserViewModel(new FakeContext(), store);
            reloadedViewModel.SelectedRecord = CreateRecord("map-b", "Map B");
            await WaitForAsync(() => reloadedViewModel.TestifyCePollTimeoutMinutesText == "11");

            Assert.Equal("FieldB\r\nFieldC", reloadedViewModel.TestifyOmitCreateFieldsText);
            Assert.Equal("NumberSequenceGroup=STD", reloadedViewModel.TestifyPreferredCreateValuesText);
            Assert.Equal("11", reloadedViewModel.TestifyCePollTimeoutMinutesText);
            Assert.True(reloadedViewModel.TestifyAllowPartialEnumCoverage);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static DualWriteMapRecord CreateRecord(string id, string displayName) =>
        new(
            id,
            solutionId: string.Empty,
            name: displayName.Replace(" ", string.Empty, StringComparison.Ordinal),
            displayName,
            version: "1.0.0.0",
            state: "Active",
            status: "Live",
            owner: "tester",
            createdOn: null,
            modifiedOn: null,
            mappingRows: Array.Empty<JsonTableRow>(),
            mappingSummaryRows: Array.Empty<MappingSummaryRow>(),
            mappingLegRows: Array.Empty<MappingLegRow>(),
            mappingFieldRows: Array.Empty<MappingFieldRow>(),
            mappingValueTransformRows: Array.Empty<MappingValueTransformRow>(),
            propertiesRows: Array.Empty<PropertyTableRow>(),
            mappingRaw: null,
            propertiesRaw: null);

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition was not met before the timeout elapsed.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class FakeContext : IPluginContext
    {
        public FakeContext()
        {
            CurrentEnv = new FoEnvironment("env-1", "Env 1", "https://contoso.operations.dynamics.com", "tenant", "USMF");
            OData = new FakeODataClient();
            Catalog = new FakeCatalogService();
            Logger = NullLogger.Instance;
        }

        public FoEnvironment CurrentEnv { get; set; }
        public IODataClient OData { get; }
        public ICatalogService Catalog { get; }
        public Microsoft.Extensions.Logging.ILogger Logger { get; }
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
