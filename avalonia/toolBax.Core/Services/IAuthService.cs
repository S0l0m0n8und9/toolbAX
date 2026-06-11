using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Acquires bearer tokens for an environment using its stored service principals (client credentials):
/// the F&amp;O app reg for OData/metadata, and the separate Dataverse app reg for the Dataverse Web API
/// (Dual-Write). Behind an interface so view-models can drive a "Test connection" and authenticated
/// calls against a fake without hitting Entra. Throws on failure (no token).
/// </summary>
public interface IAuthService
{
    Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default);

    Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default);

    /// <summary>
    /// Acquires the delegated Data Integrator token for the dual-write gateway (interactive, via the
    /// env's Data Integrator client id — silent after a prior sign-in). Distinct from the app-only F&amp;O
    /// / Dataverse tokens.
    /// </summary>
    Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default);

    /// <summary>
    /// As <see cref="AcquireFoTokenAsync(EnvProfile, CancellationToken)"/>, but <paramref name="forceRefresh"/>
    /// bypasses any cached access token (refreshing from the STS). Used by "Test connection" so a pass
    /// proves a live token, not just a cached one. Defaults to the cached path for callers that don't care.
    /// </summary>
    Task<string> AcquireFoTokenAsync(EnvProfile env, bool forceRefresh, CancellationToken ct = default)
        => AcquireFoTokenAsync(env, ct);

    /// <summary>As <see cref="AcquireDataverseTokenAsync(EnvProfile, CancellationToken)"/> with a force-refresh option.</summary>
    Task<string> AcquireDataverseTokenAsync(EnvProfile env, bool forceRefresh, CancellationToken ct = default)
        => AcquireDataverseTokenAsync(env, ct);

    /// <summary>
    /// Evicts any cached delegated (interactive) sessions for the profile so the next acquisition forces a
    /// fresh sign-in — the per-profile "Sign out" action. App-only tokens are unaffected. Default no-op so
    /// fakes need not implement it.
    /// </summary>
    Task SignOutAsync(EnvProfile env, CancellationToken ct = default) => Task.CompletedTask;
}
