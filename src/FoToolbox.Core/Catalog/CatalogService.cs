using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.Core.Profiles;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Catalog;

public sealed class CatalogService : ICatalogService
{
    private const string TablesKind = "Tables";
    private const string MetadataKind = "ODataMetadata";
    private const string MetadataSchemaVersion = "metadata-v2";
    private const string MetadataXmlKind = "ODataMetadataXml";
    private const string MetadataXmlSchemaVersion = "metadata-xml-v1";
    private const string EntityIndexKind = "ODataEntityIndex";
    private const string EntityIndexSchemaVersion = "entity-index-v1";
    private const string EntityDetailsKindPrefix = "ODataEntityDetails:";
    private const string EntityDetailsSchemaVersion = "entity-details-v1";
    private const string TableBrowserUrlTemplateKey = "TableBrowserUrlTemplate";
    private const string DefaultTableBrowserUrlTemplate = "{BaseUrl}/?mi=SysTableBrowser&table={TableName}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly ProfileStore _profileStore;
    private readonly CatalogStore _store;
    private readonly CatalogServiceOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tableLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _metadataLocks = new();
    private string? _tableBrowserUrlTemplate;

    public CatalogService(HttpClient httpClient, ProfileStore profileStore, CatalogStore store, CatalogServiceOptions? options = null)
    {
        _httpClient = httpClient;
        _profileStore = profileStore;
        _store = store;
        _options = options ?? CatalogServiceOptions.Default;
    }

    public async Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
    {
        await _store.EnsureCreatedAsync(ct).ConfigureAwait(false);
        var gate = _tableLocks.GetOrAdd(env.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cached = await _store.GetAsync(env.Id, TablesKind, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                var cachedCatalog = DeserializeTableCatalog(cached.PayloadJson);
                if (mode == CatalogRefreshMode.UseCacheIfAvailable)
                {
                    return cachedCatalog;
                }
                if (mode == CatalogRefreshMode.UseCacheIfFresh && IsFresh(cached.UpdatedUtc, _options.TableMaxAge))
                {
                    return cachedCatalog;
                }

                if (string.Equals(cachedCatalog.Source, "UserImport", StringComparison.OrdinalIgnoreCase))
                {
                    return cachedCatalog;
                }
            }

            var builtIn = LoadDefaultCatalog();
            var normalized = new TableCatalog(
                builtIn.Version,
                string.IsNullOrWhiteSpace(builtIn.Source) ? "Embedded" : builtIn.Source,
                DateTime.UtcNow,
                builtIn.Tables ?? Array.Empty<TableInfo>());

            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            await _store.SaveAsync(env.Id, TablesKind, normalized.Version, json, null, normalized.UpdatedUtc, ct).ConfigureAwait(false);
            return normalized;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
    {
        await _store.EnsureCreatedAsync(ct).ConfigureAwait(false);
        var gate = _metadataLocks.GetOrAdd(env.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cached = await _store.GetAsync(env.Id, MetadataKind, ct).ConfigureAwait(false);
            var cachedValid = cached is not null && string.Equals(cached.Version, MetadataSchemaVersion, StringComparison.OrdinalIgnoreCase);
            if (cachedValid && mode == CatalogRefreshMode.UseCacheIfAvailable)
            {
                return DeserializeMetadata(cached!.PayloadJson);
            }

            if (cachedValid && mode == CatalogRefreshMode.UseCacheIfFresh && IsFresh(cached!.UpdatedUtc, _options.MetadataMaxAge))
            {
                return DeserializeMetadata(cached!.PayloadJson);
            }

            var (xml, etag, updatedUtc) = await GetMetadataXmlNoLockAsync(env, mode, ct).ConfigureAwait(false);

            // If we already have a parsed metadata blob with the same ETag (even if stale),
            // avoid re-parsing and just bump timestamps.
            if (cachedValid && !string.IsNullOrWhiteSpace(etag) && string.Equals(cached!.ETag, etag, StringComparison.Ordinal))
            {
                await _store.TouchAsync(env.Id, MetadataKind, ct).ConfigureAwait(false);
                return DeserializeMetadata(cached!.PayloadJson);
            }

            var metadata = ODataMetadataProvider.Parse(xml, etag);
            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            await _store.SaveAsync(env.Id, MetadataKind, MetadataSchemaVersion, json, etag, updatedUtc, ct).ConfigureAwait(false);
            return metadata;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ODataEntityIndex> GetODataEntityIndexAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
    {
        await _store.EnsureCreatedAsync(ct).ConfigureAwait(false);
        var gate = _metadataLocks.GetOrAdd(env.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cached = await _store.GetAsync(env.Id, EntityIndexKind, ct).ConfigureAwait(false);
            var cachedValid = cached is not null && string.Equals(cached.Version, EntityIndexSchemaVersion, StringComparison.OrdinalIgnoreCase);
            if (cachedValid && mode == CatalogRefreshMode.UseCacheIfAvailable)
            {
                return DeserializeEntityIndex(cached!.PayloadJson);
            }

            if (cachedValid && mode == CatalogRefreshMode.UseCacheIfFresh && IsFresh(cached!.UpdatedUtc, _options.MetadataMaxAge))
            {
                return DeserializeEntityIndex(cached!.PayloadJson);
            }

            var (xml, etag, updatedUtc) = await GetMetadataXmlNoLockAsync(env, mode, ct).ConfigureAwait(false);

            // If index exists for the same metadata ETag, keep it (even if stale) and just touch.
            if (cachedValid && !string.IsNullOrWhiteSpace(etag) && string.Equals(cached!.ETag, etag, StringComparison.Ordinal))
            {
                await _store.TouchAsync(env.Id, EntityIndexKind, ct).ConfigureAwait(false);
                return DeserializeEntityIndex(cached!.PayloadJson);
            }

            var index = ODataMetadataIndexParser.ParseIndex(xml, etag);
            var json = JsonSerializer.Serialize(index, JsonOptions);
            await _store.SaveAsync(env.Id, EntityIndexKind, EntityIndexSchemaVersion, json, etag, updatedUtc, ct).ConfigureAwait(false);
            return index;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ODataEntity?> GetODataEntityDetailsAsync(FoEnvironment env, string entityName, CatalogRefreshMode mode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return null;
        }

        await _store.EnsureCreatedAsync(ct).ConfigureAwait(false);
        var gate = _metadataLocks.GetOrAdd(env.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (xml, etag, updatedUtc) = await GetMetadataXmlNoLockAsync(env, mode, ct).ConfigureAwait(false);

            var kind = EntityDetailsKindPrefix + Uri.EscapeDataString(entityName);
            var cached = await _store.GetAsync(env.Id, kind, ct).ConfigureAwait(false);
            var cachedValid = cached is not null && string.Equals(cached.Version, EntityDetailsSchemaVersion, StringComparison.OrdinalIgnoreCase);

            // Prefer ETag match for correctness; fall back to max-age when ETag is absent.
            if (cachedValid && mode == CatalogRefreshMode.UseCacheIfAvailable)
            {
                if (!string.IsNullOrWhiteSpace(etag) && string.Equals(cached!.ETag, etag, StringComparison.Ordinal))
                {
                    return DeserializeEntity(cached!.PayloadJson, entityName);
                }

                // If ETag is absent on the metadata response, prefer whatever we last cached.
                if (string.IsNullOrWhiteSpace(etag))
                {
                    return DeserializeEntity(cached!.PayloadJson, entityName);
                }
            }

            if (cachedValid && mode == CatalogRefreshMode.UseCacheIfFresh)
            {
                if (!string.IsNullOrWhiteSpace(etag) && string.Equals(cached!.ETag, etag, StringComparison.Ordinal))
                {
                    return DeserializeEntity(cached!.PayloadJson, entityName);
                }

                if (string.IsNullOrWhiteSpace(etag) && IsFresh(cached!.UpdatedUtc, _options.MetadataMaxAge))
                {
                    return DeserializeEntity(cached!.PayloadJson, entityName);
                }
            }

            var entity = ODataMetadataIndexParser.TryParseEntityDetails(xml, entityName);
            if (entity is null)
            {
                return null;
            }

            var json = JsonSerializer.Serialize(entity, JsonOptions);
            await _store.SaveAsync(env.Id, kind, EntityDetailsSchemaVersion, json, etag, updatedUtc, ct).ConfigureAwait(false);
            return entity;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
    {
        var tablesTask = GetTablesAsync(env, mode, ct);
        var metadataTask = GetODataMetadataAsync(env, mode, ct);
        await Task.WhenAll(tablesTask, metadataTask).ConfigureAwait(false);
        return new CatalogSnapshot(env.Id, env.BaseUrl, tablesTask.Result, metadataTask.Result, DateTime.UtcNow);
    }

    public async Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default)
    {
        if (scope.HasFlag(CatalogRefreshScope.Tables))
        {
            _ = await GetTablesAsync(env, CatalogRefreshMode.ForceRefresh, ct).ConfigureAwait(false);
        }

        if (scope.HasFlag(CatalogRefreshScope.ODataMetadata))
        {
            _ = await GetODataMetadataAsync(env, CatalogRefreshMode.ForceRefresh, ct).ConfigureAwait(false);
            _ = await GetODataEntityIndexAsync(env, CatalogRefreshMode.ForceRefresh, ct).ConfigureAwait(false);
        }
    }

    public async Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default)
    {
        await _store.EnsureCreatedAsync(ct).ConfigureAwait(false);
        var catalog = DeserializeTableCatalog(json);
        if (catalog.Tables is null)
        {
            catalog = new TableCatalog(catalog.Version, catalog.Source, catalog.UpdatedUtc, Array.Empty<TableInfo>());
        }

        var normalized = new TableCatalog(
            string.IsNullOrWhiteSpace(catalog.Version) ? "unknown" : catalog.Version,
            "UserImport",
            DateTime.UtcNow,
            catalog.Tables);

        var storedJson = JsonSerializer.Serialize(normalized, JsonOptions);
        await _store.SaveAsync(env.Id, TablesKind, normalized.Version, storedJson, null, normalized.UpdatedUtc, ct).ConfigureAwait(false);
        return normalized;
    }

    public async Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_tableBrowserUrlTemplate))
        {
            return _tableBrowserUrlTemplate;
        }

        await _profileStore.EnsureCreatedAsync(ct).ConfigureAwait(false);
        var stored = await _profileStore.GetSettingAsync(TableBrowserUrlTemplateKey, ct).ConfigureAwait(false);
        _tableBrowserUrlTemplate = string.IsNullOrWhiteSpace(stored) ? DefaultTableBrowserUrlTemplate : stored;
        return _tableBrowserUrlTemplate;
    }

    public async Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new ArgumentException("Template cannot be empty", nameof(template));
        }

        _tableBrowserUrlTemplate = template;
        await _profileStore.EnsureCreatedAsync(ct).ConfigureAwait(false);
        await _profileStore.SetSettingAsync(TableBrowserUrlTemplateKey, template, ct).ConfigureAwait(false);
    }

    public string BuildTableBrowserUrl(FoEnvironment env, string tableName)
    {
        var template = _tableBrowserUrlTemplate ?? DefaultTableBrowserUrlTemplate;
        return template
            .Replace("{BaseUrl}", env.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            .Replace("{TableName}", Uri.EscapeDataString(tableName), StringComparison.OrdinalIgnoreCase);
    }

    public string BuildODataEntityUrl(FoEnvironment env, string entityName)
    {
        return $"{env.BaseUrl.TrimEnd('/')}/data/{Uri.EscapeDataString(entityName)}";
    }

    private static bool IsFresh(DateTime updatedUtc, TimeSpan maxAge)
    {
        // MaxAge <= 0 means "do not consider cached data fresh".
        if (maxAge <= TimeSpan.Zero) return false;
        return (DateTime.UtcNow - updatedUtc) <= maxAge;
    }

    private static TableCatalog DeserializeTableCatalog(string json)
    {
        var catalog = JsonSerializer.Deserialize<TableCatalog>(json, JsonOptions);
        if (catalog is null)
        {
            return new TableCatalog("unknown", "Unknown", DateTime.UtcNow, Array.Empty<TableInfo>());
        }

        return catalog;
    }

    private static ODataMetadata DeserializeMetadata(string json)
    {
        var metadata = JsonSerializer.Deserialize<ODataMetadata>(json, JsonOptions);
        if (metadata is null)
        {
            return new ODataMetadata(Array.Empty<ODataEntity>(), Array.Empty<ODataEnumType>(), null);
        }

        return metadata;
    }

    private static ODataEntityIndex DeserializeEntityIndex(string json)
    {
        var index = JsonSerializer.Deserialize<ODataEntityIndex>(json, JsonOptions);
        if (index is null)
        {
            return new ODataEntityIndex(Array.Empty<ODataEntityIndexItem>(), Array.Empty<ODataEnumType>(), null);
        }

        return index;
    }

    private static ODataEntity DeserializeEntity(string json, string nameFallback)
    {
        var entity = JsonSerializer.Deserialize<ODataEntity>(json, JsonOptions);
        return entity ?? new ODataEntity(nameFallback, Array.Empty<ODataProperty>(), Array.Empty<ODataNavigationProperty>());
    }

    private async Task<(string Xml, string? ETag, DateTime UpdatedUtc)> GetMetadataXmlNoLockAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct)
    {
        var cached = await _store.GetAsync(env.Id, MetadataXmlKind, ct).ConfigureAwait(false);
        var cachedValid = cached is not null && string.Equals(cached.Version, MetadataXmlSchemaVersion, StringComparison.OrdinalIgnoreCase);
        if (cachedValid && mode == CatalogRefreshMode.UseCacheIfAvailable)
        {
            return (cached!.PayloadJson, cached!.ETag, cached!.UpdatedUtc);
        }

        if (cachedValid && mode == CatalogRefreshMode.UseCacheIfFresh && IsFresh(cached!.UpdatedUtc, _options.MetadataMaxAge))
        {
            return (cached!.PayloadJson, cached!.ETag, cached!.UpdatedUtc);
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{env.BaseUrl.TrimEnd('/')}/data/$metadata");
        if (cachedValid && !string.IsNullOrWhiteSpace(cached!.ETag))
        {
            request.Headers.IfNoneMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{cached!.ETag}\""));
        }

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cachedValid)
        {
            var touched = await _store.TouchAsync(env.Id, MetadataXmlKind, ct).ConfigureAwait(false);
            return (cached!.PayloadJson, cached!.ETag, touched);
        }

        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var etag = response.Headers.ETag?.Tag?.Trim('"');
        var updatedUtc = DateTime.UtcNow;
        await _store.SaveAsync(env.Id, MetadataXmlKind, MetadataXmlSchemaVersion, xml, etag, updatedUtc, ct).ConfigureAwait(false);
        return (xml, etag, updatedUtc);
    }

    private static TableCatalog LoadDefaultCatalog()
    {
        var assembly = typeof(CatalogService).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("table-catalog-10.0.40.json", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return new TableCatalog("unknown", "Embedded", DateTime.UtcNow, Array.Empty<TableInfo>());
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new TableCatalog("unknown", "Embedded", DateTime.UtcNow, Array.Empty<TableInfo>());
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return DeserializeTableCatalog(json);
    }
}

public sealed record CatalogServiceOptions(TimeSpan TableMaxAge, TimeSpan MetadataMaxAge)
{
    public static CatalogServiceOptions Default { get; } = new(TimeSpan.FromDays(7), TimeSpan.FromHours(6));
}
