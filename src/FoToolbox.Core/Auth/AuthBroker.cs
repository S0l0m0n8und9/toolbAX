using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using Microsoft.Identity.Client;
using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Inputs for one token acquisition. Pending* values let the Profiles "Test connection" path test
/// credentials the user has typed but not yet saved — the live path leaves them null.
/// </summary>
public sealed record AuthTokenRequest(
    string ResourceBaseUrl,
    string TenantId,
    ServicePrincipal Principal,
    string ServiceName = "service",
    string? PendingClientSecret = null,
    string? PendingBearerToken = null)
{
    /// <summary>Synthesized record printing would leak Pending* secrets; print only safe members.</summary>
    public override string ToString() => $"AuthTokenRequest({ServiceName}, {Principal.AuthMode}, {ResourceBaseUrl})";
}

/// <summary>
/// The single token-acquisition pipeline. Routes by <see cref="ServicePrincipal.AuthMode"/>:
/// Interactive → delegated MSAL (silent-first, browser fallback);
/// ClientSecret → client-credentials via <see cref="AuthService"/> (Certificate mode is accepted
/// but not yet implemented and fails with an actionable error); BearerToken → vault/env-var resolution.
/// Both the live request path and "Test connection" must call this so they can never diverge.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AuthBroker
{
    private readonly SecretVaultService _vault;
    private readonly IInteractiveTokenProvider _interactive;
    private readonly string _authorityBase;
    private readonly Action<AuthRecoveryException>? _interactiveFallback;
    private readonly MsalTokenProvider _clientCredentialProvider;
    private readonly SemaphoreSlim _interactiveGate = new(1, 1);

    public AuthBroker(
        SecretVaultService vault,
        IInteractiveTokenProvider? interactiveProvider = null,
        string authorityBase = "https://login.microsoftonline.com",
        Action<AuthRecoveryException>? interactiveFallback = null)
    {
        _vault = vault;
        _interactive = interactiveProvider ?? new MsalInteractiveTokenProvider();
        _authorityBase = authorityBase.TrimEnd('/');
        _interactiveFallback = interactiveFallback;
        _clientCredentialProvider = new MsalTokenProvider(_authorityBase, ResolveStoredCredentialAsync);
    }

    public Task<string> AcquireTokenAsync(AuthTokenRequest request, CancellationToken cancellationToken = default) =>
        request.Principal.AuthMode switch
        {
            AuthMode.Interactive => AcquireInteractiveAsync(request, cancellationToken),
            AuthMode.BearerToken => ResolveBearerAsync(request, cancellationToken),
            AuthMode.ClientSecret or AuthMode.Certificate => AcquireClientCredentialAsync(request, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported auth mode '{request.Principal.AuthMode}'."),
        };

    private async Task<string> AcquireInteractiveAsync(AuthTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Principal.ClientId))
        {
            throw new InvalidOperationException(
                $"No client ID is configured for interactive sign-in to {request.ServiceName}. Set a public-client Application (client) ID in Profiles.");
        }

        // Serialize interactive acquisitions: concurrent requests (e.g. several plugins loading at
        // once) must not each open a browser. The first acquisition populates the MSAL cache; the
        // rest then complete silently.
        // Liveness contract: this gate releases only when the in-flight interactive sign-in completes
        // or the cancellation token fires. Callers must pass a cancellable token (e.g. an HttpClient
        // timeout CancellationToken) so that a queued waiter cannot block indefinitely behind a
        // stalled browser sign-in.
        await _interactiveGate.WaitAsync(cancellationToken);
        try
        {
            InteractiveTokenResult result;
            try
            {
                result = await _interactive.AcquireTokenAsync(
                    new InteractiveTokenRequest(request.Principal.ClientId, request.TenantId, request.ResourceBaseUrl, _authorityBase),
                    cancellationToken);
            }
            catch (MsalException ex)
            {
                var message = InteractiveSignInError.Describe(ex) ?? ex.Message;
                var recovery = new AuthRecoveryException(request.ServiceName, message, requiresInteractiveReauth: true, ex);
                _interactiveFallback?.Invoke(recovery);
                throw recovery;
            }
            // TenantMismatchException is not MsalException — it propagates as-is.
            AuthService.ValidateTokenTenant(result.AccessToken, request.TenantId);
            return result.AccessToken;
        }
        finally
        {
            _interactiveGate.Release();
        }
    }

    private async Task<string> AcquireClientCredentialAsync(AuthTokenRequest request, CancellationToken cancellationToken)
    {
        // A pending (typed-but-unsaved) secret short-circuits stored resolution: that is what the
        // Test button must exercise. The transient provider is fine here — test calls are rare.
        ITokenProvider provider = string.IsNullOrWhiteSpace(request.PendingClientSecret)
            ? _clientCredentialProvider
            : new MsalTokenProvider(_authorityBase, (_, _) =>
                Task.FromResult<ClientCredential>(new ClientSecretCredential(request.PendingClientSecret!)));

        var auth = new AuthService(provider, request.ServiceName, _interactiveFallback);
        return await auth.AcquireTokenAsync(request.ResourceBaseUrl, request.TenantId, request.Principal, cancellationToken);
    }

    private async Task<ClientCredential> ResolveStoredCredentialAsync(ServicePrincipal sp, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            var secret = await VaultSecretReader.ReadClientSecretAsync(_vault, sp.SecretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(secret))
            {
                return new ClientSecretCredential(secret);
            }
        }

        var envVar = ClientSecretEnvVar(sp.Target);
        var fromEnv = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return new ClientSecretCredential(fromEnv);
        }

        if (sp.AuthMode == AuthMode.Certificate)
        {
            throw new InvalidOperationException(
                "Certificate authentication is not yet supported by the unified auth pipeline. " +
                "Configure a client secret or switch the profile to Interactive.");
        }

        throw new InvalidOperationException(
            $"No client secret configured for this profile. Set it in Profiles and Save, or set {envVar}.");
    }

    private async Task<string> ResolveBearerAsync(AuthTokenRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.PendingBearerToken))
        {
            return ValidateBearer(BearerTokenText.Normalize(request.PendingBearerToken), "Pending bearer token");
        }

        if (!string.IsNullOrWhiteSpace(request.Principal.SecretRef))
        {
            var payload = await VaultSecretReader.ReadBearerTokenAsync(_vault, request.Principal.SecretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload?.AccessToken))
            {
                return ValidateBearer(BearerTokenText.Normalize(payload.AccessToken), "Bearer token");
            }
        }

        var envVar = BearerTokenEnvVar(request.Principal.Target);
        var fromEnv = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return ValidateBearer(BearerTokenText.Normalize(fromEnv), envVar);
        }

        throw new InvalidOperationException(
            $"No bearer token configured for this profile. Paste a token in Profiles and Save, or set {envVar}.");
    }

    private static string ValidateBearer(string token, string sourceLabel)
    {
        if (JwtInspector.TryGetExpiryUtc(token, out var expiryUtc) && expiryUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException($"{sourceLabel} expired at {expiryUtc:u}. Update it in Profiles.");
        }
        return token;
    }

    internal static string ClientSecretEnvVar(AuthTarget target) =>
        target == AuthTarget.Dataverse ? "FOTB_CE_CLIENT_SECRET" : "FOTB_CLIENT_SECRET";

    internal static string BearerTokenEnvVar(AuthTarget target) =>
        target == AuthTarget.Dataverse ? "FOTB_CE_BEARER_TOKEN" : "FOTB_BEARER_TOKEN";
}
