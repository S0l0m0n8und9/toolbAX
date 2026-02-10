using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using System;
using System.Linq;

namespace FoToolbox.Core.Catalog;

public interface ICatalogService
{
    Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default);
    Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default);

    // Default implementations provide backwards-compatible fallbacks for callers that only
    // have a full-metadata implementation.
    async Task<ODataEntityIndex> GetODataEntityIndexAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
    {
        var metadata = await GetODataMetadataAsync(env, mode, ct).ConfigureAwait(false);
        var entities = metadata.Entities
            .Select(e => new ODataEntityIndexItem(e.Name, e.Properties.Count, e.Navigations.Count))
            .ToList();
        return new ODataEntityIndex(entities, metadata.Enums, metadata.ETag);
    }

    async Task<ODataEntity?> GetODataEntityDetailsAsync(FoEnvironment env, string entityName, CatalogRefreshMode mode, CancellationToken ct = default)
    {
        var metadata = await GetODataMetadataAsync(env, mode, ct).ConfigureAwait(false);
        return metadata.Entities.FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase));
    }

    Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default);
    Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default);
    Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default);
    Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default);
    Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default);
    string BuildTableBrowserUrl(FoEnvironment env, string tableName);
    string BuildODataEntityUrl(FoEnvironment env, string entityName);
}
