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

    private static string NormalizeRoot(string url)
    {
        if (url is null)
        {
            return string.Empty;
        }

        return url.Trim().TrimEnd('/');
    }
}
