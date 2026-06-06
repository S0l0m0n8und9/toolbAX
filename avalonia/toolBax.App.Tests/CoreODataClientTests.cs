using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Exercises <see cref="CoreODataClient"/> against a stub HttpMessageHandler + fake auth — no real
/// network, so it runs on Linux CI. Verifies the bearer header, the composed URL, and response mapping.
/// </summary>
public class CoreODataClientTests
{
    private static EnvProfile Env(string url = "contoso.operations.dynamics.com") =>
        new("env1", "Env", url, "tenant", "USMF", "Tier 1", EnvStatus.Connected);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
            => _response = new HttpResponseMessage(status) { Content = new StringContent(body) };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return _response;
        }
    }

    [Fact]
    public async Task Get_sends_bearer_token_and_composed_url_and_maps_response()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"value\":[]}");
        var client = new CoreODataClient(new FakeAuthService(_ => "tok-123"), () => Env(), new HttpClient(handler));

        var result = await client.SendAsync("GET", "/data/CustomersV3?$top=1", body: null, TestContext.Current.CancellationToken);

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("value", result.Body);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("tok-123", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Equal("https://contoso.operations.dynamics.com/data/CustomersV3?$top=1",
            handler.LastRequest.RequestUri!.ToString());
        Assert.Null(handler.LastBody); // GET carries no body
    }

    [Fact]
    public async Task Post_sends_the_json_body()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "{\"id\":1}");
        var client = new CoreODataClient(new FakeAuthService(), () => Env(), new HttpClient(handler));

        var result = await client.SendAsync("POST", "/data/CustomersV3", "{\"CustomerAccount\":\"US-1\"}", TestContext.Current.CancellationToken);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("US-1", handler.LastBody);
    }

    [Fact]
    public async Task Honours_a_full_url_environment()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new CoreODataClient(new FakeAuthService(), () => Env("https://demo.dynamics.com/"), new HttpClient(handler));

        await client.SendAsync("GET", "/data/Foo", null, TestContext.Current.CancellationToken);

        Assert.Equal("https://demo.dynamics.com/data/Foo", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task No_active_environment_returns_a_clear_non_success_response()
    {
        var client = new CoreODataClient(new FakeAuthService(), () => null, new HttpClient(new StubHandler(HttpStatusCode.OK, "")));

        var result = await client.SendAsync("GET", "/data/Foo", null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("environment", result.ReasonPhrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Auth_failure_maps_to_401()
    {
        var failing = new FakeAuthService(_ => throw new InvalidOperationException("token denied"));
        var client = new CoreODataClient(failing, () => Env(), new HttpClient(new StubHandler(HttpStatusCode.OK, "")));

        var result = await client.SendAsync("GET", "/data/Foo", null, TestContext.Current.CancellationToken);

        Assert.Equal(401, result.StatusCode);
        Assert.Contains("token denied", result.Body);
    }

    [Fact]
    public async Task Dispose_disposes_the_internally_created_HttpClient()
    {
        // No HttpClient injected → the client owns the one it allocated, so disposing it disposes that.
        var client = new CoreODataClient(new FakeAuthService(_ => "tok"), () => Env());
        client.Dispose();

        // Auth succeeds (fake), then SendAsync hits the disposed HttpClient → ObjectDisposedException,
        // surfaced (not thrown) as a non-success response. No network is touched.
        var result = await client.SendAsync("GET", "/data/Foo", null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Dispose_leaves_an_injected_HttpClient_usable()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler);
        var client = new CoreODataClient(new FakeAuthService(), () => Env(), http);

        client.Dispose();

        // The injected client is the caller's to own — disposing the wrapper must not dispose it.
        var resp = await http.GetAsync("https://example.invalid", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
