using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// <see cref="AuthenticatedHttpHandler"/> adds a bearer token (for the active environment) to outgoing
/// requests so <c>CatalogService</c>'s shared HttpClient is authenticated. Exercised against a stub
/// inner handler + fake auth — no real network, Linux-runnable.
/// </summary>
public class AuthenticatedHttpHandlerTests
{
    private static EnvProfile Env() =>
        new("env1", "Env", "contoso.operations.dynamics.com", "tenant", "USMF", "Tier 1", EnvStatus.Connected);

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
