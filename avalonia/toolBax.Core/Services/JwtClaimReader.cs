using System;
using System.Text;
using System.Text.Json;

namespace ToolBax.Core.Services;

/// <summary>
/// Reads a friendly account name from a delegated JWT access token's claims (preferred_username → upn →
/// unique_name → email). Used to show "Signed in as …" after an interactive sign-in. Pure and tolerant:
/// any malformed/unsigned/short input returns null rather than throwing.
/// </summary>
public static class JwtClaimReader
{
    private static readonly string[] UsernameClaims = { "preferred_username", "upn", "unique_name", "email" };

    public static string? ReadUsername(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        var parts = jwt.Split('.');
        if (parts.Length < 3)
        {
            return null; // a valid JWT is header.payload.signature
        }

        byte[] payload;
        try
        {
            payload = DecodeBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var claim in UsernameClaims)
            {
                if (document.RootElement.TryGetProperty(claim, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    var name = value.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: throw new FormatException("Invalid base64url length.");
        }

        return Convert.FromBase64String(s);
    }
}
