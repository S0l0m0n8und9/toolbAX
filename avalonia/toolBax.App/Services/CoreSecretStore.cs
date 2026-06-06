using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="ISecretStore"/> for the F&amp;O client secret: stores the plaintext in the DPAPI
/// <see cref="SecretVaultService"/> (Windows) and records the resulting vault ref on the environment's
/// F&amp;O <c>ServicePrincipal.SecretRef</c>. Presence/clear are tracked via the SP ref (no DPAPI, so
/// cross-platform); only <see cref="SetSecret"/> touches DPAPI. Plaintext is never read back here.
/// </summary>
public sealed class CoreSecretStore : ISecretStore
{
    private readonly ProfileService _profiles;
    private readonly SecretVaultService? _vault; // null on non-Windows (DPAPI unavailable)

    public CoreSecretStore(ProfileService profiles, SecretVaultService? vault)
    {
        _profiles = profiles;
        _vault = vault;
    }

    public bool HasSecret(string key, SecretTarget target = SecretTarget.Fo)
    {
        var sp = LoadSp(key, target);
        return !string.IsNullOrEmpty(sp?.SecretRef);
    }

    public void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo)
    {
        var sp = LoadSp(key, target);
        if (sp is null || string.IsNullOrEmpty(plaintext))
        {
            // Nothing to attach the secret to (set a client id first) — no-op.
            return;
        }

        if (!OperatingSystem.IsWindows() || _vault is null)
        {
            // DPAPI protection is Windows-only; refuse rather than store the secret unprotected.
            throw new PlatformNotSupportedException("The DPAPI secret vault is Windows-only.");
        }

        var previousRef = sp.SecretRef;
        var secretRef = ProtectInVault(plaintext, target);
        RunBlocking(() => _profiles.UpsertServicePrincipalAsync(sp with { SecretRef = secretRef }, CancellationToken.None));

        // Rotation: the SP now points at the new blob, so drop the previous one (no orphan accrual).
        if (!string.IsNullOrEmpty(previousRef))
        {
            RunBlocking(() => _profiles.DeleteSecretAsync(previousRef, CancellationToken.None));
        }
    }

    // Windows-only (DPAPI). Isolated so the [SupportedOSPlatform] annotation satisfies CA1416 inside
    // the lambda; SetSecret only calls this after an OperatingSystem.IsWindows() guard.
    [SupportedOSPlatform("windows")]
    private string ProtectInVault(string plaintext, SecretTarget target) =>
        RunBlocking(() => _vault!.StoreSecretAsync(
            target == SecretTarget.Dataverse ? "dataverse-client-secret" : "fo-client-secret",
            plaintext,
            CancellationToken.None));

    public void ClearSecret(string key, SecretTarget target = SecretTarget.Fo)
    {
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
