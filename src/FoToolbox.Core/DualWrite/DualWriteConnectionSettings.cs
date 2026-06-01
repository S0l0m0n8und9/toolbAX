using System;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Decrypted, in-memory dual-write connection settings: the gateway base URL, the F&amp;O
/// identifier used to resolve the linkage, and the (already-decrypted) bearer access token.
/// When the token came from an interactive sign-in it also carries a refresh token and the
/// access-token expiry so the session can renew itself silently.
/// </summary>
public sealed record DualWriteConnectionSettings(string Key, string GatewayBaseUrl, string FoIdentifier, string? BearerToken)
{
    /// <summary>Refresh token from an interactive sign-in (null for a manually-pasted token).</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Absolute expiry of <see cref="BearerToken"/> when known (from sign-in/refresh).</summary>
    public DateTimeOffset? AccessTokenExpiryUtc { get; init; }

    /// <summary>True when a refresh token is available, so the access token can be renewed silently.</summary>
    public bool HasDelegatedSession => !string.IsNullOrWhiteSpace(RefreshToken);

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(GatewayBaseUrl) &&
        !string.IsNullOrWhiteSpace(FoIdentifier) &&
        !string.IsNullOrWhiteSpace(BearerToken);
}
