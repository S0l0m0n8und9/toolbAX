using System;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Inputs for an interactive (delegated user) token acquisition. The resource base URL is the
/// API the token is for (e.g. the F&amp;O or Dataverse environment URL); the provider derives the
/// <c>{resource}/.default</c> scope and the <c>{authorityBase}/{tenantId}</c> authority from it.
/// </summary>
public sealed record InteractiveTokenRequest(
    string ClientId,
    string TenantId,
    string ResourceBaseUrl,
    string AuthorityBase = "https://login.microsoftonline.com",
    string RedirectUri = "http://localhost");

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
}
