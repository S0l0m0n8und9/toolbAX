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
/// Real <see cref="IAuthService"/>: acquires F&amp;O and Dataverse tokens via the shared
/// <see cref="AuthBroker"/> (which routes client-credentials through <see cref="AuthService"/> and
/// delegated-interactive through <see cref="MsalInteractiveTokenProvider"/>). Windows-only (DPAPI);
/// the composition root wires the in-memory fake elsewhere.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CoreAuthService : IAuthService
{
    private readonly ProfileService _profiles;
    private readonly SecretVaultService _vault;
    private readonly string _authorityBase;

    // Both objects are lazy and stable across calls; the shared Interactive instance means F&O,
    // Dataverse, and dual-write interactive flows all share the same MSAL token cache.
    private AuthBroker? _broker;
    private IInteractiveTokenProvider? _interactive;

    private AuthBroker Broker => _broker ??= new AuthBroker(_vault, Interactive, _authorityBase);
    private IInteractiveTokenProvider Interactive => _interactive ??= new MsalInteractiveTokenProvider();

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

        var resourceBase = ResourceUrlNormalizer.NormalizeFoBaseUrl(env.Url);

        // Interactive (MFA): a delegated browser sign-in (loopback), scoped to the F&O resource — no
        // app-only service principal / stored secret. Silent after the first sign-in (token cache).
        if (env.AuthMode == FoAuthMode.Interactive)
        {
            if (string.IsNullOrWhiteSpace(env.ClientId))
            {
                throw new InvalidOperationException("No F&O client ID is configured for interactive sign-in.");
            }

            var interactiveSp = new ServicePrincipal(
                $"interactive-fo-{env.Id}", env.Id, env.ClientId!, AuthMode.Interactive, null, null, AuthTarget.Fo);
            return await Broker.AcquireTokenAsync(
                new AuthTokenRequest(resourceBase, env.Tenant, interactiveSp, "F&O"), ct).ConfigureAwait(false);
        }

        var sp = await _profiles.GetServicePrincipalAsync(env.Id, AuthTarget.Fo, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No F&O service principal is configured (set a client ID on the FO Environment tab).");
        if (string.IsNullOrEmpty(sp.SecretRef))
        {
            throw new InvalidOperationException("No client secret is stored for this environment.");
        }

        return await Broker.AcquireTokenAsync(
            new AuthTokenRequest(resourceBase, env.Tenant, sp, "F&O"), ct).ConfigureAwait(false);
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

        var resourceBase = ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(env.DataverseUrl);

        // Interactive (MFA): delegated browser sign-in scoped to the (normalized) Dataverse resource.
        if (env.DataverseAuthMode == FoAuthMode.Interactive)
        {
            if (string.IsNullOrWhiteSpace(env.DataverseClientId))
            {
                throw new InvalidOperationException("No Dataverse client ID is configured for interactive sign-in.");
            }

            var interactiveSp = new ServicePrincipal(
                $"interactive-dv-{env.Id}", env.Id, env.DataverseClientId!, AuthMode.Interactive, null, null, AuthTarget.Dataverse);
            return await Broker.AcquireTokenAsync(
                new AuthTokenRequest(resourceBase, env.Tenant, interactiveSp, "Dataverse"), ct).ConfigureAwait(false);
        }

        var sp = await _profiles.GetServicePrincipalAsync(env.Id, AuthTarget.Dataverse, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No Dataverse service principal is configured (set a Dataverse client ID on the CE/Dataverse tab).");
        if (string.IsNullOrEmpty(sp.SecretRef))
        {
            throw new InvalidOperationException("No client secret is stored for the Dataverse app registration.");
        }

        // The Dataverse token is scoped to the (normalized) Dataverse resource, not F&O; the tenant is
        // shared with the F&O environment. The broker resolves THIS SP's secret from the vault.
        return await Broker.AcquireTokenAsync(
            new AuthTokenRequest(resourceBase, env.Tenant, sp, "Dataverse"), ct).ConfigureAwait(false);
    }

    public async Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.Tenant))
        {
            throw new InvalidOperationException("No tenant ID is configured for this environment.");
        }

        // The Data Integrator is a well-known first-party app — sign in with its client id by default
        // (the WPF/original tool never asks the user for one). An explicitly configured client id is
        // honored as an override.
        var clientId = string.IsNullOrWhiteSpace(env.DataIntegratorClientId)
            ? DualWriteAuthConstants.ClientId
            : env.DataIntegratorClientId;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("No Data Integrator client ID is configured for interactive sign-in.");
        }

        // Dual-write is always interactive; forward the injected authority so sovereign/GCC endpoints
        // apply here too. Uses the shared Interactive provider to share the MSAL token cache.
        var result = await Interactive
            .AcquireTokenAsync(
                new InteractiveTokenRequest(clientId, env.Tenant, DualWriteAuthConstants.ResourceBaseUrl, _authorityBase), ct)
            .ConfigureAwait(false);
        return result.AccessToken;
    }
}
