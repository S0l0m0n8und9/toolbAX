using System;

namespace FoToolbox.Core.Net;

/// <summary>
/// Guards against sending an environment-scoped bearer token to a foreign origin when a server supplies
/// an absolute URL to follow (e.g. an <c>@odata.nextLink</c>). Compares scheme, host AND port, so a
/// same-host scheme downgrade (https→http) or an alternate-port listener is refused too — not just a
/// different host. Centralised here so every paging/redirect follower applies the same check.
/// </summary>
public static class RequestOriginGuard
{
    /// <summary>
    /// True when <paramref name="candidate"/> has the same origin (scheme + host + port) as
    /// <paramref name="expectedBaseUrl"/>. A bare host in <paramref name="expectedBaseUrl"/> (no scheme)
    /// is treated as https. Returns false if either value is missing or not an absolute URI.
    /// </summary>
    public static bool IsSameOrigin(string? expectedBaseUrl, Uri? candidate)
    {
        if (candidate is null || !candidate.IsAbsoluteUri || string.IsNullOrWhiteSpace(expectedBaseUrl))
        {
            return false;
        }

        var normalized = expectedBaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? expectedBaseUrl
            : $"https://{expectedBaseUrl}";

        return Uri.TryCreate(normalized, UriKind.Absolute, out var expected) && IsSameOrigin(expected, candidate);
    }

    /// <summary>True when both URIs share scheme (case-insensitive), host (case-insensitive) and port.</summary>
    public static bool IsSameOrigin(Uri expected, Uri candidate)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(candidate);

        return string.Equals(expected.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(expected.Host, candidate.Host, StringComparison.OrdinalIgnoreCase)
            && expected.Port == candidate.Port;
    }
}
