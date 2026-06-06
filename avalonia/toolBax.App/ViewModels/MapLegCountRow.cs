using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ToolBax.Core.Models;

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

    [ObservableProperty]
    private string _ceStatus = string.Empty;

    public MapLegCountRow(DwMapLeg leg)
    {
        LegId = leg.LegId;
        SourceSchema = leg.SourceSchema;
        DestinationSchema = leg.DestinationSchema;
        FoFilter = leg.SourceFilterOData;
        CeFilter = leg.ReversedSourceFilter;
        _foEntity = GuessFoEntity(leg.SourceSchema);
    }

    // Correcting the entity invalidates any previously fetched F&O count (and its comparison).
    partial void OnFoEntityChanged(string value)
    {
        FoCount = null;
        FoStatus = string.Empty;
    }

    public string FoCountLabel => FoCount?.ToString("N0") ?? "—";

    public string CeCountLabel => CeCount?.ToString("N0") ?? "—";

    /// <summary>Match / Mismatch once both sides are counted, otherwise "—".</summary>
    public string ComparisonLabel =>
        FoCount is null || CeCount is null ? "—" : FoCount == CeCount ? "Match" : "Mismatch";

    // The dual-write source schema is a data-entity name (e.g. "CustCustomerV3Entity"); the OData entity
    // set usually drops the "Entity" suffix. A best-effort default — the user can correct it.
    private static string GuessFoEntity(string sourceSchema) =>
        sourceSchema.EndsWith("Entity", StringComparison.Ordinal)
            ? sourceSchema[..^"Entity".Length]
            : sourceSchema;
}
