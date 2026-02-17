using System.Collections.Generic;

namespace FoToolbox.Core.OData;

public sealed record ODataProperty(
    string Name,
    string Type,
    bool Nullable,
    bool IsKey = false,
    bool IsMandatory = false,
    string? MaxLength = null,
    string? Precision = null,
    string? Scale = null,
    string? MinValue = null,
    string? MaxValue = null)
{
    // FO "mandatory" is not the same thing as OData nullability.
    // When available, prefer /metadata/PublicEntities flags (IsMandatory/IsKey).
    public bool Mandatory => IsKey || IsMandatory;
}

public sealed record ODataNavigationProperty(string Name, string Type);

public sealed record ODataEnumType(string Name, IReadOnlyList<string> Members);

public sealed record ODataEntity(string Name, IReadOnlyList<ODataProperty> Properties, IReadOnlyList<ODataNavigationProperty> Navigations);

public sealed record ODataMetadata(IReadOnlyList<ODataEntity> Entities, IReadOnlyList<ODataEnumType> Enums, string? ETag);
