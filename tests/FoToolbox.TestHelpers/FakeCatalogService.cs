using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;

namespace FoToolbox.TestHelpers;

/// <summary>
/// Shared in-memory <see cref="ICatalogService"/> fake seeded from <see cref="TestCatalogBuilder"/>.
/// Replaces the previously-duplicated <c>SeededCatalogService</c> / <c>FakeCatalogService</c> so the
/// seed shape lives in one place (#39).
/// </summary>
public sealed class FakeCatalogService : ICatalogService
{
    public Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
        => Task.FromResult(TestCatalogBuilder.SeedTables());

    public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
        => Task.FromResult(TestCatalogBuilder.SeedMetadata());

    public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
        => Task.FromResult(TestCatalogBuilder.SeedSnapshot(env));

    public Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default)
        => Task.FromResult(new TableCatalog("import", "UserImport", TestCatalogBuilder.SeedTimeUtc, Array.Empty<TableInfo>()));

    public Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default)
        => Task.FromResult("{BaseUrl}/?mi=SysTableBrowser&table={TableName}");

    public Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default)
        => Task.CompletedTask;

    public string BuildTableBrowserUrl(FoEnvironment env, string tableName)
        => $"{env.BaseUrl}/?mi=SysTableBrowser&table={tableName}";

    public string BuildODataEntityUrl(FoEnvironment env, string entityName)
        => $"{env.BaseUrl}/data/{entityName}";
}
