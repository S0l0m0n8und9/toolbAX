using System;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Inputs for an interactive (delegated user) token acquisition. The resource base URL is the
/// API the token is for (e.g. the F&amp;O or Dataverse environment URL); the provider derives the
/// <c>{resource}/.default</c> scope and the <c>{authorityBase}/{tenantId}</c> authority from it.
/// When <paramref name="ForceRefresh"/> is set, a cached access token is bypassed and a fresh one is
/// fetched from the STS (used by "Test connection" so a green test proves live token acquisition).
/// </summary>
public sealed record InteractiveTokenRequest(
    string ClientId,
    string TenantId,
    string ResourceBaseUrl,
    string AuthorityBase = "https://login.microsoftonline.com",
    string RedirectUri = "http://localhost",
    bool ForceRefresh = false);

/// <summary>Result of an interactive sign-in: the access token and (when known) its expiry.</summary>
public sealed record InteractiveTokenResult(string AccessToken, DateTimeOffset? ExpiresOn);

/// <summary>
/// Acquires a delegated user access token through an interactive sign-in. The default
/// implementation (<see cref="MsalInteractiveTokenProvider"/>) drives MSAL's interactive flow;
/// tests substitute a fake.
/// </summary>
public interface IInteractiveTokenProvider
{
    Task<InteractiveTokenResult> AcquireTokenAsync(InteractiveTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts the cached delegated session (access + refresh tokens) for the given client/tenant, so the
    /// next acquisition requires a fresh interactive sign-in. Used by the per-profile "Sign out" action
    /// and when switching away from a profile, to stop a stale identity lingering in the MSAL cache.
    /// Default no-op so non-persistent fakes need not implement it.
    /// </summary>
    Task SignOutAsync(string clientId, string tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
