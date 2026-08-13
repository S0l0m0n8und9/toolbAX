using System;
using System.Collections.Generic;
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

        // Adds a response header (TryAddWithoutValidation so values like a weak ETag are accepted).
        public StubHandler WithHeader(string name, string value)
        {
            _response.Headers.TryAddWithoutValidation(name, value);
            return this;
        }

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
    public async Task Extra_headers_are_applied_to_the_request()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        var client = new CoreODataClient(new FakeAuthService(), () => Env(), new HttpClient(handler));
        var headers = new Dictionary<string, string> { ["If-Match"] = "W/\"42\"" };

        await client.SendAsync("PATCH", "/data/CustomersV3(dataAreaId='USMF',CustomerAccount='US-1')",
            "{\"OrganizationName\":\"X\"}", headers, TestContext.Current.CancellationToken);

        Assert.True(handler.LastRequest!.Headers.TryGetValues("If-Match", out var values));
        Assert.Contains("W/\"42\"", values!);
    }

    [Fact]
    public async Task Response_headers_are_captured()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty).WithHeader("ETag", "W/\"5\"");
        var client = new CoreODataClient(new FakeAuthService(), () => Env(), new HttpClient(handler));

        var result = await client.SendAsync("GET", "/data/Foo", null, TestContext.Current.CancellationToken);

        Assert.NotNull(result.Headers);
        Assert.Equal("W/\"5\"", result.Headers!["ETag"]);
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
    public async Task An_absolute_path_is_used_verbatim_for_paging()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new CoreODataClient(new FakeAuthService(), () => Env(), new HttpClient(handler));

        const string nextLink = "https://contoso.operations.dynamics.com/data/CustomersV3?$skiptoken=abc";
        await client.SendAsync("GET", nextLink, null, TestContext.Current.CancellationToken);

        Assert.Equal(nextLink, handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_paging_link_on_a_foreign_host_is_refused()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new CoreODataClient(new FakeAuthService(), () => Env(), new HttpClient(handler));

        // A nextLink pointing at a different host must not receive the env-scoped bearer.
        var result = await client.SendAsync("GET", "https://evil.example.com/data/X", null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(handler.LastRequest); // no request was sent
    }

    [Fact]
    public async Task A_same_host_paging_link_downgraded_to_http_is_refused()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new CoreODataClient(new FakeAuthService(), () => Env(), new HttpClient(handler));

        // Same host but plaintext http (env is https): the bearer must not be sent over cleartext.
        var result = await client.SendAsync("GET", "http://contoso.operations.dynamics.com/data/X", null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(handler.LastRequest);
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

    // Mimics an HTTP/socket timeout: HttpClient raises an OperationCanceledException from its own
    // internal timeout token, while the CALLER's token is still live.
    private sealed class TimingOutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new TaskCanceledException("The request timed out.", new TimeoutException());
    }

    // Honours the token the way the real HTTP stack does (HttpClient itself does not pre-check it before
    // handing off to the handler, so a stub that ignores the token can't model a cancelled request).
    private sealed class CancelObservingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public async Task A_cancelled_request_surfaces_the_cancellation_instead_of_a_failed_response()
    {
        var client = new CoreODataClient(new FakeAuthService(_ => "tok"), () => Env(),
            new HttpClient(new CancelObservingHandler()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Swallowing this into (0, "Request failed") made every caller's `catch (OperationCanceledException)`
        // dead code in production — the Query Builder's "Export cancelled." could never be reached (#168).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync("GET", "/data/Foo", null, cts.Token));
    }

    [Fact]
    public async Task Cancelling_during_token_acquisition_is_not_reported_as_401_unauthorized()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var auth = new FakeAuthService(_ => throw new OperationCanceledException(cts.Token));
        var client = new CoreODataClient(auth, () => Env(), new HttpClient(new StubHandler(HttpStatusCode.OK, "")));

        // "401 Unauthorized" told the user their credentials had been rejected when they pressed Cancel.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync("GET", "/data/Foo", null, cts.Token));
    }

    [Fact]
    public async Task A_timeout_is_still_reported_as_a_failed_request_not_a_cancellation()
    {
        var client = new CoreODataClient(new FakeAuthService(_ => "tok"), () => Env(),
            new HttpClient(new TimingOutHandler()));

        // Same exception type, but the caller never asked to stop — so it stays a reportable failure
        // rather than unwinding into a "cancelled" status the user didn't cause.
        var result = await client.SendAsync("GET", "/data/Foo", null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.StatusCode);
        Assert.Equal("Request failed", result.ReasonPhrase);
    }

    [Fact]
    public async Task A_token_timeout_during_acquisition_is_still_a_401()
    {
        // An auth OperationCanceledException with the caller's token live is an auth failure (e.g. the
        // broker timing out), not a user cancellation — it keeps the 401 mapping.
        var auth = new FakeAuthService(_ => throw new OperationCanceledException("broker timed out"));
        var client = new CoreODataClient(auth, () => Env(), new HttpClient(new StubHandler(HttpStatusCode.OK, "")));

        var result = await client.SendAsync("GET", "/data/Foo", null, TestContext.Current.CancellationToken);

        Assert.Equal(401, result.StatusCode);
    }

    // --- #168: failures reach the session log, and nothing else does ---

    [Fact]
    public async Task A_failed_request_is_traced_with_its_status_and_endpoint_path()
    {
        using var trace = new TraceCapture();
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");
        var client = new CoreODataClient(new FakeAuthService(_ => "tok"), () => Env(), new HttpClient(handler));

        await client.SendAsync("POST", "/data/CustomersV3", "{}", TestContext.Current.CancellationToken);

        Assert.Contains("500", trace.Text);
        Assert.Contains("POST /data/CustomersV3", trace.Text);
    }

    [Fact]
    public async Task A_successful_request_is_not_traced()
    {
        using var trace = new TraceCapture();
        var handler = new StubHandler(HttpStatusCode.OK, "{\"value\":[]}");
        var client = new CoreODataClient(new FakeAuthService(_ => "tok"), () => Env(), new HttpClient(handler));

        await client.SendAsync("GET", "/data/OnlyOnTheHappyPath", null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("OnlyOnTheHappyPath", trace.Text);
    }

    /// <summary>
    /// The secrecy bar for the session log (#168): a trace line may name the endpoint and nothing that
    /// could carry a credential or customer data. Every marker below is deliberately unique so a leak
    /// anywhere in the formatting shows up as a failure here rather than in a user's log file.
    /// </summary>
    [Fact]
    public async Task A_traced_failure_never_carries_the_token_the_bodies_the_headers_or_the_query()
    {
        using var trace = new TraceCapture();
        var handler = new StubHandler(HttpStatusCode.BadRequest, "{\"error\":\"RESPONSE-BODY-MARKER\"}")
            .WithHeader("x-ms-diagnostics", "RESPONSE-HEADER-MARKER");
        var client = new CoreODataClient(new FakeAuthService(_ => "BEARER-TOKEN-MARKER"),
            () => Env("marker-host.operations.dynamics.com"), new HttpClient(handler));

        await client.SendAsync("PATCH", "/data/CustomersV3?$filter=Name eq 'REQUEST-QUERY-MARKER'",
            "{\"Name\":\"REQUEST-BODY-MARKER\"}",
            new Dictionary<string, string> { ["If-Match"] = "REQUEST-HEADER-MARKER" },
            TestContext.Current.CancellationToken);

        Assert.Contains("PATCH /data/CustomersV3", trace.Text);   // the endpoint, so the line is useful at all

        Assert.DoesNotContain("BEARER-TOKEN-MARKER", trace.Text);
        Assert.DoesNotContain("RESPONSE-BODY-MARKER", trace.Text);
        Assert.DoesNotContain("RESPONSE-HEADER-MARKER", trace.Text);
        Assert.DoesNotContain("REQUEST-BODY-MARKER", trace.Text);
        Assert.DoesNotContain("REQUEST-HEADER-MARKER", trace.Text);
        // A $filter carries business data, so the query string is dropped with it.
        Assert.DoesNotContain("REQUEST-QUERY-MARKER", trace.Text);
        Assert.DoesNotContain("$filter", trace.Text);
        // And the host names the customer's environment.
        Assert.DoesNotContain("marker-host", trace.Text);
    }

    [Fact]
    public async Task A_traced_failure_of_a_paging_link_keeps_only_the_path()
    {
        using var trace = new TraceCapture();
        var handler = new StubHandler(HttpStatusCode.NotFound, "");
        var client = new CoreODataClient(new FakeAuthService(_ => "tok"),
            () => Env("nextlink-host.operations.dynamics.com"), new HttpClient(handler));

        // An absolute @odata.nextLink is followed verbatim; the trace line must still reduce to the path.
        await client.SendAsync("GET",
            "https://nextlink-host.operations.dynamics.com/data/CustomersV3?$skiptoken=SKIPTOKEN-MARKER",
            null, TestContext.Current.CancellationToken);

        Assert.Contains("GET /data/CustomersV3", trace.Text);
        Assert.DoesNotContain("nextlink-host", trace.Text);
        Assert.DoesNotContain("SKIPTOKEN-MARKER", trace.Text);
    }

    [Fact]
    public async Task A_cancelled_request_is_not_traced_as_a_failure()
    {
        // Cancelling is not failing: the cancellation propagates out of the send instead of becoming a
        // response, so there is nothing for the wrapper to trace. Were it ever mapped back to a non-success
        // response, every user cancel would start writing an error line to the session log.
        using var trace = new TraceCapture();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var auth = new FakeAuthService(_ => throw new OperationCanceledException("user cancelled sign-in"));
        var client = new CoreODataClient(auth, () => Env(), new HttpClient(new StubHandler(HttpStatusCode.OK, "")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync("GET", "/data/CancelledOnly", null, cts.Token));

        Assert.DoesNotContain("CancelledOnly", trace.Text);
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
