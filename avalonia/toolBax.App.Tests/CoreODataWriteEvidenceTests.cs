using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class CoreODataWriteEvidenceTests
{
    private static EnvProfile Env() => new("e", "Env", "https://fo.example", "tenant", "USMF", "Tier 1", EnvStatus.Connected);
    private static CoreODataClient Client(HttpClient http, IAuthService? auth = null, Func<EnvProfile?>? env = null)
        => new(auth ?? new FakeAuthService(_ => "token"), env ?? (() => Env()), http);

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

    [Theory]
    [InlineData("none")]
    [InlineData("origin")]
    [InlineData("auth")]
    public async Task Refusal_is_explicitly_not_dispatched(string cause)
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Unexpected send"));
        using var http = new HttpClient(handler);
        var auth = new FakeAuthService(_ => cause == "auth" ? throw new InvalidOperationException("denied") : "token");
        using var client = Client(http, auth, () => cause == "none" ? null : Env());
        var result = await client.SendAsync("POST", cause == "origin" ? "https://foreign.example/data/X" : "/data/X", "{}", TestContext.Current.CancellationToken);
        Assert.False(result.DispatchStarted);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Already_cancelled_write_has_false_dispatch_and_no_http()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        using var client = Client(http);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var ex = await Assert.ThrowsAsync<ODataWriteCanceledException>(() => client.SendAsync("PATCH", "/data/X(1)", "{}", cancel.Token));
        Assert.False(ex.DispatchStarted);
        Assert.Null(ex.ObservedResponse);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Server_change_then_lost_response_never_claims_not_dispatched(bool cancel)
    {
        var changed = false;
        using var cts = new CancellationTokenSource();
        using var handler = new Handler(_ => {
            changed = true;
            if (cancel) { cts.Cancel(); throw new OperationCanceledException(cts.Token); }
            throw new HttpRequestException("connection lost");
        });
        using var http = new HttpClient(handler);
        using var client = Client(http);
        if (cancel)
        {
            var ex = await Assert.ThrowsAsync<ODataWriteCanceledException>(() => client.SendAsync("POST", "/data/X", "{}", cts.Token));
            Assert.True(ex.DispatchStarted);
            Assert.Null(ex.ObservedResponse);
        }
        else
        {
            var result = await client.SendAsync("POST", "/data/X", "{}", TestContext.Current.CancellationToken);
            Assert.True(result.DispatchStarted);
            Assert.Equal(0, result.StatusCode);
            Assert.False(result.IsSuccess);
        }
        Assert.True(changed);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    public async Task Observed_headers_survive_body_failure_and_total_deadline(string mode)
    {
        using var content = new GatedContent();
        using var handler = new Handler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = content };
            response.Headers.Location = new Uri("https://fo.example/data/X(1)");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler) { Timeout = mode == "timeout" ? TimeSpan.FromSeconds(1) : Timeout.InfiniteTimeSpan };
        using var cts = new CancellationTokenSource();
        using var client = Client(http);
        var send = client.SendAsync("POST", "/data/X", "{}", cts.Token);
        try
        {
            await content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            if (mode == "failure") content.Finish.TrySetException(new IOException("sensitive body detail"));
            if (mode == "cancel") cts.Cancel();
            ODataResponse result;
            if (mode == "failure") result = await send.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            else
            {
                var ex = await Assert.ThrowsAsync<ODataWriteCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Assert.True(ex.DispatchStarted);
                result = Assert.IsType<ODataResponse>(ex.ObservedResponse);
            }
            Assert.Equal(201, result.StatusCode);
            Assert.Equal("https://fo.example/data/X(1)", result.Headers!["Location"]);
            Assert.True(result.DispatchStarted);
            Assert.False(result.BodyComplete);
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.BodyReadError);
            Assert.DoesNotContain("sensitive", result.BodyReadError);
            Assert.Equal(1, handler.Calls);
        }
        finally { content.Finish.TrySetResult(); cts.Cancel(); try { await send; } catch (Exception) { } }
    }

    [Fact]
    public async Task Configured_buffer_limit_retains_observed_status_instead_of_success()
    {
        using var handler = new Handler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(new string('x', 64)) };
            response.Headers.TryAddWithoutValidation("OData-EntityId", "https://fo.example/data/X(1)");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler) { MaxResponseContentBufferSize = 8 };
        using var client = Client(http);
        var result = await client.SendAsync("POST", "/data/X", "{}", TestContext.Current.CancellationToken);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("https://fo.example/data/X(1)", result.Headers!["OData-EntityId"]);
        Assert.False(result.BodyComplete);
        Assert.False(result.IsSuccess);
        Assert.Equal(string.Empty, result.Body);
        Assert.NotNull(result.BodyReadError);
    }

    [Fact]
    public async Task Invalid_write_request_is_not_dispatched()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await client.SendAsync("POST", "https://[bad", "{}", TestContext.Current.CancellationToken);
        Assert.False(result.DispatchStarted);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Auth_cancellation_is_not_dispatched()
    {
        using var cts = new CancellationTokenSource();
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        using var client = Client(http, new FakeAuthService(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); }));
        var error = await Assert.ThrowsAsync<ODataWriteCanceledException>(() => client.SendAsync("POST", "/data/X", "{}", cts.Token));
        Assert.False(error.DispatchStarted);
        Assert.Null(error.ObservedResponse);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Environment_edit_during_auth_is_not_dispatched()
    {
        var current = Env();
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        using var client = Client(http, new FakeAuthService(_ => {
            current = current with { Url = "https://changed.example" };
            return "captured-token";
        }), () => current);
        var result = await client.SendAsync("POST", "/data/X", "{}", TestContext.Current.CancellationToken);
        Assert.False(result.DispatchStarted);
        Assert.Equal("Environment changed", result.ReasonPhrase);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Legacy_response_has_unprovided_dispatch_evidence_and_incomplete_body_is_not_success()
    {
        var legacy = new ODataResponse(200, "OK", "{}", 0);
        Assert.Null(legacy.DispatchStarted);
        Assert.True(legacy.IsSuccess);
        Assert.False((legacy with { BodyComplete = false }).IsSuccess);
    }

    [Fact]
    public async Task Infinite_deadline_allows_gated_complete_response_and_headers()
    {
        using var content = new GatedContent();
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent) { Content = content }));
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = Client(http);
        var send = client.SendAsync("DELETE", "/data/X(1)", null, TestContext.Current.CancellationToken);
        try
        {
            await content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            content.Finish.TrySetResult();
            var result = await send;
            Assert.True(result.DispatchStarted);
            Assert.True(result.BodyComplete);
            Assert.True(result.IsSuccess);
        }
        finally { content.Finish.TrySetResult(); await send; }
    }
}
