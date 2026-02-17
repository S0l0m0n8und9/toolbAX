using FoToolbox.Core.OData;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class ODataMetadataProviderTests
{
    [Fact]
    public async Task Parses_Entities_From_Metadata()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine("Resources", "SampleMetadata.xml"));
        var db = Path.GetTempFileName();
        var store = new FoToolbox.Core.Profiles.ProfileStore(db);
        await store.EnsureCreatedAsync();
        var cache = new ODataMetadataCache(store.ConnectionString);
        var provider = new ODataMetadataProvider(new HttpClient(new StaticHandler(xml)), cache);
        var meta = await provider.GetMetadataAsync("env1", "https://contoso.operations.dynamics.com", default);

        Assert.Equal(2, meta.Entities.Count);
        Assert.Contains(meta.Entities, e => e.Name == "CustomersV3");
        var customer = meta.Entities.First(e => e.Name == "CustomersV3");
        Assert.Contains(customer.Properties, p => p.Name == "AccountNumber");
        Assert.True(customer.Properties.First(p => p.Name == "AccountNumber").IsKey);
        Assert.True(customer.Properties.First(p => p.Name == "AccountNumber").Mandatory);
        Assert.False(customer.Properties.First(p => p.Name == "Name").IsKey);
        Assert.False(customer.Properties.First(p => p.Name == "Name").Mandatory);
        Assert.Equal("20", customer.Properties.First(p => p.Name == "AccountNumber").MaxLength);

        var salesOrder = meta.Entities.First(e => e.Name == "SalesOrdersV3");
        var totalAmount = salesOrder.Properties.First(p => p.Name == "TotalAmount");
        Assert.Equal("32", totalAmount.Precision);
        Assert.Equal("6", totalAmount.Scale);
        Assert.Equal("0", totalAmount.MinValue);
        Assert.Equal("999999999999.999999", totalAmount.MaxValue);

        var quantity = salesOrder.Properties.First(p => p.Name == "Quantity");
        Assert.Equal("1", quantity.MinValue);
        Assert.Equal("1000", quantity.MaxValue);

        Assert.Contains(customer.Navigations, n => n.Name == "SalesOrders");
        Assert.Contains(meta.Enums, e => e.Name == "Default.CustomerType");
        var enumType = meta.Enums.First(e => e.Name == "Default.CustomerType");
        Assert.Contains("Retail", enumType.Members);
    }

    [Fact]
    public async Task Parses_Aliased_Annotation_Facets_And_Default_Int32_Range()
    {
        var xml = """
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="Default" xmlns="http://docs.oasis-open.org/odata/ns/edm">
      <EntityContainer Name="Container">
        <EntitySet Name="AliasEntities" EntityType="Default.AliasEntity" />
      </EntityContainer>
      <EntityType Name="AliasEntity">
        <Property Name="Code" Type="Edm.String">
          <Annotation Term="Validation.MaxLength" Int="12" />
        </Property>
        <Property Name="Level" Type="Edm.Int32" />
        <Property Name="Threshold" Type="Edm.Int32">
          <Annotation Term="Validation.Minimum" Int="5" />
        </Property>
      </EntityType>
    </Schema>
    <Schema Namespace="Org.OData.Validation.V1" Alias="Validation" xmlns="http://docs.oasis-open.org/odata/ns/edm" />
  </edmx:DataServices>
</edmx:Edmx>
""";

        var db = Path.GetTempFileName();
        var store = new FoToolbox.Core.Profiles.ProfileStore(db);
        await store.EnsureCreatedAsync();
        var cache = new ODataMetadataCache(store.ConnectionString);
        var provider = new ODataMetadataProvider(new HttpClient(new StaticHandler(xml)), cache);

        var meta = await provider.GetMetadataAsync("env1", "https://contoso.operations.dynamics.com", default);
        var entity = meta.Entities.First(e => e.Name == "AliasEntities");

        var code = entity.Properties.First(p => p.Name == "Code");
        Assert.Equal("12", code.MaxLength);

        var level = entity.Properties.First(p => p.Name == "Level");
        Assert.Equal("-2147483648", level.MinValue);
        Assert.Equal("2147483647", level.MaxValue);

        var threshold = entity.Properties.First(p => p.Name == "Threshold");
        Assert.Equal("5", threshold.MinValue);
        Assert.Equal("2147483647", threshold.MaxValue);
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly string _content;
        public StaticHandler(string content) => _content = content;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content)
            };
            resp.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag\"");
            return Task.FromResult(resp);
        }
    }
}
