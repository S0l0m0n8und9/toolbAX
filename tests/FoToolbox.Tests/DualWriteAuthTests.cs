using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
