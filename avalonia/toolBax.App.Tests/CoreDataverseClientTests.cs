using System;
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
        public int Calls { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
            => _response = new HttpResponseMessage(status) { Content = new StringContent(body) };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    private sealed class CountingDataverseAuth : IAuthService
    {
        public int Calls { get; private set; }
        public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default) =>
            Task.FromResult("fo-token");
        public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult("dv-token");
        }
        public Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default) =>
            Task.FromResult("dw-token");
    }

    private sealed class GatedDataverseAuth : IAuthService
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _token = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release(string token = "token-a") => _token.TrySetResult(token);
        public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default) =>
            Task.FromResult("fo-token");
        public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default)
        {
            _entered.TrySetResult();
            return _token.Task;
        }
        public Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default) =>
            Task.FromResult("dw-token");
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
    public async Task Token_completion_after_same_id_endpoint_edit_sends_no_request_to_old_environment()
    {
        var active = Env("https://first.crm.dynamics.com");
        var auth = new GatedDataverseAuth();
        var handler = new StubHandler(HttpStatusCode.OK, "{\"value\":[]}");
        var client = new CoreDataverseClient(auth, () => active, new HttpClient(handler));

        var send = client.GetAsync("accounts?$top=1", TestContext.Current.CancellationToken);
        await auth.Entered;
        active = active with { DataverseUrl = "https://second.crm.dynamics.com" };
        auth.Release();

        var result = await send;

        Assert.Equal(0, result.StatusCode);
        Assert.Contains("Environment changed", result.ReasonPhrase);
        Assert.Null(handler.LastRequest);
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
    public async Task Get_requests_the_total_record_count_annotations()
    {
        // #159: without these, a $count=true response silently reports the 5,000-row ceiling as a total.
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new CoreDataverseClient(new FakeAuthService(), () => Env(), new HttpClient(handler));

        await client.GetAsync("accounts?$top=1&$count=true", TestContext.Current.CancellationToken);

        var prefer = string.Join(",", handler.LastRequest!.Headers.GetValues("Prefer"));
        Assert.Contains("Microsoft.Dynamics.CRM.totalrecordcount", prefer);
        Assert.Contains("Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded", prefer);
        // One include-annotations preference carrying a comma-separated list, per the Web API docs — not
        // two competing Prefer values, the second of which Dataverse would ignore.
        Assert.Single(handler.LastRequest.Headers.GetValues("Prefer"));
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

    [Theory]
    [InlineData("https://evil.example.com/api/data/v9.2/EntityDefinitions")]
    [InlineData("http://http-preview.crm.dynamics.com/api/data/v9.2/EntityDefinitions")]
    [InlineData("https://http-preview.crm.dynamics.com:444/api/data/v9.2/EntityDefinitions")]
    public async Task Bare_http_prefixed_profile_refuses_unsafe_absolute_requests_before_auth(string requestUrl)
    {
        var auth = new CountingDataverseAuth();
        var handler = new StubHandler(HttpStatusCode.OK, "{\"value\":[]}");
        var client = new CoreDataverseClient(
            auth,
            () => Env(dataverseUrl: "http-preview.crm.dynamics.com"),
            new HttpClient(handler));

        var result = await client.GetAsync(requestUrl, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, auth.Calls);
        Assert.Equal(0, handler.Calls);
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
    public async Task A_scheme_less_dataverse_url_still_composes_a_valid_web_api_url()
    {
        // #168 low: "org.crm.dynamics.com" is the Profiles placeholder's own format, but it used to throw
        // UriFormatException here (surfaced as a bare "Request failed") while the F&O client quietly
        // repaired the same input — so the identically-typed URL worked for one tool and not the other.
        var handler = new StubHandler(HttpStatusCode.OK, "{\"value\":[]}");
        var client = new CoreDataverseClient(
            new FakeAuthService(dataverseToken: _ => "dv-tok"),
            () => Env(dataverseUrl: "contoso.crm.dynamics.com"),
            new HttpClient(handler));

        var result = await client.GetAsync("msdyn_dualwriteentitymaps", TestContext.Current.CancellationToken);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(
            "https://contoso.crm.dynamics.com/api/data/v9.2/msdyn_dualwriteentitymaps",
            handler.LastRequest!.RequestUri!.ToString());
    }

    // #168: a Map Browser failure lived in the banner only, so closing the window took the evidence with it.
    [Fact]
    public async Task A_failed_request_is_traced_with_its_status_and_endpoint_but_no_token_or_body()
    {
        using var trace = new TraceCapture();
        var handler = new StubHandler(HttpStatusCode.Forbidden, "{\"error\":\"DV-RESPONSE-BODY-MARKER\"}");
        var client = new CoreDataverseClient(
            new FakeAuthService(dataverseToken: _ => "DV-BEARER-TOKEN-MARKER"),
            () => Env(dataverseUrl: "https://dv-marker-host.crm.dynamics.com"),
            new HttpClient(handler));

        await client.GetAsync("msdyn_dualwriteentitymaps?$select=DV-QUERY-MARKER", TestContext.Current.CancellationToken);

        Assert.Contains("Dataverse request failed: 403", trace.Text);
        Assert.Contains("GET msdyn_dualwriteentitymaps", trace.Text);
        Assert.DoesNotContain("DV-BEARER-TOKEN-MARKER", trace.Text);
        Assert.DoesNotContain("DV-RESPONSE-BODY-MARKER", trace.Text);
        Assert.DoesNotContain("DV-QUERY-MARKER", trace.Text);
        Assert.DoesNotContain("dv-marker-host", trace.Text);
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
