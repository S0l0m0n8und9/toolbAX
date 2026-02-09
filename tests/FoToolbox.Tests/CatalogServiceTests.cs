using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
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
}
