using FoToolbox.Core.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace FoToolbox.Core.OData;

public sealed record ODataBatchOperation(
    HttpMethod Method,
    string Url,
    string? JsonBody = null,
    IReadOnlyDictionary<string, string>? Headers = null);

public sealed record ODataBatchBuildResult(
    string BatchUrl,
    string ContentType,
    string Body);

public static class ODataBatchBuilder
{
    public static ODataBatchBuildResult BuildWriteBatch(string baseUrl, IEnumerable<ODataBatchOperation> operations)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("BaseUrl is required.", nameof(baseUrl));
        if (operations is null) throw new ArgumentNullException(nameof(operations));

        var ops = operations.ToList();
        if (ops.Count == 0) throw new ArgumentException("At least one operation is required.", nameof(operations));

        var batchId = "batch_" + Guid.NewGuid().ToString("N");
        var changeSetId = "changeset_" + Guid.NewGuid().ToString("N");

        var sb = new StringBuilder();
        AppendCrlfLine(sb, $"--{batchId}");
        AppendCrlfLine(sb, $"Content-Type: multipart/mixed; boundary={changeSetId}");
        AppendCrlfLine(sb);

        var contentId = 1;
        foreach (var op in ops)
        {
            if (string.IsNullOrWhiteSpace(op.Url)) throw new ArgumentException("Operation Url is required.", nameof(operations));

            var pathAndQuery = ToPathAndQuery(baseUrl, op.Url, nameof(operations));

            AppendCrlfLine(sb, $"--{changeSetId}");
            AppendCrlfLine(sb, "Content-Type: application/http");
            AppendCrlfLine(sb, "Content-Transfer-Encoding: binary");
            AppendCrlfLine(sb, $"Content-ID: {contentId++}");
            AppendCrlfLine(sb);

            // Use absolute path so servers that require /data/... can resolve correctly.
            AppendCrlfLine(sb, $"{op.Method.Method.ToUpperInvariant()} {pathAndQuery} HTTP/1.1");

            var hasAccept = op.Headers is not null && op.Headers.Keys.Any(k => string.Equals(k, "Accept", StringComparison.OrdinalIgnoreCase));
            if (!hasAccept)
            {
                // Defaults that are safe for F&O OData.
                AppendCrlfLine(sb, "Accept: application/json");
            }

            // Request headers
            if (op.Headers is not null)
            {
                foreach (var kvp in op.Headers)
                {
                    AppendCrlfLine(sb, $"{kvp.Key}: {kvp.Value}");
                }
            }

            if (op.JsonBody is not null)
            {
                AppendCrlfLine(sb, "Content-Type: application/json");
                AppendCrlfLine(sb);
                AppendCrlfLine(sb, op.JsonBody);
            }
            else
            {
                AppendCrlfLine(sb);
            }
        }

        AppendCrlfLine(sb, $"--{changeSetId}--");
        AppendCrlfLine(sb, $"--{batchId}--");

        var batchUrl = $"{baseUrl.TrimEnd('/')}/data/$batch";
        var contentType = $"multipart/mixed; boundary={batchId}";
        return new ODataBatchBuildResult(batchUrl, contentType, sb.ToString());
    }

    /// <summary>
    /// MIME multipart is defined in terms of CRLF line breaks (RFC 2046 §5.1.1), so the batch body must
    /// use them regardless of host OS — <c>StringBuilder.AppendLine</c>/<c>Environment.NewLine</c> would
    /// emit a bare LF on Linux/macOS (including CI) and produce a batch the server cannot parse.
    /// </summary>
    private static void AppendCrlfLine(StringBuilder sb, string line = "") => sb.Append(line).Append("\r\n");

    private static string ToPathAndQuery(string baseUrl, string url, string paramName)
    {
        var baseUri = TryParseBaseUrl(baseUrl);

        // A "//host/path" reference is a network-path reference (RFC 3986 §4.2): it inherits the scheme
        // but carries its OWN authority, so it must be treated as an absolute URL rather than falling
        // through to the leading-'/' branch below and being emitted verbatim as the request target.
        // (HttpODataClient applies the same rule to @odata.nextLink.)
        var isNetworkPathReference = url.StartsWith("//", StringComparison.Ordinal);
        Uri? absolute = null;
        if (isNetworkPathReference)
        {
            if (baseUri is not null)
            {
                Uri.TryCreate(baseUri, url, out absolute);
            }
        }
        else
        {
            Uri.TryCreate(url, UriKind.Absolute, out absolute);
        }

        if (isNetworkPathReference || absolute is not null)
        {
            // An absolute operation URL keeps only its path here, so a foreign origin would be silently
            // discarded and the operation executed against the batch endpoint's environment under that
            // environment's bearer token. Same class of problem as a foreign @odata.nextLink, so it uses
            // the same guard (FoToolbox.Core.Net.RequestOriginGuard) — and refuses when the base URL
            // itself cannot be parsed, since then there is no origin to compare against.
            if (absolute is null || baseUri is null || !RequestOriginGuard.IsSameOrigin(baseUri, absolute))
            {
                var opOrigin = absolute is null ? url : absolute.GetLeftPart(UriPartial.Authority);
                var baseOrigin = baseUri is null ? baseUrl : baseUri.GetLeftPart(UriPartial.Authority);
                throw new ArgumentException(
                    $"Batch operation URL '{url}' targets origin '{opOrigin}', which is not the batch base URL origin " +
                    $"'{baseOrigin}'. A $batch request executes every operation against the base URL's environment " +
                    "under its token, so an operation URL from another environment cannot be sent here.",
                    paramName);
            }

            return absolute.PathAndQuery;
        }

        // If it's already a path like "/data/Foo", keep it.
        if (url.StartsWith("/", StringComparison.Ordinal))
        {
            return url;
        }

        // Try resolving against baseUrl; if that fails, treat as a relative path.
        if (baseUri is not null && Uri.TryCreate(baseUri, url, out var resolved))
        {
            return resolved.PathAndQuery;
        }

        return "/" + url;
    }

    /// <summary>
    /// Parses the batch base URL, mirroring <see cref="RequestOriginGuard"/>'s treatment of a bare host
    /// (no scheme) as https. Returns null when it is not usable as an absolute URL.
    /// </summary>
    private static Uri? TryParseBaseUrl(string baseUrl)
    {
        var normalized = baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"https://{baseUrl}";

        return Uri.TryCreate(normalized.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ? baseUri : null;
    }
}
