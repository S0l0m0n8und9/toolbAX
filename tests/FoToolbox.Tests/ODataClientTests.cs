using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Xunit;

namespace FoToolbox.Tests;

public class ODataClientTests
{
    [Fact]
    public async Task Follows_NextLink_And_Yields_All_Rows()
    {
        var handler = new SequenceHandler(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"@odata.context\":\"ctx\",\"@odata.count\":2,\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"https://next\"}", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":2}]}", Encoding.UTF8, "application/json")
            }
        });
        var client = new HttpClient(handler);
        var odata = new HttpODataClient(client);

        var pages = new List<ODataPage>();
        await foreach (var page in odata.StreamAsync(new QueryRequest("https://first"), CancellationToken.None))
        {
            pages.Add(page);
        }

        Assert.Equal(2, pages.Count);
        Assert.Equal(1L, Convert.ToInt64(pages[0].Rows[0]["Id"]));
        Assert.Equal(2L, Convert.ToInt64(pages[1].Rows[0]["Id"]));
        Assert.Equal(2L, pages[0].ODataCount);
        Assert.Equal("ctx", pages[0].ODataContext);
        Assert.NotNull(pages[0].ResponseHeaders);
        Assert.True(pages[0].ResponseHeaders!.ContainsKey("Content-Type"));
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task StreamAsync_Unauthorized_Returns_Clear_Reauth_Message()
    {
        var handler = new SequenceHandler(new[]
        {
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"unauthorized\"}", Encoding.UTF8, "application/json")
            }
        });
        var client = new HttpClient(handler);
        var odata = new HttpODataClient(client);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in odata.StreamAsync(new QueryRequest("https://first"), CancellationToken.None))
            {
            }
        });

        Assert.Contains("Authentication needs to be refreshed", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Re-authenticate in Profiles", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequenceHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No more responses configured.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
