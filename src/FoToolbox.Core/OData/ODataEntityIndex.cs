using System.Collections.Generic;

namespace FoToolbox.Core.OData;

public sealed record ODataEntityIndexItem(string Name, int PropertyCount, int NavigationCount);

/// <summary>
/// Lightweight index of entities suitable for listing/search without loading per-entity field details.
/// </summary>
public sealed record ODataEntityIndex(IReadOnlyList<ODataEntityIndexItem> Entities, IReadOnlyList<ODataEnumType> Enums, string? ETag);

