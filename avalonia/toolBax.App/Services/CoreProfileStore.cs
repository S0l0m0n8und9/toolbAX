using System;
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
            var dvSp = await profiles.GetServicePrincipalAsync(env.Id, AuthTarget.Dataverse, ct).ConfigureAwait(false);
            // Data Integrator / dual-write config lives in the key/value Settings table (Avalonia-only,
            // no schema change; the WPF app ignores these keys).
            var diClientId = await profiles.GetSettingAsync(DiClientIdKey(env.Id), ct).ConfigureAwait(false);
            var diMode = await profiles.GetSettingAsync(DiModeKey(env.Id), ct).ConfigureAwait(false);
            var gatewayUrl = await profiles.GetSettingAsync(GatewayUrlKey(env.Id), ct).ConfigureAwait(false);
            var foAuthMode = await profiles.GetSettingAsync(FoAuthModeKey(env.Id), ct).ConfigureAwait(false);
            var dvAuthMode = await profiles.GetSettingAsync(DataverseAuthModeKey(env.Id), ct).ConfigureAwait(false);
            var foClientId = await profiles.GetSettingAsync(FoClientIdKey(env.Id), ct).ConfigureAwait(false);
            var dvClientId = await profiles.GetSettingAsync(DataverseClientIdKey(env.Id), ct).ConfigureAwait(false);
            cache.Add(Map(env, dataverse?.BaseUrl, sp, dvSp, diClientId, diMode, gatewayUrl, foAuthMode, dvAuthMode, foClientId, dvClientId));
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
        SaveDataverseServicePrincipal(profile);

        // Data Integrator / dual-write config (key/value Settings; a blank value removes the row, so
        // an env with no DI config leaves no orphan rows). The mode only matters with a client id.
        if (string.IsNullOrWhiteSpace(profile.DataIntegratorClientId))
        {
            SetOrClearSetting(DiClientIdKey(profile.Id), null);
            SetOrClearSetting(DiModeKey(profile.Id), null);
        }
        else
        {
            SetOrClearSetting(DiClientIdKey(profile.Id), profile.DataIntegratorClientId);
            SetOrClearSetting(DiModeKey(profile.Id), profile.DataIntegratorMode.ToString());
        }

        SetOrClearSetting(GatewayUrlKey(profile.Id), profile.DualWriteGatewayUrl);

        // The FO/Dataverse auth mode lives in Settings: the FoToolbox SP AuthMode only models the
        // app-only ClientSecret/Certificate modes, so Interactive (delegated, no SP) couldn't round-trip
        // through the SP alone.
        SetOrClearSetting(FoAuthModeKey(profile.Id), profile.AuthMode.ToString());
        SetOrClearSetting(DataverseAuthModeKey(profile.Id), profile.DataverseAuthMode.ToString());

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
        // Explicitly remove the env's service principals (don't rely on a FK cascade), so a reused
        // env id can't inherit a stale SecretRef/CertThumbprint.
        foreach (var sp in RunBlocking(() => _profiles.GetServicePrincipalsAsync(id, CancellationToken.None)))
        {
            RunBlocking(() => _profiles.DeleteServicePrincipalAsync(sp.Id));
        }

        // Remove the env's DI/dual-write settings so a reused env id can't inherit stale config (and
        // no orphan Settings rows linger — the Settings table has no FK cascade to environments).
        RunBlocking(() => _profiles.DeleteSettingAsync(DiClientIdKey(id)));
        RunBlocking(() => _profiles.DeleteSettingAsync(DiModeKey(id)));
        RunBlocking(() => _profiles.DeleteSettingAsync(GatewayUrlKey(id)));
        RunBlocking(() => _profiles.DeleteSettingAsync(FoAuthModeKey(id)));
        RunBlocking(() => _profiles.DeleteSettingAsync(DataverseAuthModeKey(id)));
        RunBlocking(() => _profiles.DeleteSettingAsync(FoClientIdKey(id)));
        RunBlocking(() => _profiles.DeleteSettingAsync(DataverseClientIdKey(id)));

        RunBlocking(() => _profiles.DeleteEnvironmentAsync(id));
        _cache.RemoveAll(p => p.Id == id);
        if (_activeId == id)
        {
            ActiveId = null; // clears the persisted default too
        }
    }

    // Persists the F&O credential. Interactive is delegated (no app-only secret), so its (public) client
    // id lives in Settings and no service-principal row is kept — an SP only models the app-only
    // ClientSecret/Certificate modes (and carries the secret ref). For app-only modes the SP is
    // upserted (preserving the SecretRef) and the Settings client-id copy is cleared.
    private void SaveFoServicePrincipal(EnvProfile profile)
    {
        var existing = RunBlocking(() => _profiles.GetServicePrincipalAsync(profile.Id, AuthTarget.Fo, CancellationToken.None));

        if (profile.AuthMode == FoAuthMode.Interactive)
        {
            SetOrClearSetting(FoClientIdKey(profile.Id), profile.ClientId);
            if (existing is not null)
            {
                RunBlocking(() => _profiles.DeleteServicePrincipalAsync(existing.Id));
            }

            return;
        }

        SetOrClearSetting(FoClientIdKey(profile.Id), null); // app-only: the SP is the client-id source
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

    // Mirrors SaveFoServicePrincipal for the Dataverse credential (Target=Dataverse).
    private void SaveDataverseServicePrincipal(EnvProfile profile)
    {
        var existing = RunBlocking(() => _profiles.GetServicePrincipalAsync(profile.Id, AuthTarget.Dataverse, CancellationToken.None));

        if (profile.DataverseAuthMode == FoAuthMode.Interactive)
        {
            SetOrClearSetting(DataverseClientIdKey(profile.Id), profile.DataverseClientId);
            if (existing is not null)
            {
                RunBlocking(() => _profiles.DeleteServicePrincipalAsync(existing.Id));
            }

            return;
        }

        SetOrClearSetting(DataverseClientIdKey(profile.Id), null);
        if (string.IsNullOrWhiteSpace(profile.DataverseClientId))
        {
            if (existing is not null)
            {
                RunBlocking(() => _profiles.DeleteServicePrincipalAsync(existing.Id));
            }

            return;
        }

        RunBlocking(() => _profiles.UpsertServicePrincipalAsync(new ServicePrincipal(
            existing?.Id ?? $"{profile.Id}:dataverse",
            profile.Id,
            profile.DataverseClientId!,
            ToCoreAuthMode(profile.DataverseAuthMode),
            existing?.SecretRef,
            existing?.CertThumbprint,
            AuthTarget.Dataverse)));
    }

    private static EnvProfile Map(FoEnvironment env, string? dataverseUrl, ServicePrincipal? sp, ServicePrincipal? dataverseSp,
        string? diClientId, string? diMode, string? gatewayUrl, string? foAuthMode, string? dataverseAuthMode,
        string? foClientId, string? dataverseClientId)
    {
        var foMode = ResolveAuthMode(foAuthMode, sp);
        var dvMode = ResolveAuthMode(dataverseAuthMode, dataverseSp);
        return new(
            env.Id,
            env.Name,
            env.BaseUrl,
            env.TenantId,
            env.DefaultCompany ?? string.Empty,
            Tier: string.Empty,
            Status: EnvStatus.Disconnected, // a connection test sets the live status (later wiring)
            LatencyMs: null,
            DataverseUrl: string.IsNullOrWhiteSpace(dataverseUrl) ? null : dataverseUrl,
            DataIntegratorClientId: string.IsNullOrWhiteSpace(diClientId) ? null : diClientId,
            DataIntegratorMode: ParseDiMode(diMode),
            // Interactive client ids come from Settings (no SP); app-only ones from the SP. A delegated
            // connection with neither falls back to the global public client (see ResolveClientId).
            ClientId: ResolveClientId(foClientId, sp?.ClientId, foMode),
            AuthMode: foMode,
            DataverseClientId: ResolveClientId(dataverseClientId, dataverseSp?.ClientId, dvMode),
            DataverseAuthMode: dvMode,
            DualWriteGatewayUrl: string.IsNullOrWhiteSpace(gatewayUrl) ? null : gatewayUrl);
    }

    // Upserts a setting, or removes the row when the value is blank (avoids accumulating empty rows).
    private void SetOrClearSetting(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            RunBlocking(() => _profiles.DeleteSettingAsync(key));
        }
        else
        {
            RunBlocking(() => _profiles.SetSettingAsync(key, value));
        }
    }

    private static DiAuthMode ParseDiMode(string? mode) =>
        Enum.TryParse<DiAuthMode>(mode, out var parsed) ? parsed : DiAuthMode.Interactive;

    private static string DiClientIdKey(string envId) => $"di.clientId:{envId}";

    private static string DiModeKey(string envId) => $"di.mode:{envId}";

    private static string GatewayUrlKey(string envId) => $"dw.gatewayUrl:{envId}";

    private static string FoAuthModeKey(string envId) => $"fo.authMode:{envId}";

    private static string DataverseAuthModeKey(string envId) => $"dv.authMode:{envId}";

    // Interactive-mode (public) client ids live in Settings — there's no app-only SP to hold them.
    private static string FoClientIdKey(string envId) => $"fo.clientId:{envId}";

    private static string DataverseClientIdKey(string envId) => $"dv.clientId:{envId}";

    private static AuthMode ToCoreAuthMode(FoAuthMode mode) =>
        mode == FoAuthMode.Certificate ? AuthMode.Certificate : AuthMode.ClientSecret;

    private static FoAuthMode FromCoreAuthMode(AuthMode? mode) => mode switch
    {
        AuthMode.Certificate => FoAuthMode.Certificate,
        // BearerToken is FoToolbox's delegated (captured/pasted user token) mode — NOT app-only. The
        // Avalonia app's delegated equivalent is a fresh interactive MSAL sign-in, so a legacy WPF
        // bearer-token profile must surface as Interactive (and never hit the client-credentials path,
        // which rejects BearerToken). Anything else is an app-only client secret.
        AuthMode.BearerToken => FoAuthMode.Interactive,
        _ => FoAuthMode.ClientSecret,
    };

    // The effective client id: an explicit Settings value, else the linked SP's, else — for a delegated
    // (Interactive) connection with neither — Microsoft's global public client, so an interactive
    // sign-in has a usable client id out of the box (matching the Profiles UI default).
    private static string? ResolveClientId(string? settingClientId, string? spClientId, FoAuthMode mode)
    {
        var explicitId = string.IsNullOrWhiteSpace(settingClientId) ? spClientId : settingClientId;
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            return explicitId;
        }

        return mode == FoAuthMode.Interactive ? FoAuthModeExtensions.DefaultInteractiveClientId : explicitId;
    }

    // The auth mode comes from the Settings row when present (covers Interactive); otherwise it's
    // derived from a legacy app-only SP, or defaults to Interactive when neither exists.
    private static FoAuthMode ResolveAuthMode(string? setting, ServicePrincipal? sp) =>
        Enum.TryParse<FoAuthMode>(setting, out var parsed) ? parsed
        : sp is null ? FoAuthMode.Interactive
        : FromCoreAuthMode(sp.AuthMode);

    // The IProfileStore contract is synchronous but persistence is async; run it on the thread pool
    // to bridge without risking a UI-thread sync-context deadlock. SQLite writes are sub-millisecond.
    private static void RunBlocking(System.Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();

    private static T RunBlocking<T>(System.Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
