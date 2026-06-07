using System;
using System.Collections.Generic;
using System.Linq;
using FoToolbox.Core.OData;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// Bridges the Avalonia metadata model (<see cref="EntityField"/>, which carries the UI-friendly type
/// names "String"/"Decimal"/"Boolean"/"Enum"/…) to the shared, platform-neutral
/// <see cref="ODataPayloadBuilder"/> in <c>FoToolbox.Core</c>, which keys its JSON type-coercion off
/// EDM type names ("Edm.Decimal", "Edm.Boolean", …). Reusing the Core builder gives the POST Builder
/// the same validated, type-coerced payload generation the WPF plugin uses.
/// </summary>
public static class PostPayloadMapper
{
    /// <summary>
    /// Maps a Query/Metadata friendly type name back to the EDM type the payload builder coerces on.
    /// Enum members are emitted as JSON strings (their member-name), so enum → Edm.String. Anything
    /// unrecognised also falls back to Edm.String (sent verbatim as a string).
    /// </summary>
    public static string ToEdmType(string friendlyType) => friendlyType switch
    {
        "Boolean" => "Edm.Boolean",
        "Int16" => "Edm.Int16",
        "Int32" => "Edm.Int32",
        "Int64" => "Edm.Int64",
        "Decimal" => "Edm.Decimal",
        "Double" => "Edm.Double",
        "Single" => "Edm.Single",
        "Guid" => "Edm.Guid",
        // The Avalonia model collapses Edm.Date and Edm.DateTimeOffset to "DateTime"; treat it as the
        // wider DateTimeOffset for coercion (ISO 8601 accepted).
        "DateTime" => "Edm.DateTimeOffset",
        _ => "Edm.String", // String, Enum, complex/unknown
    };

    /// <summary>Projects one Avalonia field onto an <see cref="ODataProperty"/>. A field is treated as
    /// mandatory when it is a key or non-nullable (FO "mandatory" ≈ non-nullable here).</summary>
    public static ODataProperty ToProperty(EntityField f) =>
        new(f.Name, ToEdmType(f.Type), Nullable: f.Nullable, IsKey: f.IsKey, IsMandatory: !f.Nullable);

    /// <summary>Builds the <see cref="ODataEntity"/> the payload builder needs from a field list.</summary>
    public static ODataEntity ToEntity(string name, IReadOnlyList<EntityField> fields) =>
        new(name, fields.Select(ToProperty).ToList(), Array.Empty<ODataNavigationProperty>());
}
