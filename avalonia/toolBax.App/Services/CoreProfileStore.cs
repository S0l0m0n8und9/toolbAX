using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IProfileStore"/> backed by the shared FoToolbox profile database (the same SQLite
/// store the WPF app uses). Environments are loaded into memory once (<see cref="CreateAsync"/>); the
/// synchronous <see cref="Save"/>/<see cref="ActiveId"/> members persist through the async
/// <see cref="ProfileService"/> off the UI thread.
/// </summary>
public sealed class CoreProfileStore : IProfileStore
{
    private readonly ProfileService _profiles;
    private readonly List<EnvProfile> _cache;
    private string? _activeId;

    private CoreProfileStore(ProfileService profiles, List<EnvProfile> cache, string? activeId)
    {
        _profiles = profiles;
        _cache = cache;
        _activeId = activeId;
    }

    /// <summary>Loads environments from the given profile service into memory.</summary>
    public static async Task<CoreProfileStore> CreateAsync(ProfileService profiles, CancellationToken ct = default)
    {
        await profiles.EnsureCreatedAsync(ct).ConfigureAwait(false);

        var cache = new List<EnvProfile>();
        foreach (var env in await profiles.GetEnvironmentsAsync(ct).ConfigureAwait(false))
        {
            var dataverse = await profiles.GetDataverseEnvironmentAsync(env.Id, ct).ConfigureAwait(false);
            var sp = await profiles.GetServicePrincipalAsync(env.Id, AuthTarget.Fo, ct).ConfigureAwait(false);
            cache.Add(Map(env, dataverse?.BaseUrl, sp));
        }

        var activeId = await profiles.GetDefaultEnvironmentIdAsync(ct).ConfigureAwait(false);
        return new CoreProfileStore(profiles, cache, string.IsNullOrEmpty(activeId) ? null : activeId);
    }

    /// <summary>Builds a store over the default on-disk profile database (%LocalAppData%/FoToolbox).</summary>
    public static Task<CoreProfileStore> CreateDefaultAsync(CancellationToken ct = default) =>
        CreateAsync(new ProfileService(new ProfileStore(ProfilePaths.ResolveProfileDbPath())), ct);

    public IReadOnlyList<EnvProfile> GetAll() => _cache;

    public string? ActiveId
    {
        get => _activeId;
        set
        {
            _activeId = value;
            // Persist the cleared state too: an empty default-env id reads back as "none active".
            RunBlocking(() => _profiles.SetDefaultEnvironmentAsync(value ?? string.Empty));
        }
    }

    public void Save(EnvProfile profile)
    {
        RunBlocking(() => _profiles.UpsertEnvironmentAsync(new FoEnvironment(
            profile.Id,
            profile.Name,
            profile.Url,
            profile.Tenant,
            string.IsNullOrWhiteSpace(profile.Legal) ? null : profile.Legal)));

        // Always upsert the linked Dataverse env (stored as columns on the environment row): a blank
        // URL clears CeBaseUrl to NULL, so clearing the Dataverse link persists rather than lingering.
        RunBlocking(() => _profiles.UpsertDataverseEnvironmentAsync(
            new DataverseEnvironment(profile.Id, profile.DataverseUrl ?? string.Empty, profile.Tenant)));

        SaveFoServicePrincipal(profile);

        var index = _cache.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
        {
            _cache[index] = profile;
        }
        else
        {
            _cache.Add(profile);
        }
    }

    public void Delete(string id)
    {
        RunBlocking(() => _profiles.DeleteEnvironmentAsync(id));
        _cache.RemoveAll(p => p.Id == id);
        if (_activeId == id)
        {
            ActiveId = null; // clears the persisted default too
        }
    }

    // Persists the F&O (app-only) service principal for a profile: upsert when a client id is set,
    // delete the row when it's cleared. The SecretRef (vault pointer) is preserved across edits so
    // changing the client id / auth mode doesn't drop a stored secret.
    private void SaveFoServicePrincipal(EnvProfile profile)
    {
        var existing = RunBlocking(() => _profiles.GetServicePrincipalAsync(profile.Id, AuthTarget.Fo));

        if (string.IsNullOrWhiteSpace(profile.ClientId))
        {
            if (existing is not null)
            {
                RunBlocking(() => _profiles.DeleteServicePrincipalAsync(existing.Id));
            }

            return;
        }

        RunBlocking(() => _profiles.UpsertServicePrincipalAsync(new ServicePrincipal(
            existing?.Id ?? $"{profile.Id}:fo",
            profile.Id,
            profile.ClientId!,
            ToCoreAuthMode(profile.AuthMode),
            existing?.SecretRef,
            existing?.CertThumbprint,
            AuthTarget.Fo)));
    }

    private static EnvProfile Map(FoEnvironment env, string? dataverseUrl, ServicePrincipal? sp) => new(
        env.Id,
        env.Name,
        env.BaseUrl,
        env.TenantId,
        env.DefaultCompany ?? string.Empty,
        Tier: string.Empty,
        Status: EnvStatus.Disconnected, // a connection test sets the live status (later wiring)
        LatencyMs: null,
        DataverseUrl: string.IsNullOrWhiteSpace(dataverseUrl) ? null : dataverseUrl,
        DataIntegratorClientId: null,
        DataIntegratorMode: DiAuthMode.Interactive,
        ClientId: sp?.ClientId,
        AuthMode: FromCoreAuthMode(sp?.AuthMode));

    private static AuthMode ToCoreAuthMode(FoAuthMode mode) =>
        mode == FoAuthMode.Certificate ? AuthMode.Certificate : AuthMode.ClientSecret;

    private static FoAuthMode FromCoreAuthMode(AuthMode? mode) =>
        mode == AuthMode.Certificate ? FoAuthMode.Certificate : FoAuthMode.ClientSecret;

    // The IProfileStore contract is synchronous but persistence is async; run it on the thread pool
    // to bridge without risking a UI-thread sync-context deadlock. SQLite writes are sub-millisecond.
    private static void RunBlocking(System.Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();

    private static T RunBlocking<T>(System.Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
