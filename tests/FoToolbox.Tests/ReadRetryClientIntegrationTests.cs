using FoToolbox.Core.Catalog;
using FoToolbox.Core.DualWrite;
using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Net;
using FoToolbox.Core.OData;
using FoToolbox.Core.Profiles;
using System.Net;
using System.Net.Http.Headers;

namespace FoToolbox.Tests;

public sealed class ReadRetryClientIntegrationTests
{
    private static ReadRetryPolicy ImmediatePolicy() => new(initialBackoff: TimeSpan.Zero);

    [Fact]
    public async Task HttpODataClient_retries_one_transient_page_and_yields_the_success_once()
    {
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.ServiceUnavailable, "temporary", retryAfter: TimeSpan.Zero),
            _ => Response(HttpStatusCode.OK, "{\"value\":[{\"Id\":1}]}"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var pages = new List<ODataPage>();

        await foreach (var page in new HttpODataClient(http, ImmediatePolicy())
            .StreamAsync(new QueryRequest("data/Rows")))
            pages.Add(page);

        Assert.Equal(2, handler.Calls);
        Assert.Single(pages);
        Assert.Single(pages[0].Rows);
    }

    [Fact]
    public async Task HttpODataClient_exhausts_three_transient_attempts_without_yielding_a_page()
    {
        var handler = new SequenceHandler(_ => Response(HttpStatusCode.ServiceUnavailable, "temporary"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        await using var pages = new HttpODataClient(http, ImmediatePolicy())
            .StreamAsync(new QueryRequest("data/Rows"))
            .GetAsyncEnumerator();

        await Assert.ThrowsAsync<HttpRequestException>(() => pages.MoveNextAsync().AsTask());
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task HttpODataClient_accepts_page_larger_than_httpclient_response_buffer_limit()
    {
        var value = new string('x', 128);
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.OK, $"{{\"value\":[{{\"Name\":\"{value}\"}}]}}"));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/"),
            MaxResponseContentBufferSize = 16
        };
        var pages = new List<ODataPage>();

        await foreach (var page in new HttpODataClient(http, ImmediatePolicy())
            .StreamAsync(new QueryRequest("data/Rows")))
            pages.Add(page);

        var resultPage = Assert.Single(pages);
        var row = Assert.Single(resultPage.Rows);
        Assert.Equal(value, row["Name"]);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task HttpODataClient_preserves_retry_budget_timeout_classification()
    {
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.OK, "{\"value\":[]}"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var policy = new ReadRetryPolicy(
            overallBudget: TimeSpan.FromSeconds(1),
            timeProvider: new ExpiredAfterStartTimeProvider());
        await using var pages = new HttpODataClient(http, policy)
            .StreamAsync(new QueryRequest("data/Rows"))
            .GetAsyncEnumerator();

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => pages.MoveNextAsync().AsTask());

        Assert.Contains("budget", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task HttpODataClient_classifies_final_timeout_shaped_cancellation_as_timeout()
    {
        var handler = new SequenceHandler(
            _ => throw new OperationCanceledException("client timeout"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var policy = new ReadRetryPolicy(maximumAttempts: 1);
        await using var pages = new HttpODataClient(http, policy)
            .StreamAsync(new QueryRequest("data/Rows"))
            .GetAsyncEnumerator();

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => pages.MoveNextAsync().AsTask());

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task HttpODataClient_retries_transient_body_failure_before_single_parse_and_yield()
    {
        var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new FailingBodyContent(new HttpRequestException(
                    HttpRequestError.ConnectionError,
                    "transient body failure"))
            },
            _ => Response(HttpStatusCode.OK, "{\"value\":[{\"Id\":1}]}"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var pages = new List<ODataPage>();

        await foreach (var page in new HttpODataClient(http, ImmediatePolicy())
            .StreamAsync(new QueryRequest("data/Rows")))
            pages.Add(page);

        Assert.Equal(2, handler.Calls);
        var resultPage = Assert.Single(pages);
        Assert.Single(resultPage.Rows);
    }

    [Fact]
    public async Task HttpODataClient_body_that_ignores_deadline_is_observed_before_response_disposal()
    {
        var content = new GatedBodyContent();
        var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var clock = new ReadRetryPolicyTests.ManualTimeProvider();
        var policy = new ReadRetryPolicy(
            maximumAttempts: 1,
            overallBudget: TimeSpan.FromSeconds(1),
            timeProvider: clock);
        await using var pages = new HttpODataClient(http, policy)
            .StreamAsync(new QueryRequest("data/Rows"))
            .GetAsyncEnumerator();

        var pending = pages.MoveNextAsync().AsTask();
        await content.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(content.IsDisposed);

        content.Complete("{\"value\":[]}");
        await WaitForAsync(() => content.IsDisposed);
    }

    [Fact]
    public async Task HttpODataClient_caller_cancellation_during_ignoring_body_retains_caller_precedence()
    {
        var content = new GatedBodyContent();
        var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        using var caller = new CancellationTokenSource();
        await using var pages = new HttpODataClient(http, ImmediatePolicy())
            .StreamAsync(new QueryRequest("data/Rows"), caller.Token)
            .GetAsyncEnumerator();

        var pending = pages.MoveNextAsync().AsTask();
        await content.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        caller.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.False(content.IsDisposed);

        content.Complete("{\"value\":[]}");
        await WaitForAsync(() => content.IsDisposed);
    }

    [Fact]
    public async Task HttpODataClient_preserves_auth_recovery_exception()
    {
        var expected = new AuthRecoveryException("OData", "interactive sign-in required");
        var handler = new SequenceHandler(_ => throw expected);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        await using var pages = new HttpODataClient(http, new ReadRetryPolicy(maximumAttempts: 1))
            .StreamAsync(new QueryRequest("data/Rows"))
            .GetAsyncEnumerator();

        var actual = await Assert.ThrowsAsync<AuthRecoveryException>(
            () => pages.MoveNextAsync().AsTask());

        Assert.Same(expected, actual);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task HttpODataClient_unauthorized_response_keeps_reauthentication_message()
    {
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        await using var pages = new HttpODataClient(http, ImmediatePolicy())
            .StreamAsync(new QueryRequest("data/Rows"))
            .GetAsyncEnumerator();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.MoveNextAsync().AsTask());

        Assert.Contains("Re-authenticate in Profiles", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MetadataProvider_retries_429_and_preserves_conditional_etag()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine("Resources", "SampleMetadata.xml"));
        var cache = await NewMetadataCacheAsync();
        await cache.SaveAsync("env", "old-etag", xml);
        var handler = new SequenceHandler(
            _ => Response((HttpStatusCode)429, "throttled"),
            _ => Response(HttpStatusCode.OK, xml, etag: "new-etag"));
        using var http = new HttpClient(handler);
        var provider = new ODataMetadataProvider(http, cache, ODataMetadataProviderOptions.Default,
            ImmediatePolicy());

        var metadata = await provider.GetMetadataAsync(
            "env", "https://example.test", new ODataMetadataRequestOptions(true, null));

        Assert.NotEmpty(metadata.Entities);
        Assert.Equal(2, handler.Calls);
        Assert.All(handler.Requests, request => Assert.Equal("\"old-etag\"", request.IfNoneMatch));
    }

    [Fact]
    public async Task MetadataProvider_exhausts_three_transient_attempts()
    {
        var cache = await NewMetadataCacheAsync();
        var handler = new SequenceHandler(_ => Response(HttpStatusCode.ServiceUnavailable, "temporary"));
        using var http = new HttpClient(handler);
        var provider = new ODataMetadataProvider(http, cache, ODataMetadataProviderOptions.Default,
            ImmediatePolicy());

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetMetadataAsync(
            "env", "https://example.test", new ODataMetadataRequestOptions(true, null)));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Catalog_metadata_retry_preserves_etag_and_partition_on_every_attempt()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine("Resources", "SampleMetadata.xml"));
        var profileStore = new ProfileStore(TempDb("profile"));
        await profileStore.EnsureCreatedAsync();
        var catalogStore = new CatalogStore(TempDb("catalog"));
        await catalogStore.EnsureCreatedAsync();
        var env = new FoEnvironment("env", "Env", "https://example.test", "tenant", "USMF")
        {
            MetadataCachePartition = "captured-partition"
        };
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.OK, xml, etag: "warm-etag"),
            _ => Response(HttpStatusCode.ServiceUnavailable, "temporary"),
            _ => Response(HttpStatusCode.NotModified, string.Empty));
        using var http = new HttpClient(handler);
        var service = new CatalogService(http, profileStore, catalogStore,
            new CatalogServiceOptions(TimeSpan.Zero, TimeSpan.Zero), ImmediatePolicy());

        var warm = await service.GetODataEntityIndexAsync(env, CatalogRefreshMode.ForceRefresh);
        var refreshed = await service.GetODataEntityIndexAsync(env, CatalogRefreshMode.ForceRefresh);

        Assert.Equal(warm.ETag, refreshed.ETag);
        Assert.Equal(warm.Entities.Select(entity => entity.Name),
            refreshed.Entities.Select(entity => entity.Name));
        Assert.Equal(3, handler.Calls);
        Assert.All(handler.Requests, request =>
            Assert.Equal("captured-partition", request.Partition));
        Assert.Null(handler.Requests[0].IfNoneMatch);
        Assert.All(handler.Requests.Skip(1), request =>
            Assert.Equal("\"warm-etag\"", request.IfNoneMatch));
    }

    [Fact]
    public async Task Catalog_metadata_exhausts_three_transient_attempts()
    {
        var profileStore = new ProfileStore(TempDb("profile"));
        await profileStore.EnsureCreatedAsync();
        var handler = new SequenceHandler(_ => Response(HttpStatusCode.ServiceUnavailable, "temporary"));
        using var http = new HttpClient(handler);
        var service = new CatalogService(http, profileStore, new CatalogStore(TempDb("catalog")),
            new CatalogServiceOptions(TimeSpan.Zero, TimeSpan.Zero), ImmediatePolicy());

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetODataEntityIndexAsync(
            new FoEnvironment("env", "Env", "https://example.test", "tenant", "USMF"),
            CatalogRefreshMode.ForceRefresh));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Gateway_GET_retries_transient_status_but_public_mutation_stays_one_POST_with_full_body()
    {
        var readHandler = new SequenceHandler(
            _ => Response(HttpStatusCode.ServiceUnavailable, "temporary"),
            _ => Response(HttpStatusCode.OK, "{\"requestId\":\"req-1\",\"state\":\"2\"}"));
        using var readHttp = new HttpClient(readHandler) { BaseAddress = new Uri("https://gateway.test/") };
        using var readClient = new DualWriteGatewayClient(readHttp, readRetryPolicy: ImmediatePolicy());

        var status = await readClient.GetStatusAsync("req-1");

        Assert.True(status.IsSuccess);
        Assert.Equal(2, readHandler.Calls);

        var mutationHandler = new SequenceHandler(_ => Response((HttpStatusCode)429, "throttled"));
        using var mutationHttp = new HttpClient(mutationHandler) { BaseAddress = new Uri("https://gateway.test/") };
        using var mutationClient = new DualWriteGatewayClient(mutationHttp, readRetryPolicy: ImmediatePolicy());
        var map = new DualWriteMap(
            "map", "Map", "Map", "project", "Stopped",
            new DualWriteTemplate("template", "1", "author"), Array.Empty<DualWriteTemplate>());

        await Assert.ThrowsAsync<DualWriteGatewayException>(() => mutationClient.StartActionAsync(
            DualWriteActionType.Start, new[] { map }, "cid"));
        Assert.Equal(1, mutationHandler.Calls);
        var mutation = Assert.Single(mutationHandler.Requests);
        Assert.Equal(HttpMethod.Post, mutation.Method);
        Assert.Contains("\"action\":\"1\"", mutation.Body);
        Assert.Contains("\"cid\":\"cid\"", mutation.Body);
    }

    [Fact]
    public async Task Gateway_GET_exhausts_three_transient_attempts()
    {
        var handler = new SequenceHandler(_ => Response(HttpStatusCode.ServiceUnavailable, "temporary"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://gateway.test/") };
        using var client = new DualWriteGatewayClient(http, readRetryPolicy: ImmediatePolicy());

        await Assert.ThrowsAsync<DualWriteGatewayException>(() => client.GetStatusAsync("req-1"));
        Assert.Equal(3, handler.Calls);
    }

    private static async Task<ODataMetadataCache> NewMetadataCacheAsync()
    {
        var store = new ProfileStore(TempDb("metadata"));
        await store.EnsureCreatedAsync();
        var cache = new ODataMetadataCache(store.ConnectionString);
        await cache.EnsureCreatedAsync();
        return cache;
    }

    private static string TempDb(string prefix) =>
        Path.Combine(Path.GetTempPath(), $"toolbax-{prefix}-{Guid.NewGuid():N}.db");

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The test condition was not reached.");
            await Task.Delay(10);
        }
    }

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string body,
        TimeSpan? retryAfter = null,
        string? etag = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        if (retryAfter is not null)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        if (etag is not null)
            response.Headers.ETag = new EntityTagHeaderValue($"\"{etag}\"");
        return response;
    }

    private sealed class SequenceHandler(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _index;
        public int Calls { get; private set; }
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            request.Options.TryGetValue(CatalogRequestContext.MetadataCachePartition, out var partition);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.IfNoneMatch.SingleOrDefault()?.Tag,
                partition));
            return responses[Math.Min(_index++, responses.Length - 1)](request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string Body,
        string? IfNoneMatch,
        string? Partition);

    private sealed class FailingBodyContent(Exception exception) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(exception);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class GatedBodyContent : HttpContent
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<byte[]> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;
        public bool IsDisposed { get; private set; }

        public void Complete(string body) =>
            _completion.TrySetResult(System.Text.Encoding.UTF8.GetBytes(body));

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            _entered.TrySetResult();
            var bytes = await _completion.Task.ConfigureAwait(false);
            await stream.WriteAsync(bytes).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ExpiredAfterStartTimeProvider : TimeProvider
    {
        private int _timestampReads;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() =>
            Interlocked.Increment(ref _timestampReads) == 1
                ? 0
                : TimeSpan.FromSeconds(2).Ticks;
    }
}
