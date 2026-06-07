using CommunityToolkit.Mvvm.ComponentModel;
using FoToolbox.Core.DualWrite;

namespace ToolBax.App.ViewModels;

/// <summary>One row in the Operations maps grid, projecting a real <see cref="DualWriteMap"/> from the
/// gateway. <see cref="IsSelected"/> drives which maps a lifecycle action targets; the underlying
/// <see cref="Map"/> is retained so actions can pass it to the gateway.</summary>
public partial class MapRowViewModel : ObservableObject
{
    public DualWriteMap Map { get; }

    public string Id => Map.Id;

    /// <summary>The map's display name (falls back to its name).</summary>
    public string Name { get; }

    /// <summary>The Dataverse (CE) entity this map targets.</summary>
    public string CeEntity => Map.RightEntityName;

    /// <summary>Active template version.</summary>
    public string Version => Map.CurrentVersion;

    /// <summary>Active template author.</summary>
    public string Author => Map.CurrentAuthor;

    /// <summary>The gateway's lifecycle state (raw vocabulary, e.g. "Running"/"Stopped"/"Paused").</summary>
    public string State => Map.State;

    [ObservableProperty]
    private bool _isSelected;

    public MapRowViewModel(DualWriteMap map)
    {
        Map = map;
        Name = string.IsNullOrWhiteSpace(map.DisplayName) ? map.Name : map.DisplayName;
    }

    public static MapRowViewModel From(DualWriteMap map) => new(map);
}
