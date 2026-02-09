using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FoToolbox.Core.OData;

/// <summary>
/// Fetches and parses $metadata with simple caching.
/// </summary>
public sealed class ODataMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ODataMetadataCache _cache;
    private readonly ODataMetadataProviderOptions _options;
    private readonly ConcurrentDictionary<string, InMemoryEntry> _memory = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ODataMetadataProvider(HttpClient httpClient, ODataMetadataCache cache)
        : this(httpClient, cache, ODataMetadataProviderOptions.Default)
    {
    }

    public ODataMetadataProvider(HttpClient httpClient, ODataMetadataCache cache, ODataMetadataProviderOptions? options)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options ?? ODataMetadataProviderOptions.Default;
    }

    public async Task<ODataMetadata> GetMetadataAsync(string envId, string baseUrl, CancellationToken cancellationToken = default)
    {
        return await GetMetadataAsync(envId, baseUrl, ODataMetadataRequestOptions.Default, cancellationToken);
    }

    public async Task<ODataMetadata> GetMetadataAsync(
        string envId,
        string baseUrl,
        ODataMetadataRequestOptions? options,
        CancellationToken cancellationToken = default)
    {
        var requestOptions = options ?? ODataMetadataRequestOptions.Default;
        var maxAge = requestOptions.MaxAge ?? _options.MaxAge;

        if (!requestOptions.ForceRefresh && maxAge > TimeSpan.Zero && _options.EnableInMemoryCache)
        {
            if (_memory.TryGetValue(envId, out var cachedMem) && IsFresh(cachedMem.UpdatedUtc, maxAge))
            {
                return cachedMem.Metadata;
            }
        }

        await _cache.EnsureCreatedAsync(cancellationToken);
        var gate = _locks.GetOrAdd(envId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!requestOptions.ForceRefresh && maxAge > TimeSpan.Zero && _options.EnableInMemoryCache)
            {
                if (_memory.TryGetValue(envId, out var cachedMem) && IsFresh(cachedMem.UpdatedUtc, maxAge))
                {
                    return cachedMem.Metadata;
                }
            }

            var cached = await _cache.GetEntryAsync(envId, cancellationToken);
            if (!requestOptions.ForceRefresh && maxAge > TimeSpan.Zero && cached is not null && IsFresh(cached.UpdatedUtc, maxAge))
            {
                return GetCachedMetadata(envId, cached);
            }

            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/data/$metadata");
            if (!string.IsNullOrWhiteSpace(cached?.ETag))
            {
                request.Headers.IfNoneMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{cached.ETag}\""));
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cached is not null)
            {
                var touchedUtc = await _cache.TouchAsync(envId, cancellationToken);
                return GetCachedMetadata(envId, cached, touchedUtc);
            }

            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var etag = response.Headers.ETag?.Tag?.Trim('"');
            var updatedUtc = DateTime.UtcNow;

            await _cache.SaveAsync(envId, etag, xml, cancellationToken);
            return ParseAndCache(envId, xml, etag, updatedUtc);
        }
        finally
        {
            gate.Release();
        }
    }

    public static ODataMetadata Parse(string rawXml, string? etag = null)
    {
        var doc = XDocument.Parse(rawXml);
        XNamespace edm = "http://docs.oasis-open.org/odata/ns/edm";

        var entities = new List<ODataEntity>();
        var enums = new List<ODataEnumType>();
        foreach (var enumType in doc.Descendants(edm + "EnumType"))
        {
            var name = enumType.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var ns = enumType.Ancestors(edm + "Schema").FirstOrDefault()?.Attribute("Namespace")?.Value;
            var fullName = string.IsNullOrWhiteSpace(ns) ? name : $"{ns}.{name}";
            var members = enumType.Elements(edm + "Member")
                .Select(m => m.Attribute("Name")?.Value)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m!)
                .ToList();
            enums.Add(new ODataEnumType(fullName, members));
        }
        foreach (var entityType in doc.Descendants(edm + "EntityType"))
        {
            var name = entityType.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var props = new List<ODataProperty>();
            var navs = new List<ODataNavigationProperty>();
            foreach (var prop in entityType.Elements(edm + "Property"))
            {
                var propName = prop.Attribute("Name")?.Value;
                var type = prop.Attribute("Type")?.Value ?? "Edm.String";
                var nullable = prop.Attribute("Nullable")?.Value != "false";
                if (!string.IsNullOrWhiteSpace(propName))
                {
                    props.Add(new ODataProperty(propName!, type, nullable));
                }
            }

            foreach (var nav in entityType.Elements(edm + "NavigationProperty"))
            {
                var navName = nav.Attribute("Name")?.Value;
                var type = nav.Attribute("Type")?.Value;
                if (!string.IsNullOrWhiteSpace(navName) && !string.IsNullOrWhiteSpace(type))
                {
                    navs.Add(new ODataNavigationProperty(navName!, type!));
                }
            }

            entities.Add(new ODataEntity(name, props, navs));
        }

        return new ODataMetadata(entities, enums, etag);
    }

    private static bool IsFresh(DateTime updatedUtc, TimeSpan maxAge)
    {
        return (DateTime.UtcNow - updatedUtc) <= maxAge;
    }

    private ODataMetadata GetCachedMetadata(string envId, ODataMetadataCacheEntry cached, DateTime? updatedUtcOverride = null)
    {
        var updatedUtc = updatedUtcOverride ?? cached.UpdatedUtc;
        if (_options.EnableInMemoryCache &&
            _memory.TryGetValue(envId, out var mem) &&
            string.Equals(mem.ETag, cached.ETag, StringComparison.Ordinal))
        {
            if (mem.UpdatedUtc != updatedUtc)
            {
                _memory[envId] = mem with { UpdatedUtc = updatedUtc };
            }

            return mem.Metadata;
        }

        return ParseAndCache(envId, cached.RawXml, cached.ETag, updatedUtc);
    }

    private ODataMetadata ParseAndCache(string envId, string rawXml, string? etag, DateTime updatedUtc)
    {
        var metadata = Parse(rawXml, etag);
        if (_options.EnableInMemoryCache)
        {
            _memory[envId] = new InMemoryEntry(metadata, etag, updatedUtc);
        }

        return metadata;
    }

    private sealed record InMemoryEntry(ODataMetadata Metadata, string? ETag, DateTime UpdatedUtc);
}

public sealed record ODataMetadataProviderOptions(TimeSpan MaxAge, bool EnableInMemoryCache)
{
    public static ODataMetadataProviderOptions Default { get; } = new(TimeSpan.FromHours(6), true);
}

public sealed record ODataMetadataRequestOptions(bool ForceRefresh, TimeSpan? MaxAge)
{
    public static ODataMetadataRequestOptions Default { get; } = new(false, null);
}
