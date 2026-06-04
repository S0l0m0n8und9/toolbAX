using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Seeded fake plugin context implementing every optional capability so any plugin's
/// CreateTool succeeds and bindings have realistic data to resolve against.
/// </summary>
internal sealed class FakePluginContext :
    IPluginContext, IPluginContextWrite, IPluginContextDataverse, IPluginContextNavigation
{
    private static readonly DateTime SeedTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ODataEntity CustomersEntity => new(
        "Customers",
        new[]
        {
            new ODataProperty("AccountNumber", "Edm.String", false),
            new ODataProperty("Name", "Edm.String", true),
        },
        Array.Empty<ODataNavigationProperty>());

    private static ODataMetadata SeedMetadata => new(new[] { CustomersEntity }, Array.Empty<ODataEnumType>(), null);

    private static TableCatalog SeedTables => new("contoso", "Contoso", SeedTime, Array.Empty<TableInfo>());

    public FakePluginContext()
    {
        CurrentEnv = new FoEnvironment("env", "Contoso", "https://contoso.operations.dynamics.com", "tenant", "USMF");
        OData = new SeededODataClient();
        Catalog = new SeededCatalogService();
        Logger = NullLogger.Instance;
        ODataWrite = new NoopWriteClient();
    }

    public FoEnvironment CurrentEnv { get; set; }
    public IODataClient OData { get; }
    public ICatalogService Catalog { get; }
    public ILogger Logger { get; }

    // IPluginContextWrite
    public IODataWriteClient ODataWrite { get; }

    // IPluginContextDataverse
    public bool HasDataverseProfile => false;
    public DataverseEnvironment? CurrentDataverseEnv => null;
    public HttpClient? DataverseHttp => null;

    // IPluginContextNavigation
    public bool TryNavigateTo(string targetPluginId, IReadOnlyDictionary<string, string> parameters) => false;

    private sealed class SeededODataClient : IODataClient
    {
        public async IAsyncEnumerable<ODataPage> StreamAsync(
            QueryRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            var rows = new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["AccountNumber"] = "US-001", ["Name"] = "Contoso Retail" },
                new Dictionary<string, object?> { ["AccountNumber"] = "US-002", ["Name"] = "Fabrikam" },
            };
            yield return new ODataPage(rows, NextLink: null, ODataCount: rows.Length);
        }
    }

    private sealed class NoopWriteClient : IODataWriteClient
    {
        public Task<ODataWriteResponse> SendAsync(ODataWriteRequest request, CancellationToken ct = default)
            => Task.FromResult(new ODataWriteResponse(200, "{}", new Dictionary<string, string>()));
    }

    // TODO: consolidate with FakeCatalogService in tests/FoToolbox.Tests/QueryBuilderPluginTests.cs
    // to prevent the two seeded catalogs from drifting.
    private sealed class SeededCatalogService : ICatalogService
    {
        public Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(SeedTables);

        public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(SeedMetadata);

        public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(new CatalogSnapshot(env.Id, env.BaseUrl, SeedTables, SeedMetadata, SeedTime));

        public Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default)
            => Task.FromResult(new TableCatalog("import", "UserImport", SeedTime, Array.Empty<TableInfo>()));

        public Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default)
            => Task.FromResult("{BaseUrl}/?mi=SysTableBrowser&table={TableName}");

        public Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default)
            => Task.CompletedTask;

        public string BuildTableBrowserUrl(FoEnvironment env, string tableName)
            => $"{env.BaseUrl}/?mi=SysTableBrowser&table={tableName}";

        public string BuildODataEntityUrl(FoEnvironment env, string entityName)
            => $"{env.BaseUrl}/data/{entityName}";
    }
}
