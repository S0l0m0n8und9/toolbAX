using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="ISecretStore"/> over the FoToolbox profile database. Two storage shapes:
/// <list type="bullet">
/// <item>the F&amp;O / Dataverse client secret — the plaintext goes into the DPAPI
/// <see cref="SecretVaultService"/> (Windows) and the resulting vault ref is recorded on the
/// environment's <c>ServicePrincipal.SecretRef</c>;</item>
/// <item>the Data Integrator service-account (ROPC) secret — there is no service principal to hang it
/// off (<see cref="AuthTarget"/> only models F&amp;O and Dataverse), so the vault ref lives in the same
/// Settings key/value table as the rest of the DI config (see <see cref="DiSecretRefSettingKey"/>).</item>
/// </list>
/// Presence/clear are tracked via those refs (no DPAPI, so cross-platform); only <see cref="SetSecret"/>
/// touches DPAPI. Plaintext is never read back here.
/// </summary>
public sealed class CoreSecretStore : ISecretStore
{
    /// <summary>
    /// Suffix that marks an <see cref="ISecretStore"/> key as an environment's Data Integrator
    /// service-account secret rather than an F&amp;O/Dataverse client secret. (An environment id that
    /// itself ended in this suffix would collide — env ids are GUIDs or slugs, so they don't.)
    /// </summary>
    private const string DiKeySuffix = ":di";

    private readonly ProfileService _profiles;
    private readonly SecretVaultService? _vault; // null on non-Windows (DPAPI unavailable)

    public CoreSecretStore(ProfileService profiles, SecretVaultService? vault)
    {
        _profiles = profiles;
        _vault = vault;
    }

    /// <summary>
    /// Composes the <see cref="ISecretStore"/> key for an environment's Data Integrator service-account
    /// secret. Single-sourced here because this store decomposes it again, so callers and storage can't
    /// drift apart.
    /// </summary>
    public static string DiSecretKey(string envId) => $"{envId}{DiKeySuffix}";

    /// <summary>
    /// The Settings key holding an environment's DI service-account secret vault ref. Sits alongside the
    /// <c>di.clientId</c>/<c>di.mode</c> keys <see cref="CoreProfileStore"/> already writes.
    /// </summary>
    public static string DiSecretRefSettingKey(string envId) => $"di.secretRef:{envId}";

    public bool HasSecret(string key, SecretTarget target = SecretTarget.Fo)
    {
        if (TryGetDiEnvId(key, out var envId))
        {
            return !string.IsNullOrEmpty(ReadDiSecretRef(envId));
        }

        var sp = LoadSp(key, target);
        return !string.IsNullOrEmpty(sp?.SecretRef);
    }

    /// <summary>
    /// Stores <paramref name="plaintext"/> for <paramref name="key"/>. Contract: this either stores the
    /// secret or throws — it never silently discards one. The single exception is an F&amp;O/Dataverse key
    /// whose environment has no service principal yet (no <c>SecretRef</c> column to write): that returns
    /// without storing, which callers detect by re-checking <see cref="HasSecret"/> and reporting it as
    /// "set a client id and save the profile first". A DI key has no such precondition, so it always
    /// stores.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="plaintext"/> is null or empty.</exception>
    /// <exception cref="PlatformNotSupportedException">The DPAPI vault is unavailable (non-Windows).</exception>
    public void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            // Storing "no secret" would read back as "a secret is stored" on the DI path (which has no
            // service principal to gate on) — removing one is ClearSecret's job, so reject it loudly
            // rather than no-op.
            throw new ArgumentException("A secret must be non-empty; use ClearSecret to remove one.", nameof(plaintext));
        }

        if (TryGetDiEnvId(key, out var envId))
        {
            SetDiSecret(envId, plaintext);
            return;
        }

        var sp = LoadSp(key, target);
        if (sp is null)
        {
            // Nothing to attach the secret to (set a client id and save the profile first). Deliberately
            // not a throw: the caller re-checks HasSecret and turns this into UI guidance.
            return;
        }

        if (!OperatingSystem.IsWindows() || _vault is null)
        {
            // DPAPI protection is Windows-only; refuse rather than store the secret unprotected.
            throw new PlatformNotSupportedException("The DPAPI secret vault is Windows-only.");
        }

        var previousRef = sp.SecretRef;
        var secretRef = ProtectInVault(
            target == SecretTarget.Dataverse ? "dataverse-client-secret" : "fo-client-secret",
            plaintext);
        RunBlocking(() => _profiles.UpsertServicePrincipalAsync(sp with { SecretRef = secretRef }, CancellationToken.None));

        // Rotation: the SP now points at the new blob, so drop the previous one (no orphan accrual).
        if (!string.IsNullOrEmpty(previousRef))
        {
            RunBlocking(() => _profiles.DeleteSecretAsync(previousRef, CancellationToken.None));
        }
    }

    // Stores the DI service-account secret as a vault blob whose ref lives in Settings. The DI
    // credential is per-environment rather than per-audience, so the target argument doesn't apply here.
    private void SetDiSecret(string envId, string plaintext)
    {
        if (!OperatingSystem.IsWindows() || _vault is null)
        {
            // DPAPI protection is Windows-only; refuse rather than store the secret unprotected.
            throw new PlatformNotSupportedException("The DPAPI secret vault is Windows-only.");
        }

        var settingKey = DiSecretRefSettingKey(envId);
        var previousRef = ReadDiSecretRef(envId);
        var secretRef = ProtectInVault("di-service-account-secret", plaintext);
        RunBlocking(() => _profiles.SetSettingAsync(settingKey, secretRef, CancellationToken.None));

        // Rotation: the setting now points at the new blob, so drop the previous one (no orphan accrual).
        if (!string.IsNullOrEmpty(previousRef))
        {
            RunBlocking(() => _profiles.DeleteSecretAsync(previousRef, CancellationToken.None));
        }
    }

    // Windows-only (DPAPI). Isolated so the [SupportedOSPlatform] annotation satisfies CA1416 inside
    // the lambda; callers only reach this after an OperatingSystem.IsWindows() guard.
    [SupportedOSPlatform("windows")]
    private string ProtectInVault(string kind, string plaintext) =>
        RunBlocking(() => _vault!.StoreSecretAsync(kind, plaintext, CancellationToken.None));

    public void ClearSecret(string key, SecretTarget target = SecretTarget.Fo)
    {
        if (TryGetDiEnvId(key, out var envId))
        {
            ClearDiSecret(envId);
            return;
        }

        var sp = LoadSp(key, target);
        if (sp is null || string.IsNullOrEmpty(sp.SecretRef))
        {
            return;
        }

        // Drop the pointer and the stored blob (a plain SQL delete — not DPAPI, so cross-platform).
        var secretRef = sp.SecretRef;
        RunBlocking(() => _profiles.UpsertServicePrincipalAsync(sp with { SecretRef = null }, CancellationToken.None));
        RunBlocking(() => _profiles.DeleteSecretAsync(secretRef, CancellationToken.None));
    }

    private void ClearDiSecret(string envId)
    {
        var secretRef = ReadDiSecretRef(envId);
        if (string.IsNullOrEmpty(secretRef))
        {
            return;
        }

        // Drop the pointer and the stored blob (a plain SQL delete — not DPAPI, so cross-platform).
        RunBlocking(() => _profiles.DeleteSettingAsync(DiSecretRefSettingKey(envId), CancellationToken.None));
        RunBlocking(() => _profiles.DeleteSecretAsync(secretRef, CancellationToken.None));
    }

    private string? ReadDiSecretRef(string envId) =>
        RunBlocking(() => _profiles.GetSettingAsync(DiSecretRefSettingKey(envId), CancellationToken.None));

    // True when the key names a DI service-account secret; yields the environment id it belongs to.
    private static bool TryGetDiEnvId(string key, out string envId)
    {
        if (key.Length > DiKeySuffix.Length && key.EndsWith(DiKeySuffix, StringComparison.Ordinal))
        {
            envId = key[..^DiKeySuffix.Length];
            return true;
        }

        envId = string.Empty;
        return false;
    }

    private ServicePrincipal? LoadSp(string envId, SecretTarget target) =>
        RunBlocking(() => _profiles.GetServicePrincipalAsync(
            envId,
            target switch
            {
                SecretTarget.Dataverse => AuthTarget.Dataverse,
                _ => AuthTarget.Fo,
            },
            CancellationToken.None));

    private static void RunBlocking(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();

    private static T RunBlocking<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
