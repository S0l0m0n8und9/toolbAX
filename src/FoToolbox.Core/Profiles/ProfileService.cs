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

    public ProfileService(ProfileStore store)
    {
        _store = store;
    }

    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => _store.EnsureCreatedAsync(cancellationToken);

    public Task UpsertEnvironmentAsync(FoEnvironment env, CancellationToken cancellationToken = default) =>
        _store.UpsertEnvironmentAsync(env, cancellationToken);

    public Task UpsertServicePrincipalAsync(ServicePrincipal sp, CancellationToken cancellationToken = default) =>
        _store.UpsertServicePrincipalAsync(sp, cancellationToken);

    public async Task<(FoEnvironment Env, ServicePrincipal Sp)?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var envs = await _store.GetEnvironmentsAsync(cancellationToken);
        if (envs.Count == 0) return null;
        var env = envs[0];
        var principals = await _store.GetServicePrincipalsAsync(env.Id, cancellationToken);
        var sp = principals.FirstOrDefault();
        if (sp is null) return null;
        return (env, sp);
    }
}
