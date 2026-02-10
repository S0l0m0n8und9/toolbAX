using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
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
}
