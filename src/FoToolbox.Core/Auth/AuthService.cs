using FoToolbox.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Acquires tokens for F&O using pluggable token providers (MSAL by default).
/// </summary>
public sealed class AuthService
{
    private readonly ITokenProvider _tokenProvider;
    private readonly string _serviceName;
    private readonly Action<AuthRecoveryException>? _interactiveFallback;

    public AuthService(ITokenProvider tokenProvider, string serviceName = "service", Action<AuthRecoveryException>? interactiveFallback = null)
    {
        _tokenProvider = tokenProvider;
        _serviceName = string.IsNullOrWhiteSpace(serviceName) ? "service" : serviceName;
        _interactiveFallback = interactiveFallback;
    }

    public async Task<string> AcquireTokenAsync(FoEnvironment env, ServicePrincipal sp, CancellationToken cancellationToken = default)
    {
        var resourceBaseUrl = ResourceUrlNormalizer.NormalizeFoBaseUrl(env.BaseUrl);
        return await AcquireTokenAsync(resourceBaseUrl, env.TenantId, sp, cancellationToken);
    }

    public async Task<string> AcquireTokenAsync(string resourceBaseUrl, string tenantId, ServicePrincipal sp, CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        var scope = $"{resourceBaseUrl.TrimEnd('/')}/.default";
        var request = new TokenRequest(scope, tenantId, sp, forceRefresh);

        var attempts = 0;
        Exception? last = null;
        while (attempts < 3)
        {
            attempts++;
            try
            {
                var token = await _tokenProvider.GetTokenAsync(request, cancellationToken);
                ValidateTokenTenant(token, tenantId);
                return token;
            }
            catch (TenantMismatchException)
            {
                // Tenant mismatch is a hard failure — surface immediately, never retry.
                throw;
            }
            catch (Exception ex) when (attempts < 3)
            {
                last = ex;
                await Task.Delay(200 * attempts, cancellationToken);
            }
            catch (Exception ex)
            {
                last = ex;
                break;
            }
        }

        throw BuildFailure(last);
    }

    /// <summary>
    /// Validates that the `tid` claim inside the acquired JWT matches the configured tenant.
    /// Rejects with a <see cref="TenantMismatchException"/> before the token is handed to any caller,
    /// so cross-tenant misroutes never reach an API call.
    /// <para>
    /// Only applies to a GUID-shaped configured tenant — see the comment on the GUID check for why the
    /// domain / meta-tenant forms are enforced by the authority instead.
    /// </para>
    /// </summary>
    public static void ValidateTokenTenant(string token, string expectedTenantId)
    {
        if (string.IsNullOrWhiteSpace(expectedTenantId))
        {
            return;
        }

        // A token's `tid` claim is ALWAYS a tenant GUID, but the configured tenant is often not: the
        // domain form ("contoso.onmicrosoft.com") and the meta-tenants ("common", "organizations") are
        // all valid here. Comparing those to a GUID can never match, so sign-in succeeded and then this
        // check rejected it with a non-retryable TenantMismatchException telling the user to fix a
        // tenant that isn't wrong.
        //
        // Skip the equality check for a non-GUID tenant: the MSAL authority is built FROM that tenant
        // (https://login.microsoftonline.com/{tenant}), so the STS already resolved and enforced it at
        // token issuance — a token issued by that authority cannot belong to a different directory.
        // A GUID-shaped tenant is the only case we can compare claim-to-claim, and it stays strict.
        if (!Guid.TryParse(expectedTenantId, out var expectedTenant))
        {
            return;
        }

        if (!JwtInspector.TryGetTenantId(token, out var tokenTenantId))
        {
            return;
        }

        // Compare parsed GUID VALUES, not their spellings. Guid.TryParse accepts the dashless ("N"),
        // braced ("B") and parenthesised ("P") forms, but a `tid` claim is always the canonical dashed
        // ("D") form — so a tenant typed in any other valid spelling passed the GUID gate above and was
        // then reported as a cross-tenant misroute against its own directory. Same false "wrong tenant"
        // after a successful sign-in as the domain-form case, one layer narrower.
        var mismatch = Guid.TryParse(tokenTenantId, out var tokenTenant)
            ? tokenTenant != expectedTenant
            // A `tid` that is not GUID-shaped cannot be value-compared. AAD always issues one, so this
            // is not a real shape; keep the original strict string comparison rather than tolerating an
            // uninterpretable claim against a tenant we do know precisely.
            : !string.Equals(tokenTenantId, expectedTenantId, StringComparison.OrdinalIgnoreCase);

        if (mismatch)
        {
            throw new TenantMismatchException(expectedTenantId, tokenTenantId);
        }
    }

    private Exception BuildFailure(Exception? failure)
    {
        if (failure is AuthRecoveryException)
        {
            return failure;
        }

        if (failure is MsalUiRequiredException or MsalClaimsChallengeException)
        {
            return CreateReauthException(failure);
        }

        if (failure is MsalServiceException msalFailure && RequiresInteractiveReauth(msalFailure))
        {
            return CreateReauthException(failure);
        }

        return failure ?? new InvalidOperationException("Token acquisition failed.");
    }

    private AuthRecoveryException CreateReauthException(Exception failure)
    {
        var recovery = new AuthRecoveryException(
            _serviceName,
            $"{_serviceName} authentication needs to be refreshed. The host will switch to Profiles so you can complete interactive sign-in for this environment, then save and re-apply the profile.",
            requiresInteractiveReauth: true,
            failure);

        _interactiveFallback?.Invoke(recovery);
        return recovery;
    }

    private static bool RequiresInteractiveReauth(MsalServiceException failure)
    {
        if (string.Equals(failure.ErrorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(failure.ErrorCode, "interaction_required", StringComparison.OrdinalIgnoreCase)
            || string.Equals(failure.ErrorCode, "invalid_client", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return failure.Message.Contains("refresh token", StringComparison.OrdinalIgnoreCase)
            || failure.Message.Contains("user interaction", StringComparison.OrdinalIgnoreCase)
            || failure.Message.Contains("reauth", StringComparison.OrdinalIgnoreCase)
            || failure.Message.Contains("sign in again", StringComparison.OrdinalIgnoreCase);
    }
}

public interface ITokenProvider
{
    Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default);
}

public sealed record TokenRequest(string Scope, string TenantId, ServicePrincipal Principal, bool ForceRefresh = false);
