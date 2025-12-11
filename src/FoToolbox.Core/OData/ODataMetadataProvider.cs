using System;
using System.Collections.Generic;
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

    public ODataMetadataProvider(HttpClient httpClient, ODataMetadataCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<ODataMetadata> GetMetadataAsync(string envId, string baseUrl, CancellationToken cancellationToken = default)
    {
        await _cache.EnsureCreatedAsync(cancellationToken);
        var cached = await _cache.GetAsync(envId, cancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/data/$metadata");
        if (cached?.ETag is { Length: > 0 })
        {
            request.Headers.IfNoneMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{cached.Value.ETag}\""));
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cached is not null)
        {
            return Parse(cached.Value.RawXml, cached.Value.ETag);
        }

        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var etag = response.Headers.ETag?.Tag?.Trim('"');

        await _cache.SaveAsync(envId, etag, xml, cancellationToken);
        return Parse(xml, etag);
    }

    public static ODataMetadata Parse(string rawXml, string? etag = null)
    {
        var doc = XDocument.Parse(rawXml);
        XNamespace edm = "http://docs.oasis-open.org/odata/ns/edm";

        var entities = new List<ODataEntity>();
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

        return new ODataMetadata(entities, etag);
    }
}
