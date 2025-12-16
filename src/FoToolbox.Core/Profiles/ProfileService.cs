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

    public Task<IReadOnlyList<FoEnvironment>> GetEnvironmentsAsync(CancellationToken cancellationToken = default) =>
        _store.GetEnvironmentsAsync(cancellationToken);

    public Task<IReadOnlyList<ServicePrincipal>> GetServicePrincipalsAsync(string envId, CancellationToken cancellationToken = default) =>
        _store.GetServicePrincipalsAsync(envId, cancellationToken);

    public Task DeleteEnvironmentAsync(string envId, CancellationToken cancellationToken = default) =>
        _store.DeleteEnvironmentAsync(envId, cancellationToken);

    public Task DeleteServicePrincipalAsync(string id, CancellationToken cancellationToken = default) =>
        _store.DeleteServicePrincipalAsync(id, cancellationToken);

    public Task<string?> GetDefaultEnvironmentIdAsync(CancellationToken cancellationToken = default) =>
        _store.GetSettingAsync(DefaultEnvKey, cancellationToken);

    public Task SetDefaultEnvironmentAsync(string envId, CancellationToken cancellationToken = default) =>
        _store.SetSettingAsync(DefaultEnvKey, envId, cancellationToken);

    public async Task<(FoEnvironment Env, ServicePrincipal Sp)?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var envs = await _store.GetEnvironmentsAsync(cancellationToken);
        if (envs.Count == 0) return null;

        var defaultEnvId = await _store.GetSettingAsync(DefaultEnvKey, cancellationToken);
        var env = envs.FirstOrDefault(e => e.Id == defaultEnvId) ?? envs[0];

        var principals = await _store.GetServicePrincipalsAsync(env.Id, cancellationToken);
        var sp = principals.FirstOrDefault();
        if (sp is null) return null;
        return (env, sp);
    }
}
