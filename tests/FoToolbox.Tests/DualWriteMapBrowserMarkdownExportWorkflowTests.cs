using DualWriteMapBrowserPlugin;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace FoToolbox.Tests;

public sealed class DualWriteMapBrowserMarkdownExportWorkflowTests
{
    [Fact]
    public void GetSelectedMapsForExport_PrefersCheckedMaps()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-noop.json");

        try
        {
            var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), new TestifyConfigurationStore(storePath));
            var selectedRecord = CreateRecord("selected", "Selected Record");
            var checkedRecordA = CreateRecord("checked-a", "Checked Record");
            var checkedRecordB = CreateRecord("checked-b", "Checked Record");
            checkedRecordA.IsSelected = true;
            checkedRecordB.IsSelected = true;

            viewModel.SelectedRecord = selectedRecord;
            viewModel.Records.Add(selectedRecord);
            viewModel.Records.Add(checkedRecordA);
            viewModel.Records.Add(checkedRecordB);

            var selectedMaps = viewModel.GetSelectedMapsForExport();

            Assert.Equal(2, selectedMaps.Count);
            Assert.DoesNotContain(selectedRecord, selectedMaps);
            Assert.Contains(checkedRecordA, selectedMaps);
            Assert.Contains(checkedRecordB, selectedMaps);
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public void GetSelectedMapsForExport_FallsBackToSelectedRecord()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-noop.json");

        try
        {
            var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), new TestifyConfigurationStore(storePath));
            var selectedRecord = CreateRecord("selected", "Selected Record");

            viewModel.SelectedRecord = selectedRecord;

            var selectedMaps = viewModel.GetSelectedMapsForExport();

            var only = Assert.Single(selectedMaps);
            Assert.Same(selectedRecord, only);
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public async Task ExportMarkdownFilesAsync_WritesOneFilePerMap()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-noop.json");
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"toolbax-export-{Guid.NewGuid():N}");
        var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), new TestifyConfigurationStore(storePath));
        var first = CreateRecord("map-1", "Customer Map");
        var second = CreateRecord("map-2", "Customer Map");

        try
        {
            await viewModel.ExportMarkdownFilesAsync(new[] { first, second }, exportDirectory, CancellationToken.None);

            var files = Directory.GetFiles(exportDirectory, "*.md").Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.Equal(new[] { "Customer Map-2.md", "Customer Map.md" }, files);

            var firstContent = await File.ReadAllTextAsync(Path.Combine(exportDirectory, "Customer Map.md"));
            var secondContent = await File.ReadAllTextAsync(Path.Combine(exportDirectory, "Customer Map-2.md"));

            Assert.Contains("# Customer Map", firstContent);
            Assert.Contains("**Map ID:** map-1", firstContent);
            Assert.Contains("**Map ID:** map-2", secondContent);
            Assert.Equal($"Exported 2 markdown files to {exportDirectory}.", viewModel.StatusMessage);
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }

            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public async Task ExportMarkdownFilesAsync_ThrowsForMissingDestination()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-noop.json");
        var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), new TestifyConfigurationStore(storePath));
        var record = CreateRecord("map-1", "Customer Map");

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.ExportMarkdownFilesAsync(new[] { record }, string.Empty, CancellationToken.None));

            Assert.Equal("A valid export directory is required.", exception.Message);
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public async Task ExportSelectedMarkdownCommand_SurfacesInvalidDestinationErrors()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-noop.json");
        var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), new TestifyConfigurationStore(storePath));
        var first = CreateRecord("map-1", "Customer Map");
        var second = CreateRecord("map-2", "Customer Map");
        first.IsSelected = true;
        second.IsSelected = true;
        viewModel.SelectedRecord = first;
        viewModel.Records.Add(first);
        viewModel.Records.Add(second);
        viewModel.ChooseMarkdownExportDirectory = static () => string.Empty;

        try
        {
            await viewModel.ExportSelectedMarkdownCommand.ExecuteAsync(CancellationToken.None);

            Assert.Equal("Markdown export cancelled.", viewModel.StatusMessage);
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public async Task ExportMarkdownFilesAsync_ThrowsForUnwritableDestination()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-noop.json");
        var exportFile = Path.Combine(Path.GetTempPath(), $"toolbax-export-file-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(exportFile, "not a directory");

        var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), new TestifyConfigurationStore(storePath));
        var first = CreateRecord("map-1", "Customer Map");
        var second = CreateRecord("map-2", "Customer Map");

        try
        {
            var exception = await Assert.ThrowsAsync<IOException>(() =>
                viewModel.ExportMarkdownFilesAsync(new[] { first, second }, exportFile, CancellationToken.None));

            Assert.Equal($"The export destination '{exportFile}' is not a folder.", exception.Message);
        }
        finally
        {
            if (File.Exists(exportFile))
            {
                File.Delete(exportFile);
            }

            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public async Task ExportMarkdownFilesAsync_ThrowsWhenDestinationAlreadyContainsGeneratedFile()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-noop.json");
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"toolbax-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);
        await File.WriteAllTextAsync(Path.Combine(exportDirectory, "Customer Map.md"), "existing");

        var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), new TestifyConfigurationStore(storePath));
        var record = CreateRecord("map-1", "Customer Map");

        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                viewModel.ExportMarkdownFilesAsync(new[] { record }, exportDirectory, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }

            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public async Task ExportSelectedMarkdownCommand_SurfacesDestinationWriteErrors()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-noop.json");
        var exportFile = Path.Combine(Path.GetTempPath(), $"toolbax-export-file-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(exportFile, "not a directory");

        var viewModel = new DualWriteMapBrowserViewModel(new FakeContext(), new TestifyConfigurationStore(storePath));
        var first = CreateRecord("map-1", "Customer Map");
        var second = CreateRecord("map-2", "Customer Map");
        first.IsSelected = true;
        second.IsSelected = true;
        viewModel.SelectedRecord = first;
        viewModel.Records.Add(first);
        viewModel.Records.Add(second);
        viewModel.ChooseMarkdownExportDirectory = () => exportFile;

        try
        {
            await viewModel.ExportSelectedMarkdownCommand.ExecuteAsync(CancellationToken.None);

            Assert.Equal($"Markdown export failed: The export destination '{exportFile}' is not a folder.", viewModel.StatusMessage);
        }
        finally
        {
            if (File.Exists(exportFile))
            {
                File.Delete(exportFile);
            }

            if (File.Exists(storePath))
            {
                File.Delete(storePath);
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
            mappingRaw: "{}",
            propertiesRaw: "{}");

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
