using System;
using FoToolbox.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Profiles;

/// <summary>
/// Convenience service over ProfileStore for common lookups.
/// </summary>
public sealed class ProfileService
{
    private readonly ProfileStore _store;
    private const string DefaultEnvKey = "DefaultEnvId";

    public ProfileService(ProfileStore store)
    {
        _store = store;
    }

    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => _store.EnsureCreatedAsync(cancellationToken);

    public Task UpsertEnvironmentAsync(FoEnvironment env, CancellationToken cancellationToken = default) =>
        _store.UpsertEnvironmentAsync(env, cancellationToken);

    public Task UpsertServicePrincipalAsync(ServicePrincipal sp, CancellationToken cancellationToken = default) =>
        _store.UpsertServicePrincipalAsync(sp, cancellationToken);

    public Task UpsertDataverseEnvironmentAsync(DataverseEnvironment env, CancellationToken cancellationToken = default) =>
        _store.UpsertDataverseEnvironmentAsync(env, cancellationToken);

    public Task<IReadOnlyList<FoEnvironment>> GetEnvironmentsAsync(CancellationToken cancellationToken = default) =>
        _store.GetEnvironmentsAsync(cancellationToken);

    public Task<IReadOnlyList<ServicePrincipal>> GetServicePrincipalsAsync(string envId, CancellationToken cancellationToken = default) =>
        _store.GetServicePrincipalsAsync(envId, cancellationToken);

    public Task<ServicePrincipal?> GetServicePrincipalAsync(string envId, AuthTarget target, CancellationToken cancellationToken = default) =>
        _store.GetServicePrincipalAsync(envId, target, cancellationToken);

    public Task<DataverseEnvironment?> GetDataverseEnvironmentAsync(string envId, CancellationToken cancellationToken = default) =>
        _store.GetDataverseEnvironmentAsync(envId, cancellationToken);

    public Task DeleteEnvironmentAsync(string envId, CancellationToken cancellationToken = default) =>
        _store.DeleteEnvironmentAsync(envId, cancellationToken);

    public Task DeleteServicePrincipalAsync(string id, CancellationToken cancellationToken = default) =>
        _store.DeleteServicePrincipalAsync(id, cancellationToken);

    public Task DeleteSecretAsync(string id, CancellationToken cancellationToken = default) =>
        _store.DeleteSecretAsync(id, cancellationToken);

    public Task<string?> GetDefaultEnvironmentIdAsync(CancellationToken cancellationToken = default) =>
        _store.GetSettingAsync(DefaultEnvKey, cancellationToken);

    public Task SetDefaultEnvironmentAsync(string envId, CancellationToken cancellationToken = default) =>
        _store.SetSettingAsync(DefaultEnvKey, envId, cancellationToken);

    /// <summary>Reads an arbitrary key/value setting (null when unset).</summary>
    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default) =>
        _store.GetSettingAsync(key, cancellationToken);

    /// <summary>Writes an arbitrary key/value setting (upsert).</summary>
    public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default) =>
        _store.SetSettingAsync(key, value, cancellationToken);

    public async Task<(FoEnvironment Env, ServicePrincipal Sp)?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var bundle = await GetDefaultBundleAsync(cancellationToken);
        if (bundle is null) return null;
        return (bundle.FoEnvironment, bundle.FoPrincipal);
    }

    public async Task<ProfileBundle?> GetDefaultBundleAsync(CancellationToken cancellationToken = default)
    {
        var envs = await _store.GetEnvironmentsAsync(cancellationToken);
        if (envs.Count == 0) return null;

        var defaultEnvId = await _store.GetSettingAsync(DefaultEnvKey, cancellationToken);
        var env = envs.FirstOrDefault(e => e.Id == defaultEnvId) ?? envs[0];

        return await GetBundleAsync(env.Id, env, cancellationToken);
    }

    public async Task<ProfileBundle?> GetBundleAsync(string envId, CancellationToken cancellationToken = default)
    {
        var envs = await _store.GetEnvironmentsAsync(cancellationToken);
        var env = envs.FirstOrDefault(e => e.Id == envId);
        if (env is null) return null;
        return await GetBundleAsync(envId, env, cancellationToken);
    }

    private async Task<ProfileBundle?> GetBundleAsync(string envId, FoEnvironment foEnvironment, CancellationToken cancellationToken)
    {
        var foPrincipal = await _store.GetServicePrincipalAsync(envId, AuthTarget.Fo, cancellationToken)
            ?? (await _store.GetServicePrincipalsAsync(envId, cancellationToken)).FirstOrDefault(p => p.Target == AuthTarget.Fo)
            ?? new ServicePrincipal(Guid.NewGuid().ToString("N"), envId, string.Empty, AuthMode.ClientSecret, null, null, AuthTarget.Fo);

        var ceEnvironment = await _store.GetDataverseEnvironmentAsync(envId, cancellationToken)
            ?? new DataverseEnvironment(envId, string.Empty, string.Empty);

        var cePrincipal = await _store.GetServicePrincipalAsync(envId, AuthTarget.Dataverse, cancellationToken)
            ?? new ServicePrincipal(Guid.NewGuid().ToString("N"), envId, string.Empty, AuthMode.ClientSecret, null, null, AuthTarget.Dataverse);

        return new ProfileBundle(foEnvironment, foPrincipal, ceEnvironment, cePrincipal);
    }
}

public sealed record ProfileBundle(
    FoEnvironment FoEnvironment,
    ServicePrincipal FoPrincipal,
    DataverseEnvironment DataverseEnvironment,
    ServicePrincipal DataversePrincipal);
