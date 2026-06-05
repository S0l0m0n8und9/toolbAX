using System.Collections.Generic;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Entity-set catalogue + cached $metadata fields. <see cref="GetFields"/> returns null when an
/// entity's fields aren't cached yet (the UI then prompts to fetch them via Query Builder).
/// </summary>
public interface IMetadataService
{
    IReadOnlyList<EntitySet> GetEntities();

    IReadOnlyList<EntityField>? GetFields(string entityName);
}
