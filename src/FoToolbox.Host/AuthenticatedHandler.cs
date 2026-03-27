using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;

namespace FoToolbox.Host;

/// <summary>
/// Simple delegating handler that injects a bearer token using AuthService.
/// </summary>
internal sealed class AuthenticatedHandler : DelegatingHandler
{
    private readonly string _resourceBaseUrl;
    private readonly string _tenantId;
    private readonly ServicePrincipal _sp;
    private readonly AuthService _auth;
    private readonly SecretVaultService _vault;

    public AuthenticatedHandler(FoEnvironment env, ServicePrincipal sp, SecretVaultService vault)
        : this(ResourceUrlNormalizer.NormalizeFoBaseUrl(env.BaseUrl), env.TenantId, sp, vault)
    {
    }

    public AuthenticatedHandler(string resourceBaseUrl, string tenantId, ServicePrincipal sp, SecretVaultService vault)
        : base(new HttpClientHandler())
    {
        _resourceBaseUrl = resourceBaseUrl;
        _tenantId = tenantId;
        _sp = sp;
        _vault = vault;
        _auth = new AuthService(BuildTokenProvider());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _sp.AuthMode == AuthMode.BearerToken
            ? await ResolveBearerTokenAsync(_sp, cancellationToken)
            : await _auth.AcquireTokenAsync(_resourceBaseUrl, _tenantId, _sp, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }

    private ITokenProvider BuildTokenProvider()
    {
        var authorityBase = "https://login.microsoftonline.com";
        return new MsalTokenProvider(authorityBase, ResolveCredentialAsync);
    }

    private async Task<ClientCredential> ResolveCredentialAsync(ServicePrincipal sp, CancellationToken cancellationToken)
    {
        // Prefer DPAPI vault secret for this service principal.
        if (!string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            var secretPayload = await _vault.ReadSecretAsync<ClientSecretPayload>(sp.SecretRef, cancellationToken);
            if (secretPayload is not null && !string.IsNullOrWhiteSpace(secretPayload.Value))
            {
                return new ClientSecretCredential(secretPayload.Value);
            }
        }

        var secret = Environment.GetEnvironmentVariable("FOTB_CLIENT_SECRET");
        if (!string.IsNullOrWhiteSpace(secret))
        {
            return new ClientSecretCredential(secret);
        }

        throw new InvalidOperationException("No client secret configured for this profile. Set it in Profiles and Save, or set FOTB_CLIENT_SECRET.");
    }

    private async Task<string> ResolveBearerTokenAsync(ServicePrincipal sp, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            var payload = await _vault.ReadSecretAsync<BearerTokenPayload>(sp.SecretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload?.AccessToken))
            {
                var normalized = NormalizeBearerToken(payload.AccessToken);
                if (TryGetJwtExpiryUtc(normalized, out var expiryUtc) && expiryUtc <= DateTimeOffset.UtcNow)
                {
                    throw new InvalidOperationException($"Bearer token expired at {expiryUtc:u}. Update it in Profiles.");
                }
                return normalized;
            }
        }

        var token = Environment.GetEnvironmentVariable("FOTB_BEARER_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            var normalized = NormalizeBearerToken(token);
            if (TryGetJwtExpiryUtc(normalized, out var expiryUtc) && expiryUtc <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException($"FOTB_BEARER_TOKEN expired at {expiryUtc:u}. Set a fresh token.");
            }
            return normalized;
        }

        throw new InvalidOperationException("No bearer token configured for this profile. Paste a token in Profiles and Save, or set FOTB_BEARER_TOKEN.");
    }

    private static string NormalizeBearerToken(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Bearer ".Length..];
        }

        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    private static bool TryGetJwtExpiryUtc(string jwt, out DateTimeOffset expiryUtc)
    {
        expiryUtc = default;
        if (string.IsNullOrWhiteSpace(jwt)) return false;
        var parts = jwt.Split('.');
        if (parts.Length < 2) return false;

        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            if (payloadBytes.Length == 0) return false;
            using var doc = JsonDocument.Parse(payloadBytes);
            if (!doc.RootElement.TryGetProperty("exp", out var expEl)) return false;
            if (!expEl.TryGetInt64(out var seconds)) return false;
            expiryUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        var pad = s.Length % 4;
        if (pad == 2) s += "==";
        else if (pad == 3) s += "=";
        else if (pad != 0) return Array.Empty<byte>();
        return Convert.FromBase64String(s);
    }

    private sealed class ClientSecretPayload
    {
        public string? Value { get; set; }
    }

    private sealed class BearerTokenPayload
    {
        public string? AccessToken { get; set; }
        public string? ExpiresUtc { get; set; }
    }
}
