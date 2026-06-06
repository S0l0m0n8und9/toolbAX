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
            cache.Add(Map(env, dataverse?.BaseUrl));
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

    private static EnvProfile Map(FoEnvironment env, string? dataverseUrl) => new(
        env.Id,
        env.Name,
        env.BaseUrl,
        env.TenantId,
        env.DefaultCompany ?? string.Empty,
        Tier: string.Empty,
        Status: EnvStatus.Disconnected, // a connection test sets the live status (later wiring)
        LatencyMs: null,
        DataverseUrl: string.IsNullOrWhiteSpace(dataverseUrl) ? null : dataverseUrl);

    // The IProfileStore contract is synchronous but persistence is async; run it on the thread pool
    // to bridge without risking a UI-thread sync-context deadlock. SQLite writes are sub-millisecond.
    private static void RunBlocking(System.Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();
}
