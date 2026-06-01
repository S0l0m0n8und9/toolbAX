using System;
using Microsoft.Identity.Client;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Translates MSAL failures from interactive sign-in into actionable guidance. The interactive
/// (system-browser, loopback) flow requires the app registration to be a public client with an
/// <c>http://localhost</c> redirect; the most common failures are misconfigurations of exactly that.
/// </summary>
public static class InteractiveSignInError
{
    /// <summary>
    /// Returns a user-facing explanation for a known app-registration misconfiguration, or
    /// <c>null</c> if the failure isn't one we can give specific guidance for (caller should fall
    /// back to the raw message).
    /// </summary>
    public static string? Describe(Exception? exception)
    {
        if (exception is not MsalServiceException msal)
        {
            return null;
        }

        var text = $"{msal.ErrorCode} {msal.Message}".ToLowerInvariant();

        var isRedirectMismatch =
            text.Contains("aadsts50011") || text.Contains("redirect uri") || text.Contains("reply url");

        if (isRedirectMismatch)
        {
            return "Interactive sign-in failed: the app registration's redirect URI doesn't match. " +
                   "In Entra, add a 'Mobile and desktop applications' platform with redirect URI 'http://localhost', then try again.";
        }

        var requiresPublicClient =
            string.Equals(msal.ErrorCode, "unauthorized_client", StringComparison.OrdinalIgnoreCase)
            || string.Equals(msal.ErrorCode, "invalid_client", StringComparison.OrdinalIgnoreCase)
            || text.Contains("aadsts7000218")
            || text.Contains("client_assertion")
            || text.Contains("client_secret")
            || text.Contains("public client");

        if (requiresPublicClient)
        {
            return "Interactive sign-in failed: the app registration must allow public client (desktop) sign-in. " +
                   "In Entra, enable 'Allow public client flows' and add a 'Mobile and desktop applications' platform " +
                   "with redirect URI 'http://localhost', then try again.";
        }

        return null;
    }
}
