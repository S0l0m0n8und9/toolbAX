using System.Net;
using System.Text;
using FoToolbox.Core.OData;

namespace FoToolbox.Tests;

public sealed class PagingIntegrityTests
{
    [Fact]
    public async Task Self_link_throws_incomplete_before_repeat_dispatch_after_streaming_unique_page()
    {
        using var handler = new PagingHandler((uri, call) => call switch
        {
            1 => Json(1, uri.AbsoluteUri),
            _ => throw new InvalidOperationException("dispatch limit exceeded")
        });
        using var http = new HttpClient(handler);
        await using var pages = new HttpODataClient(http)
            .StreamAsync(new QueryRequest("https://host/data/Entity?$skiptoken=one"))
            .GetAsyncEnumerator();

        Assert.True(await pages.MoveNextAsync());
        Assert.Equal(1L, Convert.ToInt64(pages.Current.Rows.Single()["Id"]));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pages.MoveNextAsync().AsTask());

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_to_B_to_A_throws_before_third_dispatch()
    {
        const string first = "https://host/data/Entity?$skiptoken=A";
        const string second = "https://host/data/Entity?$skiptoken=B";
        using var handler = new PagingHandler((_, call) => call switch
        {
            1 => Json(1, second),
            2 => Json(2, first),
            _ => throw new InvalidOperationException("dispatch limit exceeded")
        });
        using var http = new HttpClient(handler);
        await using var pages = new HttpODataClient(http)
            .StreamAsync(new QueryRequest(first))
            .GetAsyncEnumerator();

        Assert.True(await pages.MoveNextAsync());
        Assert.True(await pages.MoveNextAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pages.MoveNextAsync().AsTask());

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Relative_initial_and_canonical_absolute_alias_share_actual_request_identity()
    {
        using var handler = new PagingHandler((_, call) => call switch
        {
            1 => Json(1, "https://HOST:443/root/data/Entity#not-sent"),
            _ => throw new InvalidOperationException("dispatch limit exceeded")
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host/root/") };
        await using var pages = new HttpODataClient(http)
            .StreamAsync(new QueryRequest("data/Entity"))
            .GetAsyncEnumerator();

        Assert.True(await pages.MoveNextAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pages.MoveNextAsync().AsTask());

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("https://host/root/data/Entity", handler.Requests.Single().GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task Case_distinct_skip_tokens_remain_distinct_requests()
    {
        const string first = "https://host/data/Entity?$skiptoken=Start";
        const string upper = "https://host/data/Entity?$skiptoken=Case";
        const string lower = "https://host/data/Entity?$skiptoken=case";
        using var handler = new PagingHandler((_, call) => call switch
        {
            1 => Json(1, upper),
            2 => Json(2, lower),
            3 => Json(3, null),
            _ => throw new InvalidOperationException("dispatch limit exceeded")
        });
        using var http = new HttpClient(handler);

        var ids = new List<long>();
        await foreach (var page in new HttpODataClient(http).StreamAsync(new QueryRequest(first)))
            ids.Add(Convert.ToInt64(page.Rows.Single()["Id"]));

        Assert.Equal(new long[] { 1, 2, 3 }, ids);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Relative_continuation_is_checked_against_the_actual_BaseAddress_dispatch_origin()
    {
        using var handler = new PagingHandler((_, call) => call switch
        {
            1 => Json(1, "next"),
            _ => throw new InvalidOperationException("foreign dispatch occurred")
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://base-b.example/root/") };
        await using var pages = new HttpODataClient(http)
            .StreamAsync(new QueryRequest("https://initial-a.example/data/Entity"))
            .GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pages.MoveNextAsync().AsTask());

        Assert.Contains("different origin", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain(handler.Requests, request =>
            string.Equals(request.Host, "base-b.example", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("123")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task Invalid_present_nextLink_is_incomplete_instead_of_successful_completion(string nextLinkJson)
    {
        using var handler = new PagingHandler((_, _) => RawJson(
            $"{{\"value\":[{{\"Id\":1}}],\"@odata.nextLink\":{nextLinkJson}}}"));
        using var http = new HttpClient(handler);
        await using var pages = new HttpODataClient(http)
            .StreamAsync(new QueryRequest("https://host/data/Entity"))
            .GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pages.MoveNextAsync().AsTask());

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_null_nextLink_is_a_valid_final_page(bool includeNull)
    {
        var next = includeNull ? ",\"@odata.nextLink\":null" : string.Empty;
        using var handler = new PagingHandler((_, _) => RawJson($"{{\"value\":[{{\"Id\":1}}]{next}}}"));
        using var http = new HttpClient(handler);

        var pages = new List<ODataPage>();
        await foreach (var page in new HttpODataClient(http).StreamAsync(new QueryRequest("https://host/data/Entity")))
            pages.Add(page);

        Assert.Single(pages);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"value\":{}}")]
    public async Task Malformed_collection_envelope_after_a_good_page_fails_incomplete(string malformedPage)
    {
        using var handler = new PagingHandler((_, call) => call switch
        {
            1 => Json(1, "https://host/data/Entity?$skiptoken=two"),
            2 => RawJson(malformedPage),
            _ => throw new InvalidOperationException("dispatch limit exceeded")
        });
        using var http = new HttpClient(handler);
        await using var pages = new HttpODataClient(http)
            .StreamAsync(new QueryRequest("https://host/data/Entity?$skiptoken=one"))
            .GetAsyncEnumerator();

        Assert.True(await pages.MoveNextAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pages.MoveNextAsync().AsTask());

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("skiptoken", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Calls);
    }

    private static HttpResponseMessage Json(int id, string? next)
    {
        var suffix = next is null ? string.Empty : $",\"@odata.nextLink\":{System.Text.Json.JsonSerializer.Serialize(next)}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"value\":[{{\"Id\":{id}}}]{suffix}}}", Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage RawJson(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class PagingHandler(Func<Uri, int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI missing.");
            Requests.Add(uri);
            return Task.FromResult(response(uri, Calls));
        }
    }
}
