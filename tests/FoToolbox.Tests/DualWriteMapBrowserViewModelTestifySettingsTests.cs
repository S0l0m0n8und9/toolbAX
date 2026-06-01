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
    public void OpenTestifySettings_WithoutSelectedRecord_ShowsStatusMessageAndStaysHidden()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-settings.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), store);

            viewModel.OpenTestifySettingsCommand.Execute(null);

            Assert.False(viewModel.IsTestifySettingsVisible);
            Assert.Null(viewModel.TestifySettingsViewModel);
            Assert.Equal("Select a dual-write map before opening Testify settings.", viewModel.StatusMessage);
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
    public async Task OpenTestifySettings_WithSelectedRecord_CreatesModalViewModelLoadedFromStore()
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
            seeded.CePollTimeoutSeconds = 7;
            seeded.AllowPartialEnumCoverage = true;
            await store.SaveAsync(seeded, CancellationToken.None);

            var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), store);
            viewModel.SelectedRecord = CreateRecord("map-a", "Map A");

            viewModel.OpenTestifySettingsCommand.Execute(null);

            Assert.True(viewModel.IsTestifySettingsVisible);
            Assert.NotNull(viewModel.TestifySettingsViewModel);

            var modal = viewModel.TestifySettingsViewModel!;
            await WaitForAsync(() => modal.CePollTimeoutSeconds == 7);

            Assert.Equal("FieldA", modal.OmitCreateFieldsText);
            Assert.Equal("CurrencyCode=USD", modal.PreferredCreateValuesText);
            Assert.Equal(7, modal.CePollTimeoutSeconds);
            Assert.True(modal.AllowPartialEnumCoverage);
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
    public async Task ModalSave_PersistsThroughStore_AndCloseCommandClearsViewModel()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-settings.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), store);
            viewModel.SelectedRecord = CreateRecord("map-a", "Map A");
            viewModel.OpenTestifySettingsCommand.Execute(null);

            var modal = viewModel.TestifySettingsViewModel!;
            await Task.Delay(150);

            modal.OmitCreateFieldsText = "FieldB\r\nFieldC";
            modal.PreferredCreateValuesText = "NumberSequenceGroup=STD";
            modal.CePollTimeoutSeconds = 11;
            modal.AllowPartialEnumCoverage = true;

            await modal.SaveCommand.ExecuteAsync();

            Assert.Contains("Saved Testify settings for 'Map A'", viewModel.StatusMessage);

            modal.CloseCommand.Execute(null);
            Assert.False(viewModel.IsTestifySettingsVisible);
            Assert.Null(viewModel.TestifySettingsViewModel);

            var reloaded = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            Assert.Equal(new HashSet<string>(new[] { "FieldB", "FieldC" }, StringComparer.OrdinalIgnoreCase), reloaded.OmitCreateFields);
            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
            Assert.Equal(11, reloaded.CePollTimeoutSeconds);
            Assert.True(reloaded.AllowPartialEnumCoverage);
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
