using System;
using System.Collections.Generic;
using System.Linq;
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
/// synchronous <see cref="Save"/>/<see cref="ActiveId"/> members are compatibility wrappers and may block;
/// production UI code uses the asynchronous members.
/// </summary>
public sealed class CoreProfileStore : IProfileStore
{
    private readonly ProfileService _profiles;
    private readonly List<EnvProfile> _cache;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
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
        ArgumentNullException.ThrowIfNull(profiles);
        ct.ThrowIfCancellationRequested();
        return await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            await profiles.EnsureCreatedAsync(ct).ConfigureAwait(false);

            var cache = new List<EnvProfile>();
            foreach (var env in await profiles.GetEnvironmentsAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
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
                var envType = await profiles.GetSettingAsync(EnvironmentTypeKey(env.Id), ct).ConfigureAwait(false);
                cache.Add(Map(env, dataverse?.BaseUrl, sp, dvSp, diClientId, diMode, gatewayUrl, foAuthMode, dvAuthMode, foClientId, dvClientId, envType));
            }

            var activeId = await profiles.GetDefaultEnvironmentIdAsync(ct).ConfigureAwait(false);
            return new CoreProfileStore(profiles, cache, string.IsNullOrEmpty(activeId) ? null : activeId);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Builds a store over the default on-disk profile database (%LocalAppData%/FoToolbox).</summary>
    public static Task<CoreProfileStore> CreateDefaultAsync(CancellationToken ct = default) =>
        CreateAsync(new ProfileService(new ProfileStore(ProfilePaths.ResolveProfileDbPath())), ct);

    public IReadOnlyList<EnvProfile> GetAll()
    {
        lock (_stateGate) return Array.AsReadOnly(_cache.ToArray());
    }

    public string? ActiveId
    {
        get { lock (_stateGate) return _activeId; }
        set => RunBlocking(() => SetActiveAsync(value));
    }

    public void Save(EnvProfile profile) => RunBlocking(() => SaveAsync(profile));

    public async Task SetActiveAsync(string? id, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _profiles.RunProfileMutationAsync(session =>
            {
                session.SetDefaultEnvironment(id ?? string.Empty);
                return 0;
            }, cancellationToken).ConfigureAwait(false);
            lock (_stateGate) _activeId = id;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task SaveAsync(EnvProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _profiles.RunProfileMutationAsync(session =>
            {
                SaveInTransaction(session, profile);
                return 0;
            }, cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                var index = _cache.FindIndex(existing => existing.Id == profile.Id);
                if (index >= 0) _cache[index] = profile;
                else _cache.Add(profile);
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public void Delete(string id) => RunBlocking(() => DeleteAsync(id));

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var committedActive = await _profiles.RunProfileMutationAsync(session =>
                DeleteInTransaction(session, id), cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                _cache.RemoveAll(profile => profile.Id == id);
                _activeId = committedActive;
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private static void SaveInTransaction(ProfileMutationSession session, EnvProfile profile)
    {
        var previous = LoadPersistedProfile(session, profile.Id);
        ValidateAuthEditBeforeWrite(previous, profile, AuthTarget.Fo);
        ValidateAuthEditBeforeWrite(previous, profile, AuthTarget.Dataverse);

        session.UpsertEnvironment(new FoEnvironment(
            profile.Id, profile.Name, profile.Url, profile.Tenant,
            string.IsNullOrWhiteSpace(profile.Legal) ? null : profile.Legal));
        session.UpsertDataverseEnvironment(new DataverseEnvironment(
            profile.Id, profile.DataverseUrl ?? string.Empty, profile.Tenant));

        SaveTargetInTransaction(session, previous, profile, AuthTarget.Fo);
        SaveTargetInTransaction(session, previous, profile, AuthTarget.Dataverse);

        var preservesDi = previous is not null
            && string.Equals(previous.DataIntegratorClientId, profile.DataIntegratorClientId, StringComparison.Ordinal)
            && previous.DataIntegratorMode == profile.DataIntegratorMode
            && string.Equals(previous.DualWriteGatewayUrl, profile.DualWriteGatewayUrl, StringComparison.Ordinal);
        if (!preservesDi)
        {
            if (string.IsNullOrWhiteSpace(profile.DataIntegratorClientId))
            {
                SetOrClearSetting(session, DiClientIdKey(profile.Id), null);
                SetOrClearSetting(session, DiModeKey(profile.Id), null);
            }
            else
            {
                SetOrClearSetting(session, DiClientIdKey(profile.Id), profile.DataIntegratorClientId);
                SetOrClearSetting(session, DiModeKey(profile.Id), profile.DataIntegratorMode.ToString());
            }
            SetOrClearSetting(session, GatewayUrlKey(profile.Id), profile.DualWriteGatewayUrl);
        }
        SetOrClearSetting(session, EnvironmentTypeKey(profile.Id), profile.Tier);
    }

    private static void SaveTargetInTransaction(
        ProfileMutationSession session,
        EnvProfile? previous,
        EnvProfile profile,
        AuthTarget target)
    {
        if (PreservesUnsupportedTarget(previous, profile, target)) return;
        var existing = session.GetServicePrincipal(profile.Id, target);
        var mode = target == AuthTarget.Fo ? profile.AuthMode : profile.DataverseAuthMode;
        var clientId = target == AuthTarget.Fo ? profile.ClientId : profile.DataverseClientId;
        var clientSetting = target == AuthTarget.Fo ? FoClientIdKey(profile.Id) : DataverseClientIdKey(profile.Id);
        var modeSetting = target == AuthTarget.Fo ? FoAuthModeKey(profile.Id) : DataverseAuthModeKey(profile.Id);

        if (mode == FoAuthMode.Interactive || string.IsNullOrWhiteSpace(clientId))
        {
            DropServicePrincipal(session, existing);
            SetOrClearSetting(session, clientSetting, mode == FoAuthMode.Interactive ? clientId : null);
            SetOrClearSetting(session, modeSetting, mode.ToString());
            return;
        }

        var unbindCredential = ClientIdChanged(existing, clientId) ||
                               existing is not null && existing.AuthMode != AuthMode.ClientSecret;
        session.UpsertServicePrincipal(new ServicePrincipal(
            existing?.Id ?? $"{profile.Id}:{(target == AuthTarget.Fo ? "fo" : "dataverse")}",
            profile.Id,
            clientId!,
            ToCoreAuthMode(mode),
            unbindCredential ? null : existing?.SecretRef,
            unbindCredential ? null : existing?.CertThumbprint,
            target));
        SetOrClearSetting(session, clientSetting, null);
        SetOrClearSetting(session, modeSetting, mode.ToString());
        if (unbindCredential && !string.IsNullOrEmpty(existing?.SecretRef))
            session.DeleteSecretIfUnreferenced(existing.SecretRef);
    }

    private static string? DeleteInTransaction(ProfileMutationSession session, string id)
    {
        var candidates = session.GetServicePrincipals(id)
            .Select(principal => principal.SecretRef)
            .Where(reference => !string.IsNullOrEmpty(reference))
            .Cast<string>()
            .ToList();
        var diReference = session.GetSetting(CoreSecretStore.DiSecretRefSettingKey(id));
        if (!string.IsNullOrEmpty(diReference)) candidates.Add(diReference);

        foreach (var principal in session.GetServicePrincipals(id))
            session.DeleteServicePrincipal(principal.Id);
        foreach (var key in KnownSettingKeys(id)) session.SetSetting(key, null);
        session.DeleteEnvironment(id);

        var persistedDefault = session.GetDefaultEnvironmentId();
        if (string.Equals(persistedDefault, id, StringComparison.Ordinal))
        {
            session.SetDefaultEnvironment(null);
            persistedDefault = null;
        }
        foreach (var reference in candidates.Distinct(StringComparer.Ordinal))
            session.DeleteSecretIfUnreferenced(reference);
        return string.IsNullOrEmpty(persistedDefault) ? null : persistedDefault;
    }

    private static IEnumerable<string> KnownSettingKeys(string id)
    {
        yield return CoreSecretStore.DiSecretRefSettingKey(id);
        yield return DiClientIdKey(id);
        yield return DiModeKey(id);
        yield return GatewayUrlKey(id);
        yield return FoAuthModeKey(id);
        yield return DataverseAuthModeKey(id);
        yield return FoClientIdKey(id);
        yield return DataverseClientIdKey(id);
        yield return EnvironmentTypeKey(id);
    }

    private static EnvProfile? LoadPersistedProfile(ProfileMutationSession session, string id)
    {
        var environment = session.GetEnvironment(id);
        if (environment is null) return null;
        var dataverse = session.GetDataverseEnvironment(id);
        return Map(
            environment,
            dataverse?.BaseUrl,
            session.GetServicePrincipal(id, AuthTarget.Fo),
            session.GetServicePrincipal(id, AuthTarget.Dataverse),
            session.GetSetting(DiClientIdKey(id)),
            session.GetSetting(DiModeKey(id)),
            session.GetSetting(GatewayUrlKey(id)),
            session.GetSetting(FoAuthModeKey(id)),
            session.GetSetting(DataverseAuthModeKey(id)),
            session.GetSetting(FoClientIdKey(id)),
            session.GetSetting(DataverseClientIdKey(id)),
            session.GetSetting(EnvironmentTypeKey(id)));
    }

    private static void DropServicePrincipal(ProfileMutationSession session, ServicePrincipal? principal)
    {
        if (principal is null) return;
        session.DeleteServicePrincipal(principal.Id);
        if (!string.IsNullOrEmpty(principal.SecretRef))
            session.DeleteSecretIfUnreferenced(principal.SecretRef);
    }

    private static void SetOrClearSetting(ProfileMutationSession session, string key, string? value) =>
        session.SetSetting(key, string.IsNullOrWhiteSpace(value) ? null : value);

    // A stored credential belongs to the app registration it was issued for — both the client secret and
    // the certificate thumbprint. When the client id changes neither is valid any more, so both are
    // unbound (and the secret's blob deleted); carrying them over silently re-points app A's credential at
    // app B, which surfaces as an AAD invalid_client rather than "no credential stored" and sends the user
    // hunting an unrelated auth fault. Dropping them makes HasSecret read false so the UI asks for the new
    // registration's credential. (The thumbprint needs no cleanup: it points at the machine certificate
    // store, not the vault.)
    private static bool ClientIdChanged(ServicePrincipal? existing, string? clientId) =>
        existing is not null && !string.Equals(existing.ClientId, clientId, StringComparison.Ordinal);

    private static bool IsSupportedAuthMode(FoAuthMode mode) =>
        mode is FoAuthMode.Interactive or FoAuthMode.ClientSecret;

    private static bool PreservesUnsupportedTarget(EnvProfile? before, EnvProfile after, AuthTarget target)
    {
        if (before is null) return false;
        var beforeMode = target == AuthTarget.Fo ? before.AuthMode : before.DataverseAuthMode;
        var afterMode = target == AuthTarget.Fo ? after.AuthMode : after.DataverseAuthMode;
        var beforeClient = target == AuthTarget.Fo ? before.ClientId : before.DataverseClientId;
        var afterClient = target == AuthTarget.Fo ? after.ClientId : after.DataverseClientId;
        return !IsSupportedAuthMode(beforeMode) && beforeMode == afterMode
            && string.Equals(beforeClient, afterClient, StringComparison.Ordinal);
    }

    private static void ValidateAuthEditBeforeWrite(EnvProfile? before, EnvProfile after, AuthTarget target)
    {
        var mode = target == AuthTarget.Fo ? after.AuthMode : after.DataverseAuthMode;
        if (IsSupportedAuthMode(mode)) return;
        if (PreservesUnsupportedTarget(before, after, target)) return;
        var label = target == AuthTarget.Fo ? "F&O" : "Dataverse";
        throw new InvalidOperationException(
            $"The saved {label} authentication mode is unsupported. Choose a supported mode before changing its client ID or mode.");
    }

    private static EnvProfile Map(FoEnvironment env, string? dataverseUrl, ServicePrincipal? sp, ServicePrincipal? dataverseSp,
        string? diClientId, string? diMode, string? gatewayUrl, string? foAuthMode, string? dataverseAuthMode,
        string? foClientId, string? dataverseClientId, string? environmentType)
    {
        var foMode = ResolveAuthMode(foAuthMode, sp);
        var dvMode = ResolveAuthMode(dataverseAuthMode, dataverseSp);
        return new(
            env.Id,
            env.Name,
            env.BaseUrl,
            env.TenantId,
            env.DefaultCompany ?? string.Empty,
            // The stored type drives the "Environment type" dropdown + subtitle; a blank/legacy value
            // normalises to Non-production in EnvProfile.
            Tier: environmentType ?? string.Empty,
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

    private static DiAuthMode ParseDiMode(string? mode)
    {
        if (mode is null) return DiAuthMode.Interactive;
        return Enum.TryParse<DiAuthMode>(mode, out var parsed)
            && parsed is DiAuthMode.Interactive or DiAuthMode.Ropc
                ? parsed
                : DiAuthMode.Unsupported;
    }

    private static string DiClientIdKey(string envId) => $"di.clientId:{envId}";

    private static string DiModeKey(string envId) => $"di.mode:{envId}";

    private static string GatewayUrlKey(string envId) => $"dw.gatewayUrl:{envId}";

    private static string FoAuthModeKey(string envId) => $"fo.authMode:{envId}";

    private static string DataverseAuthModeKey(string envId) => $"dv.authMode:{envId}";

    // Interactive-mode (public) client ids live in Settings — there's no app-only SP to hold them.
    private static string FoClientIdKey(string envId) => $"fo.clientId:{envId}";

    private static string DataverseClientIdKey(string envId) => $"dv.clientId:{envId}";

    // Environment type (Production / Non-production) lives in Settings — FoEnvironment has no such
    // column and the WPF app ignores this key.
    private static string EnvironmentTypeKey(string envId) => $"env.type:{envId}";

    private static AuthMode ToCoreAuthMode(FoAuthMode mode) =>
        mode == FoAuthMode.ClientSecret
            ? AuthMode.ClientSecret
            : throw new InvalidOperationException("Only Client secret can be persisted as an App service principal.");

    private static FoAuthMode FromCoreAuthMode(AuthMode? mode) => mode switch
    {
        AuthMode.Certificate => FoAuthMode.Certificate,
        AuthMode.ClientSecret => FoAuthMode.ClientSecret,
        AuthMode.Interactive => FoAuthMode.Interactive,
        _ => FoAuthMode.Unsupported,
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

        // No usable client id: Interactive falls back to the global public client; app-only modes
        // normalise blank/empty to null (never an empty string).
        return mode == FoAuthMode.Interactive ? FoAuthModeExtensions.DefaultInteractiveClientId : null;
    }

    // The auth mode comes from the Settings row when present (covers Interactive); otherwise it's
    // derived from a legacy app-only SP, or defaults to Interactive when neither exists.
    private static FoAuthMode ResolveAuthMode(string? setting, ServicePrincipal? sp)
    {
        if (setting is null)
        {
            return sp is null ? FoAuthMode.Interactive : FromCoreAuthMode(sp.AuthMode);
        }

        return Enum.TryParse<FoAuthMode>(setting, out var parsed)
            && parsed is FoAuthMode.Interactive or FoAuthMode.ClientSecret or FoAuthMode.Certificate
                ? parsed
                : FoAuthMode.Unsupported;
    }

    // The IProfileStore contract is synchronous but persistence is async; run it on the thread pool
    // to bridge without risking a UI-thread sync-context deadlock. SQLite writes are sub-millisecond.
    private static void RunBlocking(System.Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();

    private static T RunBlocking<T>(System.Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
