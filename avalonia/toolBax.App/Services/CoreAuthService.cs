using System;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

[assembly: InternalsVisibleTo("toolBax.App.Tests")]

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
    private readonly Func<string, AuthTarget, CancellationToken, Task<ServicePrincipal?>> _lookupPrincipal;
    private readonly Func<AuthTokenRequest, CancellationToken, Task<string>> _acquireToken;
    private readonly string _authorityBase;

    // Both objects are stable across calls; the shared Interactive instance means F&O,
    // Dataverse, and dual-write interactive flows all share the same MSAL token cache.
    // Eager init here eliminates a benign-but-real race when Acquire* is called from threadpool continuations.
    private readonly IInteractiveTokenProvider? _interactive;
    private readonly AuthBroker? _broker;

    public CoreAuthService(ProfileService profiles, SecretVaultService vault,
        string authorityBase = "https://login.microsoftonline.com")
    {
        _authorityBase = authorityBase;
        _interactive = new MsalInteractiveTokenProvider();
        _broker = new AuthBroker(vault, _interactive, _authorityBase);
        _lookupPrincipal = profiles.GetServicePrincipalAsync;
        _acquireToken = _broker.AcquireTokenAsync;
    }

    // Narrow deterministic boundary for principal-snapshot tests. No MSAL, vault or user cache is created.
    // Ancillary interactive/sign-out paths are intentionally unavailable through this constructor.
    internal CoreAuthService(
        Func<string, AuthTarget, CancellationToken, Task<ServicePrincipal?>> lookupPrincipal,
        Func<AuthTokenRequest, CancellationToken, Task<string>> acquireToken)
    {
        _lookupPrincipal = lookupPrincipal ?? throw new ArgumentNullException(nameof(lookupPrincipal));
        _acquireToken = acquireToken ?? throw new ArgumentNullException(nameof(acquireToken));
        _authorityBase = "https://login.microsoftonline.com";
    }

    public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default) =>
        AcquireFoTokenAsync(env, forceRefresh: false, ct);

    public async Task<string> AcquireFoTokenAsync(EnvProfile env, bool forceRefresh, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.Tenant))
        {
            throw new InvalidOperationException("No tenant ID is configured for this environment.");
        }

        if (string.IsNullOrWhiteSpace(env.Url))
        {
            throw new InvalidOperationException("No F&O environment URL is configured.");
        }

        EnsureSupportedAppMode(env.AuthMode, "F&O");

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
            return await _acquireToken(
                new AuthTokenRequest(resourceBase, env.Tenant, interactiveSp, "F&O", ForceRefresh: forceRefresh), ct).ConfigureAwait(false);
        }

        var sp = await _lookupPrincipal(env.Id, AuthTarget.Fo, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No F&O service principal is configured (set a client ID on the FO Environment tab).");
        ValidatePrincipalSnapshot(env, sp, AuthTarget.Fo);
        // Pre-check: surfaces the Avalonia-specific message immediately and keeps the broker's
        // FOTB_* env-var fallback narrowed to vault-read failures — don't remove as redundant.
        if (string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            throw new InvalidOperationException("No client secret is stored for this environment.");
        }

        return await _acquireToken(
            new AuthTokenRequest(resourceBase, env.Tenant, sp, "F&O", ForceRefresh: forceRefresh), ct).ConfigureAwait(false);
    }

    public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default) =>
        AcquireDataverseTokenAsync(env, forceRefresh: false, ct);

    public async Task<string> AcquireDataverseTokenAsync(EnvProfile env, bool forceRefresh, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.DataverseUrl))
        {
            throw new InvalidOperationException("No Dataverse URL is configured for this environment.");
        }

        if (string.IsNullOrWhiteSpace(env.Tenant))
        {
            throw new InvalidOperationException("No tenant ID is configured for this environment.");
        }


        EnsureSupportedAppMode(env.DataverseAuthMode, "Dataverse");

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
            return await _acquireToken(
                new AuthTokenRequest(resourceBase, env.Tenant, interactiveSp, "Dataverse", ForceRefresh: forceRefresh), ct).ConfigureAwait(false);
        }

        var sp = await _lookupPrincipal(env.Id, AuthTarget.Dataverse, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No Dataverse service principal is configured (set a Dataverse client ID on the CE/Dataverse tab).");
        ValidatePrincipalSnapshot(env, sp, AuthTarget.Dataverse);
        // Pre-check: surfaces the Avalonia-specific message immediately and keeps the broker's
        // FOTB_* env-var fallback narrowed to vault-read failures — don't remove as redundant.
        if (string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            throw new InvalidOperationException("No client secret is stored for the Dataverse app registration.");
        }

        // The Dataverse token is scoped to the (normalized) Dataverse resource, not F&O; the tenant is
        // shared with the F&O environment. The broker resolves THIS SP's secret from the vault.
        return await _acquireToken(
            new AuthTokenRequest(resourceBase, env.Tenant, sp, "Dataverse", ForceRefresh: forceRefresh), ct).ConfigureAwait(false);
    }

    private static void ValidatePrincipalSnapshot(EnvProfile env, ServicePrincipal principal, AuthTarget target)
    {
        var client = target == AuthTarget.Fo ? env.ClientId : env.DataverseClientId;
        var mode = target == AuthTarget.Fo ? env.AuthMode : env.DataverseAuthMode;
        var expectedMode = mode switch
        {
            FoAuthMode.ClientSecret => AuthMode.ClientSecret,
            FoAuthMode.Interactive => AuthMode.Interactive,
            _ => throw new InvalidOperationException("The captured profile has an unsupported authentication mode."),
        };

        if (!string.Equals(env.Id, principal.EnvId, StringComparison.Ordinal) || principal.Target != target
            || principal.AuthMode != expectedMode
            || !string.Equals(EnvironmentIdentity.NormalizeIdentifier(client),
                EnvironmentIdentity.NormalizeIdentifier(principal.ClientId), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The stored authentication settings changed. Retry using the current environment profile.");
        }
    }

    private static void EnsureSupportedAppMode(FoAuthMode mode, string target)
    {
        if (mode is not (FoAuthMode.Interactive or FoAuthMode.ClientSecret))
        {
            throw new InvalidOperationException(
                $"The saved {target} authentication mode is unsupported. Choose Interactive or Client secret in Profiles.");
        }
    }

    /// <summary>
    /// Evicts the cached delegated (interactive) sessions for this environment's F&amp;O and Dataverse
    /// client ids, so the next token acquisition forces a fresh sign-in. App-only (client-secret) tokens
    /// have no per-user cache to clear; MSAL refreshes those automatically.
    /// </summary>
    public async Task SignOutAsync(EnvProfile env, CancellationToken ct = default)
    {
        var broker = _broker ?? throw new InvalidOperationException("Sign-out is unavailable for injected token acquisition.");
        if (string.IsNullOrWhiteSpace(env.Tenant))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(env.ClientId))
        {
            await broker.SignOutInteractiveAsync(env.ClientId!, env.Tenant, ct).ConfigureAwait(false);
        }

        // The Dataverse app reg is keyed separately; only evict it when it differs from the F&O one.
        if (!string.IsNullOrWhiteSpace(env.DataverseClientId) &&
            !string.Equals(env.DataverseClientId, env.ClientId, StringComparison.OrdinalIgnoreCase))
        {
            await broker.SignOutInteractiveAsync(env.DataverseClientId!, env.Tenant, ct).ConfigureAwait(false);
        }
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
        var interactive = _interactive ?? throw new InvalidOperationException("Dual-write sign-in is unavailable for injected token acquisition.");
        var result = await interactive
            .AcquireTokenAsync(
                new InteractiveTokenRequest(clientId, env.Tenant, DualWriteAuthConstants.ResourceBaseUrl, _authorityBase), ct)
            .ConfigureAwait(false);
        return result.AccessToken;
    }
}
