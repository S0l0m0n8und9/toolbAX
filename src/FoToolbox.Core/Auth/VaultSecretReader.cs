using FoToolbox.Core.Profiles;
using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Reads credentials from the DPAPI vault tolerantly: the WPF host stores typed payloads
/// (<see cref="ClientSecretPayload"/>), the Avalonia host stores raw strings. Both must resolve.
/// A read that cannot be decrypted or deserialized returns <see langword="null"/> rather than throwing,
/// so the caller's documented env-var fallback still gets its turn — see <see cref="IsVaultReadFailure"/>.
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
        catch (Exception ex) when (IsVaultReadFailure(ex)) { TraceSkipped(secretRef, ex); }

        try
        {
            var raw = await vault.ReadSecretAsync<string>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw)) return raw;
        }
        catch (Exception ex) when (IsVaultReadFailure(ex)) { TraceSkipped(secretRef, ex); }

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
        catch (Exception ex) when (IsVaultReadFailure(ex)) { TraceSkipped(secretRef, ex); }

        try
        {
            var raw = await vault.ReadSecretAsync<string>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw)) return new BearerTokenPayload { AccessToken = raw };
        }
        catch (Exception ex) when (IsVaultReadFailure(ex)) { TraceSkipped(secretRef, ex); }

        return null;
    }

    /// <summary>
    /// Failures that mean "this vault entry did not yield a usable value", so the caller should move on
    /// to its next source rather than blow up.
    /// <para>
    /// <see cref="JsonException"/> is the shape mismatch between the two hosts' payload formats.
    /// <see cref="CryptographicException"/> is DPAPI refusing to decrypt — the real-world case is a
    /// profile.db restored onto another machine or Windows user account, where the CurrentUser-scoped
    /// blob is undecryptable. That used to propagate out of the resolver ahead of the documented
    /// <c>FOTB_*_CLIENT_SECRET</c> / <c>FOTB_*_BEARER_TOKEN</c> fallback, got retried three times, and
    /// surfaced as the raw "Key not valid for use in specified state." — with the env var set and ready
    /// to work. Treating it as a vault-read miss is what the callers already document.
    /// </para>
    /// </summary>
    private static bool IsVaultReadFailure(Exception ex) => ex is JsonException or CryptographicException;

    /// <summary>
    /// Records why a vault read was skipped. Logs the ref (an opaque id) and the failure type/message
    /// only — never the decrypted value, and never the plaintext of a partially-read blob.
    /// </summary>
    private static void TraceSkipped(string secretRef, Exception failure) =>
        System.Diagnostics.Trace.TraceWarning(
            $"Vault read for secret ref '{secretRef}' failed ({failure.GetType().Name}: {failure.Message}); falling back to the next credential source.");
}
