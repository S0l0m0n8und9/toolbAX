using FoToolbox.Core.DualWrite.Auth;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite;

/// <summary>Builds an <see cref="IDualWriteGateway"/> bound to a connection's gateway URL + bearer token.</summary>
public interface IDualWriteGatewayFactory
{
    /// <summary>Builds a gateway client using the connection's static bearer token.</summary>
    IDualWriteGateway Create(DualWriteConnectionSettings settings);

    /// <summary>
    /// Builds a gateway client whose token renews itself via the refresh token, invoking
    /// <paramref name="onRefreshed"/> so the caller can persist the rotated token.
    /// </summary>
    IDualWriteGateway CreateRefreshing(DualWriteConnectionSettings settings, Func<DualWriteToken, Task> onRefreshed);
}

/// <summary>
/// Default factory: builds an <see cref="HttpClient"/> whose base address is the gateway root
/// and whose handler attaches (and optionally renews) the bearer token, then wraps it in a
/// <see cref="DualWriteGatewayClient"/>.
/// </summary>
public sealed class DualWriteGatewayFactory : IDualWriteGatewayFactory
{
    // Token-endpoint calls share one long-lived client. Refresh providers only send requests
    // (they never dispose it), so a static instance avoids leaking a SocketsHttpHandler/connection
    // pool on every gateway creation.
    private static readonly HttpClient RefreshHttpClient = new(CreateRefreshTransport());

    public IDualWriteGateway Create(DualWriteConnectionSettings settings)
    {
        var gatewayOrigin = RequireGatewayOrigin(settings);
        var http = new HttpClient(new BearerTokenHandler(gatewayOrigin, settings.BearerToken))
        {
            BaseAddress = gatewayOrigin
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FoToolbox-DualWrite/0.1");
        return new DualWriteGatewayClient(http, ownsHttpClient: true);
    }

    public IDualWriteGateway CreateRefreshing(DualWriteConnectionSettings settings, Func<DualWriteToken, Task> onRefreshed)
    {
        var gatewayOrigin = RequireGatewayOrigin(settings);
        if (!settings.HasDelegatedSession)
        {
            return Create(settings);
        }
        if (settings.DelegatedBinding is null || !settings.DelegatedBinding.IsTrusted)
        {
            throw new InvalidOperationException("This Dual-write session has no trusted refresh context; sign in again.");
        }

        var token = new DualWriteToken(
            settings.BearerToken ?? string.Empty,
            settings.RefreshToken,
            settings.AccessTokenExpiryUtc ?? DateTimeOffset.UtcNow)
        {
            Binding = settings.DelegatedBinding
        };
        var refresher = new DualWriteRefreshTokenProvider(RefreshHttpClient);
        var http = new HttpClient(new RefreshingBearerTokenHandler(token, refresher, gatewayOrigin, onRefreshed))
        {
            BaseAddress = gatewayOrigin
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FoToolbox-DualWrite/0.1");
        return new DualWriteGatewayClient(http, ownsHttpClient: true);
    }

    private static Uri RequireGatewayOrigin(DualWriteConnectionSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return DualWriteEndpointPolicy.RequireGatewayBase(settings.GatewayBaseUrl);
    }

    internal static HttpClientHandler CreateGatewayTransport() => new() { AllowAutoRedirect = false };

    internal static HttpClientHandler CreateRefreshTransport() => new() { AllowAutoRedirect = false };
}

internal sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly Uri _gatewayOrigin;
    private readonly string? _token;

    /// <summary>
    /// Uses a no-redirect transport by default. A caller that replaces <see cref="InnerHandler"/>
    /// owns equivalent redirect enforcement.
    /// </summary>
    public BearerTokenHandler(Uri gatewayOrigin, string? token) : base(DualWriteGatewayFactory.CreateGatewayTransport())
    {
        _gatewayOrigin = DualWriteEndpointPolicy.RequireGatewayBase(
            gatewayOrigin?.AbsoluteUri ?? throw new ArgumentNullException(nameof(gatewayOrigin)));
        _token = token;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!DualWriteEndpointPolicy.IsSameOrigin(_gatewayOrigin, request.RequestUri))
        {
            throw new InvalidOperationException("The dual-write request does not match the authenticated gateway origin.");
        }

        if (!string.IsNullOrWhiteSpace(_token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
