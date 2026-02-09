using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;

namespace FoToolbox.Core.Catalog;

public interface ICatalogService
{
    Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default);
    Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default);
    Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default);
    Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default);
    Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default);
    Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default);
    Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default);
    string BuildTableBrowserUrl(FoEnvironment env, string tableName);
    string BuildODataEntityUrl(FoEnvironment env, string entityName);
}
