using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IMetadataService"/>: projects FoToolbox.Core's cached OData $metadata (via
/// <see cref="ICatalogService"/>) onto the Avalonia catalogue models. Reads are synchronous over an
/// in-memory cache; the Load* methods fetch the entity index / per-entity details for whichever
/// environment is active at call time and populate that cache. Cross-platform (no DPAPI) — the
/// catalogue layer caches in SQLite under %LocalAppData%.
/// </summary>
public sealed class CoreMetadataService : IMetadataService
{
    private readonly ICatalogService _catalog;
    private readonly Func<EnvProfile?> _activeEnv;

    // What's been fetched so far; the getters read these without blocking.
    private volatile IReadOnlyList<EntitySet> _entities = Array.Empty<EntitySet>();
    private readonly ConcurrentDictionary<string, IReadOnlyList<EntityField>> _fields = new(StringComparer.OrdinalIgnoreCase);
    // Navigation properties per entity, cached from the same details fetch as the fields.
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _navigations = new(StringComparer.OrdinalIgnoreCase);
    // Enum type → members, populated from the entity index ($metadata enums); drives enum cell editors.
    private volatile IReadOnlyDictionary<string, IReadOnlyList<string>> _enums =
        new Dictionary<string, IReadOnlyList<string>>();

    // The environment the caches above belong to. This service is an app-lifetime singleton, so without
    // that dimension a profile switch keeps serving the previous environment's entities/fields forever
    // (callers treat a cache hit as "already loaded" and never refetch).
    private string? _cacheEnvId;
    private readonly object _envSync = new();

    public CoreMetadataService(ICatalogService catalog, Func<EnvProfile?> activeEnv)
    {
        _catalog = catalog;
        _activeEnv = activeEnv;
    }

    public IReadOnlyList<EntitySet> GetEntities()
    {
        ResetIfEnvChanged();
        return _entities;
    }

    public IReadOnlyList<EntityField>? GetFields(string entityName)
    {
        ResetIfEnvChanged();
        return _fields.TryGetValue(entityName, out var fields) ? fields : null;
    }

    public IReadOnlyList<string>? GetNavigations(string entityName)
    {
        ResetIfEnvChanged();
        return _navigations.TryGetValue(entityName, out var navs) ? navs : null;
    }

    public IReadOnlyList<string>? GetEnumMembers(string enumType)
    {
        ResetIfEnvChanged();
        return _enums.TryGetValue(enumType, out var members) ? members : null;
    }

    // Empties every cache when the active environment changes, so the next read misses and the next load
    // refetches. Cheap enough to sit on the synchronous getters: it compares one string per call.
    private void ResetIfEnvChanged()
    {
        var envId = _activeEnv()?.Id;
        if (string.Equals(envId, _cacheEnvId, StringComparison.Ordinal))
        {
            return;
        }

        lock (_envSync)
        {
            if (string.Equals(envId, _cacheEnvId, StringComparison.Ordinal))
            {
                return;
            }

            _fields.Clear();
            _navigations.Clear();
            _entities = Array.Empty<EntitySet>();
            _enums = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            _cacheEnvId = envId;
        }
    }

    public Task LoadEntitiesAsync(CancellationToken ct = default) => LoadEntitiesAsync(false, ct);

    public async Task LoadEntitiesAsync(bool forceRefresh, CancellationToken ct = default)
    {
        ResetIfEnvChanged();
        // The cache generation this fetch belongs to. ResetIfEnvChanged has just stamped _cacheEnvId with
        // it, and the commit below only lands if it is still current — see IsStillCurrent.
        var envId = _activeEnv()?.Id;
        var env = ResolveEnv();
        if (env is null)
        {
            return;
        }

        var index = await _catalog.GetODataEntityIndexAsync(env, RefreshMode(forceRefresh), ct).ConfigureAwait(false);
        var entities = index.Entities
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => new EntitySet(e.Name, Module: string.Empty, FieldCount: e.PropertyCount, Pk: string.Empty, CompanyAware: false, Tag: "odata"))
            .ToList();

        // Key enums by their short local name, matching MapType's collapse of an enum property's type,
        // so GetEnumMembers(field.EnumType) resolves.
        var enums = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in index.Enums)
        {
            enums[LocalName(e.Name)] = e.Members;
        }

        lock (_envSync)
        {
            if (!IsStillCurrent(envId))
            {
                return;
            }

            _entities = entities;
            _enums = enums;
        }
    }

    private static string LocalName(string typeName) =>
        string.IsNullOrEmpty(typeName) ? typeName : typeName[(typeName.LastIndexOf('.') + 1)..];

    public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
        LoadFieldsAsync(entityName, false, ct);

    public async Task<bool> LoadFieldsAsync(string entityName, bool forceRefresh, CancellationToken ct = default)
    {
        ResetIfEnvChanged();
        // See LoadEntitiesAsync: the generation this fetch is for, checked again before committing.
        var envId = _activeEnv()?.Id;
        var env = ResolveEnv();
        if (env is null)
        {
            return false;
        }

        var entity = await _catalog.GetODataEntityDetailsAsync(env, entityName, RefreshMode(forceRefresh), ct).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        var fields = entity.Properties
            .Select(MapField)
            .ToList();
        var navigations = entity.Navigations
            .Select(n => n.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_envSync)
        {
            if (!IsStillCurrent(envId))
            {
                // Report "not loaded" for the now-active environment; callers treat false as a miss and
                // reload, which then fetches against the environment that is actually current.
                return false;
            }

            _fields[entityName] = fields;
            _navigations[entityName] = navigations;
        }

        return true;
    }

    // The cache-generation guard for the Load* methods. A fetch resolves its environment at entry and
    // awaits; if the active environment switches meanwhile, a getter re-stamps _cacheEnvId and empties the
    // caches (ResetIfEnvChanged), and the in-flight result no longer belongs to anything. Invariant:
    // results are only ever committed to the cache generation they were fetched for; otherwise they are
    // discarded, never relabelled as the new environment's metadata. Callers hold _envSync — the same lock
    // ResetIfEnvChanged clears under — so the check and the commit can't be split by a switch.
    private bool IsStillCurrent(string? envId) => string.Equals(envId, _cacheEnvId, StringComparison.Ordinal);

    // A forced refresh bypasses both the max-age check and the stored copy, so the Metadata Browser's
    // Refresh really goes back to the environment.
    private static CatalogRefreshMode RefreshMode(bool forceRefresh) =>
        forceRefresh ? CatalogRefreshMode.ForceRefresh : CatalogRefreshMode.UseCacheIfFresh;

    private FoEnvironment? ResolveEnv()
    {
        var env = _activeEnv();
        if (env is null)
        {
            return null;
        }

        return new FoEnvironment(env.Id, env.Name, env.Url, env.Tenant,
            string.IsNullOrWhiteSpace(env.Legal) ? null : env.Legal);
    }

    // ODataProperty carries the raw EDM type ("Edm.String") or a fully-qualified enum/complex type
    // ("Microsoft.Dynamics.DataEntities.NoYes"); collapse it to the short form the UI's TypeDisplay
    // understands ("String"/"Decimal"/"DateTime"/"Enum"), carrying length/precision/enum-name across.
    private static EntityField MapField(ODataProperty p)
    {
        var (type, enumType) = MapType(p.Type);
        return new EntityField(
            p.Name,
            type,
            Nullable: p.Nullable,
            IsKey: p.IsKey,
            Length: ParseInt(p.MaxLength),
            Precision: ParseInt(p.Precision),
            EnumType: enumType,
            Mandatory: p.Mandatory,
            Scale: ParseInt(p.Scale),
            MinValue: p.MinValue,
            MaxValue: p.MaxValue);
    }

    private static (string Type, string? EnumType) MapType(string edmType)
    {
        if (string.IsNullOrWhiteSpace(edmType))
        {
            return ("String", null);
        }

        // Collection(...) properties aren't a scalar type and have no enum members. The local-name
        // collapse below would mangle "Collection(Edm.String)" into an unbalanced Enum<String)> and offer
        // an enum editor, so label them plainly instead — nothing in the app edits a collection value
        // (POST Builder's ResolveEditor falls through to a text box, and the payload mapper to Edm.String).
        if (edmType.StartsWith("Collection(", StringComparison.Ordinal))
        {
            return ("Collection", null);
        }

        // Anything outside the Edm.* namespace is an enum/complex type in F&O metadata; surface the
        // local name as the enum type so the grid shows "Enum<NoYes>".
        if (!edmType.StartsWith("Edm.", StringComparison.Ordinal))
        {
            var local = edmType[(edmType.LastIndexOf('.') + 1)..];
            return ("Enum", local);
        }

        var primitive = edmType["Edm.".Length..];
        return primitive switch
        {
            "String" => ("String", null),
            "Decimal" => ("Decimal", null),
            "DateTimeOffset" => ("DateTime", null),
            // Kept distinct from DateTimeOffset: collapsing the two made the payload builder's date-only
            // branch unreachable, so every date was widened to a timestamp on the way to F&O.
            "Date" => ("Date", null),
            "Boolean" => ("Boolean", null),
            "Guid" => ("Guid", null),
            _ => (primitive, null), // Int32/Int64/Double/etc. pass through as-is
        };
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
