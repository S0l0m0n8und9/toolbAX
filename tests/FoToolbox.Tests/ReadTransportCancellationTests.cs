using System.Net;
using System.Text;
using FoToolbox.Core.DualWrite;
using FoToolbox.Core.OData;

namespace FoToolbox.Tests;

public sealed class ReadTransportCancellationTests
{
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return send(ct);
        }
    }

    private sealed class IgnoringBodyStream : MemoryStream
    {
        public IgnoringBodyStream() : base(Encoding.UTF8.GetBytes("{\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"https://host/data/next\"}")) { }
        public override bool CanSeek => false;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Gate.Task;
            return Read(buffer.Span);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    }

    private static HttpResponseMessage Json(string body = "{\"value\":[]}") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public async Task Streaming_precancelled_read_is_cancellation_not_plugin_error_and_never_dispatches()
    {
        using var handler = new Handler(_ => Task.FromResult(Json()));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host/") };
        var reader = new HttpODataClient(http);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await using var pages = reader.StreamAsync(new QueryRequest("data/X"), cts.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pages.MoveNextAsync().AsTask());
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Streaming_cancelled_send_other_error_has_cancellation_precedence()
    {
        using var cts = new CancellationTokenSource();
        using var handler = new Handler(_ => { cts.Cancel(); throw new HttpRequestException("late transport failure"); });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host/") };
        await using var pages = new HttpODataClient(http).StreamAsync(new QueryRequest("data/X"), cts.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pages.MoveNextAsync().AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Streaming_cancelled_ignoring_body_never_yields_success_or_other_error(bool fault)
    {
        using var body = new IgnoringBodyStream();
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host/") };
        using var cts = new CancellationTokenSource();
        await using var pages = new HttpODataClient(http).StreamAsync(new QueryRequest("data/X"), cts.Token).GetAsyncEnumerator();
        var move = pages.MoveNextAsync().AsTask();
        await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        if (fault) body.Gate.SetException(new InvalidOperationException("late body failure"));
        else body.Gate.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Streaming_cancel_after_page_stops_before_next_request()
    {
        using var handler = new Handler(_ => Task.FromResult(Json("{\"value\":[],\"@odata.nextLink\":\"https://host/data/next\"}")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host/") };
        using var cts = new CancellationTokenSource();
        await using var pages = new HttpODataClient(http).StreamAsync(new QueryRequest("data/X"), cts.Token).GetAsyncEnumerator();
        Assert.True(await pages.MoveNextAsync());
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pages.MoveNextAsync().AsTask());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Streaming_real_timeout_with_live_caller_is_truthful_timeout()
    {
        using var handler = new Handler(async ct =>
        {
            await new TaskCompletionSource().Task.WaitAsync(ct);
            throw new InvalidOperationException("unreachable");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host/"), Timeout = TimeSpan.FromMilliseconds(100) };
        using var caller = new CancellationTokenSource();
        await using var pages = new HttpODataClient(http)
            .StreamAsync(new QueryRequest("data/X"), caller.Token)
            .GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<TimeoutException>(
            () => pages.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.False(caller.IsCancellationRequested);
        Assert.Equal("The OData request timed out.", error.Message);
        Assert.DoesNotContain("Re-authenticate", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
    }

    [Fact]
    public async Task Gateway_get_precancelled_never_dispatches()
    {
        using var response = Json("[]");
        await response.Content.LoadIntoBufferAsync();
        using var handler = new Handler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host/") };
        using var gateway = new DualWriteGatewayClient(http);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.GetMapsAsync("cid", cts.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gateway_get_late_success_or_other_exception_is_cancelled(bool fault)
    {
        using var response = Json("[]");
        await response.Content.LoadIntoBufferAsync();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(_ => gate.Task);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host/") };
        using var gateway = new DualWriteGatewayClient(http);
        using var cts = new CancellationTokenSource();
        var pending = gateway.GetMapsAsync("cid", cts.Token);
        cts.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late gateway failure"));
        else gate.SetResult(response);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsNotType<DualWriteMutationCanceledException>(error);
    }

    private sealed class CancelOnDisposeContent(Action cancel) : StringContent("[]")
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) cancel();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task Gateway_get_cancelled_between_response_cleanup_and_parsing_cannot_return_success()
    {
        using var cts = new CancellationTokenSource();
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new CancelOnDisposeContent(cts.Cancel) };
        await response.Content.LoadIntoBufferAsync();
        using var handler = new Handler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host/") };
        using var gateway = new DualWriteGatewayClient(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.GetMapsAsync("cid", cts.Token));
        Assert.Equal(1, handler.Calls);
    }
}
