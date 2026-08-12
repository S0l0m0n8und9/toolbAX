using System;
using System.Text.RegularExpressions;

namespace FoToolbox.Core.Auth;

public static class ResourceUrlNormalizer
{
    private static readonly Regex DataverseApiVersionSuffix = new(
        @"/api/data/v\d+(\.\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string NormalizeFoBaseUrl(string baseUrl)
    {
        var normalized = NormalizeRoot(baseUrl);
        if (normalized.EndsWith("/data", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }

        return normalized;
    }

    public static string NormalizeDataverseResourceBaseUrl(string baseUrl)
    {
        var normalized = NormalizeRoot(baseUrl);
        if (normalized.EndsWith("/api/data", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^9];
        }
        else
        {
            normalized = DataverseApiVersionSuffix.Replace(normalized, string.Empty);
        }

        return normalized.TrimEnd('/');
    }

    public static string BuildDataverseApiBaseUrl(string baseUrl)
    {
        var normalized = NormalizeDataverseResourceBaseUrl(baseUrl);
        return $"{normalized}/api/data/v9.2";
    }

    /// <summary>
    /// Trims the URL and defaults a scheme-less host to <c>https</c>. Both F&amp;O and Dataverse
    /// normalization go through here, so the two stay symmetric.
    /// </summary>
    private static string NormalizeRoot(string url)
    {
        if (url is null)
        {
            return string.Empty;
        }

        var normalized = url.Trim().TrimEnd('/');
        if (normalized.Length == 0)
        {
            // Nothing to normalize — never fabricate a bare "https://" out of an unset URL.
            return string.Empty;
        }

        // A bare host ("org.crm.dynamics.com" — the very shape the Profiles URL placeholders show) is
        // not a usable resource identifier: it yields the scope "org.crm.dynamics.com/.default", which
        // AAD rejects with "resource principal not found", and an unparseable absolute request URI.
        // Default it to https so the scheme-less form the UI invites actually works.
        return HasScheme(normalized) ? normalized : $"https://{normalized}";
    }

    private static bool HasScheme(string url) => url.Contains("://", StringComparison.Ordinal);
}
