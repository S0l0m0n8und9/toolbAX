using System;
using System.Collections.Generic;

namespace ToolBax.Core.Models;

/// <summary>
/// A dual-write entity map (<c>msdyn_dualwriteentitymap</c>) read from the Dataverse Web API and
/// reshaped for the Map Browser. The detail collections are parsed from the <c>msdyn_mapping</c> /
/// <c>msdyn_properties</c> JSON columns. This mirrors the WPF Dual-Write Map Browser's record model.
/// </summary>
public sealed record DwMapRecord(
    string Id,
    string SolutionId,
    string Name,
    string DisplayName,
    string Version,
    string State,
    string Status,
    string Owner,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? ModifiedOn,
    IReadOnlyList<DwMapSummaryRow> SummaryRows,
    IReadOnlyList<DwMapLeg> Legs,
    IReadOnlyList<DwMapField> Fields,
    IReadOnlyList<DwMapValueTransform> ValueTransforms,
    IReadOnlyList<DwMapProperty> Properties,
    string? RawMapping,
    string? RawProperties)
{
    /// <summary>Master-list label: the display name, falling back to the logical name, then the id.</summary>
    public string Title =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
        : !string.IsNullOrWhiteSpace(Name) ? Name
        : Id;

    public string VersionLabel => string.IsNullOrWhiteSpace(Version) ? string.Empty : $"v{Version}";

    public int LegCount => Legs.Count;

    public int FieldCount => Fields.Count;

    /// <summary>The source (F&amp;O) schema of the first leg — the headline "from" entity.</summary>
    public string PrimarySource => Legs.Count > 0 ? Legs[0].SourceSchema : string.Empty;

    /// <summary>The destination (Dataverse) schema of the first leg — the headline "to" table.</summary>
    public string PrimaryDestination => Legs.Count > 0 ? Legs[0].DestinationSchema : string.Empty;

    public bool HasState => !string.IsNullOrWhiteSpace(State);

    public string ModifiedOnLabel => ModifiedOn?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? string.Empty;

    public string CreatedOnLabel => CreatedOn?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? string.Empty;
}

/// <summary>A top-level scalar property of <c>msdyn_mapping</c> (Summary tab), e.g. <c>legs.count</c>.</summary>
public sealed record DwMapSummaryRow(string Key, string Value);

/// <summary>One "leg" of a dual-write map: an F&amp;O entity ↔ Dataverse table pairing (Legs tab).</summary>
public sealed record DwMapLeg(
    string LegId,
    string SourceSchema,
    string SourceSchemaDistinctName,
    string DestinationSchema,
    string SourceEnvironmentType,
    string DestinationEnvironmentType,
    string SourceFilter,
    string ReversedSourceFilter,
    int FieldMappings)
{
    public bool HasSourceFilter => !string.IsNullOrWhiteSpace(SourceFilter);

    public bool HasReversedSourceFilter => !string.IsNullOrWhiteSpace(ReversedSourceFilter);
}

/// <summary>A field mapping within a leg (Field mappings tab), flattened across all legs.</summary>
public sealed record DwMapField(
    string LegId,
    string SourceSchema,
    string DestinationSchema,
    string SyncDirection,
    string SourceField,
    string DestinationField,
    string DestinationLookupEntity,
    bool? IsSystemGenerated,
    int ValueTransforms)
{
    public bool HasLookup => !string.IsNullOrWhiteSpace(DestinationLookupEntity);
}

/// <summary>A value transform on a field mapping (Value transforms tab), flattened across all legs.</summary>
public sealed record DwMapValueTransform(
    string LegId,
    string SourceField,
    string DestinationField,
    string TransformType,
    string? DefaultValue,
    bool HasDefaultValue,
    string ValueMap,
    bool? CreateValuesOnDestination)
{
    public bool HasValueMap => !string.IsNullOrWhiteSpace(ValueMap);
}

/// <summary>A flattened entry of the <c>msdyn_properties</c> JSON column (Properties tab).</summary>
public sealed record DwMapProperty(string Key, string Type, string Value);

/// <summary>One page of dual-write map records plus the server-driven paging link (if any).</summary>
public sealed record DwMapPage(IReadOnlyList<DwMapRecord> Records, string? NextLink);

/// <summary>Outcome of loading the dual-write map catalogue: the records, or an error to surface.</summary>
public sealed record DwMapLoadResult(IReadOnlyList<DwMapRecord> Maps, string? Error)
{
    public bool IsSuccess => Error is null;

    public static DwMapLoadResult Ok(IReadOnlyList<DwMapRecord> maps) => new(maps, null);

    public static DwMapLoadResult Fail(string error) => new(Array.Empty<DwMapRecord>(), error);
}
