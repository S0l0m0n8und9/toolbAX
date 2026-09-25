using System.Net;
using System.Net.Http.Headers;
using FoToolbox.Core.DualWrite;
using FoToolbox.Core.DualWrite.Auth;

namespace FoToolbox.Tests;

[Trait("Category", "DualWrite")]
public sealed class DualWriteEndpointTrustRegressionTests
{
    private const string GatewayRoot =
        "https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class CountingRefreshHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"fresh\",\"refresh_token\":\"r2\",\"expires_in\":3600}")
            });
        }
    }

    [Theory]
    [InlineData("https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com", "https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/")]
    [InlineData("HTTPS://PROJECTMANAGEMENTSERVICE.WEU-IL107.GATEWAY.PROD.ISLAND.POWERAPPS.COM:443/", "https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/")]
    [InlineData("https://projectmanagementservice.routing.westeurope.gateway.prod.island.powerapps.com/", "https://projectmanagementservice.routing.westeurope.gateway.prod.island.powerapps.com/")]
    public void Gateway_policy_canonicalizes_trusted_regional_roots(string candidate, string expected)
    {
        Assert.Equal(expected, DualWriteEndpointPolicy.RequireGatewayBase(candidate).AbsoluteUri);
    }

    [Theory]
    [InlineData("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://PROJECTMANAGEMENTSERVICE.WEU.GATEWAY.PROD.ISLAND.POWERAPPS.COM:443/api/dualwritemanagement/1.0/Version?x=1")]
    public void Gateway_policy_accepts_management_api_requests_on_trusted_hosts(string candidate)
    {
        Assert.True(DualWriteEndpointPolicy.IsGatewayApiRequest(new Uri(candidate)));
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/common/oauth2/v2.0/token")]
    [InlineData("https://LOGIN.MICROSOFT.COM:443/tenant-id/oauth2/v2.0/token?client-request-id=1")]
    public void Token_endpoint_detection_accepts_exact_Entra_v2_endpoints(string url)
    {
        Assert.True(DualWriteSignInCapture.IsTokenEndpoint(url));
    }

    [Theory]
    [InlineData("https://projectmanagementserviceevil.weu.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com.evil.example/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://user@projectmanagementservice.weu.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com:444/api/DualWriteManagement/1.0/Version")]
    public void Capture_rejects_gateway_lookalikes(string url)
    {
        var capture = new DualWriteSignInCapture();
        Assert.True(capture.ObserveTokenResponseBody("{\"access_token\":\"secret\",\"expires_in\":3600}"));

        Assert.False(capture.ObserveUrl(url));
        Assert.Null(capture.GatewayBaseUrl);
        Assert.Null(capture.BestEffortResult);
    }

    [Fact]
    public void Capture_does_not_pin_a_trusted_host_when_the_management_path_is_only_a_substring()
    {
        var capture = new DualWriteSignInCapture();
        Assert.True(capture.ObserveTokenResponseBody("{\"access_token\":\"secret\",\"expires_in\":3600}"));

        Assert.False(capture.ObserveUrl(
            "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/api/NotDualWriteManagement/1.0/Version"));

        Assert.Null(capture.GatewayBaseUrl);
        Assert.Equal(
            "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/",
            capture.BestEffortResult?.GatewayBaseUrl);
    }

    [Fact]
    public void Capture_uses_only_a_trusted_canonical_gateway_as_its_fallback()
    {
        var capture = new DualWriteSignInCapture();
        Assert.True(capture.ObserveTokenResponseBody("{\"access_token\":\"secret\",\"expires_in\":3600}"));

        Assert.False(capture.ObserveUrl(
            "HTTPS://PROJECTMANAGEMENTSERVICE.ROUTING.GATEWAY.PROD.ISLAND.POWERAPPS.COM:443/portal?x=1"));

        Assert.Equal(
            "https://projectmanagementservice.routing.gateway.prod.island.powerapps.com/",
            capture.BestEffortResult?.GatewayBaseUrl);
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com.evil.example/common/oauth2/v2.0/token")]
    [InlineData("https://evil.example/?next=https://login.microsoftonline.com/common/oauth2/v2.0/token")]
    [InlineData("https://login.microsoftonline.com/common/oauth2/v2.0/token/extra")]
    [InlineData("https://login.microsoftonline.com/common/oauth2/v2.0/token#fragment")]
    [InlineData("https://user@login.microsoftonline.com/common/oauth2/v2.0/token")]
    [InlineData("https://login.microsoftonline.com:444/common/oauth2/v2.0/token")]
    [InlineData("https://login.microsoftonline.com/common%2Fother/oauth2/v2.0/token")]
    [InlineData("https://login.microsoftonline.com/common%5Cother/oauth2/v2.0/token")]
    [InlineData("https://login.microsoftonline.com/tenant\\oauth2/v2.0/token")]
    [InlineData("https://login.microsoftonline.com//oauth2/v2.0/token")]
    [InlineData("http://login.microsoftonline.com/common/oauth2/v2.0/token")]
    public void Token_endpoint_detection_rejects_component_and_path_bait(string url)
    {
        Assert.False(DualWriteSignInCapture.IsTokenEndpoint(url));
    }

    [Theory]
    [InlineData("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/path")]
    [InlineData("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/?q=1")]
    [InlineData("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/#fragment")]
    [InlineData("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com.evil.example/")]
    [InlineData("https://user@projectmanagementservice.weu.gateway.prod.island.powerapps.com/")]
    [InlineData("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com:444/")]
    [InlineData("http://projectmanagementservice.weu.gateway.prod.island.powerapps.com/")]
    public void Factory_rejects_untrusted_gateway_before_constructing_a_client(string gateway)
    {
        var settings = new DualWriteConnectionSettings("key", gateway, "fo.example", "secret");

        Assert.Throws<InvalidOperationException>(() => new DualWriteGatewayFactory().Create(settings));
    }

    [Fact]
    public void Refreshing_factory_rejects_untrusted_gateway_before_persistence_setup()
    {
        var settings = new DualWriteConnectionSettings(
            "key",
            "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com.evil.example/",
            "fo.example",
            "secret")
        {
            RefreshToken = "refresh",
            AccessTokenExpiryUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var persisted = 0;

        Assert.Throws<InvalidOperationException>(() =>
            new DualWriteGatewayFactory().CreateRefreshing(
                settings,
                _ => { persisted++; return Task.CompletedTask; }));

        Assert.Equal(0, persisted);
    }

    [Theory]
    [InlineData("https://evil.example/api/DualWriteManagement/1.0/Version")]
    [InlineData("http://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com:444/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://user@projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version#fragment")]
    public async Task Static_bearer_rejects_unbound_request_before_attaching_credentials(string requestUrl)
    {
        var inner = new RecordingHandler();
        var handler = new BearerTokenHandler(new Uri(GatewayRoot), "secret") { InnerHandler = inner };
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new HttpMessageInvoker(handler).SendAsync(
                request,
                CancellationToken.None));

        Assert.Equal(0, inner.Calls);
        Assert.Null(inner.Authorization);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Static_bearer_attaches_credentials_on_the_bound_origin()
    {
        var inner = new RecordingHandler();
        var handler = new BearerTokenHandler(new Uri(GatewayRoot), "secret") { InnerHandler = inner };

        await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get, GatewayRoot + "api/DualWriteManagement/1.0/Version"),
            CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Equal("secret", inner.Authorization?.Parameter);
    }

    [Theory]
    [InlineData("https://evil.example/api/DualWriteManagement/1.0/Version")]
    [InlineData("http://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com:444/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://user@projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version")]
    [InlineData("https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version#fragment")]
    public async Task Refreshing_bearer_rejects_unbound_request_before_refresh_or_persistence(string requestUrl)
    {
        var now = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
        var refreshTransport = new CountingRefreshHandler();
        var refresher = new DualWriteRefreshTokenProvider(new HttpClient(refreshTransport)) { Clock = () => now };
        var inner = new RecordingHandler();
        var persisted = 0;
        var handler = new RefreshingBearerTokenHandler(
            new DualWriteToken("expired", "refresh", now.AddMinutes(-5)),
            refresher,
            new Uri(GatewayRoot),
            _ => { persisted++; return Task.CompletedTask; },
            () => now)
        {
            InnerHandler = inner
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new HttpMessageInvoker(handler).SendAsync(
                request,
                CancellationToken.None));

        Assert.Equal(0, refreshTransport.Calls);
        Assert.Equal(0, persisted);
        Assert.Equal(0, inner.Calls);
        Assert.Null(inner.Authorization);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Refreshing_bearer_attaches_the_current_token_on_the_bound_origin()
    {
        var now = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
        var refreshTransport = new CountingRefreshHandler();
        var refresher = new DualWriteRefreshTokenProvider(new HttpClient(refreshTransport)) { Clock = () => now };
        var inner = new RecordingHandler();
        var handler = new RefreshingBearerTokenHandler(
            new DualWriteToken("current", "refresh", now.AddHours(1)),
            refresher,
            new Uri(GatewayRoot),
            clock: () => now)
        {
            InnerHandler = inner
        };

        await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get, GatewayRoot + "api/DualWriteManagement/1.0/Version"),
            CancellationToken.None);

        Assert.Equal(0, refreshTransport.Calls);
        Assert.Equal(1, inner.Calls);
        Assert.Equal("current", inner.Authorization?.Parameter);
    }

    [Fact]
    public void Factory_owned_transports_disable_automatic_redirects()
    {
        using var gateway = DualWriteGatewayFactory.CreateGatewayTransport();
        using var refresh = DualWriteGatewayFactory.CreateRefreshTransport();

        Assert.False(gateway.AllowAutoRedirect);
        Assert.False(refresh.AllowAutoRedirect);
    }
}
