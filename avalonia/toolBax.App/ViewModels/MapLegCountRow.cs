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

    /// <summary>
    /// The leg's distinct-name variant of the source schema — the resolver's second candidate, kept on the
    /// row so a re-resolution (#209) has the same inputs the first one had.
    /// </summary>
    public string SourceSchemaDistinctName { get; }

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

    /// <summary>
    /// True when <see cref="CeCount"/> is a true total read from the platform's ≤24h snapshot
    /// (<c>RetrieveTotalRecordCount</c>) instead of counted live — uncapped, but not exact as of now (#210).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CeCountLabel))]
    [NotifyPropertyChangedFor(nameof(CeFilterTip))]
    [NotifyPropertyChangedFor(nameof(ComparisonLabel))]
    private bool _ceCountSnapshot;

    [ObservableProperty]
    private string _ceStatus = string.Empty;

    // The value FoEntity was defaulted to, so a later re-resolution can tell "still the default" from
    // "the user typed this" without a separate dirty flag the view would have to remember to set.
    private string _defaultedFoEntity;

    public MapLegCountRow(DwMapLeg leg, string? resolvedFoEntity = null)
    {
        LegId = leg.LegId;
        SourceSchema = leg.SourceSchema;
        SourceSchemaDistinctName = leg.SourceSchemaDistinctName;
        DestinationSchema = leg.DestinationSchema;
        FoFilter = leg.SourceFilterOData;
        CeFilter = leg.ReversedSourceFilter;
        _foEntity = !string.IsNullOrWhiteSpace(resolvedFoEntity) ? resolvedFoEntity : GuessFoEntity(leg.SourceSchema);
        _defaultedFoEntity = _foEntity;
    }

    // Correcting the entity invalidates any previously fetched F&O count (and its comparison).
    partial void OnFoEntityChanged(string value)
    {
        FoCount = null;
        FoStatus = string.Empty;
    }

    /// <summary>
    /// True while <see cref="FoEntity"/> still holds the value this row was defaulted with — i.e. the user
    /// hasn't corrected it, so a better resolution is allowed to replace it.
    /// </summary>
    public bool FoEntityIsDefault => string.Equals(FoEntity, _defaultedFoEntity, StringComparison.Ordinal);

    /// <summary>
    /// Replaces a defaulted F&amp;O entity with a better resolution (#209: the F&amp;O entity catalogue can
    /// arrive after the rows were built, and the pre-catalogue default is the raw map schema). The new value
    /// becomes the row's default too, so it still reads as un-corrected. A user-edited entity, or a blank
    /// resolution, changes nothing.
    /// </summary>
    public void AdoptResolvedFoEntity(string? resolved)
    {
        if (string.IsNullOrWhiteSpace(resolved) || !FoEntityIsDefault)
        {
            return;
        }

        _defaultedFoEntity = resolved;
        FoEntity = resolved;
    }

    public string FoCountLabel => FoCount?.ToString("N0") ?? "—";

    /// <summary>
    /// The CE count: suffixed with "+" when it is a capped ceiling rather than a total, prefixed with "≈"
    /// when it is a snapshot total. The cell is narrow, so the ≤24h caveat itself lives in
    /// <see cref="CeFilterTip"/> rather than in the number.
    /// </summary>
    public string CeCountLabel =>
        CeCount is null ? "—"
        : CeCountSnapshot ? "≈" + CeCount.Value.ToString("N0")
        : CeCount.Value.ToString("N0") + (CeCountCapped ? "+" : string.Empty);

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

    /// <summary>
    /// ToolTip for the CE count cell: which filter (if any) produced the number, plus the snapshot caveat
    /// when the live count was capped and the total came from <c>RetrieveTotalRecordCount</c> instead.
    /// </summary>
    public string CeFilterTip => CeCountSnapshot
        ? $"{DescribeFilter(CeFilter)} · snapshot total (≤24h old) — the live count hit the " +
          $"{DualWriteMapParser.DataverseStandardCountCap:N0}-row Dataverse ceiling"
        : DescribeFilter(CeFilter);

    private static string DescribeFilter(string filter) =>
        string.IsNullOrWhiteSpace(filter) ? "Counted unfiltered" : $"Counted with filter: {filter}";

    /// <summary>
    /// The verdict once both sides are counted, in descending order of what makes a comparison impossible:
    /// nothing counted yet → a one-sided filter (the two numbers measure different populations) → a capped
    /// CE count (a floor, not a total) → a snapshot CE total (a real total, but up to 24 hours stale, so
    /// equality is strong evidence and not proof — hence the "≈" tier rather than a plain verdict) → two
    /// live exact numbers.
    /// </summary>
    public string ComparisonLabel =>
        FoCount is null || CeCount is null ? "—"
        : !FiltersComparable ? "Not comparable (filters differ)"
        : CeCountCapped ? "Unknown (CE count capped)"
        : CeCountSnapshot ? (FoCount == CeCount ? "≈ Match" : "≈ Mismatch")
        : FoCount == CeCount ? "Match" : "Mismatch";

    // The dual-write source schema is a data-entity name (e.g. "CustCustomerV3Entity"); the OData entity
    // set usually drops the "Entity" suffix. A best-effort default — the user can correct it.
    private static string GuessFoEntity(string sourceSchema) =>
        sourceSchema.EndsWith("Entity", StringComparison.Ordinal)
            ? sourceSchema[..^"Entity".Length]
            : sourceSchema;
}
