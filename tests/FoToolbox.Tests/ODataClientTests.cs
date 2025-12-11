using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
                Content = new StringContent("{\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"https://next\"}")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":2}]}")
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
