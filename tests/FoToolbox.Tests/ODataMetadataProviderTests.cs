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
        Assert.Contains(customer.Navigations, n => n.Name == "SalesOrders");
        Assert.Contains(meta.Enums, e => e.Name == "Default.CustomerType");
        var enumType = meta.Enums.First(e => e.Name == "Default.CustomerType");
        Assert.Contains("Retail", enumType.Members);
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
