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

    public CoreMetadataService(ICatalogService catalog, Func<EnvProfile?> activeEnv)
    {
        _catalog = catalog;
        _activeEnv = activeEnv;
    }

    public IReadOnlyList<EntitySet> GetEntities() => _entities;

    public IReadOnlyList<EntityField>? GetFields(string entityName) =>
        _fields.TryGetValue(entityName, out var fields) ? fields : null;

    public IReadOnlyList<string>? GetNavigations(string entityName) =>
        _navigations.TryGetValue(entityName, out var navs) ? navs : null;

    public IReadOnlyList<string>? GetEnumMembers(string enumType) =>
        _enums.TryGetValue(enumType, out var members) ? members : null;

    public async Task LoadEntitiesAsync(CancellationToken ct = default)
    {
        var env = ResolveEnv();
        if (env is null)
        {
            return;
        }

        var index = await _catalog.GetODataEntityIndexAsync(env, CatalogRefreshMode.UseCacheIfFresh, ct).ConfigureAwait(false);
        _entities = index.Entities
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
        _enums = enums;
    }

    private static string LocalName(string typeName) =>
        string.IsNullOrEmpty(typeName) ? typeName : typeName[(typeName.LastIndexOf('.') + 1)..];

    public async Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
    {
        var env = ResolveEnv();
        if (env is null)
        {
            return false;
        }

        var entity = await _catalog.GetODataEntityDetailsAsync(env, entityName, CatalogRefreshMode.UseCacheIfFresh, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        _fields[entityName] = entity.Properties
            .Select(MapField)
            .ToList();
        _navigations[entityName] = entity.Navigations
            .Select(n => n.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

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
            "DateTimeOffset" or "Date" => ("DateTime", null),
            "Boolean" => ("Boolean", null),
            "Guid" => ("Guid", null),
            _ => (primitive, null), // Int32/Int64/Double/etc. pass through as-is
        };
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
