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
        sb.AppendLine($"--{batchId}");
        sb.AppendLine($"Content-Type: multipart/mixed; boundary={changeSetId}");
        sb.AppendLine();

        var contentId = 1;
        foreach (var op in ops)
        {
            if (string.IsNullOrWhiteSpace(op.Url)) throw new ArgumentException("Operation Url is required.", nameof(operations));

            var pathAndQuery = ToPathAndQuery(baseUrl, op.Url);

            sb.AppendLine($"--{changeSetId}");
            sb.AppendLine("Content-Type: application/http");
            sb.AppendLine("Content-Transfer-Encoding: binary");
            sb.AppendLine($"Content-ID: {contentId++}");
            sb.AppendLine();

            // Use absolute path so servers that require /data/... can resolve correctly.
            sb.AppendLine($"{op.Method.Method.ToUpperInvariant()} {pathAndQuery} HTTP/1.1");

            var hasAccept = op.Headers is not null && op.Headers.Keys.Any(k => string.Equals(k, "Accept", StringComparison.OrdinalIgnoreCase));
            if (!hasAccept)
            {
                // Defaults that are safe for F&O OData.
                sb.AppendLine("Accept: application/json");
            }

            // Request headers
            if (op.Headers is not null)
            {
                foreach (var kvp in op.Headers)
                {
                    sb.AppendLine($"{kvp.Key}: {kvp.Value}");
                }
            }

            if (op.JsonBody is not null)
            {
                sb.AppendLine("Content-Type: application/json");
                sb.AppendLine();
                sb.AppendLine(op.JsonBody);
            }
            else
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine($"--{changeSetId}--");
        sb.AppendLine($"--{batchId}--");

        var batchUrl = $"{baseUrl.TrimEnd('/')}/data/$batch";
        var contentType = $"multipart/mixed; boundary={batchId}";
        return new ODataBatchBuildResult(batchUrl, contentType, sb.ToString());
    }

    private static string ToPathAndQuery(string baseUrl, string url)
    {
        // Accept both absolute and relative URLs. We always emit a leading '/' path.
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            return abs.PathAndQuery;
        }

        // If it's already a path like "/data/Foo", keep it.
        if (url.StartsWith("/", StringComparison.Ordinal))
        {
            return url;
        }

        // Try resolving against baseUrl; if that fails, treat as a relative path.
        if (Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, url, out var resolved))
        {
            return resolved.PathAndQuery;
        }

        return "/" + url;
    }
}
