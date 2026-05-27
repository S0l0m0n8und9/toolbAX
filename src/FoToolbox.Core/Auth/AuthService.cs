using FoToolbox.Core.Models;
using System;
using System.Text;
using System.Text.Json;
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

    public async Task<string> AcquireTokenAsync(string resourceBaseUrl, string tenantId, ServicePrincipal sp, CancellationToken cancellationToken = default)
    {
        var scope = $"{resourceBaseUrl.TrimEnd('/')}/.default";
        var request = new TokenRequest(scope, tenantId, sp);

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
    /// </summary>
    public static void ValidateTokenTenant(string token, string expectedTenantId)
    {
        if (string.IsNullOrWhiteSpace(expectedTenantId))
        {
            return;
        }

        if (!TryExtractTokenTenant(token, out var tokenTenantId))
        {
            return;
        }

        if (!string.Equals(tokenTenantId, expectedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new TenantMismatchException(expectedTenantId, tokenTenantId);
        }
    }

    private static bool TryExtractTokenTenant(string token, out string tenantId)
    {
        tenantId = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        try
        {
            var payloadJson = DecodeBase64UrlToUtf8String(parts[1]);
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("tid", out var tid) && tid.ValueKind == JsonValueKind.String)
            {
                tenantId = tid.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(tenantId);
            }
        }
        catch (FormatException) { }
        catch (JsonException) { }

        return false;
    }

    private static string DecodeBase64UrlToUtf8String(string base64Url)
    {
        var normalized = base64Url.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
        }

        var bytes = Convert.FromBase64String(normalized);
        return Encoding.UTF8.GetString(bytes);
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

public sealed record TokenRequest(string Scope, string TenantId, ServicePrincipal Principal);
