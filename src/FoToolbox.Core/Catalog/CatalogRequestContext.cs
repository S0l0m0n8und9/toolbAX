using System.Net.Http;

namespace FoToolbox.Core.Catalog;

/// <summary>Binds catalog HTTP requests to the same immutable context as their metadata cache entries.</summary>
public static class CatalogRequestContext
{
    public static readonly HttpRequestOptionsKey<string> MetadataCachePartition =
        new("FoToolbox.Catalog.MetadataCachePartition");
}
