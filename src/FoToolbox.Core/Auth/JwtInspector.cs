using System;
using System.Text;
using System.Text.Json;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Read-only JWT payload inspection (no signature validation) for the claims the toolbox needs
/// during auth setup: tenant (<c>tid</c>) and expiry (<c>exp</c>).
/// </summary>
public static class JwtInspector
{
    /// <summary>
    /// Attempts to extract the <c>tid</c> (tenant ID) claim from the JWT payload.
    /// Returns <see langword="false"/> if the token is not a parseable JWT or has no <c>tid</c> claim.
    /// </summary>
    public static bool TryGetTenantId(string token, out string tenantId)
    {
        tenantId = string.Empty;
        if (!TryParsePayload(token, out var doc)) return false;
        using (doc)
        {
            if (doc!.RootElement.TryGetProperty("tid", out var tid) && tid.ValueKind == JsonValueKind.String)
            {
                tenantId = tid.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(tenantId);
            }
        }
        return false;
    }

    /// <summary>
    /// Attempts to extract the <c>exp</c> (expiry) claim from the JWT payload.
    /// Returns <see langword="false"/> if the token is not a parseable JWT or has no <c>exp</c> claim.
    /// </summary>
    public static bool TryGetExpiryUtc(string token, out DateTimeOffset expiryUtc)
    {
        expiryUtc = default;
        if (!TryParsePayload(token, out var doc)) return false;
        using (doc)
        {
            if (doc!.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
            {
                expiryUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
                return true;
            }
        }
        return false;
    }

    private static bool TryParsePayload(string token, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.');
        if (parts.Length < 2) return false;

        try
        {
            var normalized = parts[1].Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2: normalized += "=="; break;
                case 3: normalized += "="; break;
                case 1: return false;
            }
            var bytes = Convert.FromBase64String(normalized);
            document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            return true;
        }
        catch (FormatException) { return false; }
        catch (JsonException) { return false; }
    }
}
