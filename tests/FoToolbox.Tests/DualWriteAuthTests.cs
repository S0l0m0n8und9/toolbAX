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

    // #166: the clock-expiry path above only fires when our own clock says the token is stale. A gateway
    // that answers 401 for a token we still believe in (revoked, rotated server-side, clock skew) left the
    // session dead until the user signed in again — so a 401 must force exactly one refresh + replay.

    /// <summary>Answers 401 for the first <c>unauthorizedCount</c> sends, then 200. Records every token seen.</summary>
    private sealed class UnauthorizedThenOkHandler : HttpMessageHandler
    {
        private readonly int _unauthorizedCount;
        public UnauthorizedThenOkHandler(int unauthorizedCount) => _unauthorizedCount = unauthorizedCount;

        public List<string?> TokensSeen { get; } = new();
        public int Sends => TokensSeen.Count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TokensSeen.Add(request.Headers.Authorization?.Parameter);
            var status = TokensSeen.Count <= _unauthorizedCount ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    /// <summary>Counts refresh calls so a retry loop can't hide behind a successful outcome.</summary>
    private sealed class CountingTokenEndpointHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"access_token\":\"fresh{Calls}\",\"refresh_token\":\"r{Calls + 1}\",\"expires_in\":3600}}")
            });
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Unauthorized_RefreshesOnce_ReplaysTheRequest_AndSucceeds()
    {
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        // Deliberately NOT expired: the 401, not the clock, has to be what triggers the refresh.
        var live = new DualWriteToken("stale-but-unexpired", "r1", now.AddHours(1));
        var tokenEndpoint = new CountingTokenEndpointHandler();
        var refresher = new DualWriteRefreshTokenProvider(new HttpClient(tokenEndpoint)) { Clock = () => now };
        DualWriteToken? persisted = null;
        var inner = new UnauthorizedThenOkHandler(unauthorizedCount: 1);
        var handler = new RefreshingBearerTokenHandler(live, refresher, t => { persisted = t; return Task.CompletedTask; }, () => now)
        {
            InnerHandler = inner
        };

        var response = await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "https://gw.example/x") { Content = new StringContent("{}") },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, tokenEndpoint.Calls);                                  // exactly one refresh
        Assert.Equal(new[] { "stale-but-unexpired", "fresh1" }, inner.TokensSeen.ToArray());
        Assert.Equal("fresh1", persisted!.AccessToken);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Unauthorized_Twice_SurfacesThe401_WithoutLooping()
    {
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var live = new DualWriteToken("old", "r1", now.AddHours(1));
        var tokenEndpoint = new CountingTokenEndpointHandler();
        var refresher = new DualWriteRefreshTokenProvider(new HttpClient(tokenEndpoint)) { Clock = () => now };
        var inner = new UnauthorizedThenOkHandler(unauthorizedCount: int.MaxValue);
        var handler = new RefreshingBearerTokenHandler(live, refresher, null, () => now)
        {
            InnerHandler = inner
        };

        var response = await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://gw.example/x"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);   // surfaced, not swallowed
        Assert.Equal(2, inner.Sends);                                      // one retry only
        Assert.Equal(1, tokenEndpoint.Calls);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Unauthorized_WithNoRefreshToken_SurfacesImmediately()
    {
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var pasted = new DualWriteToken("pasted", null, now.AddHours(1));
        var tokenEndpoint = new CountingTokenEndpointHandler();
        var refresher = new DualWriteRefreshTokenProvider(new HttpClient(tokenEndpoint)) { Clock = () => now };
        var inner = new UnauthorizedThenOkHandler(unauthorizedCount: 1);
        var handler = new RefreshingBearerTokenHandler(pasted, refresher, null, () => now)
        {
            InnerHandler = inner
        };

        var response = await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://gw.example/x"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, inner.Sends);       // nothing to refresh with, so nothing to retry
        Assert.Equal(0, tokenEndpoint.Calls);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Unauthorized_WhenTheRefreshItselfFails_SurfacesTheOriginal401()
    {
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var live = new DualWriteToken("old", "revoked", now.AddHours(1));
        var refresher = new DualWriteRefreshTokenProvider(
            new HttpClient(new FailingTokenEndpointHandler())) { Clock = () => now };
        var inner = new UnauthorizedThenOkHandler(unauthorizedCount: 1);
        var handler = new RefreshingBearerTokenHandler(live, refresher, null, () => now)
        {
            InnerHandler = inner
        };

        // A refresh failure must not replace the gateway's own 401 with an auth exception from a retry the
        // caller never asked for — the gateway client turns the 401 into its usual message.
        var response = await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://gw.example/x"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, inner.Sends);
    }

    private sealed class FailingTokenEndpointHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"invalid_grant\"}")
            });
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
