using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteTokenParserTests
{
    [Trait("Category", "DualWrite")]
    [Fact]
    public void Parse_ReadsAccessRefreshAndExpiry()
    {
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var token = DualWriteTokenParser.Parse(
            "{\"access_token\":\"abc\",\"refresh_token\":\"r1\",\"expires_in\":3600}", now);

        Assert.NotNull(token);
        Assert.Equal("abc", token!.AccessToken);
        Assert.Equal("r1", token.RefreshToken);
        Assert.Equal(now.AddSeconds(3600), token.ExpiresUtc);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Parse_NoAccessToken_ReturnsNull()
    {
        Assert.Null(DualWriteTokenParser.Parse("{\"error\":\"x\"}", DateTimeOffset.UnixEpoch));
        Assert.Null(DualWriteTokenParser.Parse("not json", DateTimeOffset.UnixEpoch));
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void IsExpired_RespectsMargin()
    {
        var expiry = new DateTimeOffset(2026, 5, 29, 1, 0, 0, TimeSpan.Zero);
        var token = new DualWriteToken("a", "r", expiry);
        Assert.False(token.IsExpired(expiry.AddMinutes(-10)));
        Assert.True(token.IsExpired(expiry.AddMinutes(-1))); // within default 2-min margin
        Assert.True(token.IsExpired(expiry.AddMinutes(5)));
    }
}

public class DualWriteRefreshTokenProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }
        public CapturingHandler(HttpResponseMessage response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return _response;
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Refresh_PostsExactForm_AndParsesToken()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"new\",\"refresh_token\":\"r2\",\"expires_in\":3600}")
        });
        var provider = new DualWriteRefreshTokenProvider(new HttpClient(handler))
        {
            Clock = () => new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero)
        };

        var token = await provider.RefreshAsync("r1", CancellationToken.None);

        Assert.Equal("https://login.microsoftonline.com/common/oauth2/v2.0/token", handler.LastUri!.ToString());
        Assert.Contains("client_id=2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b", handler.LastBody);
        Assert.Contains("grant_type=refresh_token", handler.LastBody);
        Assert.Contains("refresh_token=r1", handler.LastBody);
        Assert.Contains("IntegratorApp.com", handler.LastBody);
        Assert.Equal("new", token.AccessToken);
        Assert.Equal("r2", token.RefreshToken);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Refresh_NoNewRefreshToken_CarriesForwardOldOne()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"new\",\"expires_in\":3600}")
        });
        var provider = new DualWriteRefreshTokenProvider(new HttpClient(handler));

        var token = await provider.RefreshAsync("r1", CancellationToken.None);

        Assert.Equal("r1", token.RefreshToken);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Refresh_NonSuccess_Throws()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"invalid_grant\"}")
        });
        var provider = new DualWriteRefreshTokenProvider(new HttpClient(handler));

        await Assert.ThrowsAsync<DualWriteAuthException>(() => provider.RefreshAsync("r1", CancellationToken.None));
    }
}

public class RefreshingBearerTokenHandlerTests
{
    private sealed class StubInner : HttpMessageHandler
    {
        public AuthenticationHeaderValue? LastAuth { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuth = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class TokenEndpointHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"fresh\",\"refresh_token\":\"r2\",\"expires_in\":3600}")
            });
    }

    private static HttpResponseMessage Send(DelegatingHandler handler, HttpRequestMessage req)
    {
        var invoker = new HttpMessageInvoker(handler);
        return invoker.SendAsync(req, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task ExpiredToken_RefreshesAndAttachesNewToken_AndPersists()
    {
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var expired = new DualWriteToken("old", "r1", now.AddMinutes(-5));
        var refresher = new DualWriteRefreshTokenProvider(new HttpClient(new TokenEndpointHandler())) { Clock = () => now };
        DualWriteToken? persisted = null;
        var inner = new StubInner();
        var handler = new RefreshingBearerTokenHandler(expired, refresher, t => { persisted = t; return Task.CompletedTask; }, () => now)
        {
            InnerHandler = inner
        };

        var response = await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://gw.example/x"), CancellationToken.None);

        Assert.Equal("fresh", inner.LastAuth!.Parameter);
        Assert.NotNull(persisted);
        Assert.Equal("fresh", persisted!.AccessToken);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task FreshToken_DoesNotRefresh()
    {
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var fresh = new DualWriteToken("good", "r1", now.AddHours(1));
        var refresher = new DualWriteRefreshTokenProvider(new HttpClient(new TokenEndpointHandler())) { Clock = () => now };
        var persisted = false;
        var inner = new StubInner();
        var handler = new RefreshingBearerTokenHandler(fresh, refresher, _ => { persisted = true; return Task.CompletedTask; }, () => now)
        {
            InnerHandler = inner
        };

        await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://gw.example/x"), CancellationToken.None);

        Assert.Equal("good", inner.LastAuth!.Parameter);
        Assert.False(persisted);
    }
}

public class DualWriteSignInCaptureTests
{
    private static readonly Func<DateTimeOffset> Clock = () => new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Capture_CompletesWhenTokenAndGatewaySeen()
    {
        var capture = new DualWriteSignInCapture(Clock);
        Assert.False(capture.IsComplete);

        Assert.True(capture.ObserveTokenResponseBody("{\"access_token\":\"abc\",\"refresh_token\":\"r\",\"expires_in\":3600}"));
        Assert.False(capture.IsComplete);

        Assert.True(capture.ObserveUrl("https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version"));
        Assert.True(capture.IsComplete);

        var result = capture.Result!;
        Assert.Equal("abc", result.Token.AccessToken);
        Assert.Equal("https://projectmanagementservice.weu-il107.gateway.prod.island.powerapps.com", result.GatewayBaseUrl);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Capture_IgnoresMarkersInThePathOfAForeignHost()
    {
        var capture = new DualWriteSignInCapture(Clock);

        // Both markers appear, but only in the path of an attacker host — the gateway (and the token it
        // would carry) must NOT be pinned to it.
        Assert.False(capture.ObserveUrl(
            "https://attacker.example/projectmanagementservice/DualWriteManagement/1.0/Version"));
        Assert.Null(capture.GatewayBaseUrl);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Capture_RejectsANonHttpsGatewayUrl()
    {
        var capture = new DualWriteSignInCapture(Clock);

        Assert.False(capture.ObserveUrl(
            "http://projectmanagementservice.weu.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version"));
        Assert.Null(capture.GatewayBaseUrl);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Capture_IgnoresUnrelatedUrls_AndSecondToken()
    {
        var capture = new DualWriteSignInCapture(Clock);
        Assert.False(capture.ObserveUrl("https://login.microsoftonline.com/common/oauth2/v2.0/token"));
        Assert.Null(capture.GatewayBaseUrl);

        Assert.True(capture.ObserveTokenResponseBody("{\"access_token\":\"first\",\"expires_in\":60}"));
        Assert.False(capture.ObserveTokenResponseBody("{\"access_token\":\"second\",\"expires_in\":60}"));
        Assert.Equal("first", capture.Token!.AccessToken);
    }

    [Trait("Category", "DualWrite")]
    [Theory]
    [InlineData("https://login.microsoftonline.com/common/oauth2/v2.0/token", true)]
    [InlineData("https://login.microsoft.com/common/oauth2/v2.0/token", true)]
    [InlineData("https://contoso.crm.dynamics.com/api/data/v9.2/accounts", false)]
    public void IsTokenEndpoint_DetectsEntraTokenUrls(string url, bool expected)
    {
        Assert.Equal(expected, DualWriteSignInCapture.IsTokenEndpoint(url));
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void BuildSignInUrl_EncodesIdentifier()
    {
        var url = DualWriteAuthConstants.BuildSignInUrl("https://uat.operations.dynamics.com");
        Assert.StartsWith("https://dataintegrator.trafficmanager.net/dualWrite?axenv=", url);
        Assert.Contains("https%3A%2F%2Fuat.operations.dynamics.com", url);
    }
}
