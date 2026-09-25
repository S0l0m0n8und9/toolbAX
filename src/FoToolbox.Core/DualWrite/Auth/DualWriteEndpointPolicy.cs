using System;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Defines the URI trust boundary for the commercial Dual-write gateway and Entra token endpoint.
/// Matching is component based so credentials are never authorized by text appearing in a path,
/// query or lookalike hostname.
/// </summary>
public static class DualWriteEndpointPolicy
{
    private static readonly string[] GatewaySuffixLabels =
        ["gateway", "prod", "island", "powerapps", "com"];

    private const string GatewayFirstLabel = "projectmanagementservice";
    private const string GatewayApiPrefix = "/api/DualWriteManagement/";

    /// <summary>Validates and canonicalizes a configured gateway base URL to its HTTPS origin.</summary>
    public static Uri RequireGatewayBase(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            !Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri) ||
            !TryGetGatewayOrigin(uri, out var origin) ||
            !IsRootPath(uri) ||
            !string.IsNullOrEmpty(uri.Query))
        {
            throw new InvalidOperationException("The dual-write gateway URL is not a trusted commercial gateway root.");
        }

        return origin;
    }

    /// <summary>
    /// Returns the canonical origin when a request URI belongs to the trusted commercial gateway host family.
    /// Request paths and queries are allowed; user information and fragments are not.
    /// </summary>
    public static bool TryGetGatewayOrigin(Uri? candidate, out Uri origin)
    {
        origin = null!;
        if (candidate is null || !candidate.IsAbsoluteUri ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            candidate.Port != 443 ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Fragment))
        {
            return false;
        }

        string host;
        try
        {
            host = candidate.IdnHost.ToLowerInvariant();
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (!IsTrustedGatewayHost(host))
        {
            return false;
        }

        origin = new Uri($"https://{host}/", UriKind.Absolute);
        return true;
    }

    /// <summary>True only for the management API path on a trusted gateway origin.</summary>
    public static bool IsGatewayApiRequest(Uri? candidate)
    {
        if (!TryGetGatewayOrigin(candidate, out _))
        {
            return false;
        }

        var path = candidate!.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        if (ContainsEncodedSeparator(path))
        {
            return false;
        }

        return ("/" + path).StartsWith(GatewayApiPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True only for an exact Entra v2 token endpoint URI.</summary>
    public static bool IsTokenEndpoint(Uri? candidate)
    {
        if (candidate is null || !candidate.IsAbsoluteUri ||
            candidate.OriginalString.Contains('\\') ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            candidate.Port != 443 ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Fragment) ||
            !(string.Equals(candidate.IdnHost, "login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(candidate.IdnHost, "login.microsoft.com", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var path = candidate.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        if (ContainsEncodedSeparator(path))
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Length != 4 ||
            !string.Equals(segments[1], "oauth2", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[2], "v2.0", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[3], "token", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tenant = Uri.UnescapeDataString(segments[0]);
        return !string.IsNullOrWhiteSpace(tenant) &&
               tenant.IndexOfAny(['/', '\\']) < 0;
    }

    /// <summary>True when a request is absolute and stays on the validated gateway origin.</summary>
    public static bool IsSameOrigin(Uri trustedGatewayOrigin, Uri? requestUri)
    {
        ArgumentNullException.ThrowIfNull(trustedGatewayOrigin);
        return requestUri is not null && requestUri.IsAbsoluteUri &&
               string.IsNullOrEmpty(requestUri.UserInfo) &&
               string.IsNullOrEmpty(requestUri.Fragment) &&
               string.Equals(trustedGatewayOrigin.Scheme, requestUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(trustedGatewayOrigin.IdnHost, requestUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               trustedGatewayOrigin.Port == requestUri.Port;
    }

    private static bool IsTrustedGatewayHost(string host)
    {
        var labels = host.Split('.', StringSplitOptions.None);
        if (labels.Length < GatewaySuffixLabels.Length + 1 ||
            !string.Equals(labels[0], GatewayFirstLabel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffixStart = labels.Length - GatewaySuffixLabels.Length;
        for (var index = 0; index < GatewaySuffixLabels.Length; index++)
        {
            if (!string.Equals(labels[suffixStart + index], GatewaySuffixLabels[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        for (var index = 1; index < suffixStart; index++)
        {
            if (string.IsNullOrEmpty(labels[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRootPath(Uri uri)
    {
        var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        return string.IsNullOrEmpty(path);
    }

    private static bool ContainsEncodedSeparator(string path) =>
        path.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("%5c", StringComparison.OrdinalIgnoreCase) ||
        path.Contains('\\');
}
