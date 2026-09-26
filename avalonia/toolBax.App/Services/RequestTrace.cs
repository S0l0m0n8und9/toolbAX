using System.Diagnostics;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Mirrors a failed HTTP request to <see cref="Trace"/> so the session log (#168) keeps it after the
/// status line that showed it has been replaced. Until this existed a failed request was reported in the
/// UI only, and closing the window took the evidence with it.
/// <para>
/// <b>This is the one place a failed request is allowed to say anything, so it is the one place to read
/// for what leaks.</b> A trace line carries only a finite API family, a finite verb and the numeric status.
/// Never the target, reason phrase, response/request body, or a header.
/// </para>
/// </summary>
internal static class RequestTrace
{
    /// <summary>
    /// Traces one non-success response. <paramref name="api"/> names the endpoint family ("F&amp;O",
    /// "Dataverse") so a log line says which of the two clients failed.
    /// </summary>
    internal static void Failure(string api, string method, string pathOrUrl, ODataResponse response)
    {
        _ = pathOrUrl;
        Trace.TraceWarning($"{ApiCategory(api)} request failed: status {response.StatusCode}; verb {Verb(method)}.");
    }

    private static string ApiCategory(string api) => api switch
    {
        "F&O" => "F&O",
        "Dataverse" => "Dataverse",
        _ => "Unknown API",
    };

    private static string Verb(string method)
    {
        return method switch
        {
            "GET" => "GET",
            "POST" => "POST",
            "PUT" => "PUT",
            "PATCH" => "PATCH",
            "DELETE" => "DELETE",
            "HEAD" => "HEAD",
            "OPTIONS" => "OPTIONS",
            _ => "OTHER",
        };
    }
}
