using System.Net;
using FoToolbox.Core.DualWrite;

namespace FoToolbox.Tests;

public sealed class DualWriteMutationEvidenceTests
{
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return send(token); }
    }
    private sealed class GatedContent : HttpContent
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        { Entered.TrySetResult(); await Finish.Task.WaitAsync(token); }
    }
    private static HttpClient Http(Handler handler) => new(handler) { BaseAddress = new Uri("https://gateway.example/"), Timeout = Timeout.InfiniteTimeSpan };

    [Fact]
    public async Task Precancelled_mutation_records_not_dispatched()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = Http(handler);
        using var client = new DualWriteGatewayClient(http);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = await Assert.ThrowsAsync<DualWriteMutationCanceledException>(() => client.RefreshTablesAsync("mapping", cts.Token));
        Assert.False(ex.Evidence.DispatchStarted);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Applied_mutation_with_lost_response_has_dispatch_evidence(bool cancel)
    {
        var changed = false;
        using var cts = new CancellationTokenSource();
        using var handler = new Handler(_ => {
            changed = true;
            if (cancel) { cts.Cancel(); throw new OperationCanceledException(cts.Token); }
            throw new HttpRequestException("lost");
        });
        using var http = Http(handler);
        using var client = new DualWriteGatewayClient(http);
        var error = await Record.ExceptionAsync(() => client.RefreshTablesAsync("mapping", cts.Token));
        var evidence = Assert.IsAssignableFrom<IDualWriteMutationFailure>(error).Evidence!;
        Assert.True(evidence.DispatchStarted);
        Assert.Null(evidence.StatusCode);
        Assert.True(changed);
        Assert.Equal(1, handler.Calls);
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(error);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    public async Task Headers_survive_body_failure_with_total_deadline(string mode)
    {
        using var content = new GatedContent();
        using var handler = new Handler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = content };
            response.Headers.TryAddWithoutValidation("request-id", "observed-request");
            return Task.FromResult(response);
        });
        using var http = Http(handler);
        if (mode == "timeout") http.Timeout = TimeSpan.FromSeconds(1);
        using var client = new DualWriteGatewayClient(http);
        using var cts = new CancellationTokenSource();
        var send = client.RefreshTablesAsync("mapping", cts.Token);
        try
        {
            await content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (mode == "failure") content.Finish.TrySetException(new IOException("sensitive content"));
            if (mode == "cancel") cts.Cancel();
            var error = await Record.ExceptionAsync(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
            var evidence = Assert.IsAssignableFrom<IDualWriteMutationFailure>(error).Evidence!;
            Assert.Equal(202, evidence.StatusCode);
            Assert.Equal("observed-request", evidence.Headers!["request-id"]);
            Assert.True(evidence.DispatchStarted);
            Assert.False(evidence.BodyComplete);
            Assert.NotNull(evidence.FailureKind);
            Assert.DoesNotContain("sensitive", error!.Message);
            Assert.Equal(1, handler.Calls);
            if (mode != "failure") Assert.IsAssignableFrom<OperationCanceledException>(error);
        }
        finally { content.Finish.TrySetResult(); cts.Cancel(); try { await send; } catch (Exception) { } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Action_acknowledgment_retains_status_and_request_id_without_claiming_completion(bool lifecycle)
    {
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{\"requestId\":\"r-1\"}") }));
        using var http = Http(handler);
        using var client = new DualWriteGatewayClient(http);
        var map = new DualWriteMap("template", "map", "Map", "pid", "Stopped", null, Array.Empty<DualWriteTemplate>());
        var result = lifecycle
            ? await client.StartActionAsync(DualWriteActionType.Start, new[] { map }, "cid")
            : await client.SwitchActiveTemplateAsync("cid", "pid", "template");
        Assert.Equal("r-1", result.RequestId);
        Assert.Null(result.State);
        Assert.Equal(202, result.Acknowledgment?.StatusCode);
        Assert.True(result.Acknowledgment?.BodyComplete);
        Assert.True(result.Acknowledgment?.DispatchStarted);
    }

    [Fact]
    public async Task Configured_buffer_limit_keeps_headers_and_incomplete_body_evidence()
    {
        using var handler = new Handler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(new string('x', 64)) };
            response.Headers.TryAddWithoutValidation("request-id", "r-1");
            return Task.FromResult(response);
        });
        using var http = Http(handler);
        http.MaxResponseContentBufferSize = 8;
        using var client = new DualWriteGatewayClient(http);
        var error = await Assert.ThrowsAsync<DualWriteMutationException>(() => client.RefreshTablesAsync("mapping"));
        Assert.Equal(202, error.Evidence.StatusCode);
        Assert.Equal("r-1", error.Evidence.Headers!["request-id"]);
        Assert.False(error.Evidence.BodyComplete);
        Assert.NotNull(error.Evidence.FailureKind);
    }

    [Fact]
    public async Task Parameter_refusal_is_not_dispatched()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = Http(handler);
        using var client = new DualWriteGatewayClient(http);
        var error = await Assert.ThrowsAsync<DualWriteMutationValidationException>(() => client.RefreshTablesAsync(" "));
        Assert.False(error.Evidence.DispatchStarted);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Pipeline_auth_work_remains_inside_total_timeout_without_claiming_not_sent()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async token => {
            entered.TrySetResult();
            await new TaskCompletionSource().Task.WaitAsync(token);
            throw new InvalidOperationException("Unreachable");
        });
        using var http = Http(handler);
        http.Timeout = TimeSpan.FromSeconds(1);
        using var client = new DualWriteGatewayClient(http);
        var send = client.RefreshTablesAsync("mapping");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<DualWriteMutationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(error.Evidence.DispatchStarted);
        Assert.Null(error.Evidence.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Opaque_handler_http_exception_does_not_erase_dispatch_evidence()
    {
        using var handler = new Handler(_ => throw new DualWriteGatewayException("handler failure", HttpStatusCode.BadGateway));
        using var http = Http(handler);
        using var client = new DualWriteGatewayClient(http);
        var error = await Assert.ThrowsAsync<DualWriteMutationException>(() => client.RefreshTablesAsync("mapping"));
        Assert.True(error.Evidence.DispatchStarted);
        Assert.Null(error.Evidence.StatusCode); // No actual response was observed by this transport.
    }

    [Fact]
    public async Task Complete_5xx_keeps_legacy_body_diagnostic_and_new_http_evidence()
    {
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("legacy diagnostic") }));
        using var http = Http(handler);
        using var client = new DualWriteGatewayClient(http);
        var error = await Assert.ThrowsAsync<DualWriteGatewayException>(() => client.RefreshTablesAsync("mapping"));
        Assert.Contains("legacy diagnostic", error.Message);
        Assert.Equal(500, error.Evidence?.StatusCode);
        Assert.True(error.Evidence?.DispatchStarted);
        Assert.True(error.Evidence?.BodyComplete);
    }

    [Fact]
    public async Task Malformed_acknowledgment_keeps_observed_success_status()
    {
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{invalid") }));
        using var http = Http(handler);
        using var client = new DualWriteGatewayClient(http);
        var error = await Assert.ThrowsAsync<DualWriteMutationException>(() => client.SwitchActiveTemplateAsync("cid", "pid", "template"));
        Assert.Equal(202, error.Evidence.StatusCode);
        Assert.True(error.Evidence.BodyComplete);
        Assert.True(error.Evidence.DispatchStarted);
    }

    [Fact]
    public async Task Get_cancellation_keeps_existing_exception_behavior()
    {
        using var cts = new CancellationTokenSource();
        using var handler = new Handler(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });
        using var http = Http(handler);
        using var client = new DualWriteGatewayClient(http);
        var error = await Record.ExceptionAsync(() => client.GetMapsAsync("cid", cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.IsNotType<DualWriteMutationCanceledException>(error);
    }
}
