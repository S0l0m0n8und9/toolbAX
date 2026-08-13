using System;
using System.Diagnostics;
using System.Text;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Mirrors a failed HTTP request to <see cref="Trace"/> so the session log (#168) keeps it after the
/// status line that showed it has been replaced. Until this existed a failed request was reported in the
/// UI only, and closing the window took the evidence with it.
/// <para>
/// <b>This is the one place a failed request is allowed to say anything, so it is the one place to read
/// for what leaks.</b> A trace line carries the status code, the reason phrase, the verb and the endpoint
/// path — and nothing else. Never the response body, never the request body, never a header (the
/// <c>Authorization</c> bearer lives in one), never the host (it names the customer's environment) and
/// never the query string (a <c>$filter</c> carries business data and a <c>$skiptoken</c> is opaque
/// session state).
/// </para>
/// </summary>
internal static class RequestTrace
{
    /// <summary>
    /// Traces one non-success response. <paramref name="api"/> names the endpoint family ("F&amp;O",
    /// "Dataverse") so a log line says which of the two clients failed.
    /// </summary>
    internal static void Failure(string api, string method, string pathOrUrl, ODataResponse response) =>
        Trace.TraceWarning($"{api} request failed: {response.StatusCode} {Clean(response.ReasonPhrase)} · " +
            $"{Clean(method)} {Endpoint(pathOrUrl)}");

    /// <summary>
    /// The endpoint path only: the query string is dropped, and an absolute URL (a server-driven
    /// <c>@odata.nextLink</c>) is reduced to its path so the environment host never reaches the file.
    /// </summary>
    private static string Endpoint(string pathOrUrl)
    {
        var query = pathOrUrl.IndexOf('?', StringComparison.Ordinal);
        var withoutQuery = query < 0 ? pathOrUrl : pathOrUrl[..query];

        return Uri.TryCreate(withoutQuery, UriKind.Absolute, out var absolute)
            ? Clean(absolute.AbsolutePath)
            : Clean(withoutQuery);
    }

    // Strips control characters so a pasted multi-line path (the POST Builder's path is a free-text box)
    // cannot forge extra lines in the log file.
    private static string Clean(string value)
    {
        var trimmed = value.Trim();
        var cleaned = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (!char.IsControl(c))
            {
                cleaned.Append(c);
            }
        }

        return cleaned.ToString();
    }
}
