using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>Atomic profile-database secret facade. Synchronous members are compatibility wrappers.</summary>
public sealed class CoreSecretStore : ISecretStore
{
    private readonly ProfileService _profiles;
    private readonly SecretVaultService? _vault;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public CoreSecretStore(ProfileService profiles, SecretVaultService? vault)
    {
        _profiles = profiles;
        _vault = vault;
    }

    public static string DiSecretRefSettingKey(string envId) => $"di.secretRef:{envId}";

    public bool HasSecret(string key, SecretTarget target = SecretTarget.Fo) =>
        RunBlocking(() => HasSecretAsync(key, target));

    public async Task<bool> HasSecretAsync(
        string key,
        SecretTarget target = SecretTarget.Fo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target == SecretTarget.DataIntegrator)
                return !string.IsNullOrEmpty(await _profiles.GetSettingAsync(
                    DiSecretRefSettingKey(key), cancellationToken).ConfigureAwait(false));
            var principal = await _profiles.GetServicePrincipalAsync(
                key, CoreTarget(target), cancellationToken).ConfigureAwait(false);
            return !string.IsNullOrEmpty(principal?.SecretRef);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    public void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo) =>
        RunBlocking(() => SetSecretAsync(key, plaintext, target));

    public async Task SetSecretAsync(
        string key,
        string plaintext,
        SecretTarget target = SecretTarget.Fo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("A secret must be non-empty; use ClearSecret to remove one.", nameof(plaintext));
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _profiles.RunProfileMutationAsync(session =>
            {
                if (target == SecretTarget.DataIntegrator)
                {
                    EnsureVaultAvailable();
                    RotateSettingSecret(session, key, plaintext);
                    return 0;
                }

                var principal = session.GetServicePrincipal(key, CoreTarget(target));
                if (principal is null) return 0; // existing compatibility: nothing to attach to
                EnsureVaultAvailable();
                var prepared = Prepare(
                    target == SecretTarget.Dataverse ? "dataverse-client-secret" : "fo-client-secret",
                    plaintext);
                var previous = principal.SecretRef;
                session.InsertProtectedSecret(prepared);
                session.UpsertServicePrincipal(principal with { SecretRef = prepared.Id });
                if (!string.IsNullOrEmpty(previous)) session.DeleteSecretIfUnreferenced(previous);
                return 0;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public void ClearSecret(string key, SecretTarget target = SecretTarget.Fo) =>
        RunBlocking(() => ClearSecretAsync(key, target));

    public async Task ClearSecretAsync(
        string key,
        SecretTarget target = SecretTarget.Fo,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _profiles.RunProfileMutationAsync(session =>
            {
                if (target == SecretTarget.DataIntegrator)
                {
                    var setting = DiSecretRefSettingKey(key);
                    var diReference = session.GetSetting(setting);
                    if (string.IsNullOrEmpty(diReference)) return 0;
                    session.SetSetting(setting, null);
                    session.DeleteSecretIfUnreferenced(diReference);
                    return 0;
                }

                var principal = session.GetServicePrincipal(key, CoreTarget(target));
                if (principal is null || string.IsNullOrEmpty(principal.SecretRef)) return 0;
                var principalReference = principal.SecretRef;
                session.UpsertServicePrincipal(principal with { SecretRef = null });
                session.DeleteSecretIfUnreferenced(principalReference);
                return 0;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private void RotateSettingSecret(
        ProfileMutationSession session,
        string environmentId,
        string plaintext)
    {
        var setting = DiSecretRefSettingKey(environmentId);
        var previous = session.GetSetting(setting);
        var prepared = Prepare("di-service-account-secret", plaintext);
        session.InsertProtectedSecret(prepared);
        session.SetSetting(setting, prepared.Id);
        if (!string.IsNullOrEmpty(previous)) session.DeleteSecretIfUnreferenced(previous);
    }

    private static AuthTarget CoreTarget(SecretTarget target) => target switch
    {
        SecretTarget.Dataverse => AuthTarget.Dataverse,
        _ => AuthTarget.Fo,
    };

    private void EnsureVaultAvailable()
    {
        if (!OperatingSystem.IsWindows() || _vault is null)
            throw new PlatformNotSupportedException("The DPAPI secret vault is Windows-only.");
    }

    private ProtectedSecretRow Prepare(string kind, string plaintext)
    {
        if (!OperatingSystem.IsWindows() || _vault is null)
            throw new PlatformNotSupportedException("The DPAPI secret vault is Windows-only.");
        return _vault.PrepareSecret(kind, plaintext);
    }

    private static void RunBlocking(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();
    private static T RunBlocking<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
