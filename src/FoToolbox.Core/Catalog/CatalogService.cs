using FoToolbox.Core.Models;
using FoToolbox.Core.Auth;
using FoToolbox.Core.OData;
using FoToolbox.Core.Net;
using FoToolbox.Core.Profiles;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private const string EntityDetailsSchemaVersion = "entity-details-v2";
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
    private readonly ReadRetryPolicy _readRetryPolicy;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tableLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _metadataLocks = new();
    private string? _tableBrowserUrlTemplate;

    // One-deep memo of the metadata XML for the most recently used environment. F&O $metadata is tens of
    // megabytes, so every read of the ODataMetadataXml row materializes a large-object-heap string out of
    // SQLite; holding the last one live is far cheaper than re-materializing it per entity click.
    //
    // Ceiling, deliberately: exactly ONE entry, keyed by the environment cache key. Switching
    // environments (or repointing a profile's URL) evicts it, so the memory cost stays at a single
    // metadata document rather than one per environment visited. The memo mirrors what the
    // ODataMetadataXml row would have returned and is consulted under the same refresh-mode/max-age rules
    // as that row (see TryUseMetadataXmlMemo), so it never widens the freshness window. A shared generation
    // invalidates it after coordinated pruning by any service/store on this file path in this process.
    // Ordinary external writes/deletes and other processes retain the existing snapshot semantics.
    private MetadataXmlMemo? _metadataXmlMemo;

    public CatalogService(
        HttpClient httpClient,
        ProfileStore profileStore,
        CatalogStore store,
        CatalogServiceOptions? options = null,
        ReadRetryPolicy? readRetryPolicy = null)
    {
        _httpClient = httpClient;
        _profileStore = profileStore;
        _store = store;
        _options = options ?? CatalogServiceOptions.Default;
        _readRetryPolicy = readRetryPolicy ?? new ReadRetryPolicy();
    }

    public async Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
    {
        await _store.EnsureCreatedAsync(ct).ConfigureAwait(false);
        var key = CacheKey(env);
        var gate = _tableLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cached = await _store.GetAsync(key, TablesKind, ct).ConfigureAwait(false);
            if (cached is null)
            {
                // H08b changed every live cache key to a case-preserving namespace. Imported table
                // catalogs are user data, however, so recover only a valid import from the precise former
                // Tables key. Metadata and ordinary embedded catalogs are intentionally never migrated.
                var legacy = await _store.GetAsync(LegacyCacheKey(env), TablesKind, ct).ConfigureAwait(false);
                if (legacy is not null && IsUserImport(legacy))
                {
                    if (!CanRecoverLegacyTableCatalog(env))
                    {
                        throw new LegacyTableCatalogRecoveryRequiredException(legacy.PayloadJson);
                    }

                    await _store.InsertIfAbsentAsync(key, TablesKind, legacy.Version, legacy.PayloadJson,
                        legacy.ETag, legacy.UpdatedUtc, ct).ConfigureAwait(false);
                    cached = await _store.GetAsync(key, TablesKind, ct).ConfigureAwait(false);
                }
            }

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
            await _store.SaveAsync(key, TablesKind, normalized.Version, json, null, normalized.UpdatedUtc, ct).ConfigureAwait(false);
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
        var key = MetadataCacheKey(env);
        await using var lease = env.MetadataCachePartition is null ? null
            : await _store.LeaseMetadataPartitionAsync(env.Id, key, ct).ConfigureAwait(false);
        var gate = _metadataLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cached = await _store.GetAsync(key, MetadataKind, ct).ConfigureAwait(false);
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
                await _store.TouchAsync(key, MetadataKind, ct).ConfigureAwait(false);
                return DeserializeMetadata(cached!.PayloadJson);
            }

            var metadata = ODataMetadataProvider.Parse(xml, etag);
            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            await _store.SaveAsync(key, MetadataKind, MetadataSchemaVersion, json, etag, updatedUtc, ct).ConfigureAwait(false);
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
        var key = MetadataCacheKey(env);
        await using var lease = env.MetadataCachePartition is null ? null
            : await _store.LeaseMetadataPartitionAsync(env.Id, key, ct).ConfigureAwait(false);
        var gate = _metadataLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cached = await _store.GetAsync(key, EntityIndexKind, ct).ConfigureAwait(false);
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
                await _store.TouchAsync(key, EntityIndexKind, ct).ConfigureAwait(false);
                return DeserializeEntityIndex(cached!.PayloadJson);
            }

            var index = ODataMetadataIndexParser.ParseIndex(xml, etag);
            var json = JsonSerializer.Serialize(index, JsonOptions);
            await _store.SaveAsync(key, EntityIndexKind, EntityIndexSchemaVersion, json, etag, updatedUtc, ct).ConfigureAwait(false);
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
        var key = MetadataCacheKey(env);
        await using var lease = env.MetadataCachePartition is null ? null
            : await _store.LeaseMetadataPartitionAsync(env.Id, key, ct).ConfigureAwait(false);
        var gate = _metadataLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Cache-first. The per-entity details row is a few KB; the metadata XML it was parsed from is
            // tens of MB, so the small row decides and the blob is only pulled when the answer isn't
            // already here. Both cache-honouring modes key off the *metadata* ETag, so this shortcut is
            // only available while that ETag is knowable without reading the XML row — i.e. from the
            // one-deep memo, which the entity-index load that populates the browser has already warmed.
            // An absent/stale memo means the ETag is unverifiable and we fall through to the XML exactly
            // as before; the freshness test itself (IsEntityDetailsUsable) is the same either way.
            var kind = EntityDetailsKindPrefix + Uri.EscapeDataString(entityName);
            var cached = await _store.GetAsync(key, kind, ct).ConfigureAwait(false);
            var cachedValid = cached is not null && string.Equals(cached.Version, EntityDetailsSchemaVersion, StringComparison.OrdinalIgnoreCase);

            if (cachedValid
                && TryUseMetadataXmlMemo(key, mode) is { } memo
                && IsEntityDetailsUsable(cached!, mode, memo.ETag))
            {
                return DeserializeEntity(cached!.PayloadJson, entityName);
            }

            var (xml, etag, updatedUtc) = await GetMetadataXmlNoLockAsync(env, mode, ct).ConfigureAwait(false);

            if (cachedValid && IsEntityDetailsUsable(cached!, mode, etag))
            {
                return DeserializeEntity(cached!.PayloadJson, entityName);
            }

            var entity = ODataMetadataIndexParser.TryParseEntityDetails(xml, entityName);
            if (entity is null)
            {
                return null;
            }

            // FO's /metadata layer exposes accurate key/mandatory flags for Data Entities.
            // OData $metadata nullability is not a reliable proxy for "mandatory" and keys can be inherited.
            entity = await TryEnrichEntityFromPublicEntitiesAsync(env, entity, ct).ConfigureAwait(false) ?? entity;
            entity = await TryEnrichEntityFromDataManagementTargetMapAsync(env, entity, ct).ConfigureAwait(false) ?? entity;

            var json = JsonSerializer.Serialize(entity, JsonOptions);
            await _store.SaveAsync(key, kind, EntityDetailsSchemaVersion, json, etag, updatedUtc, ct).ConfigureAwait(false);
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
        var tables = await tablesTask.ConfigureAwait(false);
        var metadata = await metadataTask.ConfigureAwait(false);
        return new CatalogSnapshot(env.Id, env.BaseUrl, tables, metadata, DateTime.UtcNow);
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
        await _store.SaveAsync(CacheKey(env), TablesKind, normalized.Version, storedJson, null, normalized.UpdatedUtc, ct).ConfigureAwait(false);
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
            .Replace("{BaseUrl}", NormalizedBaseUrl(env), StringComparison.OrdinalIgnoreCase)
            .Replace("{TableName}", Uri.EscapeDataString(tableName), StringComparison.OrdinalIgnoreCase);
    }

    public string BuildODataEntityUrl(FoEnvironment env, string entityName)
    {
        return $"{NormalizedBaseUrl(env)}/data/{Uri.EscapeDataString(entityName)}";
    }

    // Cache rows (and the per-environment locks) are keyed by profile id *and* normalized request base,
    // not by id alone: repointing a profile at another environment keeps its id. The versioned JSON
    // namespace deliberately prevents old lower-cased keys from being reused for a case-sensitive path.
    private static string CacheKey(FoEnvironment env)
    {
        return "catalog-v2:" + JsonSerializer.Serialize(new[] { env.Id, NormalizedBaseUrl(env) });
    }

    private static string NormalizedBaseUrl(FoEnvironment env) => ResourceUrlNormalizer.NormalizeFoBaseUrl(env.BaseUrl);

    private static string MetadataCacheKey(FoEnvironment env) =>
        env.MetadataCachePartition is null
            ? CacheKey(env)
            : "catalog-meta-v1:" + JsonSerializer.Serialize(new[] { env.Id, NormalizedBaseUrl(env), env.MetadataCachePartition });

    private static void BindMetadataContext(HttpRequestMessage request, FoEnvironment env)
    {
        if (env.MetadataCachePartition is { } partition)
        {
            request.Options.Set(CatalogRequestContext.MetadataCachePartition, partition);
        }
    }

    private static string LegacyCacheKey(FoEnvironment env)
    {
        var url = env.BaseUrl.Trim().ToLowerInvariant();
        if (url.Length > 0 && !url.StartsWith("http", StringComparison.Ordinal))
        {
            url = "https://" + url;
        }

        return $"{env.Id}|{url.TrimEnd('/')}";
    }

    private static bool IsUserImport(CatalogRecord record)
    {
        try
        {
            return string.Equals(DeserializeTableCatalog(record.PayloadJson).Source, "UserImport", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool CanRecoverLegacyTableCatalog(FoEnvironment env)
    {
        var normalized = ResourceUrlNormalizer.NormalizeFoBaseUrl(env.BaseUrl);
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    // Whether a cached per-entity details row may be served for this refresh mode, given the metadata ETag
    // the XML layer reports. This is the pre-existing rule set, lifted verbatim out of
    // GetODataEntityDetailsAsync so the cache-first ordering can apply it before the XML is materialized:
    //   * ForceRefresh    → never; a forced refresh always reparses.
    //   * ETag present    → serve only on an exact (ordinal) match, for both cache-honouring modes, and
    //                       then regardless of the row's age: a matching ETag proves the metadata the row
    //                       was parsed from has not moved, so a stale-but-matching row is still correct.
    //   * ETag absent     → UseCacheIfAvailable serves whatever was last cached (no ETag, no better
    //                       evidence, and "if available" accepts stale); UseCacheIfFresh serves it only
    //                       while the row is within MetadataMaxAge, since age is the only proof left.
    //   * ETag mismatched → never; the metadata moved, so reparse.
    private bool IsEntityDetailsUsable(CatalogRecord cached, CatalogRefreshMode mode, string? metadataETag)
    {
        if (mode != CatalogRefreshMode.UseCacheIfAvailable && mode != CatalogRefreshMode.UseCacheIfFresh)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(metadataETag))
        {
            return string.Equals(cached.ETag, metadataETag, StringComparison.Ordinal);
        }

        return mode == CatalogRefreshMode.UseCacheIfAvailable
            || IsFresh(cached.UpdatedUtc, _options.MetadataMaxAge);
    }

    // The memo, but only when it may stand in for the ODataMetadataXml row under this refresh mode — the
    // same test GetMetadataXmlNoLockAsync applies to the stored row, so serving from memory can never
    // return metadata the store read would have rejected as stale.
    private MetadataXmlMemo? TryUseMetadataXmlMemo(string key, CatalogRefreshMode mode)
    {
        var memo = Volatile.Read(ref _metadataXmlMemo);
        if (memo is null || memo.PruneGeneration != _store.MetadataPruneGeneration
            || !string.Equals(memo.Key, key, StringComparison.Ordinal))
        {
            return null;
        }

        return mode switch
        {
            CatalogRefreshMode.UseCacheIfAvailable => memo,
            CatalogRefreshMode.UseCacheIfFresh when IsFresh(memo.UpdatedUtc, _options.MetadataMaxAge) => memo,
            _ => null,
        };
    }

    // Replaces the single memo slot (evicting whatever environment was there) with what the XML row now
    // holds, so the next caller for this environment can skip the blob read entirely.
    private void RememberMetadataXml(string key, string xml, string? etag, DateTime updatedUtc) =>
        Volatile.Write(ref _metadataXmlMemo, new MetadataXmlMemo(key, xml, etag, updatedUtc, _store.MetadataPruneGeneration));

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
        var key = MetadataCacheKey(env);

        // Consecutive misses for different entities under one environment would otherwise re-read (and
        // re-materialize) the same multi-MB blob out of SQLite per call.
        if (TryUseMetadataXmlMemo(key, mode) is { } memo)
        {
            return (memo.Xml, memo.ETag, memo.UpdatedUtc);
        }

        var cached = await _store.GetAsync(key, MetadataXmlKind, ct).ConfigureAwait(false);
        var cachedValid = cached is not null && string.Equals(cached.Version, MetadataXmlSchemaVersion, StringComparison.OrdinalIgnoreCase);
        if (cachedValid && mode == CatalogRefreshMode.UseCacheIfAvailable)
        {
            RememberMetadataXml(key, cached!.PayloadJson, cached!.ETag, cached!.UpdatedUtc);
            return (cached!.PayloadJson, cached!.ETag, cached!.UpdatedUtc);
        }

        if (cachedValid && mode == CatalogRefreshMode.UseCacheIfFresh && IsFresh(cached!.UpdatedUtc, _options.MetadataMaxAge))
        {
            RememberMetadataXml(key, cached!.PayloadJson, cached!.ETag, cached!.UpdatedUtc);
            return (cached!.PayloadJson, cached!.ETag, cached!.UpdatedUtc);
        }

        var target = new Uri($"{NormalizedBaseUrl(env)}/data/$metadata", UriKind.Absolute);
        var conditionalEtag = cachedValid ? cached!.ETag : null;
        HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, target);
            BindMetadataContext(request, env);
            request.Headers.Accept.ParseAdd("application/xml");
            if (!string.IsNullOrWhiteSpace(conditionalEtag))
            {
                request.Headers.IfNoneMatch.Add(
                    new System.Net.Http.Headers.EntityTagHeaderValue($"\"{conditionalEtag}\""));
            }
            return request;
        }

        using var response = await _readRetryPolicy.SendAsync(
            _httpClient,
            CreateRequest,
            HttpCompletionOption.ResponseContentRead,
            ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cachedValid)
        {
            var touched = await _store.TouchAsync(key, MetadataXmlKind, ct).ConfigureAwait(false);
            RememberMetadataXml(key, cached!.PayloadJson, cached!.ETag, touched);
            return (cached!.PayloadJson, cached!.ETag, touched);
        }

        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var etag = response.Headers.ETag?.Tag?.Trim('"');
        var updatedUtc = DateTime.UtcNow;
        await _store.SaveAsync(key, MetadataXmlKind, MetadataXmlSchemaVersion, xml, etag, updatedUtc, ct).ConfigureAwait(false);
        RememberMetadataXml(key, xml, etag, updatedUtc);
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

    private async Task<ODataEntity?> TryEnrichEntityFromPublicEntitiesAsync(FoEnvironment env, ODataEntity entity, CancellationToken ct)
    {
        try
        {
            var flags = await TryGetPublicEntityPropertyFlagsAsync(env, entity.Name, ct).ConfigureAwait(false);
            if (flags is null || flags.Count == 0)
            {
                return null;
            }

            var updatedProps = entity.Properties
                .Select(p =>
                {
                    if (flags.TryGetValue(p.Name, out var f))
                    {
                        return p with { IsKey = f.IsKey, IsMandatory = f.IsMandatory };
                    }
                    return p;
                })
                .ToList();

            return entity with { Properties = updatedProps };
        }
        catch
        {
            // Best-effort enrichment only; callers should still work using OData $metadata alone.
            return null;
        }
    }

    private async Task<ODataEntity?> TryEnrichEntityFromDataManagementTargetMapAsync(FoEnvironment env, ODataEntity entity, CancellationToken ct)
    {
        try
        {
            var lengths = await TryGetDataManagementFieldLengthsAsync(env, entity.Name, ct).ConfigureAwait(false);
            if (lengths is null || lengths.Count == 0)
            {
                return null;
            }

            var changed = false;
            var updatedProps = entity.Properties
                .Select(p =>
                {
                    if (HasMeaningfulMaxLength(p.MaxLength))
                    {
                        return p;
                    }

                    if (!TryResolveFieldLength(lengths, p.Name, out var maxLength))
                    {
                        return p;
                    }

                    changed = true;
                    return p with { MaxLength = maxLength };
                })
                .ToList();

            if (!changed)
            {
                return null;
            }

            return entity with { Properties = updatedProps };
        }
        catch
        {
            // Best-effort enrichment only; callers should still work using OData metadata alone.
            return null;
        }
    }

    private async Task<Dictionary<string, (bool IsKey, bool IsMandatory)>?> TryGetPublicEntityPropertyFlagsAsync(
        FoEnvironment env,
        string entitySetName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entitySetName))
        {
            return null;
        }

        // Example:
        //   /metadata/PublicEntities?$filter=EntitySetName%20eq%20%27CDSParties%27
        var escapedLiteral = entitySetName.Replace("'", "''", StringComparison.Ordinal);
        var filter = Uri.EscapeDataString($"EntitySetName eq '{escapedLiteral}'");
        var url = $"{NormalizedBaseUrl(env)}/metadata/PublicEntities?$filter={filter}";

        HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            BindMetadataContext(request, env);
            request.Headers.Accept.ParseAdd("application/json");
            return request;
        }

        using var resp = await _readRetryPolicy.SendAsync(
            _httpClient,
            CreateRequest,
            HttpCompletionOption.ResponseContentRead,
            ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<PublicEntitiesResponse>(json, JsonOptions);
        var match = parsed?.Value?.FirstOrDefault(x =>
            string.Equals(x.EntitySetName, entitySetName, StringComparison.OrdinalIgnoreCase));

        if (match?.Properties is null || match.Properties.Count == 0)
        {
            return null;
        }

        var flags = new Dictionary<string, (bool IsKey, bool IsMandatory)>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in match.Properties)
        {
            if (!string.IsNullOrWhiteSpace(prop.Name))
            {
                flags[prop.Name] = (prop.IsKey, prop.IsMandatory);
            }
        }
        return flags;
    }

    private async Task<Dictionary<string, string>?> TryGetDataManagementFieldLengthsAsync(
        FoEnvironment env,
        string entitySetName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entitySetName))
        {
            return null;
        }

        foreach (var candidate in BuildEntityNameCandidates(entitySetName))
        {
            var rows = await FetchDataManagementTargetMapRowsAsync(env, candidate, ct).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                continue;
            }

            var map = BuildFieldLengthMap(rows);
            if (map.Count > 0)
            {
                return map;
            }
        }

        return null;
    }

    private async Task<List<DataManagementTargetMapRow>> FetchDataManagementTargetMapRowsAsync(
        FoEnvironment env,
        string entityName,
        CancellationToken ct)
    {
        var rows = new List<DataManagementTargetMapRow>();
        var escapedLiteral = entityName.Replace("'", "''", StringComparison.Ordinal);
        var filter = Uri.EscapeDataString($"Entity eq '{escapedLiteral}'");
        var select = Uri.EscapeDataString("StagingField,ShortStagingField,TargetField,FieldAOTName,DataSourceField,FieldLength");
        var nextUrl = $"{NormalizedBaseUrl(env)}/data/DataManagementTargetMapEntities?$filter={filter}&$select={select}&$top=1000&$count=true&cross-company=true";
        var visits = new PageVisitTracker();
        var origin = new Uri(nextUrl, UriKind.Absolute);
        var firstRequest = true;

        while (!string.IsNullOrWhiteSpace(nextUrl))
        {
            ct.ThrowIfCancellationRequested();
            if (!visits.TryVisit(nextUrl, baseAddress: null, out var resolvedRequestUri))
            {
                return new List<DataManagementTargetMapRow>();
            }
            if (!firstRequest && (resolvedRequestUri is null || !RequestOriginGuard.IsSameOrigin(origin, resolvedRequestUri)))
            {
                return new List<DataManagementTargetMapRow>();
            }
            firstRequest = false;

            var requestUri = resolvedRequestUri ?? new Uri(nextUrl, UriKind.RelativeOrAbsolute);
            HttpRequestMessage CreateRequest()
            {
                var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                BindMetadataContext(request, env);
                request.Headers.Accept.ParseAdd("application/json");
                return request;
            }

            using var resp = await _readRetryPolicy.SendAsync(
                _httpClient,
                CreateRequest,
                HttpCompletionOption.ResponseContentRead,
                ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!resp.IsSuccessStatusCode)
            {
                return new List<DataManagementTargetMapRow>();
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("value", out var valueNode) ||
                valueNode.ValueKind != JsonValueKind.Array)
            {
                return new List<DataManagementTargetMapRow>();
            }

            foreach (var item in valueNode.EnumerateArray())
            {
                var fieldLength = ReadInt32Property(item, "FieldLength");
                if (fieldLength is null || fieldLength <= 0)
                {
                    continue;
                }

                rows.Add(new DataManagementTargetMapRow(
                    ReadStringProperty(item, "StagingField"),
                    ReadStringProperty(item, "ShortStagingField"),
                    ReadStringProperty(item, "TargetField"),
                    ReadStringProperty(item, "FieldAOTName"),
                    ReadStringProperty(item, "DataSourceField"),
                    fieldLength.Value));
            }

            if (!root.TryGetProperty("@odata.nextLink", out var nextLinkNode) ||
                nextLinkNode.ValueKind == JsonValueKind.Null)
            {
                break;
            }
            if (nextLinkNode.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nextLinkNode.GetString()))
            {
                return new List<DataManagementTargetMapRow>();
            }
            var nextLink = nextLinkNode.GetString()!;

            if (Uri.TryCreate(nextLink, UriKind.Absolute, out var absolute))
            {
                nextUrl = absolute.ToString();
            }
            else
            {
                nextUrl = $"{NormalizedBaseUrl(env)}/{nextLink.TrimStart('/')}";
            }
        }

        return rows;
    }

    private static Dictionary<string, string> BuildFieldLengthMap(IEnumerable<DataManagementTargetMapRow> rows)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var candidate in EnumerateFieldNameCandidates(row))
            {
                var normalized = NormalizeFieldName(candidate);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (!map.TryGetValue(normalized, out var existing) || row.FieldLength > existing)
                {
                    map[normalized] = row.FieldLength;
                }
            }
        }

        return map.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string?> EnumerateFieldNameCandidates(DataManagementTargetMapRow row)
    {
        yield return row.FieldAOTName;
        yield return row.StagingField;
        yield return row.ShortStagingField;
        yield return row.TargetField;
        yield return row.DataSourceField;
    }

    private static bool TryResolveFieldLength(IReadOnlyDictionary<string, string> lengths, string propertyName, out string maxLength)
    {
        maxLength = string.Empty;
        var normalized = NormalizeFieldName(propertyName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return lengths.TryGetValue(normalized, out maxLength!);
    }

    private static bool HasMeaningfulMaxLength(string? maxLength)
    {
        if (string.IsNullOrWhiteSpace(maxLength))
        {
            return false;
        }

        if (int.TryParse(maxLength, out var value))
        {
            return value > 0;
        }

        return true;
    }

    private static string? NormalizeFieldName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();

        return chars.Length == 0 ? null : new string(chars);
    }

    private static IReadOnlyList<string> BuildEntityNameCandidates(string entitySetName)
    {
        var values = new List<string>();
        AddCandidate(values, entitySetName);

        var spaced = InsertEntityNameSpaces(entitySetName);
        AddCandidate(values, spaced);

        var singular = RemovePluralSuffixBeforeVersion(entitySetName);
        if (!string.IsNullOrWhiteSpace(singular))
        {
            AddCandidate(values, singular);
            AddCandidate(values, InsertEntityNameSpaces(singular));
        }

        return values;
    }

    private static void AddCandidate(List<string> values, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        if (!values.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(candidate);
        }
    }

    private static string InsertEntityNameSpaces(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var chars = new List<char>(value.Length * 2);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0)
            {
                var previous = value[i - 1];
                var boundary =
                    (char.IsLower(previous) && char.IsUpper(current)) ||
                    (char.IsDigit(previous) && char.IsLetter(current));
                if (boundary && chars.Count > 0 && chars[^1] != ' ')
                {
                    chars.Add(' ');
                }
            }

            chars.Add(current);
        }

        return new string(chars.ToArray());
    }

    private static string? RemovePluralSuffixBeforeVersion(string entitySetName)
    {
        if (string.IsNullOrWhiteSpace(entitySetName))
        {
            return null;
        }

        var i = entitySetName.Length - 1;
        while (i >= 0 && char.IsDigit(entitySetName[i]))
        {
            i--;
        }

        if (i <= 1 || char.ToUpperInvariant(entitySetName[i]) != 'V')
        {
            return null;
        }

        var pluralIndex = i - 1;
        if (pluralIndex <= 0 || char.ToLowerInvariant(entitySetName[pluralIndex]) != 's')
        {
            return null;
        }

        return entitySetName.Remove(pluralIndex, 1);
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString(),
            JsonValueKind.Number => node.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? ReadInt32Property(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var node))
        {
            return null;
        }

        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var number))
        {
            return number;
        }

        if (node.ValueKind == JsonValueKind.String &&
            int.TryParse(node.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// The last environment's metadata XML held in memory alongside the ETag/timestamp of the row it came
    /// from, so a cached-details decision (and a sibling entity's parse) needs no second blob read.
    /// Immutable, so the single field can be swapped wholesale without tearing.
    /// </summary>
    private sealed record MetadataXmlMemo(string Key, string Xml, string? ETag, DateTime UpdatedUtc, long PruneGeneration);

    private sealed record PublicEntitiesResponse(List<PublicEntityDto>? Value);
    private sealed record PublicEntityDto(string? EntitySetName, List<PublicEntityPropertyDto>? Properties);
    private sealed record PublicEntityPropertyDto(string? Name, bool IsKey, bool IsMandatory);
    private sealed record DataManagementTargetMapRow(
        string? StagingField,
        string? ShortStagingField,
        string? TargetField,
        string? FieldAOTName,
        string? DataSourceField,
        int FieldLength);
}

public sealed record CatalogServiceOptions(TimeSpan TableMaxAge, TimeSpan MetadataMaxAge)
{
    public static CatalogServiceOptions Default { get; } = new(TimeSpan.FromDays(7), TimeSpan.FromHours(6));
}
