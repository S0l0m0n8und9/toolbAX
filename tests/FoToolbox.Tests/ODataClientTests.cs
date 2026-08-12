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
            // The nextLink stays on the same origin as the initial request (as a real server returns).
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"@odata.context\":\"ctx\",\"@odata.count\":2,\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"https://host/data/Entity?$skiptoken=2\"}", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":2}]}", Encoding.UTF8, "application/json")
            }
        });
        var client = new HttpClient(handler);
        var odata = new HttpODataClient(client);

        var pages = new List<ODataPage>();
        await foreach (var page in odata.StreamAsync(new QueryRequest("https://host/data/Entity"), CancellationToken.None))
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

    [Fact]
    public async Task Refuses_To_Follow_A_NextLink_On_A_Different_Origin()
    {
        var handler = new SequenceHandler(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"https://evil.example.com/steal\"}", Encoding.UTF8, "application/json")
            }
        });
        var odata = new HttpODataClient(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in odata.StreamAsync(new QueryRequest("https://host/data/Entity"), CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task Refuses_A_Cross_Origin_NextLink_When_The_Initial_Url_Is_Relative()
    {
        // A relative request URL + HttpClient.BaseAddress: the origin must come from BaseAddress so the
        // guard isn't silently bypassed (the request URL alone isn't absolute).
        var handler = new SequenceHandler(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"https://evil.example.com/steal\"}", Encoding.UTF8, "application/json")
            }
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://host") };
        var odata = new HttpODataClient(client);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in odata.StreamAsync(new QueryRequest("data/Entity"), CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task Refuses_A_Scheme_Relative_NextLink_That_Swaps_The_Host()
    {
        // "//attacker.example/steal" is a network-path reference (RFC 3986 4.2): it is NOT parseable as
        // an absolute URI, so an absolute-only origin check skips it, and HttpClient then resolves it
        // against BaseAddress -- keeping the scheme but replacing the authority, bearer token included.
        var handler = new SequenceHandler(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"//attacker.example/steal\"}", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":2}]}", Encoding.UTF8, "application/json")
            }
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://host") };
        var odata = new HttpODataClient(client);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in odata.StreamAsync(new QueryRequest("https://host/data/Entity"), CancellationToken.None))
            {
            }
        });

        Assert.Contains("different origin", failure.Message, StringComparison.OrdinalIgnoreCase);
        // The message must name what was refused and what it was measured against: on Windows this link
        // parses as an implicit UNC file:// URI, so an absolute-only check can reject it by accident
        // (scheme mismatch) while the same link sails straight through on Linux.
        Assert.Contains("//attacker.example/steal", failure.Message, StringComparison.Ordinal);
        Assert.Contains("https://host", failure.Message, StringComparison.Ordinal);
        // The decisive assertion: the request to the foreign authority was never made.
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(handler.Requests, uri => uri is not null && uri.Contains("attacker.example", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Follows_A_Scheme_Relative_NextLink_On_The_Same_Origin()
    {
        // Same reference form, same origin: guarded, not blanket-rejected.
        var handler = new SequenceHandler(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"//host/data/Entity?$skiptoken=2\"}", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":2}]}", Encoding.UTF8, "application/json")
            }
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://host") };
        var odata = new HttpODataClient(client);

        var pages = new List<ODataPage>();
        await foreach (var page in odata.StreamAsync(new QueryRequest("https://host/data/Entity"), CancellationToken.None))
        {
            pages.Add(page);
        }

        Assert.Equal(2, pages.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Follows_A_Relative_NextLink_On_The_Request_Origin()
    {
        var handler = new SequenceHandler(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"/data/Entity?$skiptoken=2\"}", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"Id\":2}]}", Encoding.UTF8, "application/json")
            }
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://host") };
        var odata = new HttpODataClient(client);

        var pages = new List<ODataPage>();
        await foreach (var page in odata.StreamAsync(new QueryRequest("https://host/data/Entity"), CancellationToken.None))
        {
            pages.Add(page);
        }

        Assert.Equal(2, pages.Count);
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

        /// <summary>Absolute URI of every request that actually left the client, in order.</summary>
        public List<string?> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri?.ToString());

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No more responses configured.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
