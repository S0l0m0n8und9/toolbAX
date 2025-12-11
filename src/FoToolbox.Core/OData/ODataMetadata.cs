using System.Collections.Generic;

namespace FoToolbox.Core.OData;

public sealed record ODataProperty(string Name, string Type, bool Nullable);

public sealed record ODataNavigationProperty(string Name, string Type);

public sealed record ODataEntity(string Name, IReadOnlyList<ODataProperty> Properties, IReadOnlyList<ODataNavigationProperty> Navigations);

public sealed record ODataMetadata(IReadOnlyList<ODataEntity> Entities, string? ETag);
