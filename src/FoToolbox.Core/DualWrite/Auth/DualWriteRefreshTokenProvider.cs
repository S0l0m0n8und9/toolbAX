using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Renews a Dual-write delegated token using its refresh token — the clean, browser-free
/// half of the flow. Mirrors <c>DWLibary/TokenRefresh.getLoginDataRefreshed</c> exactly:
/// a form POST pinned to the captured actual tenant with the captured first-party client/resource
/// context. Legacy refresh tokens without trusted binding fail before network access.
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

    public Task<DualWriteToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        throw new DualWriteAuthException("This legacy Dual-write session has no trusted refresh context; sign in again.");

    public async Task<DualWriteToken> RefreshAsync(
        DualWriteToken currentToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentToken);
        if (string.IsNullOrWhiteSpace(currentToken.RefreshToken))
        {
            throw new DualWriteAuthException("A refresh token is required.");
        }
        var binding = currentToken.Binding;
        if (binding is null || !binding.IsTrusted)
        {
            throw new DualWriteAuthException("This Dual-write session has no trusted refresh context; sign in again.");
        }

        var endpoint = new Uri(
            $"https://login.microsoftonline.com/{binding.TenantId:D}/oauth2/v2.0/token");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Origin", DualWriteAuthConstants.DataIntegratorBaseUrl);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", binding.ClientId),
            new KeyValuePair<string, string>("scope", binding.Scope),
            new KeyValuePair<string, string>("redirect_uri", DualWriteAuthConstants.RedirectUri),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", currentToken.RefreshToken)
        });

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DualWriteAuthException($"Dual-write token refresh failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        if (!DualWriteTokenResponseValidator.TryValidateRefresh(
                body,
                (int)response.StatusCode,
                binding,
                currentToken.RefreshToken,
                Clock(),
                out var token))
        {
            throw new DualWriteAuthException("Dual-write token refresh returned an untrusted response.");
        }

        return token!;
    }
}

/// <summary>Raised when a dual-write delegated-auth operation fails.</summary>
public sealed class DualWriteAuthException : Exception
{
    public DualWriteAuthException(string message) : base(message)
    {
    }
}
