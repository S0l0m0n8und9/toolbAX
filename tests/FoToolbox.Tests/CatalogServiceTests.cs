using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class CatalogServiceTests
{
    [Fact]
    public async Task GetODataMetadataAsync_Uses_Cache_When_Fresh()
    {
        var xml = await System.IO.File.ReadAllTextAsync(System.IO.Path.Combine("Resources", "SampleMetadata.xml"));
        var handler = new CountingMetadataHandler(xml);
        var httpClient = new HttpClient(handler);
        var profileDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.db");
        var catalogDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.db");
        var profileStore = new ProfileStore(profileDb);
        await profileStore.EnsureCreatedAsync();
        var catalogStore = new CatalogStore(catalogDb);
        var service = new CatalogService(httpClient, profileStore, catalogStore, new CatalogServiceOptions(TimeSpan.FromDays(1), TimeSpan.FromDays(1)));
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");

        _ = await service.GetODataMetadataAsync(env, CatalogRefreshMode.UseCacheIfFresh, default);
        _ = await service.GetODataMetadataAsync(env, CatalogRefreshMode.UseCacheIfFresh, default);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetODataEntityIndexAsync_Refetches_When_The_Same_Profile_Id_Points_At_A_New_Url()
    {
        var xml = await System.IO.File.ReadAllTextAsync(System.IO.Path.Combine("Resources", "SampleMetadata.xml"));
        var handler = new CountingMetadataHandler(xml);
        var httpClient = new HttpClient(handler);
        var profileDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.db");
        var catalogDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.db");
        var profileStore = new ProfileStore(profileDb);
        await profileStore.EnsureCreatedAsync();
        var catalogStore = new CatalogStore(catalogDb);
        var service = new CatalogService(httpClient, profileStore, catalogStore, new CatalogServiceOptions(TimeSpan.FromDays(1), TimeSpan.FromDays(1)));

        // Editing a profile's URL keeps its id, so an id-only cache key would serve the first host's
        // $metadata for the second environment until the row aged out.
        var original = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");
        var repointed = new FoEnvironment("env", "Env", "https://fabrikam.operations.dynamics.com", "tenant", "USMF");

        _ = await service.GetODataEntityIndexAsync(original, CatalogRefreshMode.UseCacheIfFresh, default);
        _ = await service.GetODataEntityIndexAsync(repointed, CatalogRefreshMode.UseCacheIfFresh, default);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetODataEntityIndexAsync_Uses_Cache_For_The_Same_Id_And_Url()
    {
        var xml = await System.IO.File.ReadAllTextAsync(System.IO.Path.Combine("Resources", "SampleMetadata.xml"));
        var handler = new CountingMetadataHandler(xml);
        var httpClient = new HttpClient(handler);
        var profileDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.db");
        var catalogDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.db");
        var profileStore = new ProfileStore(profileDb);
        await profileStore.EnsureCreatedAsync();
        var catalogStore = new CatalogStore(catalogDb);
        var service = new CatalogService(httpClient, profileStore, catalogStore, new CatalogServiceOptions(TimeSpan.FromDays(1), TimeSpan.FromDays(1)));

        // Same id, and a URL differing only in casing/trailing slash — the key normalizes, so this is
        // still one environment and must hit the cache.
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");
        var cosmetic = new FoEnvironment("env", "Env", "https://Contoso.Operations.Dynamics.com/", "tenant", "USMF");

        _ = await service.GetODataEntityIndexAsync(env, CatalogRefreshMode.UseCacheIfFresh, default);
        _ = await service.GetODataEntityIndexAsync(cosmetic, CatalogRefreshMode.UseCacheIfFresh, default);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetTablesAsync_Does_Not_Use_Cache_When_MaxAge_Is_Zero()
    {
        var handler = new CountingMetadataHandler("<root />");
        var httpClient = new HttpClient(handler);
        var profileDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.db");
        var catalogDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.db");
        var profileStore = new ProfileStore(profileDb);
        await profileStore.EnsureCreatedAsync();
        var catalogStore = new CatalogStore(catalogDb);
        var service = new CatalogService(httpClient, profileStore, catalogStore, new CatalogServiceOptions(TimeSpan.Zero, TimeSpan.FromDays(1)));
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");

        var first = await service.GetTablesAsync(env, CatalogRefreshMode.UseCacheIfFresh, default);
        await Task.Delay(25);
        var second = await service.GetTablesAsync(env, CatalogRefreshMode.UseCacheIfFresh, default);

        Assert.True(second.UpdatedUtc > first.UpdatedUtc);
    }

    [Fact]
    public async Task ImportTableCatalogAsync_Overrides_Default()
    {
        var handler = new CountingMetadataHandler("<root />");
        var httpClient = new HttpClient(handler);
        var profileDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.db");
        var catalogDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.db");
        var profileStore = new ProfileStore(profileDb);
        await profileStore.EnsureCreatedAsync();
        var catalogStore = new CatalogStore(catalogDb);
        var service = new CatalogService(httpClient, profileStore, catalogStore);
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");

        var json = """
{
  "version": "test",
  "source": "User",
  "updatedUtc": "2026-02-09T00:00:00Z",
  "tables": [
    {
      "name": "TestTable",
      "label": "Test",
      "isView": false,
      "configurationKey": null,
      "isDeprecated": false,
      "notes": null
    }
  ]
}
""";

        var imported = await service.ImportTableCatalogAsync(env, json, default);
        var tables = await service.GetTablesAsync(env, CatalogRefreshMode.UseCacheIfFresh, default);

        Assert.Equal("UserImport", imported.Source);
        Assert.Equal("UserImport", tables.Source);
        Assert.Single(tables.Tables);
    }

    [Fact]
    public async Task GetODataEntityDetailsAsync_Enriches_Key_And_Mandatory_From_PublicEntities()
    {
        var xml = await System.IO.File.ReadAllTextAsync(System.IO.Path.Combine("Resources", "SampleMetadata.xml"));
        var json = """
{
  "value": [
    {
      "name": "CustomerV3",
      "entitySetName": "CustomersV3",
      "properties": [
        { "name": "AccountNumber", "isKey": true, "isMandatory": true },
        { "name": "Name", "isKey": false, "isMandatory": true },
        { "name": "CustomerType", "isKey": false, "isMandatory": false }
      ]
    }
  ]
}
""";

        var handler = new SwitchingHandler(xml, json);
        var httpClient = new HttpClient(handler);
        var profileDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.db");
        var catalogDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.db");
        var profileStore = new ProfileStore(profileDb);
        await profileStore.EnsureCreatedAsync();
        var catalogStore = new CatalogStore(catalogDb);
        var service = new CatalogService(httpClient, profileStore, catalogStore, new CatalogServiceOptions(TimeSpan.FromDays(1), TimeSpan.FromDays(1)));
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");

        var entity = await service.GetODataEntityDetailsAsync(env, "CustomersV3", CatalogRefreshMode.ForceRefresh, default);

        Assert.NotNull(entity);
        Assert.True(handler.MetadataCalls > 0);
        Assert.True(handler.PublicEntitiesCalls > 0);

        var account = entity!.Properties.First(p => p.Name == "AccountNumber");
        Assert.True(account.IsKey);
        Assert.True(account.IsMandatory);
        Assert.True(account.Mandatory);

        var name = entity.Properties.First(p => p.Name == "Name");
        Assert.False(name.IsKey);
        Assert.True(name.IsMandatory);
        Assert.True(name.Mandatory);

        var customerType = entity.Properties.First(p => p.Name == "CustomerType");
        Assert.False(customerType.IsKey);
        Assert.False(customerType.IsMandatory);
        Assert.False(customerType.Mandatory);
    }

    [Fact]
    public async Task GetODataEntityDetailsAsync_Enriches_MaxLength_From_DataManagementTargetMapEntities_With_Paging()
    {
        var xml = """
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="Default" xmlns="http://docs.oasis-open.org/odata/ns/edm">
      <EntityContainer Name="Container">
        <EntitySet Name="CustomersV3" EntityType="Default.CustomerV3" />
      </EntityContainer>
      <EntityType Name="CustomerV3">
        <Key>
          <PropertyRef Name="AccountNumber" />
        </Key>
        <Property Name="AccountNumber" Type="Edm.String" Nullable="false" />
        <Property Name="IdentificationNumber" Type="Edm.String" />
      </EntityType>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>
""";
        var publicEntitiesJson = """
{ "value": [] }
""";
        var targetMapPage1 = """
{
  "value": [
    { "Entity": "Customers V3", "FieldAOTName": "IgnoredField", "FieldLength": 10 }
  ],
  "@odata.nextLink": "https://contoso.operations.dynamics.com/data/DataManagementTargetMapEntities?$skiptoken=page2"
}
""";
        var targetMapPage2 = """
{
  "value": [
    { "Entity": "Customers V3", "FieldAOTName": "IdentificationNumber", "FieldLength": 50 }
  ]
}
""";

        var handler = new DataManagementPagingHandler(xml, publicEntitiesJson, targetMapPage1, targetMapPage2);
        var httpClient = new HttpClient(handler);
        var profileDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.db");
        var catalogDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.db");
        var profileStore = new ProfileStore(profileDb);
        await profileStore.EnsureCreatedAsync();
        var catalogStore = new CatalogStore(catalogDb);
        var service = new CatalogService(httpClient, profileStore, catalogStore, new CatalogServiceOptions(TimeSpan.FromDays(1), TimeSpan.FromDays(1)));
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");

        var entity = await service.GetODataEntityDetailsAsync(env, "CustomersV3", CatalogRefreshMode.ForceRefresh, default);

        Assert.NotNull(entity);
        Assert.True(handler.TargetMapCalls >= 2);
        Assert.Contains(handler.TargetMapQueries, q => q.Contains("Entity%20eq%20%27Customers%20V3%27", StringComparison.OrdinalIgnoreCase));
        var identificationNumber = entity!.Properties.First(p => p.Name == "IdentificationNumber");
        Assert.Equal("50", identificationNumber.MaxLength);
    }

    private sealed class CountingMetadataHandler : HttpMessageHandler
    {
        private readonly string _content;
        public int Calls { get; private set; }

        public CountingMetadataHandler(string content)
        {
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content)
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag\"");
            return Task.FromResult(response);
        }
    }

    private sealed class SwitchingHandler : HttpMessageHandler
    {
        private readonly string _xml;
        private readonly string _json;

        public int MetadataCalls { get; private set; }
        public int PublicEntitiesCalls { get; private set; }

        public SwitchingHandler(string xml, string json)
        {
            _xml = xml;
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string content;

            if (path.EndsWith("/data/$metadata", StringComparison.OrdinalIgnoreCase))
            {
                MetadataCalls++;
                content = _xml;
            }
            else if (path.EndsWith("/metadata/PublicEntities", StringComparison.OrdinalIgnoreCase))
            {
                PublicEntitiesCalls++;
                content = _json;
            }
            else
            {
                content = "{}";
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag\"");
            return Task.FromResult(response);
        }
    }

    private sealed class DataManagementPagingHandler : HttpMessageHandler
    {
        private readonly string _xml;
        private readonly string _publicEntitiesJson;
        private readonly string _targetMapPage1;
        private readonly string _targetMapPage2;

        public int TargetMapCalls { get; private set; }
        public List<string> TargetMapQueries { get; } = new();

        public DataManagementPagingHandler(
            string xml,
            string publicEntitiesJson,
            string targetMapPage1,
            string targetMapPage2)
        {
            _xml = xml;
            _publicEntitiesJson = publicEntitiesJson;
            _targetMapPage1 = targetMapPage1;
            _targetMapPage2 = targetMapPage2;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? new Uri("https://contoso.operations.dynamics.com/");
            var path = uri.AbsolutePath;
            var query = uri.Query ?? string.Empty;

            string content;
            if (path.EndsWith("/data/$metadata", StringComparison.OrdinalIgnoreCase))
            {
                content = _xml;
            }
            else if (path.EndsWith("/metadata/PublicEntities", StringComparison.OrdinalIgnoreCase))
            {
                content = _publicEntitiesJson;
            }
            else if (path.EndsWith("/data/DataManagementTargetMapEntities", StringComparison.OrdinalIgnoreCase))
            {
                TargetMapCalls++;
                TargetMapQueries.Add(query);
                var isPage2 = query.Contains("skiptoken=page2", StringComparison.OrdinalIgnoreCase);
                var hasExpectedEntityFilter = query.Contains("Entity%20eq%20%27Customers%20V3%27", StringComparison.OrdinalIgnoreCase);
                if (isPage2)
                {
                    content = _targetMapPage2;
                }
                else if (!hasExpectedEntityFilter)
                {
                    content = """{ "value": [] }""";
                }
                else
                {
                    content = _targetMapPage1;
                }
            }
            else
            {
                content = "{}";
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag\"");
            return Task.FromResult(response);
        }
    }
}
