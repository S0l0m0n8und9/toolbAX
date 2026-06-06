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
    private readonly SecretVaultService _vault;

    public CoreSecretStore(ProfileService profiles, SecretVaultService vault)
    {
        _profiles = profiles;
        _vault = vault;
    }

    public bool HasSecret(string key)
    {
        var sp = LoadFoSp(key);
        return !string.IsNullOrEmpty(sp?.SecretRef);
    }

    public void SetSecret(string key, string plaintext)
    {
        var sp = LoadFoSp(key);
        if (sp is null || string.IsNullOrEmpty(plaintext))
        {
            // Nothing to attach the secret to (set a client id first) — no-op.
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            // DPAPI protection is Windows-only; refuse rather than store the secret unprotected.
            throw new PlatformNotSupportedException("The DPAPI secret vault is Windows-only.");
        }

        var secretRef = ProtectInVault(plaintext);
        RunBlocking(() => _profiles.UpsertServicePrincipalAsync(sp with { SecretRef = secretRef }, CancellationToken.None));
    }

    // Windows-only (DPAPI). Isolated so the [SupportedOSPlatform] annotation satisfies CA1416 inside
    // the lambda; SetSecret only calls this after an OperatingSystem.IsWindows() guard.
    [SupportedOSPlatform("windows")]
    private string ProtectInVault(string plaintext) =>
        RunBlocking(() => _vault.StoreSecretAsync("fo-client-secret", plaintext, CancellationToken.None));

    public void ClearSecret(string key)
    {
        var sp = LoadFoSp(key);
        if (sp is null || string.IsNullOrEmpty(sp.SecretRef))
        {
            return;
        }

        // Drop the pointer; the orphaned vault row is harmless (no plaintext is recoverable without it).
        RunBlocking(() => _profiles.UpsertServicePrincipalAsync(sp with { SecretRef = null }, CancellationToken.None));
    }

    private ServicePrincipal? LoadFoSp(string envId) =>
        RunBlocking(() => _profiles.GetServicePrincipalAsync(envId, AuthTarget.Fo, CancellationToken.None));

    private static void RunBlocking(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();

    private static T RunBlocking<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
