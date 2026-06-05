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
public sealed record EntityField(
    string Name,
    string Type,
    bool Nullable,
    bool IsKey = false,
    int? Length = null,
    int? Precision = null,
    string? EnumType = null)
{
    /// <summary>Human type, e.g. "Enum&lt;NoYes&gt;", "String(20)", "Decimal(32)", "DateTime".</summary>
    public string TypeDisplay => Type switch
    {
        "Enum" when EnumType is not null => $"Enum<{EnumType}>",
        "String" when Length.HasValue => $"String({Length})",
        "Decimal" when Precision.HasValue => $"Decimal({Precision})",
        _ => Type,
    };
}
