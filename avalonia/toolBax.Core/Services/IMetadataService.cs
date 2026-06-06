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
}
