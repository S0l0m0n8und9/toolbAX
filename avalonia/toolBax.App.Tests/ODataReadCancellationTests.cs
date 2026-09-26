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

public sealed class ODataReadCancellationTests
{
    private static EnvProfile Environment() => new("read-cancel", "Probe", "https://example.test", "tenant", "USMF", "", EnvStatus.Connected);

    private sealed class Auth : IAuthService
    {
        public int Calls { get; private set; }
        public Func<CancellationToken, Task<string>> Acquire { get; set; } = _ => Task.FromResult("fake-token");
        public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default) { Calls++; return Acquire(ct); }
        public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Func<CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return Respond(ct); }
    }

    [Fact]
    public async Task Get_precancelled_does_not_authenticate_or_dispatch()
    {
        var auth = new Auth();
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var client = new CoreODataClient(auth, Environment, http);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync("GET", "/data/E", null, cts.Token));
        Assert.Equal(0, auth.Calls);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_cancelled_ignoring_auth_does_not_dispatch_or_translate_failure(bool fault)
    {
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var auth = new Auth { Acquire = _ => gate.Task };
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var client = new CoreODataClient(auth, Environment, http);
        using var cts = new CancellationTokenSource();
        using var trace = new TraceCapture();
        var pending = client.SendAsync("GET", "/data/H10aAuthCanary", null, cts.Token);
        cts.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("H10aAuthCanaryFault"));
        else gate.SetResult("token");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, handler.Calls);
        Assert.DoesNotContain("H10aAuthCanary", trace.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_cancelled_ignoring_http_does_not_return_failure_or_success(bool fault)
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler { Respond = _ => gate.Task };
        using var http = new HttpClient(handler);
        using var client = new CoreODataClient(new Auth(), Environment, http);
        using var cts = new CancellationTokenSource();
        using var trace = new TraceCapture();
        var pending = client.SendAsync("GET", "/data/H10aHttpCanary", null, cts.Token);
        cts.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("H10aHttpCanaryFault"));
        else gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.DoesNotContain("H10aHttpCanary", trace.Text);
    }

    private sealed class HeldContent : HttpContent
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Entered.TrySetResult();
            await Finish.Task;
            stream.WriteByte((byte)'{');
            stream.WriteByte((byte)'}');
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_cancelled_ignoring_body_does_not_return_or_trace_response(bool fault)
    {
        using var content = new HeldContent();
        using var handler = new Handler { Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }) };
        using var http = new HttpClient(handler);
        using var client = new CoreODataClient(new Auth(), Environment, http);
        using var cts = new CancellationTokenSource();
        using var trace = new TraceCapture();
        var pending = client.SendAsync("GET", "/data/H10aBodyCanary", null, cts.Token);
        await content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cts.Cancel();
        if (fault) content.Finish.SetException(new InvalidOperationException("H10aBodyCanaryFault"));
        else content.Finish.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.DoesNotContain("H10aBodyCanary", trace.Text);
    }

    [Fact]
    public async Task Get_live_caller_timeout_stays_failure()
    {
        using var handler = new Handler { Respond = _ => throw new OperationCanceledException("timeout") };
        using var http = new HttpClient(handler);
        using var client = new CoreODataClient(new Auth(), Environment, http);
        var result = await client.SendAsync("GET", "/data/E", null, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.StatusCode);
    }
}
