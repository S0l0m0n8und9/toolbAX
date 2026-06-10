using System;
using System.Text;

namespace FoToolbox.Core.Auth;

/// <summary>Normalizes a pasted bearer token: strips a "Bearer " prefix and all whitespace.</summary>
public static class BearerTokenText
{
    /// <summary>
    /// Strips a leading <c>Bearer </c> prefix (case-insensitive) and removes all whitespace characters
    /// from the token string.
    /// </summary>
    public static string Normalize(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Bearer ".Length..];
        }

        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }
}
