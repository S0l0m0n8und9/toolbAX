using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Entity-set catalogue + cached $metadata fields. The getters are a synchronous view over a cache:
/// <see cref="GetEntities"/> returns what's loaded so far and <see cref="GetFields"/> returns null
/// when an entity's fields aren't cached yet (the UI then prompts to fetch them). The Load* methods
/// populate that cache asynchronously from the active environment's live $metadata; the in-memory
/// fake implements them as no-ops over its seeded data.
/// </summary>
public interface IMetadataService
{
    IReadOnlyList<EntitySet> GetEntities();

    IReadOnlyList<EntityField>? GetFields(string entityName);

    /// <summary>Populates the entity-set list for the active environment.</summary>
    Task LoadEntitiesAsync(CancellationToken ct = default);

    /// <summary>Populates one entity's fields; returns false when the entity has no metadata.</summary>
    Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default);

    /// <summary>
    /// As <see cref="LoadEntitiesAsync(CancellationToken)"/>, but with <paramref name="forceRefresh"/>
    /// bypassing every cached copy (in-memory and on-disk) so the environment is really re-read. Defaults
    /// to the cache-respecting overload, which is already a no-op for implementations without a cache.
    /// </summary>
    Task LoadEntitiesAsync(bool forceRefresh, CancellationToken ct = default) => LoadEntitiesAsync(ct);

    /// <summary>
    /// As <see cref="LoadFieldsAsync(string, CancellationToken)"/>, but with <paramref name="forceRefresh"/>
    /// bypassing every cached copy of the entity's metadata.
    /// </summary>
    Task<bool> LoadFieldsAsync(string entityName, bool forceRefresh, CancellationToken ct = default) =>
        LoadFieldsAsync(entityName, ct);

    /// <summary>
    /// The members of an enum type (e.g. "NoYes" → ["No","Yes"]), or null when not known. Used by the
    /// POST Builder to drive enum-dropdown cell editors. Defaults to null so implementations that don't
    /// surface enum metadata (most test fakes) need not implement it.
    /// </summary>
    IReadOnlyList<string>? GetEnumMembers(string enumType) => null;

    /// <summary>
    /// The navigation properties of an entity (e.g. "PrimaryContact", "SalesOrderLines"), used by the
    /// Query Builder to offer <c>$expand</c> joins to related entities. Null when not cached yet; loaded
    /// alongside an entity's fields (<see cref="LoadFieldsAsync"/>). Defaults to null so implementations
    /// that don't surface navigations need not implement it.
    /// </summary>
    IReadOnlyList<string>? GetNavigations(string entityName) => null;
}
