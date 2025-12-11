using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Host;

/// <summary>
/// Simple delegating handler that injects a bearer token using AuthService.
/// </summary>
internal sealed class AuthenticatedHandler : DelegatingHandler
{
    private readonly FoEnvironment _env;
    private readonly ServicePrincipal _sp;
    private readonly AuthService _auth;

    public AuthenticatedHandler(FoEnvironment env, ServicePrincipal sp)
        : base(new HttpClientHandler())
    {
        _env = env;
        _sp = sp;
        _auth = new AuthService(BuildTokenProvider());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _auth.AcquireTokenAsync(_env, _sp, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }

    private ITokenProvider BuildTokenProvider()
    {
        var authorityBase = "https://login.microsoftonline.com";
        return new MsalTokenProvider(authorityBase, ResolveCredential);
    }

    private ClientCredential ResolveCredential(ServicePrincipal sp)
    {
        // Prefer DPAPI vault secret for this service principal.
        var dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "profile.db");
        var store = new ProfileStore(dbPath);
        var vault = new SecretVaultService(store.ConnectionString);
        if (!string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            var secretPayload = vault.ReadSecretAsync<SecretPayload>(sp.SecretRef).GetAwaiter().GetResult();
            if (secretPayload is not null && !string.IsNullOrWhiteSpace(secretPayload.Value))
            {
                return new ClientSecretCredential(secretPayload.Value);
            }
        }

        var secret = Environment.GetEnvironmentVariable("FOTB_CLIENT_SECRET");
        if (!string.IsNullOrWhiteSpace(secret))
        {
            return new ClientSecretCredential(secret);
        }

        // Fallback: no secret, return dummy to avoid null.
        return new ClientSecretCredential("dummy");
    }

    private sealed class SecretPayload
    {
        public string? Value { get; set; }
    }
}
