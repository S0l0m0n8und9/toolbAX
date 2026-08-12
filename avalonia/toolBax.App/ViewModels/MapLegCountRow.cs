using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// One row of the Map Browser's "Row counts" panel: a dual-write map leg's F&amp;O and Dataverse (CE)
/// sides and their on-demand row counts. The F&amp;O entity is editable (best-effort default from the
/// source schema, user-correctable); the counts/status are filled by the VM's count command.
/// </summary>
public partial class MapLegCountRow : ObservableObject
{
    public string LegId { get; }
    public string SourceSchema { get; }
    public string DestinationSchema { get; }

    /// <summary>The leg's source filter as OData (for the F&amp;O count).</summary>
    public string FoFilter { get; }

    /// <summary>The leg's reversed source filter (already OData) for the Dataverse count.</summary>
    public string CeFilter { get; }

    /// <summary>The F&amp;O OData entity set to count — defaulted from the source schema, user-editable.</summary>
    [ObservableProperty]
    private string _foEntity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FoCountLabel))]
    [NotifyPropertyChangedFor(nameof(ComparisonLabel))]
    private long? _foCount;

    [ObservableProperty]
    private string _foStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CeCountLabel))]
    [NotifyPropertyChangedFor(nameof(ComparisonLabel))]
    private long? _ceCount;

    /// <summary>
    /// True when <see cref="CeCount"/> is the Dataverse count ceiling rather than a total (the real number
    /// is that many or more) — see <see cref="DualWriteMapParser.DataverseStandardCountCap"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CeCountLabel))]
    [NotifyPropertyChangedFor(nameof(ComparisonLabel))]
    private bool _ceCountCapped;

    [ObservableProperty]
    private string _ceStatus = string.Empty;

    public MapLegCountRow(DwMapLeg leg, string? resolvedFoEntity = null)
    {
        LegId = leg.LegId;
        SourceSchema = leg.SourceSchema;
        DestinationSchema = leg.DestinationSchema;
        FoFilter = leg.SourceFilterOData;
        CeFilter = leg.ReversedSourceFilter;
        _foEntity = !string.IsNullOrWhiteSpace(resolvedFoEntity) ? resolvedFoEntity : GuessFoEntity(leg.SourceSchema);
    }

    // Correcting the entity invalidates any previously fetched F&O count (and its comparison).
    partial void OnFoEntityChanged(string value)
    {
        FoCount = null;
        FoStatus = string.Empty;
    }

    public string FoCountLabel => FoCount?.ToString("N0") ?? "—";

    /// <summary>The CE count, suffixed with "+" when it is a capped ceiling rather than a total.</summary>
    public string CeCountLabel =>
        CeCount is null ? "—" : CeCount.Value.ToString("N0") + (CeCountCapped ? "+" : string.Empty);

    /// <summary>True when the F&amp;O count was taken with the leg's (converted) source filter applied.</summary>
    public bool FoFilterApplied => !string.IsNullOrWhiteSpace(FoFilter);

    /// <summary>True when the CE count was taken with the leg's reversed source filter applied.</summary>
    public bool CeFilterApplied => !string.IsNullOrWhiteSpace(CeFilter);

    /// <summary>
    /// False when exactly one side was filtered — a forward-only map has a source filter and an empty
    /// reversed source filter, so the F&amp;O count is a subset while the CE count is the whole table. The
    /// two numbers are then measuring different populations and must not be reported as (mis)matching.
    /// </summary>
    public bool FiltersComparable => FoFilterApplied == CeFilterApplied;

    /// <summary>ToolTip for the F&amp;O count cell: which filter (if any) produced the number.</summary>
    public string FoFilterTip => DescribeFilter(FoFilter);

    /// <summary>ToolTip for the CE count cell: which filter (if any) produced the number.</summary>
    public string CeFilterTip => DescribeFilter(CeFilter);

    private static string DescribeFilter(string filter) =>
        string.IsNullOrWhiteSpace(filter) ? "Counted unfiltered" : $"Counted with filter: {filter}";

    /// <summary>
    /// The verdict once both sides are counted. Match / Mismatch is only claimed when the two numbers are
    /// actually comparable: a one-sided filter means they measure different populations, and a capped CE
    /// count is a floor rather than a total — either way the answer is unknown, not a mismatch.
    /// </summary>
    public string ComparisonLabel =>
        FoCount is null || CeCount is null ? "—"
        : !FiltersComparable ? "Not comparable (filters differ)"
        : CeCountCapped ? "Unknown (CE count capped)"
        : FoCount == CeCount ? "Match" : "Mismatch";

    // The dual-write source schema is a data-entity name (e.g. "CustCustomerV3Entity"); the OData entity
    // set usually drops the "Entity" suffix. A best-effort default — the user can correct it.
    private static string GuessFoEntity(string sourceSchema) =>
        sourceSchema.EndsWith("Entity", StringComparison.Ordinal)
            ? sourceSchema[..^"Entity".Length]
            : sourceSchema;
}
