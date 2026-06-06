using CommunityToolkit.Mvvm.ComponentModel;
using ToolBax.Core.Models;

namespace ToolBax.App.ViewModels;

/// <summary>
/// One row of the Map Browser's "Row counts" panel: a dual-write map leg's Dataverse (CE) side and its
/// on-demand row count. The count + status are filled by the VM's count command.
/// </summary>
public partial class MapLegCountRow : ObservableObject
{
    public string LegId { get; }
    public string SourceSchema { get; }
    public string DestinationSchema { get; }
    public string CeFilter { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CeCountLabel))]
    private long? _ceCount;

    [ObservableProperty]
    private string _ceStatus = string.Empty;

    public MapLegCountRow(DwMapLeg leg)
    {
        LegId = leg.LegId;
        SourceSchema = leg.SourceSchema;
        DestinationSchema = leg.DestinationSchema;
        CeFilter = leg.ReversedSourceFilter;
    }

    /// <summary>The CE row count formatted with thousands separators, or "—" before it's counted.</summary>
    public string CeCountLabel => CeCount?.ToString("N0") ?? "—";
}
