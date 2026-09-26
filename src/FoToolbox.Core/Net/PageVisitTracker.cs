using System;
using System.Collections.Generic;
using System.Net.Http;

namespace FoToolbox.Core.Net;

/// <summary>Tracks the actual HTTP request targets visited by one paging traversal.</summary>
public sealed class PageVisitTracker
{
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves <paramref name="requestTarget"/> as <see cref="HttpClient"/> would and records its
    /// case-sensitive HTTP request URL. Returns false when that target has already been visited.
    /// </summary>
    public bool TryVisit(string requestTarget, Uri? baseAddress, out Uri? resolvedRequestUri)
    {
        resolvedRequestUri = ResolveRequestUri(requestTarget, baseAddress);
        var key = resolvedRequestUri is null
            ? requestTarget
            : resolvedRequestUri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
        return _visited.Add(key);
    }

    /// <summary>Resolves only absolute HTTP(S) request targets; invalid relatives retain legacy handling.</summary>
    public static Uri? ResolveRequestUri(string requestTarget, Uri? baseAddress)
    {
        if (baseAddress is not null && baseAddress.IsAbsoluteUri &&
            Uri.TryCreate(baseAddress, requestTarget, out var resolved) && IsHttp(resolved))
        {
            return resolved;
        }

        return Uri.TryCreate(requestTarget, UriKind.Absolute, out var absolute) && IsHttp(absolute)
            ? absolute
            : null;
    }

    private static bool IsHttp(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
