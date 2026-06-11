using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IConnectionTester"/>: forces a fresh token and probes the exact endpoint the tool
/// screens depend on — F&amp;O <c>/data/$metadata</c> and Dataverse <c>/WhoAmI</c> — so a green test
/// guarantees the tools can load, rather than merely confirming a token was minted.
/// </summary>
public sealed class CoreConnectionTester : IConnectionTester
{
    private readonly IAuthService _auth;
    private readonly HttpClient _http;

    public CoreConnectionTester(IAuthService auth, HttpClient? http = null)
    {
        _auth = auth;
        _http = http ?? new HttpClient();
    }

    public Task<ConnectionTestResult> TestFoAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.Url))
        {
            return Task.FromResult(new ConnectionTestResult(false, "No F&O environment URL is configured."));
        }

        var baseUrl = WithScheme(ResourceUrlNormalizer.NormalizeFoBaseUrl(env.Url));
        return ProbeAsync(() => _auth.AcquireFoTokenAsync(env, forceRefresh: true, ct),
            $"{baseUrl.TrimEnd('/')}/data/$metadata", "application/xml", "F&O metadata reachable.", ct);
    }

    public Task<ConnectionTestResult> TestDataverseAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.DataverseUrl))
        {
            return Task.FromResult(new ConnectionTestResult(false, "No Dataverse URL is configured for this environment."));
        }

        var apiBase = WithScheme(ResourceUrlNormalizer.BuildDataverseApiBaseUrl(env.DataverseUrl));
        return ProbeAsync(() => _auth.AcquireDataverseTokenAsync(env, forceRefresh: true, ct),
            $"{apiBase.TrimEnd('/')}/WhoAmI", "application/json", "Dataverse reachable (WhoAmI).", ct);
    }

    private async Task<ConnectionTestResult> ProbeAsync(
        Func<Task<string>> acquireToken, string url, string accept, string successMessage, CancellationToken ct)
    {
        string token;
        try
        {
            token = await acquireToken().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Token acquisition itself failed (bad client id/secret, cancelled sign-in, tenant mismatch…).
            return new ConnectionTestResult(false, ex.Message);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new ConnectionTestResult(true, successMessage)
                : new ConnectionTestResult(false, $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim());
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    // env URLs may be a bare host or a full URL; the probe needs an absolute https URL either way.
    private static string WithScheme(string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"https://{url}";
}
