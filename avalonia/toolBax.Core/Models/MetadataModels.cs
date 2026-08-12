namespace ToolBax.Core.Models;

/// <summary>An OData entity set in the metadata catalogue (Metadata Browser master list).</summary>
public sealed record EntitySet(
    string Name,
    string Module,
    int FieldCount,
    string Pk,
    bool CompanyAware,
    string Tag);

/// <summary>A property of an entity set (Metadata Browser detail grid).</summary>
/// <param name="EnumType">Short, local enum type name (e.g. "NoYes") — the key enum members are cached
/// under, so <c>IMetadataService.GetEnumMembers(EnumType)</c> resolves. Display-friendly, but NOT a valid
/// OData type reference.</param>
/// <param name="QualifiedEnumType">Namespace-qualified enum type name exactly as $metadata declared it
/// (e.g. "Microsoft.Dynamics.DataEntities.NoYes"). Required to build an OData v4 enum literal —
/// <c>Microsoft.Dynamics.DataEntities.NoYes'Yes'</c> — which F&amp;O demands for a genuine enum property;
/// the bare <c>'Yes'</c> form is a 400. Null when the source didn't carry a qualified name.</param>
public sealed record EntityField(
    string Name,
    string Type,
    bool Nullable,
    bool IsKey = false,
    int? Length = null,
    int? Precision = null,
    string? EnumType = null,
    bool Mandatory = false,
    int? Scale = null,
    string? MinValue = null,
    string? MaxValue = null,
    string? QualifiedEnumType = null)
{
    /// <summary>Human type, e.g. "Enum&lt;NoYes&gt;", "String(20)", "Decimal(32)", "DateTime".</summary>
    public string TypeDisplay => Type switch
    {
        "Enum" when EnumType is not null => $"Enum<{EnumType}>",
        "String" when Length.HasValue => $"String({Length})",
        "Decimal" when Precision.HasValue => $"Decimal({Precision})",
        _ => Type,
    };

    /// <summary>Max length as text for the grid ("" when not applicable).</summary>
    public string MaxLengthDisplay => Length?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>"precision/scale" (e.g. "32/4"), or "" when neither is known.</summary>
    public string PrecisionScale => Precision is null && Scale is null
        ? string.Empty
        : $"{Precision?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"}/{Scale?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"}";

    /// <summary>"min .. max" (e.g. "0 .. 9999999"), or "" when neither bound is known.</summary>
    public string Range => MinValue is null && MaxValue is null
        ? string.Empty
        : $"{MinValue ?? "—"} .. {MaxValue ?? "—"}";
}
