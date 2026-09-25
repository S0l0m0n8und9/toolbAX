using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// <see cref="AuthenticatedHttpHandler"/> adds a bearer token (for the active environment) to outgoing
/// requests so <c>CatalogService</c>'s shared HttpClient is authenticated. Exercised against a stub
/// inner handler + fake auth — no real network, Linux-runnable.
/// </summary>
public class AuthenticatedHttpHandlerTests
{
    private static EnvProfile Env(string url = "contoso.operations.dynamics.com") =>
        new("env1", "Env", url, "tenant", "USMF", "Tier 1", EnvStatus.Connected);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static HttpClient Client(AuthenticatedHttpHandler handler, CapturingHandler inner)
    {
        handler.InnerHandler = inner;
        return new HttpClient(handler);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Tagged_stale_context_never_dispatches_even_with_preset_authorization(bool hasActive, bool preset)
    {
        var inner = new CapturingHandler();
        var authCalls = 0;
        using var http = Client(new AuthenticatedHttpHandler(new FakeAuthService(_ =>
        {
            authCalls++;
            return "token";
        }), () => hasActive ? Env() : null), inner);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://contoso.operations.dynamics.com/data/$metadata");
        request.Options.Set(CatalogRequestContext.MetadataCachePartition, "obsolete-context");
        if (preset) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "preset");

        await Assert.ThrowsAsync<InvalidOperationException>(() => http.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Null(inner.LastRequest);
        Assert.Equal(0, authCalls);
    }

    [Theory]
    [InlineData("https://evil.example/data/$metadata")]
    [InlineData("http://contoso.operations.dynamics.com/data/$metadata")]
    [InlineData("https://contoso.operations.dynamics.com:8443/data/$metadata")]
    public async Task Tagged_context_refuses_foreign_origin_before_token_acquisition(string uri)
    {
        var env = Env();
        var inner = new CapturingHandler();
        var authCalls = 0;
        using var http = Client(new AuthenticatedHttpHandler(new FakeAuthService(_ =>
        {
            authCalls++;
            return "token";
        }), () => env), inner);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Options.Set(CatalogRequestContext.MetadataCachePartition, EnvironmentIdentity.Create(env).ToMetadataCachePartition());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "preset");

        await Assert.ThrowsAsync<InvalidOperationException>(() => http.SendAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(0, authCalls);
        Assert.Null(inner.LastRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tagged_request_rechecks_identity_after_token_await(bool removeActive)
    {
        EnvProfile? active = Env();
        var captured = active;
        var auth = new GatedAuth();
        var inner = new CapturingHandler();
        using var http = Client(new AuthenticatedHttpHandler(auth, () => active), inner);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://contoso.operations.dynamics.com/data/$metadata");
        request.Options.Set(CatalogRequestContext.MetadataCachePartition, EnvironmentIdentity.Create(captured).ToMetadataCachePartition());
        var pending = http.SendAsync(request, TestContext.Current.CancellationToken);
        await auth.Entered.Task;
        active = removeActive ? null : active with { Tenant = "tenant-b" };
        auth.Release.TrySetResult("token-a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Same(captured, auth.Profile);
        Assert.Null(inner.LastRequest);
    }

    [Fact]
    public async Task Tagged_matching_context_authenticates_its_snapshot_despite_preset_header_and_cosmetic_edit()
    {
        var active = Env();
        var captured = active;
        var auth = new GatedAuth();
        var inner = new CapturingHandler();
        using var http = Client(new AuthenticatedHttpHandler(auth, () => active), inner);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://contoso.operations.dynamics.com/data/$metadata");
        request.Options.Set(CatalogRequestContext.MetadataCachePartition, EnvironmentIdentity.Create(captured).ToMetadataCachePartition());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "untrusted-preset");
        var pending = http.SendAsync(request, TestContext.Current.CancellationToken);
        await auth.Entered.Task;
        active = active with { Name = "Renamed", Status = EnvStatus.TokenExpired };
        auth.Release.TrySetResult("captured-token");
        using var response = await pending;

        Assert.Same(captured, auth.Profile);
        Assert.Equal("captured-token", inner.LastRequest!.Headers.Authorization!.Parameter);
    }

    private sealed class GatedAuth : IAuthService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public EnvProfile? Profile { get; private set; }
        public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default)
        {
            Profile = env;
            Entered.TrySetResult();
            return Release.Task;
        }
        public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Adds_a_bearer_token_for_the_active_environment()
    {
        var inner = new CapturingHandler();
        var handler = new AuthenticatedHttpHandler(new FakeAuthService(_ => "tok-xyz"), () => Env());
        var http = Client(handler, inner);

        await http.GetAsync("https://contoso.operations.dynamics.com/data/$metadata", TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", inner.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("tok-xyz", inner.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Adds_a_bearer_token_when_the_active_profile_url_has_whitespace()
    {
        var inner = new CapturingHandler();
        var handler = new AuthenticatedHttpHandler(new FakeAuthService(_ => "tok-xyz"),
            () => Env("  contoso.operations.dynamics.com/data  "));
        var http = Client(handler, inner);

        await http.GetAsync("https://contoso.operations.dynamics.com/data/$metadata", TestContext.Current.CancellationToken);

        Assert.Equal("tok-xyz", inner.LastRequest!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Sends_without_an_auth_header_when_no_environment_is_active()
    {
        var inner = new CapturingHandler();
        var handler = new AuthenticatedHttpHandler(new FakeAuthService(_ => "tok"), () => null);
        var http = Client(handler, inner);

        await http.GetAsync("https://demo.dynamics.com/data/$metadata", TestContext.Current.CancellationToken);

        Assert.Null(inner.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task Does_not_add_the_token_for_a_request_to_a_foreign_origin()
    {
        var inner = new CapturingHandler();
        var handler = new AuthenticatedHttpHandler(new FakeAuthService(_ => "tok"), () => Env());
        var http = Client(handler, inner);

        // A server-supplied @odata.nextLink to another host (followed by CatalogService) must not carry
        // the env-scoped bearer off-origin.
        await http.GetAsync("https://evil.example.com/data/$metadata", TestContext.Current.CancellationToken);

        Assert.Null(inner.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task Does_not_overwrite_an_existing_authorization_header()
    {
        var inner = new CapturingHandler();
        var handler = new AuthenticatedHttpHandler(new FakeAuthService(_ => "fresh"), () => Env());
        var http = Client(handler, inner);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://contoso.operations.dynamics.com/data/$metadata");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "preset");
        await http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("preset", inner.LastRequest!.Headers.Authorization!.Parameter);
    }
}
