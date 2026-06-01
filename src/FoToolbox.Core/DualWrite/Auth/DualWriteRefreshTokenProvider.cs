using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Renews a Dual-write delegated token using its refresh token — the clean, browser-free
/// half of the flow. Mirrors <c>DWLibary/TokenRefresh.getLoginDataRefreshed</c> exactly:
/// a form POST to the Entra v2 common token endpoint with the first-party client id, the
/// IntegratorApp scope (+ offline_access), the portal redirect URI, and grant_type=refresh_token.
/// </summary>
public sealed class DualWriteRefreshTokenProvider
{
    private readonly HttpClient _http;

    /// <summary>Clock for computing token expiry; overridable for tests.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public DualWriteRefreshTokenProvider(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<DualWriteToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("A refresh token is required.", nameof(refreshToken));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, DualWriteAuthConstants.TokenEndpoint);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Origin", DualWriteAuthConstants.DataIntegratorBaseUrl);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", DualWriteAuthConstants.ClientId),
            new KeyValuePair<string, string>("scope", DualWriteAuthConstants.Scope),
            new KeyValuePair<string, string>("redirect_uri", DualWriteAuthConstants.RedirectUri),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DualWriteAuthException($"Dual-write token refresh failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var token = DualWriteTokenParser.Parse(body, Clock());
        if (token is null)
        {
            throw new DualWriteAuthException("Dual-write token refresh returned no access token.");
        }

        // Entra may not re-issue a refresh token; carry the previous one forward so the
        // session keeps renewing.
        return token.RefreshToken is null ? token with { RefreshToken = refreshToken } : token;
    }
}

/// <summary>Raised when a dual-write delegated-auth operation fails.</summary>
public sealed class DualWriteAuthException : Exception
{
    public DualWriteAuthException(string message) : base(message)
    {
    }
}
