using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using FoToolbox.Core.Net;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDataverseClient"/>: issues an authenticated GET against the active environment's
/// Dataverse Web API (<c>{dataverse}/api/data/v9.2</c>) with a bearer token from
/// <see cref="IAuthService.AcquireDataverseTokenAsync"/>. Failures (no active env, no Dataverse URL,
/// auth error, HTTP error) are returned as a non-2xx <see cref="ODataResponse"/> rather than thrown,
/// so the Map Browser can surface them in a status banner.
/// </summary>
public sealed class CoreDataverseClient : IDataverseClient, IDisposable
{
    private readonly IAuthService _auth;
    private readonly Func<EnvProfile?> _activeEnv;
    private readonly HttpClient _http;
    // We own (and therefore must dispose) the HttpClient only when we allocated it; an injected one
    // belongs to the caller.
    private readonly bool _ownsHttp;

    public CoreDataverseClient(IAuthService auth, Func<EnvProfile?> activeEnv, HttpClient? http = null)
    {
        _auth = auth;
        _activeEnv = activeEnv;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
    }

    public async Task<ODataResponse> GetAsync(string pathOrUrl, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var env = _activeEnv();
        if (env is null)
        {
            return new ODataResponse(0, "No active environment", "Select an environment first.", (int)sw.ElapsedMilliseconds);
        }

        if (string.IsNullOrWhiteSpace(env.DataverseUrl))
        {
            return new ODataResponse(0, "No Dataverse URL",
                "Configure a Dataverse URL on the CE/Dataverse tab for this environment.", (int)sw.ElapsedMilliseconds);
        }

        var uri = BuildUri(env.DataverseUrl, pathOrUrl);

        // A server-driven @odata.nextLink is used verbatim, but only if it stays on the Dataverse
        // environment's origin (scheme + host + port) — otherwise the env-scoped Dataverse bearer would
        // be sent to a foreign origin. Refuse before acquiring a token.
        if (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && !RequestOriginGuard.IsSameOrigin(env.DataverseUrl, uri))
        {
            return new ODataResponse(0, "Refused",
                "The paging link points to a different origin than the Dataverse environment.", (int)sw.ElapsedMilliseconds);
        }

        string token;
        try
        {
            token = await _auth.AcquireDataverseTokenAsync(env, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ODataResponse(401, "Unauthorized", ex.Message, (int)sw.ElapsedMilliseconds);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Ask Dataverse to inline formatted (display) values for option-set / lookup columns
            // (statecode, statuscode, ownerid) so the parser can show human-readable text, and the
            // total-record-count annotations so a $count=true response says whether it hit the 5,000-row
            // cap instead of silently reporting the ceiling as a total. One header, comma-separated list.
            request.Headers.Add("Prefer",
                "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue," +
                $"{DualWriteMapParser.CountAnnotations}\"");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            sw.Stop();
            return new ODataResponse((int)response.StatusCode, response.ReasonPhrase ?? string.Empty, responseBody, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return new ODataResponse(0, "Request failed", ex.Message, (int)sw.ElapsedMilliseconds);
        }
    }

    // An absolute URL (a server-driven @odata.nextLink) is used verbatim; a relative path is resolved
    // against the normalized Dataverse Web API base ({dataverse}/api/data/v9.2).
    private static Uri BuildUri(string dataverseUrl, string pathOrUrl)
    {
        if (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(pathOrUrl);
        }

        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(dataverseUrl);

        // Same scheme repair CoreODataClient.BuildUri already does for env.Url: a scheme-less Dataverse
        // URL ("org.crm.dynamics.com") would otherwise throw UriFormatException here, so the F&O tools
        // worked and the Dataverse ones didn't for identically-typed input. The normalizer now defaults
        // the scheme too; keeping the repair local means this can't regress on that alone.
        if (!apiBase.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            apiBase = $"https://{apiBase}";
        }

        return new Uri($"{apiBase}/{pathOrUrl.TrimStart('/')}");
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
