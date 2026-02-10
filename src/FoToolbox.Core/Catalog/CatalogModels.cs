using System;
using System.Collections.Generic;
using FoToolbox.Core.OData;

namespace FoToolbox.Core.Catalog;

public sealed record TableInfo(
    string Name,
    string? Label,
    bool IsView,
    string? ConfigurationKey,
    bool IsDeprecated,
    string? Notes);

public sealed record TableCatalog(
    string Version,
    string Source,
    DateTime UpdatedUtc,
    IReadOnlyList<TableInfo> Tables);

public sealed record CatalogSnapshot(
    string EnvId,
    string BaseUrl,
    TableCatalog Tables,
    ODataMetadata OData,
    DateTime UpdatedUtc);

public enum CatalogRefreshMode
{
    /// <summary>
    /// Use any cached payload if it exists (even if stale). Falls back to fetching/building when absent.
    /// </summary>
    UseCacheIfAvailable,
    UseCacheIfFresh,
    ForceRefresh
}

[Flags]
public enum CatalogRefreshScope
{
    None = 0,
    Tables = 1,
    ODataMetadata = 2,
    All = Tables | ODataMetadata
}
