using System;
using System.Collections.Generic;
using System.Globalization;

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

    public string ModifiedOnLabel => ModifiedOn?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty;

    public string CreatedOnLabel => CreatedOn?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty;
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
    string SourceFilterOData,
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

/// <summary>
/// A Dataverse solution (for the Map Browser's "filter maps by solution" picker). <see cref="All"/> is
/// the sentinel "no filter" entry shown first in the dropdown.
/// </summary>
public sealed record DwSolution(
    string Id,
    string UniqueName,
    string FriendlyName,
    string Version,
    string PublisherUniqueName,
    string PublisherDisplayName)
{
    /// <summary>The "All solutions" sentinel — selecting it clears the solution filter.</summary>
    public static DwSolution All { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    public bool IsAll => string.IsNullOrEmpty(Id) && string.IsNullOrEmpty(UniqueName);

    /// <summary>Dropdown label, e.g. "Customer Master [cust_master] v1.0.0.3".</summary>
    public string Display
    {
        get
        {
            if (IsAll)
            {
                return "All solutions";
            }

            var head = string.IsNullOrWhiteSpace(FriendlyName) ? UniqueName : $"{FriendlyName} [{UniqueName}]";
            return string.IsNullOrWhiteSpace(Version) ? head : $"{head} v{Version}";
        }
    }
}

/// <summary>A solution publisher (secondary filter for the solution picker). <see cref="All"/> = no filter.</summary>
public sealed record DwPublisher(string UniqueName, string DisplayName, int SolutionCount)
{
    public static DwPublisher All { get; } = new(string.Empty, string.Empty, 0);

    public bool IsAll => string.IsNullOrEmpty(UniqueName);

    public string Label => IsAll ? "All publishers" : $"{DisplayName} ({SolutionCount})";
}

/// <summary>One page of solutions plus the server-driven paging link (if any).</summary>
public sealed record DwSolutionPage(IReadOnlyList<DwSolution> Solutions, string? NextLink);

/// <summary>One page of solution-component object ids (dual-write maps in a solution) plus paging link.</summary>
public sealed record DwComponentIdPage(IReadOnlyList<Guid> ObjectIds, string? NextLink);

/// <summary>
/// A row count read from an OData <c>$count=true</c> response.
/// </summary>
/// <param name="Count">The number the platform reported — a total, or a ceiling if it was capped.</param>
/// <param name="CapExceeded">
/// What the response said about the platform's count cap: <c>true</c>/<c>false</c> from the Dataverse
/// <c>Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded</c> annotation, or <c>null</c> when the
/// response carried no such annotation (an F&amp;O response never does — F&amp;O counts aren't capped).
/// </param>
public sealed record DwRowCount(long Count, bool? CapExceeded)
{
    /// <summary>
    /// True when <see cref="Count"/> is a platform ceiling rather than a total. The annotation is
    /// authoritative when the response carried one; without it, a count sitting exactly on
    /// <paramref name="cap"/> is read conservatively as "<paramref name="cap"/> or more".
    /// </summary>
    public bool IsCappedAt(long cap) => CapExceeded ?? Count == cap;
}

/// <summary>Outcome of a row-count query: the count, or an error to surface.</summary>
/// <param name="Capped">
/// True when <paramref name="Count"/> is the platform's count ceiling, not a total — the real number is
/// "<paramref name="Count"/> or more". Callers must not treat a capped count as an exact figure.
/// </param>
/// <param name="Snapshot">
/// True when <paramref name="Count"/> is a true total read from a platform snapshot rather than counted
/// live: Dataverse's <c>RetrieveTotalRecordCount</c> answers from a snapshot less than 24 hours old
/// (#210), so the number is uncapped but not exact as of this moment. Never set together with
/// <paramref name="Capped"/> — a snapshot total <i>replaces</i> a capped count, it doesn't annotate one.
/// </param>
public sealed record DwCountResult(long? Count, string? Error, bool Capped = false, bool Snapshot = false)
{
    public bool IsSuccess => Error is null;

    public static DwCountResult Ok(long count, bool capped = false) => new(count, null, capped);

    /// <summary>A true total read from the platform's ≤24h snapshot (see <see cref="Snapshot"/>).</summary>
    public static DwCountResult FromSnapshot(long total) => new(total, null, Capped: false, Snapshot: true);

    public static DwCountResult Fail(string error) => new(null, error);
}

/// <summary>Outcome of loading the solution list: the solutions, or an error to surface.</summary>
public sealed record DwSolutionLoadResult(IReadOnlyList<DwSolution> Solutions, string? Error)
{
    public bool IsSuccess => Error is null;

    public static DwSolutionLoadResult Ok(IReadOnlyList<DwSolution> solutions) => new(solutions, null);

    public static DwSolutionLoadResult Fail(string error) => new(Array.Empty<DwSolution>(), error);
}

/// <summary>Outcome of loading the dual-write map catalogue: the records, or an error to surface.</summary>
public sealed record DwMapLoadResult(IReadOnlyList<DwMapRecord> Maps, string? Error)
{
    public bool IsSuccess => Error is null;

    public static DwMapLoadResult Ok(IReadOnlyList<DwMapRecord> maps) => new(maps, null);

    public static DwMapLoadResult Fail(string error) => new(Array.Empty<DwMapRecord>(), error);
}
