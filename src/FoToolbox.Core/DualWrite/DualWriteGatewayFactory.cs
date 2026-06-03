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
    private static readonly HttpClient RefreshHttpClient = new();

    public IDualWriteGateway Create(DualWriteConnectionSettings settings)
    {
        var http = new HttpClient(new BearerTokenHandler(RequireGatewayUrl(settings)))
        {
            BaseAddress = GatewayUri(settings)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FoToolbox-DualWrite/0.1");
        return new DualWriteGatewayClient(http);
    }

    public IDualWriteGateway CreateRefreshing(DualWriteConnectionSettings settings, Func<DualWriteToken, Task> onRefreshed)
    {
        RequireGatewayUrl(settings);
        if (!settings.HasDelegatedSession)
        {
            return Create(settings);
        }

        var token = new DualWriteToken(
            settings.BearerToken ?? string.Empty,
            settings.RefreshToken,
            settings.AccessTokenExpiryUtc ?? DateTimeOffset.UtcNow);
        var refresher = new DualWriteRefreshTokenProvider(RefreshHttpClient);
        var http = new HttpClient(new RefreshingBearerTokenHandler(token, refresher, onRefreshed))
        {
            BaseAddress = GatewayUri(settings)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FoToolbox-DualWrite/0.1");
        return new DualWriteGatewayClient(http);
    }

    private static string RequireGatewayUrl(DualWriteConnectionSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(settings.GatewayBaseUrl))
        {
            throw new InvalidOperationException("Gateway base URL is not configured.");
        }

        return settings.BearerToken ?? string.Empty;
    }

    private static Uri GatewayUri(DualWriteConnectionSettings settings) =>
        new(settings.GatewayBaseUrl.TrimEnd('/') + "/");
}

internal sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly string? _token;

    public BearerTokenHandler(string? token) : base(new HttpClientHandler())
    {
        _token = token;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
