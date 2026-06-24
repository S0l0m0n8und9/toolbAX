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
/// Exercises <see cref="CoreDataverseClient"/> against a stub HttpMessageHandler + fake auth — no real
/// network, so it runs on Linux CI. Verifies the bearer (Dataverse) token, the composed Web API URL,
/// the FormattedValue Prefer header, nextLink pass-through, and graceful failure mapping.
/// </summary>
public class CoreDataverseClientTests
{
    private static EnvProfile Env(string? dataverseUrl = "https://contoso.crm.dynamics.com") =>
        new("env1", "Env", "contoso.operations.dynamics.com", "tenant", "USMF", "Tier 1",
            EnvStatus.Connected, DataverseUrl: dataverseUrl);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
            => _response = new HttpResponseMessage(status) { Content = new StringContent(body) };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    [Fact]
    public async Task Get_sends_dataverse_bearer_token_and_composed_web_api_url()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"value\":[]}");
        var client = new CoreDataverseClient(
            new FakeAuthService(dataverseToken: _ => "dv-tok"), () => Env(), new HttpClient(handler));

        var result = await client.GetAsync("msdyn_dualwriteentitymaps?$select=msdyn_name", TestContext.Current.CancellationToken);

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("value", result.Body);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("dv-tok", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Equal(
            "https://contoso.crm.dynamics.com/api/data/v9.2/msdyn_dualwriteentitymaps?$select=msdyn_name",
            handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task Get_requests_formatted_value_annotations()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new CoreDataverseClient(new FakeAuthService(), () => Env(), new HttpClient(handler));

        await client.GetAsync("msdyn_dualwriteentitymaps", TestContext.Current.CancellationToken);

        var prefer = string.Join(",", handler.LastRequest!.Headers.GetValues("Prefer"));
        Assert.Contains("OData.Community.Display.V1.FormattedValue", prefer);
    }

    [Fact]
    public async Task An_absolute_nextLink_is_used_verbatim()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new CoreDataverseClient(new FakeAuthService(), () => Env(), new HttpClient(handler));

        const string nextLink = "https://contoso.crm.dynamics.com/api/data/v9.2/msdyn_dualwriteentitymaps?$skiptoken=abc";
        await client.GetAsync(nextLink, TestContext.Current.CancellationToken);

        Assert.Equal(nextLink, handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_nextLink_on_a_foreign_host_is_refused()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new CoreDataverseClient(new FakeAuthService(dataverseToken: _ => "dv-tok"), () => Env(), new HttpClient(handler));

        // A Dataverse nextLink pointing at a different origin must not receive the env-scoped bearer.
        var result = await client.GetAsync("https://evil.example.com/api/data/v9.2/x", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(handler.LastRequest); // no request was sent, so the token never left
    }

    [Fact]
    public async Task No_active_environment_returns_a_clear_non_success_response()
    {
        var client = new CoreDataverseClient(new FakeAuthService(), () => null, new HttpClient(new StubHandler(HttpStatusCode.OK, "")));

        var result = await client.GetAsync("msdyn_dualwriteentitymaps", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("environment", result.ReasonPhrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_missing_dataverse_url_returns_a_clear_non_success_response()
    {
        var client = new CoreDataverseClient(new FakeAuthService(), () => Env(dataverseUrl: null),
            new HttpClient(new StubHandler(HttpStatusCode.OK, "")));

        var result = await client.GetAsync("msdyn_dualwriteentitymaps", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("Dataverse", result.ReasonPhrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Auth_failure_maps_to_401()
    {
        var failing = new FakeAuthService(dataverseToken: _ => throw new InvalidOperationException("dv token denied"));
        var client = new CoreDataverseClient(failing, () => Env(), new HttpClient(new StubHandler(HttpStatusCode.OK, "")));

        var result = await client.GetAsync("msdyn_dualwriteentitymaps", TestContext.Current.CancellationToken);

        Assert.Equal(401, result.StatusCode);
        Assert.Contains("dv token denied", result.Body);
    }

    [Fact]
    public async Task Dispose_disposes_the_internally_created_HttpClient()
    {
        var client = new CoreDataverseClient(new FakeAuthService(dataverseToken: _ => "tok"), () => Env());
        client.Dispose();

        // Auth succeeds (fake), then GetAsync hits the disposed HttpClient → surfaced as non-success.
        var result = await client.GetAsync("msdyn_dualwriteentitymaps", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
    }
}
