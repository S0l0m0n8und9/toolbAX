using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using FoToolbox.Core.DualWrite.Auth;
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
    private IInteractiveTokenProvider? _interactive;

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

        if (string.IsNullOrWhiteSpace(env.Url))
        {
            throw new InvalidOperationException("No F&O environment URL is configured.");
        }

        // Interactive (MFA): a delegated browser sign-in (loopback), scoped to the F&O resource — no
        // app-only service principal / stored secret. Silent after the first sign-in (token cache).
        if (env.AuthMode == FoAuthMode.Interactive)
        {
            return await AcquireInteractiveTokenAsync(
                env.ClientId, env.Tenant, ResourceUrlNormalizer.NormalizeFoBaseUrl(env.Url), "F&O", ct).ConfigureAwait(false);
        }

        var sp = await _profiles.GetServicePrincipalAsync(env.Id, AuthTarget.Fo, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No F&O service principal is configured (set a client ID on the FO Environment tab).");
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

        // Interactive (MFA): delegated browser sign-in scoped to the (normalized) Dataverse resource.
        if (env.DataverseAuthMode == FoAuthMode.Interactive)
        {
            return await AcquireInteractiveTokenAsync(
                env.DataverseClientId, env.Tenant, ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(env.DataverseUrl), "Dataverse", ct).ConfigureAwait(false);
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

    public async Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.DataIntegratorClientId))
        {
            throw new InvalidOperationException("No Data Integrator client ID is configured (set one on the Data Integrator tab).");
        }

        if (string.IsNullOrWhiteSpace(env.Tenant))
        {
            throw new InvalidOperationException("No tenant ID is configured for this environment.");
        }

        return await AcquireInteractiveTokenAsync(
            env.DataIntegratorClientId, env.Tenant, DualWriteAuthConstants.ResourceBaseUrl, "Data Integrator", ct).ConfigureAwait(false);
    }

    // Delegated (interactive) token via the loopback MSAL provider — silent after a prior sign-in.
    // Used by the Interactive auth mode (F&O / Dataverse) and the Data Integrator gateway.
    private async Task<string> AcquireInteractiveTokenAsync(string? clientId, string tenant, string resourceBaseUrl, string label, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException($"No {label} client ID is configured for interactive sign-in.");
        }

        _interactive ??= new MsalInteractiveTokenProvider();
        var result = await _interactive
            .AcquireTokenAsync(new InteractiveTokenRequest(clientId, tenant, resourceBaseUrl), ct)
            .ConfigureAwait(false);
        return result.AccessToken;
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
