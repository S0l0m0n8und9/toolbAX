using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IAuthService"/>: mints an F&amp;O bearer token via FoToolbox's
/// <see cref="AuthService"/> + <see cref="MsalTokenProvider"/> (client credentials), resolving the
/// environment's service-principal secret from the DPAPI vault. Windows-only (DPAPI); the composition
/// root wires the in-memory fake elsewhere.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CoreAuthService : IAuthService
{
    private readonly ProfileService _profiles;
    private readonly SecretVaultService _vault;
    private readonly string _authorityBase;

    // Stable across calls (the SP is supplied per acquisition via the credential callback), so the
    // MSAL provider/auth object graph is reused rather than rebuilt each Test-connection.
    private MsalTokenProvider? _provider;
    private AuthService? _auth;

    public CoreAuthService(ProfileService profiles, SecretVaultService vault,
        string authorityBase = "https://login.microsoftonline.com")
    {
        _profiles = profiles;
        _vault = vault;
        _authorityBase = authorityBase;
    }

    public async Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.Tenant))
        {
            throw new InvalidOperationException("No tenant ID is configured for this environment.");
        }

        var sp = await _profiles.GetServicePrincipalAsync(env.Id, AuthTarget.Fo, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No F&O service principal is configured (set a client ID on the Auth tab).");
        if (string.IsNullOrEmpty(sp.SecretRef))
        {
            throw new InvalidOperationException("No client secret is stored for this environment.");
        }

        _provider ??= new MsalTokenProvider(_authorityBase, ResolveCredentialAsync);
        _auth ??= new AuthService(_provider);
        var foEnv = new FoEnvironment(env.Id, env.Name, env.Url, env.Tenant,
            string.IsNullOrWhiteSpace(env.Legal) ? null : env.Legal);

        return await _auth.AcquireTokenAsync(foEnv, sp, ct).ConfigureAwait(false);
    }

    public async Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.DataverseUrl))
        {
            throw new InvalidOperationException("No Dataverse URL is configured for this environment.");
        }

        if (string.IsNullOrWhiteSpace(env.Tenant))
        {
            throw new InvalidOperationException("No tenant ID is configured for this environment.");
        }

        var sp = await _profiles.GetServicePrincipalAsync(env.Id, AuthTarget.Dataverse, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No Dataverse service principal is configured (set a Dataverse client ID on the CE/Dataverse tab).");
        if (string.IsNullOrEmpty(sp.SecretRef))
        {
            throw new InvalidOperationException("No client secret is stored for the Dataverse app registration.");
        }

        _provider ??= new MsalTokenProvider(_authorityBase, ResolveCredentialAsync);
        _auth ??= new AuthService(_provider);

        // The Dataverse token is scoped to the (normalized) Dataverse resource, not F&O; the tenant is
        // shared with the F&O environment. The credential callback resolves THIS SP's secret.
        var resourceBaseUrl = ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(env.DataverseUrl);
        return await _auth.AcquireTokenAsync(resourceBaseUrl, env.Tenant, sp, ct).ConfigureAwait(false);
    }

    private async Task<ClientCredential> ResolveCredentialAsync(ServicePrincipal sp, CancellationToken ct)
    {
        if (sp.AuthMode == AuthMode.Certificate)
        {
            throw new NotSupportedException("Certificate auth is not yet wired in the Avalonia host; use a client secret.");
        }

        if (sp.AuthMode != AuthMode.ClientSecret)
        {
            throw new NotSupportedException($"Auth mode '{sp.AuthMode}' is not supported for the client-credentials flow.");
        }

        var secret = await _vault.ReadSecretAsync<string>(sp.SecretRef!, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The stored client secret could not be read.");

        return new ClientSecretCredential(secret);
    }
}
