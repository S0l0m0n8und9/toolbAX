using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Exercises <see cref="CoreConnectionTester"/> against a stub HttpMessageHandler + recording auth — no
/// real network. Proves the exact $metadata / WhoAmI probe endpoint,
/// forces a fresh token, and maps the HTTP status honestly.
/// </summary>
public class CoreConnectionTesterTests
{
    private static EnvProfile Env(string url = "contoso.operations.dynamics.com", string? dataverseUrl = "contoso.crm.dynamics.com") =>
        new("env1", "Env", url, "tenant", "USMF", "Tier 1", EnvStatus.Connected) { DataverseUrl = dataverseUrl };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(HttpStatusCode status, string body = "")
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
        }
    }

    // Records what was asked of the auth service (notably the forceRefresh flag).
    private sealed class RecordingAuthService : IAuthService
    {
        public bool? FoForceRefresh { get; private set; }
        public bool? DataverseForceRefresh { get; private set; }
        private readonly string _token;
        private readonly Exception? _throw;

        public RecordingAuthService(string token = "tok-123", Exception? @throw = null)
        {
            _token = token;
            _throw = @throw;
        }

        public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default) => Token();
        public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default) => Token();
        public Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default) => Token();

        public Task<string> AcquireFoTokenAsync(EnvProfile env, bool forceRefresh, CancellationToken ct = default)
        {
            FoForceRefresh = forceRefresh;
            return Token();
        }

        public Task<string> AcquireDataverseTokenAsync(EnvProfile env, bool forceRefresh, CancellationToken ct = default)
        {
            DataverseForceRefresh = forceRefresh;
            return Token();
        }

        private Task<string> Token() => _throw is not null ? Task.FromException<string>(_throw) : Task.FromResult(_token);
    }

    [Fact]
    public async Task TestFo_succeeds_when_metadata_endpoint_returns_2xx()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "<edmx/>");
        var auth = new RecordingAuthService("tok-123");
        var tester = new CoreConnectionTester(auth, new HttpClient(handler));

        var result = await tester.TestFoAsync(Env(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        // Hit the SAME endpoint the tools use, with a fresh bearer token.
        Assert.Equal("https://contoso.operations.dynamics.com/data/$metadata", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("tok-123", handler.LastRequest.Headers.Authorization!.Parameter);
        Assert.True(auth.FoForceRefresh); // freshness was forced, not served from cache
    }

    [Fact]
    public async Task TestFo_fails_with_the_status_when_metadata_returns_non_2xx()
    {
        var tester = new CoreConnectionTester(new RecordingAuthService(), new HttpClient(new StubHandler(HttpStatusCode.Unauthorized)));

        var result = await tester.TestFoAsync(Env(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("401", result.Message);
    }

    [Fact]
    public async Task TestFo_maps_a_token_failure_to_a_clear_message()
    {
        var auth = new RecordingAuthService(@throw: new InvalidOperationException("AADSTS700016: app not found"));
        var tester = new CoreConnectionTester(auth, new HttpClient(new StubHandler(HttpStatusCode.OK)));

        var result = await tester.TestFoAsync(Env(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("AADSTS700016", result.Message);
    }

    [Fact]
    public async Task TestFo_requires_an_fo_url()
    {
        var tester = new CoreConnectionTester(new RecordingAuthService(), new HttpClient(new StubHandler(HttpStatusCode.OK)));

        var result = await tester.TestFoAsync(Env(url: ""), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("URL", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestDataverse_hits_WhoAmI_and_succeeds_on_2xx()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"UserId\":\"...\"}");
        var auth = new RecordingAuthService("dv-tok");
        var tester = new CoreConnectionTester(auth, new HttpClient(handler));

        var result = await tester.TestDataverseAsync(Env(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("https://contoso.crm.dynamics.com/api/data/v9.2/WhoAmI", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("dv-tok", handler.LastRequest.Headers.Authorization!.Parameter);
        Assert.True(auth.DataverseForceRefresh);
    }

    [Fact]
    public async Task TestDataverse_requires_a_dataverse_url()
    {
        var tester = new CoreConnectionTester(new RecordingAuthService(), new HttpClient(new StubHandler(HttpStatusCode.OK)));

        var result = await tester.TestDataverseAsync(Env(dataverseUrl: null), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Dataverse", result.Message);
    }

    private sealed class GatedProbeAuth : IAuthService
    {
        public TaskCompletionSource<string> Token { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default) => Token.Task;
        public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default) => Token.Task;
        public Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default) => Token.Task;
    }

    private sealed class GatedProbeHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Entered.TrySetResult();
            return Response.Task;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancelled_auth_never_dispatches_and_propagates_cancellation(bool dataverse)
    {
        var auth = new GatedProbeAuth();
        using var handler = new GatedProbeHandler();
        using var http = new HttpClient(handler);
        handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        var tester = new CoreConnectionTester(auth, http);
        using var cts = new CancellationTokenSource();
        var pending = dataverse ? tester.TestDataverseAsync(Env(), cts.Token) : tester.TestFoAsync(Env(), cts.Token);
        cts.Cancel();
        auth.Token.SetResult("late-token");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancelled_http_cannot_publish_late_success(bool dataverse)
    {
        using var handler = new GatedProbeHandler();
        using var http = new HttpClient(handler);
        var tester = new CoreConnectionTester(new RecordingAuthService(), http);
        using var cts = new CancellationTokenSource();
        var pending = dataverse ? tester.TestDataverseAsync(Env(), cts.Token) : tester.TestFoAsync(Env(), cts.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cts.Cancel();
        handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Auth_timeout_without_caller_cancellation_remains_a_connection_failure()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK));
        var tester = new CoreConnectionTester(new RecordingAuthService(@throw: new OperationCanceledException("auth timeout")), http);
        var result = await tester.TestFoAsync(Env(), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Contains("auth timeout", result.Message);
    }

    [Fact]
    public async Task Http_timeout_without_caller_cancellation_exhausts_bounded_retries_as_a_failure()
    {
        using var handler = new GatedProbeHandler();
        handler.Response.SetException(new TaskCanceledException("HTTP timeout", new TimeoutException()));
        using var http = new HttpClient(handler);
        var tester = new CoreConnectionTester(new RecordingAuthService(), http);
        var result = await tester.TestFoAsync(Env(), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Contains("timeout", result.Message);
        Assert.Equal(3, handler.Calls);
    }
}
