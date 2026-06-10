using FoToolbox.Core.Profiles;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Reads credentials from the DPAPI vault tolerantly: the WPF host stores typed payloads
/// (<see cref="ClientSecretPayload"/>), the Avalonia host stores raw strings. Both must resolve.
/// </summary>
[SupportedOSPlatform("windows")]
public static class VaultSecretReader
{
    /// <summary>
    /// Reads a client secret from the vault. Handles both the WPF typed payload shape
    /// (<c>{"Value":"…"}</c>) and the Avalonia raw-string shape (<c>"…"</c>).
    /// Returns <see langword="null"/> if the ref is missing or contains no usable value.
    /// </summary>
    public static async Task<string?> ReadClientSecretAsync(
        SecretVaultService vault,
        string secretRef,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await vault.ReadSecretAsync<ClientSecretPayload>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload?.Value)) return payload.Value;
        }
        catch (JsonException) { }

        try
        {
            var raw = await vault.ReadSecretAsync<string>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw)) return raw;
        }
        catch (JsonException) { }

        return null;
    }

    /// <summary>
    /// Reads a bearer token from the vault. Handles the typed payload shape
    /// (<c>{"AccessToken":"…","ExpiresUtc":"…"}</c>) and falls back to a raw-string token.
    /// Returns <see langword="null"/> if the ref is missing or contains no usable access token.
    /// </summary>
    public static async Task<BearerTokenPayload?> ReadBearerTokenAsync(
        SecretVaultService vault,
        string secretRef,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await vault.ReadSecretAsync<BearerTokenPayload>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload?.AccessToken)) return payload;
        }
        catch (JsonException) { }

        try
        {
            var raw = await vault.ReadSecretAsync<string>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw)) return new BearerTokenPayload { AccessToken = raw };
        }
        catch (JsonException) { }

        return null;
    }
}
