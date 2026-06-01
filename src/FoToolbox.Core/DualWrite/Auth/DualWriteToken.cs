using System;
using System.Text.Json;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>A delegated access token with its refresh token and absolute expiry.</summary>
public sealed record DualWriteToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresUtc)
{
    /// <summary>True when the token is at/near expiry (default 2-minute safety margin).</summary>
    public bool IsExpired(DateTimeOffset now, TimeSpan? margin = null) =>
        now >= ExpiresUtc - (margin ?? TimeSpan.FromMinutes(2));
}

/// <summary>
/// Parses an Entra v2 token-endpoint JSON response (<c>access_token</c>, <c>refresh_token</c>,
/// <c>expires_in</c>). Tolerant of missing fields. The absolute expiry is computed from an
/// injected "now" so callers/tests stay deterministic.
/// </summary>
public static class DualWriteTokenParser
{
    public static DualWriteToken? Parse(string json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("access_token", out var accessEl) ||
                accessEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var accessToken = accessEl.GetString();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            string? refreshToken = null;
            if (root.TryGetProperty("refresh_token", out var refreshEl) && refreshEl.ValueKind == JsonValueKind.String)
            {
                refreshToken = refreshEl.GetString();
            }

            var expiresIn = 3600;
            if (root.TryGetProperty("expires_in", out var expiresEl))
            {
                if (expiresEl.ValueKind == JsonValueKind.Number && expiresEl.TryGetInt32(out var n))
                {
                    expiresIn = n;
                }
                else if (expiresEl.ValueKind == JsonValueKind.String && int.TryParse(expiresEl.GetString(), out var s))
                {
                    expiresIn = s;
                }
            }

            return new DualWriteToken(accessToken!, refreshToken, now.AddSeconds(expiresIn));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
