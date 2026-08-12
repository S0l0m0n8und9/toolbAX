using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
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

    // ── Cache-first entity details (#168) ─────────────────────────────────────────────────────────────
    //
    // GetODataEntityDetailsAsync used to materialize the whole $metadata document — tens of MB on F&O —
    // out of SQLite before it looked at the per-entity details row, so a full cache hit still paid for a
    // large-object-heap string per entity click. The tests below pin the cheap path and, just as
    // importantly, that the freshness rules did not move when the order did.

    private const string TwoEntityMetadataXml = """
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="Default" xmlns="http://docs.oasis-open.org/odata/ns/edm">
      <EntityContainer Name="Container">
        <EntitySet Name="CustomersV3" EntityType="Default.CustomerV3" />
        <EntitySet Name="VendorsV2" EntityType="Default.VendorV2" />
      </EntityContainer>
      <EntityType Name="CustomerV3">
        <Key>
          <PropertyRef Name="AccountNumber" />
        </Key>
        <Property Name="AccountNumber" Type="Edm.String" Nullable="false" />
        <Property Name="Name" Type="Edm.String" />
      </EntityType>
      <EntityType Name="VendorV2">
        <Key>
          <PropertyRef Name="VendorAccount" />
        </Key>
        <Property Name="VendorAccount" Type="Edm.String" Nullable="false" />
      </EntityType>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>
""";

    // The same document with a third property on CustomersV3, so a reparse is visible in the result.
    private const string WidenedCustomerMetadataXml = """
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
        <Property Name="Name" Type="Edm.String" />
        <Property Name="CreditLimit" Type="Edm.Decimal" />
      </EntityType>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>
""";

    private static readonly FoEnvironment Contoso =
        new("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");

    private static async Task<(CatalogService Service, string CatalogDb)> MakeServiceAsync(
        HttpMessageHandler handler,
        TimeSpan metadataMaxAge)
    {
        var profileDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.db");
        var catalogDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.db");
        var profileStore = new ProfileStore(profileDb);
        await profileStore.EnsureCreatedAsync();
        var service = new CatalogService(
            new HttpClient(handler),
            profileStore,
            new CatalogStore(catalogDb),
            new CatalogServiceOptions(TimeSpan.FromDays(1), metadataMaxAge));
        return (service, catalogDb);
    }

    // Drops the cached $metadata blob straight out of the database. This is what makes "did not read the
    // metadata-XML row" observable: a service that still reads that row per entity click has to fall back
    // to HTTP once the row is gone, while one answering from its in-memory memo does not.
    private static async Task DeleteCachedMetadataXmlAsync(string catalogDb)
    {
        await using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = catalogDb }.ToString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CatalogData WHERE Kind = 'ODataMetadataXml'";

        // Guard the guard: if the warm-up never cached the XML, the assertions below would prove nothing.
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task GetODataEntityDetailsAsync_Serves_A_Cached_Entity_Without_Reading_The_Metadata_Xml()
    {
        var handler = new ScriptedMetadataHandler((TwoEntityMetadataXml, "etag-1"));
        var (service, catalogDb) = await MakeServiceAsync(handler, TimeSpan.FromDays(1));

        var first = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfAvailable, default);
        Assert.NotNull(first);
        Assert.Equal(1, handler.MetadataCalls);

        await DeleteCachedMetadataXmlAsync(catalogDb);

        // Clicking the same entity again is a full cache hit: the details row plus the memoized ETag settle
        // it, so neither the (now deleted) XML row nor the network is touched.
        var second = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfAvailable, default);

        Assert.NotNull(second);
        Assert.Equal(1, handler.MetadataCalls);
        Assert.Equal(first!.Properties.Select(p => p.Name), second!.Properties.Select(p => p.Name));
    }

    [Fact]
    public async Task GetODataEntityDetailsAsync_Parses_A_Second_Entity_From_The_Memo_Without_Re_Reading_The_Xml()
    {
        var handler = new ScriptedMetadataHandler((TwoEntityMetadataXml, "etag-1"));
        var (service, catalogDb) = await MakeServiceAsync(handler, TimeSpan.FromDays(1));

        _ = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfAvailable, default);
        Assert.Equal(1, handler.MetadataCalls);

        await DeleteCachedMetadataXmlAsync(catalogDb);

        // A miss for a *different* entity under the same environment still needs the document — but the
        // memo already holds it, so browsing entity to entity doesn't re-read the blob per click.
        var vendors = await service.GetODataEntityDetailsAsync(Contoso, "VendorsV2", CatalogRefreshMode.UseCacheIfAvailable, default);

        Assert.NotNull(vendors);
        Assert.Contains(vendors!.Properties, p => p.Name == "VendorAccount");
        Assert.Equal(1, handler.MetadataCalls);
    }

    [Fact]
    public async Task GetODataEntityDetailsAsync_Refetches_When_The_Cached_Row_Is_Stale_And_There_Is_No_ETag()
    {
        // No ETag on the response and MetadataMaxAge = 0 → age is the only evidence, and it never says
        // "fresh". Both the memo and the cached details row must be refused every time.
        var handler = new ScriptedMetadataHandler((TwoEntityMetadataXml, null));
        var (service, _) = await MakeServiceAsync(handler, TimeSpan.Zero);

        _ = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfFresh, default);
        _ = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfFresh, default);

        Assert.Equal(2, handler.MetadataCalls);
    }

    [Fact]
    public async Task GetODataEntityDetailsAsync_Serves_A_Stale_Row_When_The_ETag_Still_Matches()
    {
        // The mirror of the test above: with MetadataMaxAge = 0 the row is always stale, but a matching
        // ETag proves the metadata it was parsed from hasn't moved, so the row is still served (and the
        // document is not re-parsed) — the revalidation round-trip happens, the reparse does not.
        var handler = new ScriptedMetadataHandler((TwoEntityMetadataXml, "etag-1"), (WidenedCustomerMetadataXml, "etag-1"));
        var (service, _) = await MakeServiceAsync(handler, TimeSpan.Zero);

        _ = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfFresh, default);
        var second = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfFresh, default);

        Assert.Equal(2, handler.MetadataCalls);
        Assert.DoesNotContain(second!.Properties, p => p.Name == "CreditLimit");
    }

    [Fact]
    public async Task GetODataEntityDetailsAsync_Reparses_When_The_Metadata_ETag_Changed()
    {
        // Same setup, but the second response carries a new ETag and a widened entity: the cached row's
        // ETag no longer matches, so it must be discarded and the entity reparsed.
        var handler = new ScriptedMetadataHandler((TwoEntityMetadataXml, "etag-1"), (WidenedCustomerMetadataXml, "etag-2"));
        var (service, _) = await MakeServiceAsync(handler, TimeSpan.Zero);

        var first = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfFresh, default);
        var second = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfFresh, default);

        Assert.DoesNotContain(first!.Properties, p => p.Name == "CreditLimit");
        Assert.Contains(second!.Properties, p => p.Name == "CreditLimit");
    }

    [Fact]
    public async Task GetODataEntityDetailsAsync_ForceRefresh_Reparses_Even_On_A_Warm_Memo()
    {
        var handler = new ScriptedMetadataHandler((TwoEntityMetadataXml, "etag-1"), (WidenedCustomerMetadataXml, "etag-1"));
        var (service, _) = await MakeServiceAsync(handler, TimeSpan.FromDays(1));

        _ = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfAvailable, default);
        var forced = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.ForceRefresh, default);

        // A forced refresh bypasses the memo and the cached row even though the ETag is unchanged.
        Assert.Equal(2, handler.MetadataCalls);
        Assert.Contains(forced!.Properties, p => p.Name == "CreditLimit");
    }

    [Fact]
    public async Task GetODataEntityDetailsAsync_Does_Not_Serve_Another_Environments_Memo()
    {
        // The memo holds exactly one environment. Repointing a profile's URL keeps its id, so a memo keyed
        // on anything looser than the cache key would answer the new host with the old host's metadata.
        var handler = new ScriptedMetadataHandler((TwoEntityMetadataXml, "etag-1"), (WidenedCustomerMetadataXml, "etag-2"));
        var (service, _) = await MakeServiceAsync(handler, TimeSpan.FromDays(1));
        var repointed = new FoEnvironment("env", "Env", "https://fabrikam.operations.dynamics.com", "tenant", "USMF");

        var contoso = await service.GetODataEntityDetailsAsync(Contoso, "CustomersV3", CatalogRefreshMode.UseCacheIfAvailable, default);
        var fabrikam = await service.GetODataEntityDetailsAsync(repointed, "CustomersV3", CatalogRefreshMode.UseCacheIfAvailable, default);

        Assert.Equal(2, handler.MetadataCalls);
        Assert.DoesNotContain(contoso!.Properties, p => p.Name == "CreditLimit");
        Assert.Contains(fabrikam!.Properties, p => p.Name == "CreditLimit");
    }

    // Serves a scripted sequence of $metadata responses (body + optional ETag), counting only the
    // $metadata calls; every other path answers "{}" so the best-effort enrichment round-trips stay inert.
    // The last scripted entry repeats, so a test scripts only the responses it cares about.
    private sealed class ScriptedMetadataHandler : HttpMessageHandler
    {
        private readonly (string Xml, string? ETag)[] _script;

        public ScriptedMetadataHandler(params (string Xml, string? ETag)[] script) => _script = script;

        public int MetadataCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/data/$metadata", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }

            var step = _script[Math.Min(MetadataCalls, _script.Length - 1)];
            MetadataCalls++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(step.Xml) };
            if (step.ETag is not null)
            {
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{step.ETag}\"");
            }

            return Task.FromResult(response);
        }
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
